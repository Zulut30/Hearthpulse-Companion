using System.Collections.Concurrent;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Xunit.Abstractions;

namespace Hearthpulse.Tests;

/// <summary>
/// Runs the parser over Power logs kept by the local Hearthstone client. Tests are no-ops on
/// machines without those logs.
/// </summary>
public sealed class RealLogTests(ITestOutputHelper output)
{
    internal const string LogRoot = @"D:\Hearthstone\Logs";

    private static CardDb Cards { get; } = LoadCards();

    private static CardDb LoadCards()
    {
        var path = Path.Combine(Path.GetTempPath(), "hl", "cards.json");
        if (!File.Exists(path))
        {
            return CardDb.Empty;
        }

        using var stream = File.OpenRead(path);
        return CardDb.Parse("itIT", stream);
    }

    /// <summary>Reads a log the running game may still be writing, the way the app does (shared read).</summary>
    internal static IEnumerable<string> SharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static IEnumerable<(string Path, DateTime Start)> PowerLogs(string root = LogRoot)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(root, "Hearthstone_*"))
        {
            var start = Core.Hearthstone.HearthstoneInstall.SessionStart(dir);
            foreach (var name in new[] { "Power_old.log", "Power.log" })
            {
                var file = Path.Combine(dir, name);
                if (start is not null && File.Exists(file))
                {
                    yield return (file, start.Value);
                }
            }
        }
    }

    /// <summary>
    /// True when a kept Power log under <paramref name="root"/> holds a finished game of <paramref name="gameType"/>
    /// (<c>GT_RANKED</c>): a player's final <c>PLAYSTATE</c> after the game's <c>GameType</c> line. A line scan, not the
    /// parser: it runs while the tests are discovered.
    /// </summary>
    internal static bool HasFinishedGame(string root, string gameType)
    {
        const string TypeLine = "GameState.DebugPrintGame() - GameType=";
        string[] finalStates = ["tag=PLAYSTATE value=WON", "tag=PLAYSTATE value=LOST", "tag=PLAYSTATE value=TIED", "tag=PLAYSTATE value=CONCEDED"];
        foreach (var (path, _) in PowerLogs(root))
        {
            var wanted = false;
            foreach (var line in SharedLines(path))
            {
                if (line.Contains(TypeLine, StringComparison.Ordinal))
                {
                    wanted = line.TrimEnd().EndsWith(TypeLine + gameType, StringComparison.Ordinal);
                }
                else if (wanted && finalStates.Any(state => line.Contains(state, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [RealLogsFact]
    public void Backfill_parses_every_kept_log_into_sane_records()
    {
        var total = 0;
        foreach (var (path, start) in PowerLogs())
        {
            var records = LogBackfill.Parse(path, start, Cards);
            foreach (var r in records)
            {
                output.WriteLine($"{r.StartedAt:g} {r.Mode,-13} {r.PlayerName,-14} hero={Cards.NameOf(r.HeroCardId),-22} place={r.Placement} result={r.Result} turns={r.Turns} tier={r.TavernTier} vs={r.OpponentClass}");
                Assert.False(string.IsNullOrEmpty(r.PlayerName));
                Assert.False(string.IsNullOrEmpty(r.HeroCardId));
                if (r.IsBattlegrounds)
                {
                    Assert.InRange(r.Placement, 1, 8);
                    Assert.InRange(r.TavernTier, 1, 7);
                    Assert.True(r.Turns >= 1);
                }
            }

            total += records.Count;
        }

        output.WriteLine($"{total} games");
    }

    [RealLogsFact]
    public void Kept_battlegrounds_log_tracks_lobby_boards_and_combats()
    {
        // Machine-dependent smoke run over the newest kept Battlegrounds log; the deterministic check is
        // BattlegroundsFixtureTests, which runs the same code on a fragment kept in the repository.
        var log = PowerLogs().LastOrDefault(l => SharedLines(l.Path).Take(20_000).Any(x => x.Contains("GameType=GT_BATTLEGROUNDS")));
        if (log.Path is null)
        {
            output.WriteLine("No Battlegrounds game in the kept logs.");
            return;
        }

        var replay = BattlegroundsReplay.Run(SharedLines(log.Path), DateOnly.FromDateTime(log.Start), Cards, every: 10);
        Assert.NotNull(replay.HeroPick);
        Assert.InRange(replay.HeroPick!.HeroOptions.Count, 2, 4);
        Assert.NotNull(replay.Best);
        replay.Print(output, Cards);
        Assert.Equal(7, replay.Best!.Opponents.Count);
    }
}

/// <summary>
/// A fact that is skipped on machines without the Hearthstone client's kept logs, or, when <paramref name="log"/> names
/// one session folder (<c>Hearthstone_2026_11_15_20_36_45</c>), without that folder's <c>Power.log</c>, or, when
/// <paramref name="gameType"/> names a game type (<c>GT_RANKED</c>), without a finished game of that type in the kept
/// logs (the client keeps only its last sessions): a machine that keeps other logs skips the test instead of failing it.
/// </summary>
public sealed class RealLogsFactAttribute : FactAttribute
{
    /// <summary>Per game type, whether the kept logs hold a finished game of it: scanned once per test run.</summary>
    private static readonly ConcurrentDictionary<string, bool> FinishedGames = new(StringComparer.Ordinal);

    public RealLogsFactAttribute(string? log = null, string? gameType = null)
    {
        GameType = gameType;
        if (!OperatingSystem.IsWindows() || !Directory.Exists(RealLogTests.LogRoot))
        {
            Skip = $"No kept Hearthstone logs ({RealLogTests.LogRoot}) on this machine.";
        }
        else if (log is not null && !File.Exists(Path.Combine(RealLogTests.LogRoot, log, "Power.log")))
        {
            Skip = $"No kept Hearthstone log {log} ({RealLogTests.LogRoot}) on this machine.";
        }
        else if (gameType is not null && !FinishedGames.GetOrAdd(gameType, type => RealLogTests.HasFinishedGame(RealLogTests.LogRoot, type)))
        {
            Skip = $"No finished {gameType} game in the kept Hearthstone logs ({RealLogTests.LogRoot}) on this machine.";
        }
    }

    /// <summary>The game type the test needs a finished game of in the kept logs; null when any kept logs do.</summary>
    public string? GameType { get; }
}

using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Replay;
using Xunit.Abstractions;

namespace Hearthpulse.Tests;

/// <summary>Game records built from the kept Hearthstone logs: every finished game of a sent mode, nothing hidden.</summary>
public sealed class GameStatsRealLogTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-gamestats-real-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Every finished game of the kept logs, with the build numbers its log file wrote.</summary>
    private static IEnumerable<(GameRecord Game, HashSet<int> Builds)> KeptGames(ReplayStore? store = null)
    {
        foreach (var dir in Directory.EnumerateDirectories(RealLogTests.LogRoot, "Hearthstone_*"))
        {
            var start = Core.Hearthstone.HearthstoneInstall.SessionStart(dir);
            foreach (var name in new[] { "Power_old.log", "Power.log" })
            {
                var file = Path.Combine(dir, name);
                if (start is null || !File.Exists(file))
                {
                    continue;
                }

                var builds = BuildNumbers(file);
                foreach (var game in LogBackfill.Parse(file, start.Value, CardDb.Empty, replays: store))
                {
                    yield return (game, builds);
                }
            }
        }
    }

    /// <summary>The numbers of the log's <c>BuildNumber=</c> lines, read apart from the parser.</summary>
    private static HashSet<int> BuildNumbers(string file)
    {
        const string Marker = "GameState.DebugPrintGame() - BuildNumber=";
        var builds = new HashSet<int>();
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at >= 0 && int.TryParse(line.AsSpan(at + Marker.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var build))
            {
                builds.Add(build);
            }
        }

        return builds;
    }

    [RealLogsFact]
    public void Kept_logs_give_records_within_the_pool_limits_and_with_nothing_hidden()
    {
        var store = new ReplayStore(_dir);
        var records = 0;
        foreach (var (game, builds) in KeptGames(store))
        {
            var text = store.Load(game.Replay);
            if (GameStatsRecord.Build(game, new string('a', 32), text, null, CardDb.Empty, "0.18.0") is not { } record)
            {
                Assert.True(GameStatsRecord.ModeOf(game.Mode) is null || game.Result is not ("Win" or "Loss" or "Tie") || game.IsBattlegrounds,
                    $"{game.Mode} {game.Result} gave no record");
                continue;
            }

            records++;
            var json = record.ToJsonString();
            output.WriteLine($"{game.StartedAt:g} {game.Mode,-13} {game.Result,-5} build {game.Build} {json.Length / 1024.0,5:F1} KB");
            if (Environment.GetEnvironmentVariable("HP_PRINT_RECORDS") == "1")
            {
                output.WriteLine(json);
            }

            Assert.True(json.Length < 24 * 1024, $"{json.Length} bytes");
            foreach (var who in new[] { game.PlayerName, game.OpponentName })
            {
                if (who is { Length: > 2 })
                {
                    Assert.DoesNotContain(who, json, StringComparison.Ordinal);
                }
            }

            // The build is the number the log wrote for its games; the history row and the record carry the same.
            if (builds.Count > 0)
            {
                Assert.NotNull(game.Build);
                Assert.Contains(game.Build.Value, builds);
            }

            Assert.Equal(game.Build, (int?)record["build"]);

            if (text is null || game.IsBattlegrounds)
            {
                continue;
            }

            // An opponent card in the record was shown during the game: an event line names it, not only the post-game part.
            var body = ReplayFile.Read(text)!.Value.Body;
            var shown = body.Where(l => l.StartsWith("E ", StringComparison.Ordinal)).Select(l => l.Split(' '))
                .Where(t => t.Length > 5 && t[2] == "O" && t[5] != "-").Select(t => t[5]).ToHashSet(StringComparer.Ordinal);
            foreach (var play in record["oplays"]!.AsArray())
            {
                Assert.Contains((string)play![1]!, shown);
            }

            Assert.All(record["plays"]!.AsArray(), p => Assert.InRange((int)p![0]!, 0, GameStatsRecord.MaxTurn));
        }

        output.WriteLine($"{records} records");
        Assert.True(records > 0);
    }

    /// <summary>
    /// A Ranked game of the kept logs (TASK-2026-11-26-pro-collection AC1) saved in a history twice, at Diamond 3 (star
    /// level 48) and at Diamond 5 (46), as memory reading gives the rank: both records carry the build its log wrote, only
    /// Diamond 3 the band "d4-1", and nothing else changes between them.
    /// </summary>
    [RealLogsFact(gameType: "GT_RANKED")]
    public void A_kept_ranked_game_sends_the_build_of_its_log_and_only_at_diamond_3_the_band()
    {
        var (game, builds) = KeptGames().FirstOrDefault(g => g.Game.Mode == "Ranked" && g.Builds.Count > 0);
        Assert.True(game is not null, "The kept logs hold a finished Ranked game, but the backfill gave none with a build number.");
        Directory.CreateDirectory(_dir);
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        var diamond3 = game with { Key = game.Key + "|d3", Rank = "Diamond 3", RankStarLevel = 48 };
        var diamond5 = game with { Key = game.Key + "|d5", Rank = "Diamond 5", RankStarLevel = 46 };
        Assert.True(history.Add(diamond3));
        Assert.True(history.Add(diamond5));

        JsonObject Record(GameRecord saved) => GameStatsRecord.Build(history.Game(saved.Key)!, new string('a', 32), null, null, CardDb.Empty, "0.30.0")!;
        var high = Record(diamond3);
        var low = Record(diamond5);
        output.WriteLine($"{game.StartedAt:g} {game.Result} build {game.Build}: {high.ToJsonString()}");

        var build = (int?)high["build"];
        Assert.NotNull(build);
        Assert.Contains(build.Value, builds);
        Assert.Equal(build, (int?)low["build"]);
        Assert.Equal(("ranked", "diamond", "d4-1"), ((string?)high["mode"], (string?)high["rank"], (string?)high["band"]));
        Assert.Equal(("ranked", "diamond"), ((string?)low["mode"], (string?)low["rank"]));
        Assert.Null(low["band"]);
        high.Remove("band");
        Assert.Equal(high.ToJsonString(), low.ToJsonString());
    }

    /// <summary>
    /// The Ranked test above is skipped, not failed, where the kept logs hold no finished Ranked game: the client keeps
    /// only its last sessions, and these can all be Battlegrounds or Arena. A Ranked game the client is still in does not
    /// count.
    /// </summary>
    [Fact]
    public void Kept_logs_without_a_finished_ranked_game_skip_the_ranked_test()
    {
        var fact = typeof(GameStatsRealLogTests).GetMethod(nameof(A_kept_ranked_game_sends_the_build_of_its_log_and_only_at_diamond_3_the_band))!
            .GetCustomAttribute<RealLogsFactAttribute>()!;
        Assert.Equal("GT_RANKED", fact.GameType);

        var log = Path.Combine(_dir, "Hearthstone_2026_11_26_20_00_00", "Power.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllLines(log, HistoryStoreBuildTests.Game(HistoryStoreBuildTests.LogBuild, "20:00:00", "GT_CASUAL")
            .Concat(HistoryStoreBuildTests.Game(HistoryStoreBuildTests.LogBuild, "20:30:00").SkipLast(4)));
        Assert.True(RealLogTests.HasFinishedGame(_dir, "GT_CASUAL"));
        Assert.False(RealLogTests.HasFinishedGame(_dir, "GT_RANKED"));

        File.AppendAllLines(log, HistoryStoreBuildTests.Game(HistoryStoreBuildTests.LogBuild, "21:00:00"));
        Assert.True(RealLogTests.HasFinishedGame(_dir, "GT_RANKED"));
    }
}

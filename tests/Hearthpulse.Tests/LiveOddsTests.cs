using System.IO.Compression;
using Hearthpulse.Bench;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;
using Microsoft.Data.Sqlite;

namespace Hearthpulse.Tests;

/// <summary>
/// The live odds path (TASK-2026-11-26 AC5): a Battlegrounds log fed to a <see cref="GameSession"/> in small batches with
/// a tick after each, as the log watcher does, and every published snapshot read as the overlay reads it
/// (<see cref="LiveOddsProbe"/>). Every fight with a minion attack shows odds while it is on screen and keeps them: in the
/// history row the Battlegrounds page's accuracy line reads once the game is over, in the session's combat list while it
/// is on; no line goes unread. Logs: the repository's real game (Fixtures/BattlegroundsGame) and the golden solo and Duos replays
/// (Fixtures/Replay/Golden). The golden logs carry one timestamp, so a batch is a few lines: in a real log the client's
/// replay of the fight keeps it on screen for seconds of batches. Bench <c>simlive</c> runs the same probe over kept logs.
/// </summary>
public sealed class LiveOddsTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(TestDataFolder.Scratch("hp-live-odds")).FullName;
    private readonly HearthstoneMemory _memory = new();

    public void Dispose()
    {
        _memory.Dispose();
        SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_folder);
    }

    [Fact]
    public void The_kept_real_game_shows_odds_for_its_fight()
    {
        using var gz = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BattlegroundsGame", "cards.json.gz")), CompressionMode.Decompress);
        var cards = CardDb.Parse("ruRU", gz);

        var (games, rows) = Replay(BattlegroundsFixtureTests.Lines(), cards, new DateTime(2026, 10, 23, 2, 1, 43));

        // Hero pick, two shop turns and the first fight, then the client was closed: no history row.
        var game = Assert.Single(games);
        Assert.False(game.Duos);
        var fight = Assert.Single(game.Fights);
        Assert.Equal((1, "Tie"), (fight.Turn, fight.Result));
        Assert.NotNull(fight.Shown);
        Assert.NotNull(fight.Recorded);
        Assert.Equal(0, game.ParseErrors);
        Assert.Empty(rows);
    }

    [Fact]
    public void The_golden_solo_game_shows_and_keeps_odds_for_every_fight()
    {
        var (games, rows) = Replay(Golden("battlegrounds-5th"), CardDb.Empty, new DateTime(2026, 11, 5, 12, 30, 0));

        var game = Assert.Single(games);
        Assert.False(game.Duos);
        Assert.True(game.HasRow);
        Assert.Equal(2, game.Fights.Count);
        Assert.All(game.Fights, f =>
        {
            Assert.NotNull(f.Shown);
            Assert.NotNull(f.Kept);
        });
        // The first fight is in a later view too; the game ends in the batch of the second (no view after the end).
        Assert.NotNull(game.Fights[0].Recorded);
        Assert.Equal(0, game.ParseErrors);
        Assert.Equal(2, Hearthpulse.Core.Stats.BattlegroundsStats.SimAccuracy(rows).Fights);
    }

    [Fact]
    public void The_golden_duos_game_shows_and_keeps_odds_for_its_fight()
    {
        var (games, _) = Replay(Golden("duos-partner"), CardDb.Empty, new DateTime(2026, 11, 5, 12, 30, 0));

        var game = Assert.Single(games);
        Assert.True(game.Duos);
        Assert.True(game.HasRow);
        var fight = Assert.Single(game.Fights);
        Assert.NotNull(fight.Shown);
        Assert.NotNull(fight.Kept);
        Assert.Equal(0, game.ParseErrors);
    }

    /// <summary>
    /// A golden replay's log with the line every real log has at a fight's start and these leave out: the opponent player's
    /// <c>BACON_CURRENT_COMBAT_PLAYER_ID</c> (both name Rival#4321, the lobby's PLAYER_ID 4). Without it the tracker
    /// records no combat (BattlegroundsTracker.EndCombat: a fight whose opponent was never named is not one).
    /// </summary>
    private static IEnumerable<string> Golden(string name)
    {
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Replay", "Golden", name + ".log")))
        {
            yield return line;
            if (line.Contains("tag=BOARD_VISUAL_STATE value=2", StringComparison.Ordinal))
            {
                yield return "D 12:30:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Rival#4321 tag=BACON_CURRENT_COMBAT_PLAYER_ID value=4 ";
            }
        }
    }

    /// <summary>
    /// The log through a session: batches of a few lines 200 ms of clock apart, a tick right after each (so every batch
    /// is published, and a construct a batch ends inside is completed by the next one, not flushed), then the quiet flush
    /// and the wait for the final place.
    /// </summary>
    private (IReadOnlyList<LiveGame> Games, IReadOnlyList<GameRecord> Rows) Replay(IEnumerable<string> lines, CardDb cards, DateTime start)
    {
        var history = new HistoryStore(Path.Combine(_folder, $"history-{Guid.NewGuid():N}.db"));
        var now = start.ToUniversalTime();
        var session = new GameSession(history, _memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            WarmUpSimulator = () => { },
            Cards = cards,
        };
        var probe = new LiveOddsProbe();
        session.SnapshotChanged += probe.Observe;
        session.GameFinished += probe.Finished;
        session.OnSessionStarted("Hearthstone_" + start.ToString("yyyy_MM_dd_HH_mm_ss", System.Globalization.CultureInfo.InvariantCulture), start, attachedMidSession: false);
        foreach (var batch in lines.Chunk(5))
        {
            now = now.AddMilliseconds(200);
            session.OnLines("Power.log", batch, catchingUp: false);
            session.OnTick();
        }

        foreach (var wait in new[] { 1, 60 })
        {
            now = now.AddSeconds(wait);
            session.OnTick();
        }

        var rows = history.Recent(10);
        return (probe.Games(rows), rows);
    }
}

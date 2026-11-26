using System.Text.Json;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Replay;

namespace Hearthpulse.Tests;

/// <summary>
/// Games watched in spectator mode are somebody else's (owner, 2026-11-26): no history row, match replay, fight capture,
/// event or source capture, live or imported. The client marks spectator mode with lines of their own around the games.
/// The marker lines here are synthetic: their text follows the log format as described ("Begin Spectating", "Start
/// Spectator" ... "End Spectator"); no kept real log has one (D:\Hearthstone\Logs, 2026-11-26), so the exact line is not
/// confirmed by a real log.
/// </summary>
public sealed class SpectatorTests : IDisposable
{
    private const string BeginSpectating = "D 12:29:00.0000000 ================== Begin Spectating 1st player ==================";
    private const string StartSpectator = "D 12:30:00.5000000 ================== Start Spectator Game ==================";
    private const string EndSpectator = "D 12:45:00.0000000 ================== End Spectator Mode ==================";

    private readonly SessionRig _rig = new("hp-spectator");

    public void Dispose() => _rig.Dispose();

    /// <summary>A short Battlegrounds game whose fights are captured: the lobby names each combat's opponent (as in CombatCaptureStoreTests).</summary>
    private static List<string> BattlegroundsGame() => SessionRig.At("12:30:00.0000000",
    [
        .. BattlegroundsReplayTests.Setup,
        BattlegroundsReplayTests.Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4),
        .. BattlegroundsReplayTests.Game5th().Skip(BattlegroundsReplayTests.Setup.Length),
    ]).ToList();

    [Fact]
    public async Task A_spectated_game_leaves_no_row_replay_capture_or_event()
    {
        // The same game played by us leaves all of them: what the marker takes away.
        using (var own = new SessionRig("hp-spectator-own"))
        {
            own.Play(BattlegroundsGame());
            Assert.Single(own.History.Recent());
            Assert.Single(own.Outcomes);
            Assert.Equal((1, 1), await own.SavedAsync());
            Assert.Contains(own.Frames, f => f.Record.Kind == "start");
        }

        _rig.Play([BeginSpectating, .. BattlegroundsGame()]);
        _rig.Tick(TimeSpan.FromMinutes(10));

        Assert.True(_rig.Session.Game!.IsSpectated);
        Assert.Empty(_rig.History.Recent());
        Assert.Empty(_rig.Outcomes);
        Assert.Equal((0, 0), await _rig.SavedAsync());
        Assert.Empty(_rig.Frames);
    }

    [Fact]
    public void Consecutive_spectated_games_stay_unrecorded_until_End_Spectator()
    {
        _rig.Play(
        [
            BeginSpectating,
            .. SessionRig.RankedWin("12:30:00.0000000"),
            .. SessionRig.RankedWin("12:40:00.0000000"),
            EndSpectator,
            .. SessionRig.RankedWin("12:50:00.0000000"),
        ]);

        var saved = Assert.Single(_rig.History.Recent());
        Assert.Equal(new DateTime(2026, 11, 26, 12, 50, 0), saved.StartedAt);
        Assert.Equal(saved.Key, Assert.Single(_rig.Outcomes).Key);
    }

    [Fact]
    public void A_marker_mid_game_marks_that_game()
    {
        // The marker comes after the game's CREATE_GAME, at turn 5.
        var game = SessionRig.RankedWin("12:30:00.0000000");
        _rig.Play([.. game.SkipLast(2), StartSpectator, .. game.TakeLast(2)]);

        Assert.True(_rig.Session.Game!.IsSpectated);
        Assert.Empty(_rig.History.Recent());
        Assert.Empty(_rig.Outcomes);
        // A source capture begun before the marker ends without the watched player's result.
        var end = SourceCaptureRecorderTests.Data(_rig.Frames.Last());
        Assert.Equal("end", _rig.Frames.Last().Record.Kind);
        Assert.Equal(JsonValueKind.Null, end.GetProperty("result").ValueKind);

        // A marker after our own result does not take our game: a Battlegrounds game we were out of, left to watch a
        // friend's, read when the app started.
        using var left = new SessionRig("hp-spectator-left");
        var bg = BattlegroundsGame();
        var lost = bg.FindIndex(l => l.Contains("tag=PLAYSTATE value=LOST", StringComparison.Ordinal));
        left.CatchUp([.. bg.Take(lost + 1), BeginSpectating]);
        left.Tick(TimeSpan.FromSeconds(2));

        Assert.False(left.Session.Game!.IsSpectated);
        Assert.Single(left.History.Recent());
        Assert.True(Assert.Single(left.Outcomes).Recovered);
    }

    [Fact]
    public void Backfill_skips_spectated_games()
    {
        var log = Path.Combine(_rig.Dir, "Power.log");
        File.WriteAllLines(log,
        [
            .. SessionRig.RankedWin("12:20:00.0000000"),
            BeginSpectating,
            .. SessionRig.RankedWin("12:30:00.0000000"),
            EndSpectator,
            .. SessionRig.RankedWin("12:50:00.0000000"),
        ]);
        var replays = new ReplayStore(_rig.Replays);

        var records = LogBackfill.Parse(log, SessionRig.SessionStart, CardDb.Empty, replays: replays);

        Assert.Equal([new TimeSpan(12, 20, 0), new TimeSpan(12, 50, 0)], records.Select(r => r.StartedAt.TimeOfDay));
        Assert.Equal(2, replays.Names().Count);
    }

    [Fact]
    public void Catch_up_from_the_last_game_replays_the_earlier_marker()
    {
        // Our own game, then a friend's game watched: the app starts after both and joins the log at the last game.
        var session = Path.Combine(_rig.Dir, SessionRig.Folder);
        Directory.CreateDirectory(session);
        var log = Path.Combine(session, "Power.log");
        File.WriteAllLines(log, [.. SessionRig.RankedWin("12:20:00.0000000"), BeginSpectating, .. SessionRig.RankedWin("12:30:00.0000000")]);
        var tail = new LogTail("Power.log");

        tail.Reset(session, TailStart.LastGame);
        var lines = tail.ReadNewLines();
        _rig.CatchUp(lines);

        Assert.Equal(BeginSpectating, lines[0]);
        Assert.EndsWith("CREATE_GAME", lines[1], StringComparison.Ordinal);
        Assert.Empty(tail.ReadNewLines());
        Assert.Empty(_rig.History.Recent());
        Assert.Empty(_rig.Outcomes);

        // Watching ended before the last game: that game is ours, found at start-up as before.
        File.WriteAllLines(log, [BeginSpectating, .. SessionRig.RankedWin("12:20:00.0000000"), EndSpectator, .. SessionRig.RankedWin("12:50:00.0000000")]);
        tail.Reset(session, TailStart.LastGame);
        var after = tail.ReadNewLines();
        _rig.Start();
        _rig.CatchUp(after);

        Assert.Equal(EndSpectator, after[0]);
        Assert.Equal(new TimeSpan(12, 50, 0), Assert.Single(_rig.History.Recent()).StartedAt.TimeOfDay);
        Assert.True(Assert.Single(_rig.Outcomes).Recovered);
    }

    [Fact]
    public void Source_capture_never_starts_while_spectating()
    {
        _rig.Play([BeginSpectating, .. SessionRig.RankedWin("12:30:00.0000000")]);
        Assert.Empty(_rig.Frames);

        // Our next game after watching is captured as before.
        _rig.Play([EndSpectator, .. SessionRig.RankedWin("12:50:00.0000000")]);
        Assert.Equal("start", _rig.Frames[0].Record.Kind);
        Assert.Equal("end", _rig.Frames[^1].Record.Kind);
    }

    [Fact]
    public void Spectat_inside_a_GameState_line_flags_nothing()
    {
        // Names in game lines (a player, a card in the client's PowerTaskList copy of a line) are never markers.
        string[] named =
        [
            "D 12:30:00.0000000 GameState.DebugPrintGame() - PlayerID=2, PlayerName=Begin Spectating",
            "D 12:30:00.0000000 PowerTaskList.DebugPrintPower() -     SHOW_ENTITY - Updating Entity=[entityName=Start Spectator id=99 zone=HAND zonePos=1 cardId= player=1] CardID=CORE_CS2_004",
        ];
        var game = SessionRig.RankedWin("12:30:00.0000000");
        _rig.Play([.. game.SkipLast(2), .. named, .. game.TakeLast(2)]);

        Assert.False(_rig.Session.Game!.IsSpectated);
        Assert.Single(_rig.History.Recent());
        Assert.Single(_rig.Outcomes);

        // Nor are they fed first when the app joins the log at the next game.
        var session = Path.Combine(_rig.Dir, SessionRig.Folder);
        Directory.CreateDirectory(session);
        File.WriteAllLines(Path.Combine(session, "Power.log"), [.. game.SkipLast(2), .. named, .. game.TakeLast(2), .. SessionRig.RankedWin("12:50:00.0000000")]);
        var tail = new LogTail("Power.log");
        tail.Reset(session, TailStart.LastGame);
        Assert.EndsWith("CREATE_GAME", tail.ReadNewLines()[0], StringComparison.Ordinal);
    }
}

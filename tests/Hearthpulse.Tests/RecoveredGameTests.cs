using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Overlay;
using Xunit.Abstractions;

namespace Hearthpulse.Tests;

/// <summary>
/// A game that ended before the app started is found when the app catches up with the log (from the last CREATE_GAME).
/// It used to be saved to History only: no profile event, no game record, no shared game. It is now announced once, when
/// it is first saved, as <see cref="GameOutcome.Recovered"/>: without the parts seen live, outside this session's results
/// and without a toast.
/// </summary>
public sealed class RecoveredGameTests(ITestOutputHelper output) : IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private readonly SessionRig _rig = new("hp-recovered");

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void A_game_finished_during_catch_up_is_announced_once_as_recovered()
    {
        _rig.CatchUp(SessionRig.RankedWin());
        _rig.Tick(TimeSpan.FromSeconds(5));
        _rig.Play(["D 12:31:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER"]);

        var saved = Assert.Single(_rig.History.Recent());
        var outcome = Assert.Single(_rig.Outcomes);
        Assert.True(outcome.Recovered);
        Assert.Equal(saved.Key, outcome.Key);
        Assert.Equal(("Ranked", "Win", "GT_RANKED", "FT_STANDARD"), (outcome.Mode, outcome.Result, outcome.GameType, outcome.Format));
        Assert.Null(outcome.Details);
        Assert.Null(outcome.OfferedHeroes);
    }

    [Fact]
    public void A_replayed_saved_game_is_not_announced_again()
    {
        // Played live: announced as a game of this session.
        _rig.Play(SessionRig.RankedWin());
        Assert.False(Assert.Single(_rig.Outcomes).Recovered);

        // The app starts again and joins the session at that game; then the watcher replays it (resync).
        _rig.Start();
        _rig.CatchUp(SessionRig.RankedWin());
        _rig.Session.OnResync();
        _rig.CatchUp(SessionRig.RankedWin());

        Assert.Single(_rig.Outcomes);
        Assert.Single(_rig.History.Recent());
    }

    [Fact]
    public void A_recovered_game_replacing_an_abandoned_row_is_announced()
    {
        // The client was closed mid-game while the app ran: the game is kept as abandoned, with no event.
        _rig.Play(SessionRig.At("12:30:00.0000000", ReplayRecorderTests.Setup));
        _rig.Session.OnSessionStarted("Hearthstone_2026_11_26_13_00_00", new DateTime(2026, 11, 26, 13, 0, 0), attachedMidSession: false);
        Assert.True(Assert.Single(_rig.History.Recent()).IsAbandoned);
        Assert.Empty(_rig.Outcomes);

        // The game went on in the same log and ended after the app closed; the next start finds its end.
        _rig.Start();
        _rig.CatchUp(SessionRig.RankedWin());

        var saved = Assert.Single(_rig.History.Recent());
        Assert.Equal("Win", saved.Result);
        var outcome = Assert.Single(_rig.Outcomes);
        Assert.True(outcome.Recovered);
        Assert.Equal(saved.Key, outcome.Key);
    }

    [Fact]
    public void A_recovered_game_leaves_the_session_tally_alone()
    {
        _rig.CatchUp(SessionRig.RankedWin());
        Assert.Equal((0, 0, 0), Tally());

        // A game played afterwards counts as before.
        _rig.Play(SessionRig.RankedWin("12:40:00.0000000"));
        Assert.Equal(2, _rig.History.Recent().Count);
        Assert.Equal([true, false], _rig.Outcomes.Select(o => o.Recovered));
        Assert.Equal((1, 1, 0), Tally());

        (int Games, int Wins, int Losses) Tally() => (_rig.Session.Current.Session.Games, _rig.Session.Current.Session.Wins, _rig.Session.Current.Session.Losses);
    }

    [Fact]
    public void A_bg_game_saved_by_the_place_wait_after_catch_up_is_recovered()
    {
        // The log read at start-up ends right after our PLAYSTATE: the game waits for the final place on later ticks,
        // with no line read live in between. It was over for us before the app started.
        var lines = SessionRig.At("12:30:00.0000000", BattlegroundsReplayTests.Game5th()).ToList();
        var lost = lines.FindIndex(l => l.Contains("tag=PLAYSTATE value=LOST", StringComparison.Ordinal));
        _rig.CatchUp(lines.Take(lost + 1));
        Assert.Empty(_rig.History.Recent());

        _rig.Tick(TimeSpan.FromSeconds(2));

        var saved = Assert.Single(_rig.History.Recent());
        var outcome = Assert.Single(_rig.Outcomes);
        Assert.True(outcome.Recovered);
        Assert.Equal(saved.Key, outcome.Key);
        Assert.Null(outcome.Details);
        Assert.Null(outcome.OfferedHeroes);
        Assert.Empty(_rig.Session.Current.Session.Placements);
    }

    [Fact]
    public void A_recovered_bg_record_comes_from_its_history_row()
    {
        var game = new GameRecord
        {
            Key = "20261126123000|Me#1234|GT_BATTLEGROUNDS",
            StartedAt = new DateTime(2026, 11, 26, 12, 30, 0, DateTimeKind.Utc),
            EndedAt = new DateTime(2026, 11, 26, 12, 50, 0, DateTimeKind.Utc),
            Mode = "Battlegrounds",
            GameType = "GT_BATTLEGROUNDS",
            Result = "Loss",
            Placement = 5,
            HeroCardId = "TB_BaconShop_HERO_50",
            TavernTier = 4,
            Races = "BEAST,MECHANICAL",
            AnomalyCardId = "BG27_Anomaly_100",
        };
        _rig.History.Add(game);
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolSecret = new string('5', 64) };
        using var http = new HttpClient(new NoNetwork());
        using var uploader = new GameStatsUploader(settings, http, _rig.History, () => null, () => CardDb.Empty, "0.18.0",
            new GameStatsQueues(Path.Combine(_rig.Dir, "game-stats")));
        var outcome = new GameOutcome("Battlegrounds", "Loss", 5, null, null, game.HeroCardId) { Key = game.Key, GameType = game.GameType };

        var recovered = uploader.Build(outcome with { Recovered = true })!["bg"]!.AsObject();
        var live = uploader.Build(outcome)!["bg"]!.AsObject();

        // Without the live view there is no hero offer; the lobby and the tier are the row's.
        Assert.False(recovered.ContainsKey("offered"));
        Assert.Equal("TB_BaconShop_HERO_50", (string?)recovered["picked"]);
        Assert.Equal(4, (int)recovered["tier"]!);
        Assert.Equal(["BEAST", "MECHANICAL"], recovered["tribes"]!.AsArray().Select(t => (string)t!));
        Assert.Equal("BG27_Anomaly_100", (string?)recovered["anomaly"]);
        Assert.True(live.ContainsKey("offered"));
        Assert.False(live.ContainsKey("tier"));
    }

    [RealLogsFact]
    public void The_last_game_of_each_kept_log_is_recovered_once()
    {
        var logs = 0;
        foreach (var dir in Directory.EnumerateDirectories(RealLogTests.LogRoot, "Hearthstone_*"))
        {
            var log = Path.Combine(dir, "Power.log");
            if (HearthstoneInstall.SessionStart(dir) is not { } start || !File.Exists(log))
            {
                continue;
            }

            // The app starts after the log's last game, and once more after that.
            using var rig = new SessionRig("hp-recovered-real", Path.GetFileName(dir), start);
            rig.CatchUpFromLastGame(dir);
            rig.Tick(TimeSpan.FromSeconds(3));
            rig.Start();
            rig.CatchUpFromLastGame(dir);
            rig.Tick(TimeSpan.FromSeconds(3));

            // The same game imported from the whole log: a finished one is raised once as recovered, a left one never.
            var last = rig.Session.Game?.StartedAt;
            var imported = LogBackfill.Parse(log, start, CardDb.Empty).SingleOrDefault(r => r.StartedAt == last);
            if (imported is { IsAbandoned: false })
            {
                var outcome = Assert.Single(rig.Outcomes);
                Assert.True(outcome.Recovered);
                Assert.Equal(imported.Key, outcome.Key);
            }
            else
            {
                Assert.Empty(rig.Outcomes);
            }

            _output.WriteLine($"{Path.GetFileName(dir)}: last game {imported?.Mode ?? "(none)"} {imported?.Result}, raised {rig.Outcomes.Count} recovered");
            logs++;
        }

        Assert.True(logs > 0);
    }

    [Fact]
    public void A_recovered_game_shows_no_toast()
    {
        var overlay = new OverlayViewModel();
        var outcome = new GameOutcome("Ranked", "Win", 0, null, null, "HERO_09") { Key = "k", Recovered = true };

        overlay.ShowOutcome(outcome);
        Assert.False(overlay.ShowToast);

        overlay.ShowOutcome(outcome with { Recovered = false });
        Assert.True(overlay.ShowToast);
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Building a game record must not touch the network.");
    }
}

/// <summary>
/// A <see cref="GameSession"/> on a scratch folder of its own (history, replays, fight captures) with a clock the test
/// moves; it collects every announced game and every source capture frame. <see cref="Start"/> is the app starting again
/// on the same history.
/// </summary>
internal sealed class SessionRig : IDisposable
{
    public const string Folder = "Hearthstone_2026_11_26_12_00_00";
    public static readonly DateTime SessionStart = new(2026, 11, 26, 12, 0, 0);

    private readonly HearthstoneMemory _memory = new();
    private readonly string _folder;
    private readonly DateTime _start;

    public SessionRig(string name, string folder = Folder, DateTime? start = null)
    {
        _folder = folder;
        _start = start ?? SessionStart;
        Dir = TestDataFolder.Scratch(name);
        Directory.CreateDirectory(Dir);
        History = new HistoryStore(Path.Combine(Dir, "history.db"));
        Session = Start();
    }

    public string Dir { get; }

    public HistoryStore History { get; }

    public GameSession Session { get; private set; }

    public DateTime Now { get; set; } = new(2026, 11, 26, 11, 0, 0, DateTimeKind.Utc);

    public List<GameOutcome> Outcomes { get; } = [];

    public List<SourceCaptureFrame> Frames { get; } = [];

    public string Replays => Path.Combine(Dir, "replays");

    public string Captures => Path.Combine(Dir, "captures");

    /// <summary>A new session on the same history, joined to the log session at its last game.</summary>
    public GameSession Start()
    {
        var session = new GameSession(History, _memory)
        {
            Clock = () => Now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Replays = new ReplayStore(Replays),
            CombatCaptures = new CombatCaptureStore(Captures),
        };
        session.GameFinished += Outcomes.Add;
        session.SourceCaptureRecorded += Frames.Add;
        session.SetSourceCaptureEnabled(true);
        session.OnSessionStarted(_folder, _start, attachedMidSession: true);
        Session = session;
        return session;
    }

    /// <summary>What the log watcher does when the app starts: the log from its last game (<see cref="TailStart.LastGame"/>), caught up.</summary>
    public void CatchUpFromLastGame(string sessionDir)
    {
        var tail = new LogTail("Power.log");
        tail.Reset(sessionDir, TailStart.LastGame);
        List<string> lines;
        while ((lines = tail.ReadNewLines()).Count > 0)
        {
            CatchUp(lines);
        }
    }

    /// <summary>Lines read when the app starts (catching up), then a tick.</summary>
    public void CatchUp(IEnumerable<string> lines)
    {
        Session.OnLines("Power.log", lines.ToList(), catchingUp: true);
        Tick();
    }

    /// <summary>Lines read live, then a tick.</summary>
    public void Play(IEnumerable<string> lines)
    {
        Session.OnLines("Power.log", lines.ToList(), catchingUp: false);
        Tick();
    }

    public void Tick(TimeSpan? after = null)
    {
        Now += after ?? TimeSpan.FromMilliseconds(100);
        Session.OnTick();
    }

    /// <summary>The saved match replays and fight capture files, once the session's last save is done.</summary>
    public async Task<(int Replays, int Captures)> SavedAsync()
    {
        await Session.CombatCapturesSaved.WaitAsync(TimeSpan.FromSeconds(30));
        return (Count(Replays, "*.hpr"), Count(Captures, "*" + CombatCaptureStore.Extension));

        static int Count(string folder, string pattern) => Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Length : 0;
    }

    public void Dispose()
    {
        _memory.Dispose();
        TestDataFolder.TryDelete(Dir);
    }

    /// <summary>GameState lines of <paramref name="bodies"/> written at <paramref name="time"/>.</summary>
    public static IEnumerable<string> At(string time, IEnumerable<string> bodies) => bodies.Select(b => $"D {time} GameState." + b);

    /// <summary>A Ranked game the local player won, from its CREATE_GAME to its end (<see cref="ReplayRecorderTests.Setup"/>).</summary>
    public static List<string> RankedWin(string time = "12:30:00.0000000") =>
        At(time,
        [
            .. ReplayRecorderTests.Setup,
            "DebugPrintPower() - TAG_CHANGE Entity=velox#21276 tag=PLAYSTATE value=WON ",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
        ]).ToList();
}

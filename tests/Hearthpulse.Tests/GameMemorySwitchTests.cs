using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC7, the session side of "Read data from the game client" (<see cref="AppSettings.ReadGameMemory"/>):
/// on by default and in a settings file written before it existed; off, the memory reader gets no request and lets go
/// of the game, the card under the mouse is not wanted, what is known of the collection stays as saved data, and the
/// tracking from the logs goes on. The pages' side: <see cref="GameMemoryPagesTests"/>.
/// </summary>
public sealed class GameMemorySwitchTests
{
    private static readonly DateTime Noon = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CardDb HoverCards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"HERO_09","dbfId":813,"name":"Anduin Wrynn","type":"HERO","cardClass":"PRIEST"},
          {"id":"HERO_08","dbfId":637,"name":"Jaina Proudmoore","type":"HERO","cardClass":"MAGE"},
          {"id":"GEN_1","dbfId":10,"name":"Muster for Battle","type":"SPELL","cost":3,"set":"TGT","collectible":true},
          {"id":"GEN_1t","dbfId":11,"name":"Silver Hand Recruit","type":"MINION","cost":1,"set":"TGT"},
          {"id":"GEN_1t2","dbfId":12,"name":"Light's Justice","type":"WEAPON","cost":1,"set":"TGT"}
        ]
        """u8.ToArray()));

    // GEN_1 Discovers a Beast: a card with a pool, the kind of card whose hover is read (related cards alone are not,
    // TASK-2026-11-26 P-9).
    private static readonly CardPoolCatalog HoverCatalog = new(
    [
        new PoolCard("GEN_1", 0, "Muster for Battle", "Discover a Beast.", "SPELL", "COMMON", 1, null, null, ["NEUTRAL"], [], null, [], [], "X", true),
        new PoolCard("BEAST_1", 0, "Wolf", "A wolf.", "MINION", "COMMON", 1, 1, 1, ["NEUTRAL"], ["BEAST"], null, [], [], "X", true),
    ], "test", Noon);

    [Fact]
    public void Reading_the_client_is_on_by_default_and_a_settings_file_without_it_keeps_it_on()
    {
        Assert.True(new AppSettings().ReadGameMemory);
        Assert.True(AppSettings.FromJson("{}").ReadGameMemory);

        // A settings.json written before the switch existed, read from disk as the app reads it at start.
        var path = TestDataFolder.Scratch("hp-game-memory") + ".json";
        try
        {
            File.WriteAllText(path, """{"ShowCardPool":false,"SendCrashReports":true,"SettingsTab":"Data"}""");
            var old = AppSettings.Load(path);
            Assert.True(old.ReadGameMemory);
            Assert.False(old.ShowCardPool);
        }
        finally
        {
            TestDataFolder.TryDelete(path);
        }

        // Off is kept through a save and a read.
        var off = AppSettings.FromJson("""{"ReadGameMemory":false}""");
        Assert.False(off.ReadGameMemory);
        Assert.False(AppSettings.FromJson(off.ToJson()).ReadGameMemory);
    }

    [Fact]
    public void A_reset_or_a_settings_file_leaves_the_choice_as_the_player_set_it()
    {
        var settings = new AppSettings { ReadGameMemory = false };
        SettingsGroups.ResetAll(settings);
        Assert.False(settings.ReadGameMemory);

        // Not carried by an exported file, and not taken from one either: it is this computer's choice.
        Assert.DoesNotContain(nameof(AppSettings.ReadGameMemory), SettingsGroups.Export(settings), StringComparison.Ordinal);
        var target = new AppSettings();
        Assert.True(SettingsGroups.Import(target, """{"hearthpulseSettings":1,"settings":{"ReadGameMemory":false,"Theme":"Grove"}}"""));
        Assert.Equal("Grove", target.Theme);
        Assert.True(target.ReadGameMemory);
    }

    [Fact]
    public void Turned_off_the_memory_reader_gets_no_request_and_the_log_tracking_goes_on()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        // The session may read memory here: only the player's setting stands between it and the client. Its reader
        // starts no thread even if a request came, so a regression never reads a game running on this machine.
        var session = ManualSession(memory, () => now);
        session.ApplySettings(new AppSettings { ReadGameMemory = false });
        Assert.False(session.GameMemoryEnabled);
        Assert.True(memory.Blocked);

        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        session.OnLines("Power.log", BattlegroundsFixtureTests.Lines().ToList(), catchingUp: false);
        for (var tick = 0; tick < 5; tick++)
        {
            now = now.AddSeconds(3);
            session.OnTick();
        }

        // The game is tracked from Power.log as always.
        Assert.NotNull(session.Current.Battlegrounds);
        Assert.True(session.Current.HearthstoneRunning);
        // Not one request was made; the game was never opened.
        Assert.Equal(0, session.MemoryReader.Posted);
        Assert.Null(session.MemoryReader.Pending);
        Assert.False(memory.IsAttached);
        Assert.False(session.CanReadClient);
        Assert.False(session.Current.MemoryAttached);
    }

    [Fact]
    public void Turned_off_the_rating_read_for_now_is_forgotten_and_a_later_game_is_recorded_without_one()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var history = HistoryStore.InMemory();
        var session = ManualSession(memory, () => now, history);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        void Poll(IEnumerable<string> lines)
        {
            session.OnLines("Power.log", lines.ToList(), catchingUp: false);
            now = now.AddMilliseconds(100);
            session.OnTick();
        }

        // Reading on: the client gives a rating of 6,000, and a game starts with it.
        session.OnTick();
        session.MemoryReader.Deliver(Reading(session.BuildMemoryRequest(), rating: new BattlegroundsRating(6000, 0)));
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Equal(6000, session.Current.Rating?.Solo);
        var first = BgGame("12:30:00");
        var lost = first.FindIndex(l => l.Contains("tag=PLAYSTATE value=LOST", StringComparison.Ordinal));
        Poll(first.Take(lost));

        // Off in the middle of it: the rating read for now goes at once, and the game's end asks the client for nothing
        // (no rating after it is waited for).
        session.SetGameMemoryEnabled(false);
        var posted = session.MemoryReader.Posted;
        Poll(first.Skip(lost));
        now = now.AddSeconds(3);
        session.OnTick();
        Assert.Null(session.Current.Rating);
        Assert.Equal(posted, session.MemoryReader.Posted);
        Assert.Null(session.MemoryReader.Pending);
        // That game began while reading was on: it keeps the rating read at its start.
        var kept = Assert.Single(history.Recent());
        Assert.Equal(6000, kept.RatingBefore);

        // A game started after the switch has no rating at all, and no earlier one stands in for it.
        Poll(BgGame("13:30:00"));
        now = now.AddSeconds(3);
        session.OnTick();
        var later = Assert.Single(history.Recent(), g => g.Key != kept.Key);
        Assert.Null(later.RatingBefore);
        Assert.Null(later.RatingAfter);
        Assert.Empty(BattlegroundsStats.RatingTimeline(history.Recent()));
        Assert.Equal(posted, session.MemoryReader.Posted);
    }

    [Fact]
    public void Turned_off_the_tavern_buttons_cost_read_for_now_is_forgotten()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var session = ManualSession(memory, () => now);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        session.OnLines("Power.log", BattlegroundsFixtureTests.Lines().ToList(), catchingUp: false);
        now = now.AddSeconds(1);
        session.OnTick();
        var inGame = session.BuildMemoryRequest();
        Assert.True(inGame is { InGame: true, Battlegrounds: true });

        session.MemoryReader.Deliver(Reading(inGame, buttons: new BattlegroundsButtonCosts(0, 1, 5, null)));
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Equal(5, session.Current.Battlegrounds!.TavernUpgradeCost);

        // Off: the advisor works out the upgrade's cost from the log again, not from the last price read.
        session.SetGameMemoryEnabled(false);
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.NotNull(session.Current.Battlegrounds);
        Assert.Null(session.Current.Battlegrounds!.TavernUpgradeCost);
        Assert.Null(session.Current.Battlegrounds.ButtonCosts);
    }

    [Fact]
    public void A_reading_taken_before_the_switch_is_never_applied_after_it()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var session = ManualSession(memory, () => now);
        session.SetBgOpponentMmrEnabled(true);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        session.OnLines("Power.log", BattlegroundsFixtureTests.Lines().ToList(), catchingUp: false);
        now = now.AddSeconds(1);
        session.OnTick();
        var opponent = session.Current.Battlegrounds!.Opponents[0].PlayerId;
        var names = new Dictionary<int, string> { [opponent] = "Alpha" };
        var asked = session.BuildMemoryRequest() with { WantLobbyNames = true };
        string? Name() => session.Current.Battlegrounds!.Opponents.Single(o => o.PlayerId == opponent).PlayerName;

        // Reading on: a finished scan is applied.
        session.MemoryReader.Deliver(Reading(asked, rating: new BattlegroundsRating(7450, 0), lobby: names));
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Equal("Alpha", Name());
        Assert.Equal(7450, session.Current.Rating?.Solo);

        // A scan under way at the switch finishes after it: dropped, the names and the rating stay forgotten.
        session.MemoryReader.Deliver(Reading(asked, rating: new BattlegroundsRating(7450, 0), lobby: names));
        session.SetGameMemoryEnabled(false);
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Null(Name());
        Assert.Null(session.Current.Rating);
        Assert.All(session.Current.Battlegrounds!.Opponents, o => Assert.Equal(BgMmrStatus.Unknown, o.Mmr!.Status));

        // Nor once reading is back on: that scan was asked for before the switch.
        session.MemoryReader.Deliver(Reading(asked, rating: new BattlegroundsRating(7450, 0), lobby: names));
        session.SetGameMemoryEnabled(true);
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Null(Name());
        Assert.Null(session.Current.Rating);
    }

    /// <summary>
    /// QA m6: off and on again, the scan of what the session asks for after the switch is applied as before. Only a
    /// reading asked for before the switch is dropped: the request carries the switch's count (<see cref="MemoryRequest.Epoch"/>).
    /// </summary>
    [Fact]
    public void Turned_off_and_on_again_the_next_reading_is_applied()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var session = ManualSession(memory, () => now);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        session.OnTick();
        var first = session.MemoryReader.Pending;
        Assert.NotNull(first);
        session.MemoryReader.Deliver(Reading(first, rating: new BattlegroundsRating(6000, 0)));
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Equal(6000, session.Current.Rating?.Solo);

        session.SetGameMemoryEnabled(false);
        session.SetGameMemoryEnabled(true);
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Null(session.Current.Rating);

        // The request the session posted after the switch, scanned as the reader scans it: its reading is current.
        var asked = session.MemoryReader.Pending;
        Assert.NotNull(asked);
        session.MemoryReader.Deliver(Reading(asked, rating: new BattlegroundsRating(6150, 0)));
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.Equal(6150, session.Current.Rating?.Solo);
    }

    [Fact]
    public void Turned_off_nothing_attaches_and_no_value_is_read_not_even_under_the_mouse()
    {
        var source = new CountingSource(HoveredTileImage("TB_BaconShop_HERO_01"));
        using var memory = ClientImage.Attach(source);
        var session = Session(memory, () => Noon, readsMemory: true);
        session.SetGameMemoryEnabled(false);
        Assert.True(memory.Blocked);

        // A read that passed its check just before the switch, and a scan under way: from their next value on they read
        // nothing, and nothing attaches.
        var reads = source.Reads;
        Assert.Null(memory.GetHoveredLeaderboardHero());
        Assert.Null(memory.GetHoveredCardId());
        Assert.Null(memory.GetBattlegroundsRating());
        Assert.Null(memory.GetBattlegroundsLobbyNames());
        Assert.False(memory.EnsureAttached());
        Assert.Equal(reads, source.Reads);

        // The reader let go between its scans (MemoryPoller.Release); on again, a read under the mouse never attaches by
        // itself: only the reader does. Checked where no game runs, so a regression cannot open a live one. With no game
        // an attach fails anyway; trying at all leaves its reason in Status ("Hearthstone not running", off Windows "only
        // available on Windows"), so Status shows whether one was tried.
        memory.Detach();
        session.SetGameMemoryEnabled(true);
        Assert.False(memory.Blocked);
        Assert.Equal("Detached", memory.Status);
        if (System.Diagnostics.Process.GetProcessesByName("Hearthstone").Length == 0)
        {
            Assert.Null(memory.GetHoveredLeaderboardHero());
            Assert.Null(memory.GetHoveredCardId());
            Assert.False(memory.IsAttached);
            Assert.Equal("Detached", memory.Status);
            Assert.Equal(reads, source.Reads);
        }
    }

    [Fact]
    public async Task Turned_on_the_reader_is_asked_and_turned_off_it_lets_go_of_the_game()
    {
        // A made-up client image, never a game running on this machine: the reader thread scans it.
        using var memory = ClientImage.Attach(new ClientImage().Build());
        var now = Noon;
        var session = Session(memory, () => now, readsMemory: true);
        session.OnTick();
        Assert.True(session.MemoryReader.Started);
        Assert.NotNull(session.MemoryReader.Pending);
        Assert.True(session.CanReadClient);

        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.MemoryReader.Released += () => released.TrySetResult();
        session.SetGameMemoryEnabled(false);

        // At once: no request, and nothing may read the client any more.
        Assert.Null(session.MemoryReader.Pending);
        Assert.False(session.CanReadClient);
        // The reader closes its handle between scans (the wait only bounds a hang).
        await released.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(memory.IsAttached);

        now = now.AddSeconds(3);
        session.OnTick();
        Assert.Null(session.MemoryReader.Pending);
        Assert.False(session.Current.MemoryAttached);
    }

    [Fact]
    public void Turning_reading_off_keeps_the_collection_cosmetics_and_achievements_as_saved_and_on_again_reads_each_once()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var session = Session(memory, () => now, readsMemory: false);
        var collection = session.CollectionWatch;
        var cosmetics = session.CosmeticsWatch;
        var achievements = session.AchievementsWatch;
        session.OnTick();
        var tickets = (collection.Ticket(now, inGame: false), cosmetics.Ticket(now, inGame: false), achievements.Ticket(now, inGame: false));
        Assert.DoesNotContain(0, new[] { tickets.Item1, tickets.Item2, tickets.Item3 });
        collection.Apply(new CardCollection(new Dictionary<string, OwnedCopies>(StringComparer.Ordinal), 100, now), tickets.Item1, now);
        cosmetics.Apply(Cosmetics(now), tickets.Item2, now);
        achievements.Apply(AchievementsScene.Build(now, confirmed: false), tickets.Item3, now);
        Assert.True(session.Collection!.Confirmed);
        Assert.True(session.Cosmetics!.Confirmed);
        Assert.True(session.Achievements!.Confirmed);

        // Off: what is known stays, as saved data, and no read is wanted.
        session.SetGameMemoryEnabled(false);
        now = now.AddMinutes(1);
        session.OnTick();
        Assert.False(session.Collection!.Confirmed);
        Assert.False(session.Cosmetics!.Confirmed);
        Assert.False(session.Achievements!.Confirmed);
        Assert.Equal((0, 0, 0), (collection.Ticket(now, inGame: false), cosmetics.Ticket(now, inGame: false), achievements.Ticket(now, inGame: false)));

        // On again with the client running: one read of each, as at the client's start.
        session.SetGameMemoryEnabled(true);
        now = now.AddMinutes(1);
        session.OnTick();
        Assert.NotEqual(0, collection.Ticket(now, inGame: false));
        Assert.NotEqual(0, cosmetics.Ticket(now, inGame: false));
        Assert.NotEqual(0, achievements.Ticket(now, inGame: false));
    }

    [Fact]
    public void Turned_off_the_card_under_the_mouse_is_not_wanted_and_the_game_is_still_tracked()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var session = Session(memory, () => now, readsMemory: false);
        session.Cards = HoverCards;
        session.SetCardPoolCatalog(HoverCatalog);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", Noon, attachedMidSession: false);
        session.OnLines("Power.log", ConstructedGame("GEN_1"), catchingUp: false);
        now = now.AddSeconds(1);
        session.OnTick();

        Assert.NotNull(session.Current.Constructed);
        // A card with a pool in your hand and Ctrl held (the card pool's default hover mode): its hover is read from the
        // game where a reader exists (Windows).
        session.SetCardPoolControl(true);
        Assert.Equal(HearthstoneMemory.IsSupported, session.HoverReadsWanted);

        session.SetGameMemoryEnabled(false);
        Assert.False(session.HoverReadsWanted);
        now = now.AddSeconds(1);
        session.OnTick();
        Assert.False(session.HoverReadsWanted);
        Assert.NotNull(session.Current.Constructed);
    }

    [Fact]
    public void Turned_off_the_leaderboard_tile_under_the_mouse_is_not_read()
    {
        // The overlay's hover over the Battlegrounds leaderboard reads the game outside the memory reader: both hosts ask
        // the session for it. A made-up client with a hovered tile; no tick, so no reader thread either.
        var source = new CountingSource(HoveredTileImage("TB_BaconShop_HERO_01"));
        using var memory = ClientImage.Attach(source);
        var session = Session(memory, () => Noon, readsMemory: true);
        Assert.Equal("TB_BaconShop_HERO_01", session.HoveredLeaderboardHero());

        session.SetGameMemoryEnabled(false);
        var reads = source.Reads;
        Assert.Null(session.HoveredLeaderboardHero());
        Assert.False(session.CanReadClient);
        Assert.Equal(reads, source.Reads);

        session.SetGameMemoryEnabled(true);
        Assert.Equal("TB_BaconShop_HERO_01", session.HoveredLeaderboardHero());
    }

    /// <summary>A client whose Battlegrounds leaderboard has the tile of <paramref name="heroCardId"/> under the mouse.</summary>
    private static MemoryImage HoveredTileImage(string heroCardId)
    {
        const byte Boolean = 0x02, String = 0x0E, Class = 0x12;
        var b = new MonoImageBuilder();
        var manager = b.Class("PlayerLeaderboardManager",
            ("s_instance", 0, true, Class), ("m_isMousedOver", 16, false, Boolean), ("m_currentlyMousedOverTile", 24, false, Class));
        var tileClass = b.Class("PlayerLeaderboardCard", ("m_playerHeroEntity", 16, false, Class));
        var entity = b.Class("Entity", ("m_cardIdInternal", 16, false, String));
        var hero = b.Object(entity);
        b.Ptr(hero + 16, b.MonoString(heroCardId));
        var tile = b.Object(tileClass);
        b.Ptr(tile + 16, hero);
        var instance = b.Object(manager);
        b.Bool(instance + 16, true);
        b.Ptr(instance + 24, tile);
        b.Ptr(b.Statics(manager), instance);
        return b.Build();
    }

    /// <summary>A memory source that counts the reads made through it.</summary>
    private sealed class CountingSource(IMemorySource inner) : IMemorySource
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public bool HasExited => inner.HasExited;

        public ulong FindModuleBase(string moduleName) => inner.FindModuleBase(moduleName);

        public bool TryRead(ulong address, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            return inner.TryRead(address, buffer);
        }
    }

    private static GameSession Session(HearthstoneMemory memory, Func<DateTime> clock, bool readsMemory, HistoryStore? history = null) =>
        new(history ?? HistoryStore.InMemory(), memory)
        {
            Clock = clock,
            IsHearthstoneRunning = () => true,
            ReadsMemory = readsMemory,
            CollectionWatch = new CollectionWatch(null),
            CosmeticsWatch = new CosmeticsWatch(null),
            AchievementsWatch = new AchievementsWatch(null),
            Trimmer = new MemoryTrimmer(() => { }),
        };

    /// <summary>
    /// A session that may read memory, whose reader starts no thread: its requests are only counted, and readings come in
    /// by hand (<see cref="MemoryPoller.Deliver"/>) as a finished scan leaves them. No game on this machine is ever read.
    /// </summary>
    private static GameSession ManualSession(HearthstoneMemory memory, Func<DateTime> clock, HistoryStore? history = null)
    {
        var session = Session(memory, clock, readsMemory: true, history);
        session.MemoryReader.Manual = true;
        return session;
    }

    /// <summary>What a scan of <paramref name="asked"/> found: the values given, nothing else.</summary>
    private static MemoryReading Reading(MemoryRequest asked, BattlegroundsRating? rating = null, BattlegroundsButtonCosts? buttons = null,
        IReadOnlyDictionary<int, string>? lobby = null) => new(asked, rating, null, null, buttons, null, LobbyNames: lobby);

    /// <summary>A whole Battlegrounds game (5th place) whose lines are dated <paramref name="time"/>.</summary>
    private static List<string> BgGame(string time) =>
        [.. BattlegroundsReplayTests.Game5th().Select(body => $"D {time}.0000000 GameState." + body)];

    private static CosmeticsCollection Cosmetics(DateTime readAt) => new(
        [new CosmeticCardBack(0, "Classic", string.Empty, CardBackCategory.Base, 0)],
        new HashSet<int> { 0 },
        new HashSet<int>(),
        [],
        new HashSet<int>(),
        new HashSet<int>(),
        readAt);

    /// <summary>A ranked Standard game in the client's line shapes: you are player 1 with <paramref name="handCard"/> in hand.</summary>
    private static List<string> ConstructedGame(string handCard) =>
    [
        .. new[]
        {
            "DebugPrintPower() - CREATE_GAME",
            "DebugPrintPower() -     GameEntity EntityID=1",
            "DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "DebugPrintPower() -     Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=11]",
            "DebugPrintPower() -         tag=CONTROLLER value=1",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=20",
            "DebugPrintPower() -     Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=22]",
            "DebugPrintPower() -         tag=CONTROLLER value=2",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=30",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=20 CardID=HERO_09",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            "DebugPrintPower() -     tag=CONTROLLER value=1",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=30 CardID=HERO_08",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            "DebugPrintPower() -     tag=CONTROLLER value=2",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintGame() - GameType=GT_RANKED",
            "DebugPrintGame() - FormatType=FT_STANDARD",
            "DebugPrintGame() - PlayerID=1, PlayerName=Tester",
            "DebugPrintGame() - PlayerID=2, PlayerName=UNKNOWN HUMAN PLAYER",
            $"DebugPrintPower() - FULL_ENTITY - Creating ID=99 CardID={handCard}",
            "DebugPrintPower() -     tag=ZONE value=HAND",
            "DebugPrintPower() -     tag=CONTROLLER value=1",
            "DebugPrintPower() -     tag=CARDTYPE value=SPELL",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=1 ",
        }.Select(body => "D 12:30:00.0000000 GameState." + body),
    ];
}

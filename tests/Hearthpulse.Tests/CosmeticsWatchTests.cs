using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// When the card backs and coins are read and believed (<see cref="CosmeticsWatch"/>, the rules of
/// <see cref="ClientReadWatch{T}"/>, docs/collection-cosmetics.md): once per session of the client, again after a game
/// or a closed collection or pack screen at most once a minute, never in a game, retries 10 s… 1 min; confirmed only
/// for the running client; one long read per memory scan; scenes replayed from the log are no visits.
/// </summary>
public sealed class CosmeticsWatchTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = TestDataFolder.Scratch("hp-cosmetics-watch");

    public CosmeticsWatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private string File_ => Path.Combine(_dir, "cosmetics.json");

    private static CosmeticsCollection Sample(int owned = 1, DateTime? readAt = null) => new(
        [new CosmeticCardBack(0, "Classic", string.Empty, CardBackCategory.Base, 0), new CosmeticCardBack(5, "Other", string.Empty, CardBackCategory.Seasonal, 1)],
        owned == 1 ? new HashSet<int> { 0 } : new HashSet<int> { 0, 5 },
        new HashSet<int>(),
        [],
        new HashSet<int>(),
        new HashSet<int>(),
        readAt ?? Noon);

    // ------------------------------------------------------------------------------------------- when it is read

    [Fact]
    public void A_read_is_wanted_once_per_client_session_and_never_in_a_game()
    {
        var watch = new CosmeticsWatch(null);
        Assert.Equal(0, watch.Ticket(Noon, inGame: false));

        watch.ClientStarted();
        Assert.Equal(0, watch.Ticket(Noon, inGame: true));
        var ticket = watch.Ticket(Noon, inGame: false);
        Assert.NotEqual(0, ticket);

        watch.Apply(Sample(), ticket, Noon);
        Assert.Equal(0, watch.Ticket(Noon.AddHours(3), inGame: false));
        Assert.True(watch.Current!.Confirmed);

        // Closed: nothing is read; started again: one read.
        watch.ClientStopped();
        Assert.Equal(0, watch.Ticket(Noon.AddHours(3), inGame: false));
        watch.ClientStarted();
        Assert.NotEqual(0, watch.Ticket(Noon.AddHours(3), inGame: false));
    }

    [Theory]
    [InlineData("GAMEPLAY", "HUB", true)]
    [InlineData("GAMEPLAY", "BACON", true)]
    [InlineData("COLLECTIONMANAGER", "HUB", true)]
    [InlineData("PACKOPENING", "HUB", true)]
    [InlineData("HUB", "LOGIN", true)]
    [InlineData("HUB", "COLLECTIONMANAGER", false)]
    [InlineData("HUB", "PACKOPENING", false)]
    [InlineData("HUB", "GAMEPLAY", false)]
    [InlineData("HUB", "TOURNAMENT", false)]
    [InlineData("GAMEPLAY", "GAMEPLAY", false)]
    public void A_game_that_ended_or_a_closed_collection_or_pack_screen_asks_for_a_read(string from, string to, bool wanted)
    {
        var watch = new CosmeticsWatch(null);

        watch.SceneChanged(from, to);

        Assert.Equal(wanted, watch.Ticket(Noon, inGame: false) != 0);
    }

    [Fact]
    public void Reads_after_games_are_no_more_frequent_than_once_a_minute()
    {
        var watch = new CosmeticsWatch(null);
        watch.ClientStarted();
        watch.Apply(Sample(), watch.Ticket(Noon, inGame: false), Noon);

        watch.SceneChanged("GAMEPLAY", "HUB");
        Assert.Equal(TimeSpan.FromSeconds(60), ClientReadWatch<CosmeticsCollection>.MinInterval);
        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(59), inGame: false));
        Assert.NotEqual(0, watch.Ticket(Noon.AddSeconds(60), inGame: false));
        // And still not while the next game is on.
        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(90), inGame: true));
    }

    [Fact]
    public void A_read_that_gave_nothing_is_tried_again_after_10_20_and_at_most_60_seconds()
    {
        var watch = new CosmeticsWatch(null);
        watch.ClientStarted();
        var ticket = watch.Ticket(Noon, inGame: false);

        watch.Apply(null, ticket, Noon);
        Assert.Null(watch.Current);
        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(9), inGame: false));
        Assert.Equal(ticket, watch.Ticket(Noon.AddSeconds(10), inGame: false));
        watch.Apply(null, ticket, Noon);
        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(19), inGame: false));
        Assert.Equal(ticket, watch.Ticket(Noon.AddSeconds(20), inGame: false));
        for (var i = 0; i < 20; i++)
        {
            watch.Apply(null, ticket, Noon);
        }

        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(59), inGame: false));
        Assert.Equal(ticket, watch.Ticket(Noon.AddSeconds(60), inGame: false));
    }

    // ----------------------------------------------------------------------------- confirmed, stale and late reads

    [Theory]
    [InlineData("stopped")]
    [InlineData("restarted")]
    [InlineData("login")]
    public void The_end_of_the_clients_session_takes_the_confirmation_away_and_asks_again(string how)
    {
        var watch = new CosmeticsWatch(null);
        var events = 0;
        watch.Changed += () => events++;
        watch.ClientStarted();
        watch.Apply(Sample(), watch.Ticket(Noon, inGame: false), Noon);
        Assert.True(watch.Current!.Confirmed);
        Assert.Equal(1, events);

        switch (how)
        {
            case "stopped": watch.ClientStopped(); break;
            case "restarted": watch.ClientStarted(); break;
            default: watch.SceneChanged("HUB", "LOGIN"); break;
        }

        Assert.False(watch.Current.Confirmed);
        Assert.Equal(Sample().OwnedCardBacks.Order(), watch.Current.OwnedCardBacks.Order());
        Assert.Equal(2, events);
        Assert.Equal(how != "stopped", watch.Ticket(Noon, inGame: false) != 0);
    }

    [Fact]
    public void A_result_of_an_earlier_client_session_is_dropped_and_a_failed_read_keeps_what_was_known()
    {
        var watch = new CosmeticsWatch(null);
        watch.ClientStarted();
        var old = watch.Ticket(Noon, inGame: false);
        watch.ClientStopped();
        watch.ClientStarted();

        watch.Apply(Sample(owned: 2), old, Noon);
        Assert.Null(watch.Current);

        var fresh = watch.Ticket(Noon, inGame: false);
        watch.Apply(Sample(), fresh, Noon);
        watch.SceneChanged("GAMEPLAY", "HUB");
        watch.Apply(null, watch.Ticket(Noon.AddMinutes(2), inGame: false), Noon.AddMinutes(2));
        Assert.NotNull(watch.Current);
        Assert.True(watch.Current.Confirmed);
    }

    [Fact]
    public async Task A_read_is_saved_and_the_saved_file_is_unconfirmed_until_the_client_is_read()
    {
        var first = new CosmeticsWatch(new CosmeticsStore(File_));
        first.ClientStarted();
        first.Apply(Sample(owned: 2), first.Ticket(Noon, inGame: false), Noon);
        await first.Saved.WaitAsync(TimeSpan.FromSeconds(30));

        var second = new CosmeticsWatch(new CosmeticsStore(File_));
        second.Start();
        await second.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, second.Current!.OwnedCardBacks.Count);
        Assert.False(second.Current.Confirmed);
        Assert.Equal(0, second.Ticket(Noon, inGame: false));

        second.ClientStarted();
        second.Apply(null, second.Ticket(Noon, inGame: false), Noon);
        Assert.False(second.Current.Confirmed);
        second.Apply(Sample(readAt: Noon.AddDays(1)), second.Ticket(Noon.AddDays(1), inGame: false), Noon.AddDays(1));
        Assert.True(second.Current.Confirmed);
        await second.Saved.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_subscriber_that_throws_stops_neither_the_read_nor_the_save()
    {
        var watch = new CosmeticsWatch(new CosmeticsStore(File_));
        watch.Changed += () => throw new InvalidOperationException("a view failed");
        watch.ClientStarted();

        watch.Apply(Sample(owned: 2), watch.Ticket(Noon, inGame: false), Noon);

        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, new CosmeticsStore(File_).Load()!.OwnedCardBacks.Count);
        Assert.Contains("A subscriber of the cosmetics failed", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------- the memory scan

    private static MemoryRequest Request(int collection = 0, int cosmetics = 0, int achievements = 0, bool inGame = false) =>
        new(1, inGame, Battlegrounds: false, ReadRating: true, WantRaces: false, WantRanks: false, AwaitingRatingKey: null,
            CollectionTicket: collection, CosmeticsTicket: cosmetics, AchievementsTicket: achievements,
            AchievementsCatalog: AchievementsScene.Build(Noon, confirmed: true));

    [Fact]
    public void A_memory_scan_makes_at_most_one_long_read_and_the_others_wait_for_the_next()
    {
        var all = Request(collection: 1, cosmetics: 2, achievements: 3);

        Assert.Equal(LongReadKind.Collection, MemoryPoller.NextLongRead(all, all));
        var served = MemoryPoller.Served(all, LongReadKind.Collection);
        // The reads not made carry no ticket in what the reading answers: no miss, the wish stands.
        Assert.Equal((1, 0, 0), (served.CollectionTicket, served.CosmeticsTicket, served.AchievementsTicket));
        Assert.Null(served.AchievementsCatalog);

        var next = Request(cosmetics: 2, achievements: 3);
        Assert.Equal(LongReadKind.Cosmetics, MemoryPoller.NextLongRead(next, next));
        Assert.Equal((0, 2, 0), (MemoryPoller.Served(next, LongReadKind.Cosmetics).CollectionTicket,
            MemoryPoller.Served(next, LongReadKind.Cosmetics).CosmeticsTicket, MemoryPoller.Served(next, LongReadKind.Cosmetics).AchievementsTicket));

        var last = Request(achievements: 3);
        Assert.Equal(LongReadKind.Achievements, MemoryPoller.NextLongRead(last, last));
        Assert.Same(last, MemoryPoller.Served(last, LongReadKind.Achievements));
        Assert.Equal(LongReadKind.None, MemoryPoller.NextLongRead(Request(), Request()));
    }

    [Fact]
    public void No_long_read_starts_in_a_game_without_a_client_or_while_the_draft_waits()
    {
        var asked = Request(cosmetics: 2, achievements: 3);

        Assert.Equal(LongReadKind.None, MemoryPoller.NextLongRead(asked, asked with { InGame = true }));
        Assert.Equal(LongReadKind.None, MemoryPoller.NextLongRead(asked, null));
        Assert.Equal(LongReadKind.None, MemoryPoller.NextLongRead(asked with { InGame = true }, asked));
        Assert.Equal(LongReadKind.None, MemoryPoller.NextLongRead(asked, asked with { WantDraftChoices = true }));
        Assert.True(MemoryPoller.LongReadStale(2, asked, asked with { InGame = true }));
        Assert.False(MemoryPoller.LongReadStale(2, asked, asked));
    }

    // ------------------------------------------------------------------------------------------------ the session

    [Fact]
    public async Task The_session_asks_after_games_and_closed_screens_and_never_for_replayed_lines()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var running = true;
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => running,
            ReadsMemory = false,
            CosmeticsWatch = new CosmeticsWatch(new CosmeticsStore(File_)),
            AchievementsWatch = new AchievementsWatch(null),
        };
        var cosmetics = session.CosmeticsWatch;
        var achievements = session.AchievementsWatch;
        var announced = 0;
        session.CosmeticsChanged += () => announced++;

        // The running client is found on the first tick: one read of each is wanted.
        session.OnTick();
        var ticket = cosmetics.Ticket(now, inGame: false);
        Assert.NotEqual(0, ticket);
        cosmetics.Apply(Sample(), ticket, now);
        achievements.Apply(AchievementsScene.Build(now, confirmed: false), achievements.Ticket(now, inGame: false), now);
        Assert.True(session.Cosmetics!.Confirmed);
        Assert.Equal(1, announced);
        now = now.AddMinutes(5);

        // Old lines replayed at start-up are no game and no visit: a game that ended in them asks for nothing.
        session.OnLines("LoadingScreen.log",
        [
            "D 11:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=GAMEPLAY",
            "D 11:20:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=GAMEPLAY currMode=HUB",
            "D 11:21:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=COLLECTIONMANAGER",
            "D 11:22:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=COLLECTIONMANAGER currMode=HUB",
        ], catchingUp: true);
        Assert.Equal((0, 0), (cosmetics.Ticket(now, inGame: false), achievements.Ticket(now, inGame: false)));

        // A live end of a game is.
        session.OnLines("LoadingScreen.log", ["D 12:05:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=GAMEPLAY"], catchingUp: false);
        Assert.Equal(0, cosmetics.Ticket(now, inGame: false));
        session.OnLines("LoadingScreen.log", ["D 12:25:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=GAMEPLAY currMode=HUB"], catchingUp: false);
        Assert.NotEqual(0, cosmetics.Ticket(now, inGame: false));
        Assert.NotEqual(0, achievements.Ticket(now, inGame: false));

        // The client is closed: what is known stays, unconfirmed, and nothing is asked.
        running = false;
        now = now.AddSeconds(5);
        session.OnTick();
        Assert.False(session.Cosmetics.Confirmed);
        Assert.Equal(2, announced);
        Assert.Equal(0, cosmetics.Ticket(now, inGame: false));
        Assert.Equal(0, achievements.Ticket(now, inGame: false));

        await cosmetics.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(new CosmeticsStore(File_).Load());
    }

    private static GameSession RunningSession(HearthstoneMemory memory, Func<DateTime> clock) => new(HistoryStore.InMemory(), memory)
    {
        Clock = clock,
        IsHearthstoneRunning = () => true,
        ReadsMemory = false,
        CosmeticsWatch = new CosmeticsWatch(null),
        AchievementsWatch = new AchievementsWatch(null),
    };

    private static void Scene(GameSession session, string from, string to) =>
        session.OnLines("LoadingScreen.log", [$"D 12:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode={from} currMode={to}"], catchingUp: false);

    [Fact]
    public void No_read_is_asked_for_in_the_game_scene_or_while_a_game_is_on()
    {
        using var memory = new HearthstoneMemory();
        var session = RunningSession(memory, () => Noon);
        session.OnTick();

        // Wanted since the client was found, and asked for in the menu.
        var menu = session.BuildMemoryRequest();
        Assert.NotEqual(0, menu.CosmeticsTicket);
        Assert.NotEqual(0, menu.AchievementsTicket);

        // The game's scene comes before its first Power.log line, and stays on its end screen: still wanted, not asked.
        Scene(session, "HUB", "GAMEPLAY");
        var scene = session.BuildMemoryRequest();
        Assert.False(scene.InGame);
        Assert.Equal((0, 0), (scene.CosmeticsTicket, scene.AchievementsTicket));
        Scene(session, "GAMEPLAY", "HUB");
        Assert.NotEqual(0, session.BuildMemoryRequest().CosmeticsTicket);

        // A game in Power.log outside the game scene (a new log folder: the scene is not known yet) is no time either.
        session.OnSessionStarted("synthetic", new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Local), attachedMidSession: false);
        Assert.NotEqual(0, session.BuildMemoryRequest().AchievementsTicket);
        session.OnLines("Power.log", ReplayRecorderTests.Setup.Select(b => "D 12:30:00.0000000 GameState." + b).ToArray(), catchingUp: false);
        var game = session.BuildMemoryRequest();
        Assert.True(game.InGame);
        Assert.Equal((0, 0), (game.CosmeticsTicket, game.AchievementsTicket));
    }

    [Fact]
    public void The_sessions_catalog_goes_with_the_request_and_the_read_takes_the_states_only()
    {
        using var detached = new HearthstoneMemory();
        var session = RunningSession(detached, () => Noon);
        session.OnTick();
        Assert.Null(session.BuildMemoryRequest().AchievementsCatalog);

        // The session's first read is confirmed: from now on the request carries it as the catalog.
        var catalog = AchievementsScene.Build(Noon, confirmed: false);
        session.AchievementsWatch.Apply(catalog, session.BuildMemoryRequest().AchievementsTicket, Noon);
        Assert.Same(session.AchievementsWatch.Current, session.BuildMemoryRequest().AchievementsCatalog);

        // Another sign-in: the catalog is read again.
        Scene(session, "HUB", "LOGIN");
        Assert.Null(session.BuildMemoryRequest().AchievementsCatalog);

        // And a scan hands the catalog to the read: the book that comes back is that catalog with the client's states.
        using var memory = ClientImage.Attach(new ClientImage().Build());
        var poller = new MemoryPoller(memory);
        var request = new MemoryRequest(1, InGame: false, Battlegrounds: false, ReadRating: false, WantRaces: false, WantRanks: false,
            AwaitingRatingKey: null, AchievementsTicket: 5, AchievementsCatalog: catalog);
        var withCatalog = poller.ScanOnce(request)!.Achievements;
        var withoutCatalog = poller.ScanOnce(request with { AchievementsCatalog = null })!.Achievements;

        Assert.NotNull(withCatalog);
        Assert.Same(catalog.Achievements, withCatalog.Achievements);
        Assert.NotNull(withoutCatalog);
        Assert.NotSame(catalog.Achievements, withoutCatalog.Achievements);
        Assert.Equal([1, 2, 3, 4, 8], withoutCatalog.Achievements.Keys.Order());
    }
}

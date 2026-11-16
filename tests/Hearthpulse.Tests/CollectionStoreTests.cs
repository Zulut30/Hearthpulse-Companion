using System.Text.RegularExpressions;
using Hearthpulse.Core;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The local file of the collection (<see cref="CollectionStore"/>) and when the collection is read and believed
/// (<see cref="CollectionWatch"/>, docs/collection.md): it comes back as saved, a damaged file is "no collection",
/// reads follow the client's start and the collection screen, only a read of the running client is confirmed, a late
/// result of an old read changes nothing it should not, and no source file that sends anything names the collection.
/// </summary>
public sealed partial class CollectionStoreTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = TestDataFolder.Scratch("hp-collection");

    public CollectionStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private string File_ => Path.Combine(_dir, "collection.json");

    private static CardCollection Sample(long dust = 1200, DateTime? readAt = null) => new(
        new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            ["CORE_EX1_001"] = new(2, 0, 0),
            ["TOY_100"] = new(1, 1, 0),
            ["TLC_463"] = new(0, 0, 3),
        },
        dust,
        readAt ?? Noon.AddTicks(1234567));

    // ------------------------------------------------------------------------------------------------ the file

    [Fact]
    public void A_saved_collection_comes_back_the_same_and_unconfirmed()
    {
        var store = new CollectionStore(File_);
        var saved = Sample() with { Confirmed = true };

        store.Save(saved);
        var loaded = new CollectionStore(File_).Load();

        Assert.NotNull(loaded);
        Assert.Equal(saved.Cards, loaded.Cards);
        Assert.Equal(1200, loaded.Dust);
        Assert.Equal(saved.ReadAt, loaded.ReadAt);
        Assert.Equal(DateTimeKind.Utc, loaded.ReadAt.Kind);
        // The file does not say whose collection it is or whether the client still has it.
        Assert.False(loaded.Confirmed);
        Assert.Equal(["collection.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_new_save_replaces_the_old_one_and_creates_the_folder()
    {
        var store = new CollectionStore(Path.Combine(_dir, "deeper", "collection.json"));

        store.Save(Sample());
        store.Save(Sample(dust: 10) with { Cards = new Dictionary<string, OwnedCopies>(StringComparer.Ordinal) { ["BT_134"] = new(0, 2, 0) } });

        var loaded = store.Load()!;
        Assert.Equal(10, loaded.Dust);
        Assert.Equal(["BT_134"], loaded.Cards.Keys);
    }

    [Fact]
    public void No_file_is_no_collection()
    {
        Assert.Null(new CollectionStore(File_).Load());
        Assert.Null(new CollectionStore(Path.Combine(_dir, "missing", "collection.json")).Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[1,0""")]
    [InlineData("[]")]
    [InlineData("""{"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cards":{"TOY_100":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":-5,"cards":{"TOY_100":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":1000000001,"cards":{"TOY_100":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"yesterday","dust":5,"cards":{"TOY_100":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[1,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[1,-1,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[0,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":2}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":"many","cards":{"TOY_100":[1,0,0]}}""")]
    // Counts that overflow a sum, or are past the bounds one by one or together.
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[2147483647,1,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[10001,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[6000,6000,0]}}""")]
    // Keys that are no card ids: they could end up in a path or an address.
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"../../secret":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100?x=1":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"Карта":[1,0,0]}}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":5,"cards":{"TOY_100":[1,0,0],"TOY_100":[1,0,0]}}""")]
    public void A_damaged_file_is_no_collection_and_is_left_as_it_is(string content)
    {
        File.WriteAllText(File_, content);

        Assert.Null(new CollectionStore(File_).Load());
        Assert.Equal(content, File.ReadAllText(File_));
    }

    [Fact]
    public void The_bounds_themselves_are_read()
    {
        File.WriteAllText(File_, """{"version":1,"readAt":"2026-11-16T12:00:00Z","dust":1000000000,"cards":{"TOY_100":[10000,0,0],"A":[0,0,1]}}""");

        var loaded = new CollectionStore(File_).Load()!;

        Assert.Equal(1_000_000_000, loaded.Dust);
        Assert.Equal(10_000, loaded.Owned("TOY_100").Total);
    }

    [Fact]
    public void A_file_of_another_format_version_is_not_read()
    {
        new CollectionStore(File_).Save(Sample());
        var text = File.ReadAllText(File_);
        Assert.Contains($"\"version\":{CollectionStore.FormatVersion},", text, StringComparison.Ordinal);
        Assert.NotNull(new CollectionStore(File_).Load());

        File.WriteAllText(File_, text.Replace($"\"version\":{CollectionStore.FormatVersion},", $"\"version\":{CollectionStore.FormatVersion + 1},", StringComparison.Ordinal));
        Assert.Null(new CollectionStore(File_).Load());

        File.WriteAllText(File_, text.Replace($"\"version\":{CollectionStore.FormatVersion},", "\"version\":0,", StringComparison.Ordinal));
        Assert.Null(new CollectionStore(File_).Load());
    }

    [Fact]
    public void A_save_that_fails_throws_and_leaves_no_temp_file()
    {
        // A folder where the file goes: the collection is written beside it, and the move onto it fails on every OS.
        Directory.CreateDirectory(File_);
        var store = new CollectionStore(File_);

        Assert.ThrowsAny<Exception>(() => store.Save(Sample()));

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.True(Directory.Exists(File_));
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task A_temp_file_left_by_a_killed_save_is_removed_at_start_and_by_the_next_save()
    {
        var store = new CollectionStore(File_);
        store.Save(Sample());
        File.WriteAllText(store.TempPath, "half a collection");

        var watch = new CollectionWatch(store);
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["collection.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.NotNull(watch.Current);

        File.WriteAllText(store.TempPath, "half a collection");
        store.Save(Sample(dust: 7));
        Assert.Equal(["collection.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal(7, store.Load()!.Dust);
    }

    [Fact]
    public void The_file_holds_card_ids_counts_dust_and_the_time_and_nothing_else()
    {
        new CollectionStore(File_).Save(Sample() with { Confirmed = true });

        Assert.Equal(
            """{"version":1,"readAt":"2026-11-16T12:00:00.1234567Z","dust":1200,"cards":{"CORE_EX1_001":[2,0,0],"TLC_463":[0,0,3],"TOY_100":[1,1,0]}}""",
            File.ReadAllText(File_));
    }

    // ------------------------------------------------------------------------------------------- when it is read

    [Fact]
    public void The_collection_is_wanted_once_per_client_start_and_not_during_a_game()
    {
        var watch = new CollectionWatch(null);
        Assert.Equal(0, watch.Ticket(Noon, inGame: false));

        watch.ClientStarted();
        Assert.Equal(0, watch.Ticket(Noon, inGame: true));
        var ticket = watch.Ticket(Noon, inGame: false);
        Assert.NotEqual(0, ticket);

        watch.Apply(Sample(), ticket, Noon);
        Assert.Equal(0, watch.Ticket(Noon.AddHours(3), inGame: false));
        Assert.Equal(1200, watch.Current!.Dust);

        // The client was closed and started again.
        watch.ClientStopped();
        Assert.Equal(0, watch.Ticket(Noon.AddHours(3), inGame: false));
        watch.ClientStarted();
        Assert.NotEqual(0, watch.Ticket(Noon.AddHours(3), inGame: false));
    }

    [Theory]
    [InlineData("HUB", "COLLECTIONMANAGER", true)]
    [InlineData("TOURNAMENT", "COLLECTIONMANAGER", true)]
    [InlineData("COLLECTIONMANAGER", "HUB", true)]
    [InlineData("PACKOPENING", "HUB", true)]
    [InlineData("HUB", "LOGIN", true)]
    [InlineData("HUB", "PACKOPENING", false)]
    [InlineData("HUB", "TOURNAMENT", false)]
    [InlineData("GAMEPLAY", "HUB", false)]
    [InlineData("HUB", "BACON_COLLECTION", false)]
    public void Opening_the_collection_and_leaving_it_or_the_packs_asks_for_a_read(string from, string to, bool wanted)
    {
        var watch = new CollectionWatch(null);

        watch.SceneChanged(from, to);

        Assert.Equal(wanted, watch.Ticket(Noon, inGame: false) != 0);
    }

    [Fact]
    public void Reads_are_no_more_frequent_than_the_limit()
    {
        var watch = new CollectionWatch(null);
        watch.ClientStarted();
        watch.Apply(Sample(), watch.Ticket(Noon, inGame: false), Noon);

        // In and out of the collection right after the read: the next one waits for the limit.
        watch.SceneChanged("HUB", "COLLECTIONMANAGER");
        Assert.Equal(0, watch.Ticket(Noon.AddSeconds(1), inGame: false));
        Assert.Equal(0, watch.Ticket(Noon + CollectionWatch.MinInterval - TimeSpan.FromSeconds(1), inGame: false));
        Assert.NotEqual(0, watch.Ticket(Noon + CollectionWatch.MinInterval, inGame: false));
    }

    [Fact]
    public void A_read_that_gave_nothing_is_tried_again_later_and_later()
    {
        var watch = new CollectionWatch(null);
        watch.ClientStarted();
        var now = Noon;
        var ticket = watch.Ticket(now, inGame: false);

        watch.Apply(null, ticket, now);
        Assert.Null(watch.Current);
        Assert.Equal(0, watch.Ticket(now + CollectionWatch.RetryStep - TimeSpan.FromSeconds(1), inGame: false));
        Assert.Equal(ticket, watch.Ticket(now + CollectionWatch.RetryStep, inGame: false));

        watch.Apply(null, ticket, now);
        Assert.Equal(0, watch.Ticket(now + CollectionWatch.RetryStep, inGame: false));
        Assert.Equal(ticket, watch.Ticket(now + CollectionWatch.RetryStep * 2, inGame: false));

        for (var i = 0; i < 50; i++)
        {
            watch.Apply(null, ticket, now);
        }

        Assert.Equal(0, watch.Ticket(now + CollectionWatch.RetryMax - TimeSpan.FromSeconds(1), inGame: false));
        Assert.Equal(ticket, watch.Ticket(now + CollectionWatch.RetryMax, inGame: false));

        // A miss keeps what was known, and the first good read ends the waiting.
        watch.Apply(Sample(), ticket, now);
        Assert.NotNull(watch.Current);
        watch.Apply(null, ticket, now);
        Assert.NotNull(watch.Current);
    }

    // ----------------------------------------------------------------------------- confirmed, stale and late reads

    [Fact]
    public async Task The_saved_collection_is_unconfirmed_until_the_running_client_was_read()
    {
        new CollectionStore(File_).Save(Sample(dust: 111));
        var watch = new CollectionWatch(new CollectionStore(File_));
        var events = 0;
        watch.Changed += () => events++;
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));

        // The file of an earlier run: known, with its time, not confirmed.
        Assert.Equal(1, events);
        Assert.Equal(111, watch.Current!.Dust);
        Assert.False(watch.Current.Confirmed);
        Assert.Equal(Sample().ReadAt, watch.Current.ReadAt);

        // The client runs, but its read gives nothing (signing in, or an update renamed the class): still unconfirmed,
        // however often it is tried. The earlier owner's numbers never become "current".
        watch.ClientStarted();
        for (var i = 0; i < 5; i++)
        {
            watch.Apply(null, watch.Ticket(Noon.AddHours(i), inGame: false), Noon.AddHours(i));
        }

        Assert.False(watch.Current.Confirmed);
        Assert.Equal(111, watch.Current.Dust);
        Assert.Equal(1, events);

        // A read of the running client is confirmed.
        watch.Apply(Sample(dust: 222, readAt: Noon.AddDays(1)), watch.Ticket(Noon.AddDays(1), inGame: false), Noon.AddDays(1));
        Assert.True(watch.Current.Confirmed);
        Assert.Equal(222, watch.Current.Dust);
        Assert.Equal(2, events);
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("restarted")]
    [InlineData("login")]
    public void The_end_of_the_clients_session_takes_the_confirmation_away(string how)
    {
        var watch = new CollectionWatch(null);
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

        // The same cards and time, no longer vouched for; the views are told.
        Assert.False(watch.Current.Confirmed);
        Assert.Equal(Sample().Cards, watch.Current.Cards);
        Assert.Equal(Sample().ReadAt, watch.Current.ReadAt);
        Assert.Equal(2, events);

        // Once more changes nothing and tells nobody.
        watch.ClientStopped();
        Assert.Equal(2, events);
    }

    [Fact]
    public void A_result_asked_for_in_an_earlier_client_session_is_dropped()
    {
        var watch = new CollectionWatch(null);
        watch.ClientStarted();
        var old = watch.Ticket(Noon, inGame: false);

        // The read was under way when the client was closed and another one started (another account, maybe).
        watch.ClientStopped();
        watch.ClientStarted();
        watch.Apply(Sample(dust: 111), old, Noon);

        Assert.Null(watch.Current);
        var fresh = watch.Ticket(Noon, inGame: false);
        Assert.True(fresh > old);

        // Neither does an old miss delay the new session's read, nor a ticket that was never handed out count.
        watch.Apply(null, old, Noon);
        Assert.Equal(fresh, watch.Ticket(Noon, inGame: false));
        watch.Apply(Sample(dust: 5), fresh + 10, Noon);
        watch.Apply(Sample(dust: 5), 0, Noon);
        Assert.Null(watch.Current);

        watch.Apply(Sample(dust: 222), fresh, Noon);
        Assert.Equal(222, watch.Current!.Dust);
        Assert.True(watch.Current.Confirmed);
    }

    [Fact]
    public void A_result_of_an_older_wish_is_taken_and_the_newer_wish_stands()
    {
        var watch = new CollectionWatch(null);
        watch.ClientStarted();
        watch.SceneChanged("HUB", "COLLECTIONMANAGER");
        var entering = watch.Ticket(Noon, inGame: false);

        // The read of "entered the collection" is still running when the player leaves it after crafting.
        watch.SceneChanged("COLLECTIONMANAGER", "HUB");
        watch.Apply(Sample(dust: 111), entering, Noon);

        // The read is this session's and good, but the collection may have changed since: the wish for a read stays.
        Assert.Equal(111, watch.Current!.Dust);
        Assert.True(watch.Current.Confirmed);
        var leaving = watch.Ticket(Noon + CollectionWatch.MinInterval, inGame: false);
        Assert.True(leaving > entering);

        watch.Apply(Sample(dust: 71, readAt: Noon.AddMinutes(1)), leaving, Noon.AddMinutes(1));
        Assert.Equal(71, watch.Current.Dust);
        Assert.Equal(0, watch.Ticket(Noon.AddHours(1), inGame: false));
    }

    [Fact]
    public void A_later_read_never_gives_way_to_an_earlier_one()
    {
        var watch = new CollectionWatch(null);
        watch.ClientStarted();
        var ticket = watch.Ticket(Noon, inGame: false);
        watch.Apply(Sample(dust: 222, readAt: Noon.AddMinutes(5)), ticket, Noon.AddMinutes(5));
        var events = 0;
        watch.Changed += () => events++;

        watch.Apply(Sample(dust: 111, readAt: Noon), ticket, Noon.AddMinutes(6));

        Assert.Equal(222, watch.Current!.Dust);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task A_read_that_arrives_before_the_file_is_loaded_wins_over_the_file()
    {
        // An older file is on disk; the client's read is in before the load of that file starts.
        new CollectionStore(File_).Save(Sample(dust: 111));
        var watch = new CollectionWatch(new CollectionStore(File_));
        var seen = new List<(long Dust, bool Confirmed)>();
        watch.Changed += () => seen.Add((watch.Current!.Dust, watch.Current.Confirmed));

        watch.ClientStarted();
        watch.Apply(Sample(dust: 222, readAt: Noon.AddDays(1)), watch.Ticket(Noon, inGame: false), Noon);
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));

        // Whatever the order of the threads, the last thing a subscriber saw is the read, and so is the file.
        Assert.Equal(222, watch.Current!.Dust);
        Assert.True(watch.Current.Confirmed);
        Assert.Equal((222, true), seen[^1]);
        Assert.DoesNotContain((111, false), seen);
        Assert.Equal(222, new CollectionStore(File_).Load()!.Dust);
    }

    [Fact]
    public async Task A_read_is_saved_announced_and_known_to_the_next_run()
    {
        var first = new CollectionWatch(new CollectionStore(File_));
        var announced = 0;
        first.Changed += () => announced++;
        first.Start();
        await first.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(first.Current);

        first.ClientStarted();
        first.Apply(Sample(), first.Ticket(Noon, inGame: false), Noon);
        await first.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, announced);

        var second = new CollectionWatch(new CollectionStore(File_));
        second.Start();
        second.Start();
        await second.Loaded.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(Sample().Cards, second.Current!.Cards);
        Assert.False(second.Current.Confirmed);
        // The saved one needs no read of its own: nothing is wanted until the client starts.
        Assert.Equal(0, second.Ticket(Noon, inGame: false));
    }

    [Fact]
    public async Task A_save_that_failed_is_tried_again()
    {
        // A folder in the file's place makes the save fail; removing it lets the retry through.
        Directory.CreateDirectory(File_);
        var watch = new CollectionWatch(new CollectionStore(File_));
        watch.ClientStarted();
        watch.Apply(Sample(dust: 333), watch.Ticket(Noon, inGame: false), Noon);
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(Directory.Exists(File_));
        Assert.Equal(333, watch.Current!.Dust);

        Directory.Delete(File_);
        watch.Tick(Noon);
        watch.Tick(Noon + CollectionWatch.SaveRetry - TimeSpan.FromSeconds(1));
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(File.Exists(File_));

        watch.Tick(Noon + CollectionWatch.SaveRetry);
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(333, new CollectionStore(File_).Load()!.Dust);
    }

    [Fact]
    public async Task A_subscriber_that_throws_stops_neither_the_read_nor_the_save()
    {
        var watch = new CollectionWatch(new CollectionStore(File_));
        watch.Changed += () => throw new InvalidOperationException("a view failed");
        watch.ClientStarted();

        watch.Apply(Sample(dust: 444), watch.Ticket(Noon, inGame: false), Noon);
        watch.ClientStopped();

        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(444, watch.Current!.Dust);
        Assert.Equal(444, new CollectionStore(File_).Load()!.Dust);
        Assert.Contains("A collection subscriber failed", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the session

    [Fact]
    public async Task The_session_asks_around_the_collection_screen_and_passes_the_collection_on()
    {
        using var memory = new HearthstoneMemory();
        var now = Noon;
        var running = true;
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => running,
            ReadsMemory = false,
            CollectionWatch = new CollectionWatch(new CollectionStore(File_)),
        };
        var watch = session.CollectionWatch;
        var announced = 0;
        session.CollectionChanged += () => announced++;
        Assert.Null(session.Collection);

        // The running client is found on the first tick: one read is wanted.
        session.OnTick();
        var ticket = watch.Ticket(now, inGame: false);
        Assert.NotEqual(0, ticket);
        watch.Apply(Sample(), ticket, now);
        Assert.Equal(1200, session.Collection!.Dust);
        Assert.True(session.Collection.Confirmed);
        Assert.Equal(1, announced);
        now = now.AddMinutes(5);
        Assert.Equal(0, watch.Ticket(now, inGame: false));

        // Old lines replayed at start-up are no visit; a live scene change is.
        session.OnLines("LoadingScreen.log", ["D 12:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=COLLECTIONMANAGER"], catchingUp: true);
        Assert.Equal(0, watch.Ticket(now, inGame: false));
        session.OnLines("LoadingScreen.log", ["D 12:05:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=COLLECTIONMANAGER currMode=HUB"], catchingUp: false);
        Assert.NotEqual(0, watch.Ticket(now, inGame: false));
        Assert.True(session.Collection.Confirmed);

        // The client is closed: the collection stays, as the earlier session's.
        running = false;
        now = now.AddSeconds(5);
        session.OnTick();
        Assert.False(session.Collection.Confirmed);
        Assert.Equal(1200, session.Collection.Dust);
        Assert.Equal(2, announced);
        Assert.Equal(0, watch.Ticket(now, inGame: false));

        // Started again: a new read is wanted, and until it comes nothing is confirmed.
        running = true;
        now = now.AddSeconds(5);
        session.OnTick();
        Assert.NotEqual(0, watch.Ticket(now, inGame: false));
        Assert.False(session.Collection.Confirmed);

        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(new CollectionStore(File_).Load());
    }

    [Fact]
    public async Task The_session_loads_the_saved_collection_without_a_client()
    {
        new CollectionStore(File_).Save(Sample(dust: 77));
        using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => Noon,
            IsHearthstoneRunning = () => false,
            ReadsMemory = false,
            CollectionWatch = new CollectionWatch(new CollectionStore(File_)),
        };
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.CollectionChanged += () => told.TrySetResult();

        session.OnTick();
        await told.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(77, session.Collection!.Dust);
        Assert.False(session.Collection.Confirmed);
    }

    [Fact]
    public void A_collection_read_is_dropped_when_a_game_or_the_draft_screen_came_meanwhile()
    {
        var asked = new MemoryRequest(1, InGame: false, Battlegrounds: false, ReadRating: true, WantRaces: false, WantRanks: false, AwaitingRatingKey: null, CollectionTicket: 3);

        Assert.False(MemoryPoller.CollectionReadStale(asked, asked));
        Assert.False(MemoryPoller.CollectionReadStale(asked, asked with { CollectionTicket = 0 }));
        // A game began, the client went away, or the Arena draft screen opened and waits for its offer.
        Assert.True(MemoryPoller.CollectionReadStale(asked, asked with { InGame = true }));
        Assert.True(MemoryPoller.CollectionReadStale(asked, null));
        Assert.True(MemoryPoller.CollectionReadStale(asked, asked with { WantDraftChoices = true }));
        Assert.False(MemoryPoller.CollectionReadStale(asked with { WantDraftChoices = true }, asked with { WantDraftChoices = true }));
        // Never asked for, or asked for during a game.
        Assert.True(MemoryPoller.CollectionReadStale(asked with { CollectionTicket = 0 }, asked));
        Assert.True(MemoryPoller.CollectionReadStale(asked with { InGame = true }, asked));
    }

    // ------------------------------------------------------------------------ no sending source names the collection

    /// <summary>The words through which code reaches the collection: its records, its store, its file, its reads.</summary>
    [GeneratedRegex(@"CardCollection|CollectionStore|CollectionWatch|OwnedCopies|DeckCost|collection\.json|GetCollection|CollectionChanged|\.Collection\b(?!<)", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionWords();

    /// <summary>
    /// A check of names in source text, not of behaviour: it does not see a sender that has none of the words below, a
    /// payload built in another file, or a path through a view model (docs/collection.md, "Privacy").
    /// </summary>
    [Fact]
    public void No_source_file_with_a_sending_call_names_the_collection()
    {
        string[] sendingWords =
        [
            "PostAsync", "PutAsync", "PatchAsync", "PostAsJsonAsync", "PutAsJsonAsync", "HttpMethod.Post", "HttpMethod.Put", "HttpMethod.Patch",
            "SendAsync", "MultipartFormDataContent", "CrashReport", "NamedPipeClientStream", "ClientWebSocket",
        ];
        var senders = RepoFiles.Sources(".cs")
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return sendingWords.Any(word => text.Contains(word, StringComparison.Ordinal));
            })
            .ToList();

        Assert.True(senders.Count >= 5, $"{senders.Count} sending files found");
        Assert.Contains(senders, f => Path.GetFileName(f) == "DiscordIpc.cs");
        Assert.Empty(senders.Where(f => CollectionWords().IsMatch(File.ReadAllText(f))).Select(Path.GetFileName));

        // And the collection's own code names no network type.
        var own = RepoFiles.Sources(".cs")
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Collection{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(f) is "GameSession.Collection.cs" or "HearthstoneMemory.Collection.cs")
            .ToList();
        Assert.Equal(6, own.Count);
        foreach (var file in own)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Http", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Net", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_collection_file_is_in_none_of_the_folders_that_are_read_for_sending()
    {
        var file = Path.GetFullPath(AppPaths.Collection);

        Assert.Equal(Path.GetFullPath(AppPaths.Root), Path.GetDirectoryName(file));
        Assert.Equal("collection.json", Path.GetFileName(file));
        foreach (var sent in new[] { AppPaths.Replays, AppPaths.SavedReplays, AppPaths.ImportedReplays, AppPaths.ShareTemp, AppPaths.Cache, AppPaths.CombatCaptures })
        {
            Assert.False(file.StartsWith(Path.GetFullPath(sent), StringComparison.OrdinalIgnoreCase), sent);
        }
    }
}

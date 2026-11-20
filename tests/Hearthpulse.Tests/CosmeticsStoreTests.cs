using System.Text.Json;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The local file of the card backs and coins (<see cref="CosmeticsStore"/>, docs/collection-cosmetics.md): it comes back
/// as saved and unconfirmed, a damaged file is "none" and is left alone, no temp file outlives a save or a start, the
/// file holds the catalog and the player's ids and nothing about the account; and the rules every read of the client
/// shares (<see cref="CosmeticsCollection.FromClient"/>). All made-up data.
/// </summary>
public sealed class CosmeticsStoreTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = TestDataFolder.Scratch("hp-cosmetics");

    public CosmeticsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private string File_ => Path.Combine(_dir, "cosmetics.json");

    private static CosmeticsCollection Sample(DateTime? readAt = null) => new(
        [
            new CosmeticCardBack(0, "Classic", "The first one: the client numbers it 0.", CardBackCategory.Base, 0),
            new CosmeticCardBack(7, "Рубашка с «кавычками» и \"escapes\"", string.Empty, CardBackCategory.Seasonal, 3),
            new CosmeticCardBack(9, "Odd", "Group the client has no name for.", CardBackCategory.Unknown, -1),
        ],
        new HashSet<int> { 0, 7, 400 },
        new HashSet<int> { 7 },
        [new CosmeticCoin(1, 1746, "The Coin", string.Empty), new CosmeticCoin(5, 0, "Монетка", "Описание")],
        new HashSet<int> { 1, 5 },
        new HashSet<int> { 5 },
        readAt ?? Noon.AddTicks(1234567));

    private static void AssertSame(CosmeticsCollection expected, CosmeticsCollection actual)
    {
        Assert.Equal(expected.CardBacks, actual.CardBacks);
        Assert.Equal(expected.Coins, actual.Coins);
        Assert.Equal(expected.OwnedCardBacks.Order(), actual.OwnedCardBacks.Order());
        Assert.Equal(expected.FavoriteCardBacks.Order(), actual.FavoriteCardBacks.Order());
        Assert.Equal(expected.OwnedCoins.Order(), actual.OwnedCoins.Order());
        Assert.Equal(expected.FavoriteCoins.Order(), actual.FavoriteCoins.Order());
        Assert.Equal(expected.ReadAt, actual.ReadAt);
    }

    // ------------------------------------------------------------------------------------------------ the file

    [Fact]
    public void Saved_cosmetics_come_back_the_same_and_unconfirmed()
    {
        var saved = Sample() with { Confirmed = true };

        new CosmeticsStore(File_).Save(saved);
        var loaded = new CosmeticsStore(File_).Load();

        Assert.NotNull(loaded);
        AssertSame(saved, loaded);
        Assert.Equal(DateTimeKind.Utc, loaded.ReadAt.Kind);
        Assert.False(loaded.Confirmed);
        Assert.Equal(["cosmetics.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Empty_lists_round_trip()
    {
        var empty = new CosmeticsCollection([], new HashSet<int>(), new HashSet<int>(), [], new HashSet<int>(), new HashSet<int>(), Noon);

        new CosmeticsStore(File_).Save(empty);

        AssertSame(empty, new CosmeticsStore(File_).Load()!);
    }

    [Fact]
    public void No_file_is_no_cosmetics()
    {
        Assert.Null(new CosmeticsStore(File_).Load());
        Assert.Null(new CosmeticsStore(Path.Combine(_dir, "missing", "cosmetics.json")).Load());
    }

    private const string Good =
        """{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":"A","description":"","category":"Base","sortOrder":0}],"ownedCardBacks":[1],"favoriteCardBacks":[],"coins":[{"id":2,"cardDbfId":1746,"name":"C","description":""}],"ownedCoins":[2],"favoriteCoins":[2]}""";

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[""")]
    [InlineData("""{"version":2,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"yesterday","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"1999-12-31T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":{},"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[-3],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[4,4],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":["4"],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":"A","description":"","category":"Base","sortOrder":0},{"id":1,"name":"B","description":"","category":"Base","sortOrder":1}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":-1,"name":"A","description":"","category":"Base","sortOrder":0}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":"A","description":"","category":"Weird","sortOrder":0}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":"A","description":"","category":"5","sortOrder":0}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":"A","description":"","category":"Base, Fireside","sortOrder":0}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[{"id":1,"name":null,"description":"","category":"Base","sortOrder":0}],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[{"id":2,"cardDbfId":-1,"name":"C","description":""}],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[{"id":2,"name":"C","description":""}],"ownedCoins":[],"favoriteCoins":[]}""")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","cardBacks":[],"ownedCardBacks":[],"favoriteCardBacks":[],"coins":[],"ownedCoins":[2147483648],"favoriteCoins":[]}""")]
    public void A_damaged_file_is_no_cosmetics_and_is_left_as_it_is(string content)
    {
        File.WriteAllText(File_, content);

        Assert.Null(new CosmeticsStore(File_).Load());
        Assert.Equal(content, File.ReadAllText(File_));
    }

    [Fact]
    public void The_well_formed_sample_of_the_damage_cases_is_read()
    {
        File.WriteAllText(File_, Good);

        var loaded = new CosmeticsStore(File_).Load()!;
        Assert.Equal([new CosmeticCardBack(1, "A", string.Empty, CardBackCategory.Base, 0)], loaded.CardBacks);
        Assert.Equal([2], loaded.FavoriteCoins);
    }

    [Fact]
    public void A_text_past_the_bound_makes_the_file_unreadable()
    {
        var name = new string('x', CosmeticsLimits.MaxText);
        File.WriteAllText(File_, Good.Replace("\"name\":\"A\"", $"\"name\":\"{name}\"", StringComparison.Ordinal));
        Assert.NotNull(new CosmeticsStore(File_).Load());

        File.WriteAllText(File_, Good.Replace("\"name\":\"A\"", $"\"name\":\"{name}x\"", StringComparison.Ordinal));
        Assert.Null(new CosmeticsStore(File_).Load());
    }

    [Fact]
    public void A_file_of_another_format_version_is_not_read()
    {
        new CosmeticsStore(File_).Save(Sample());
        var text = File.ReadAllText(File_);
        Assert.Contains($"\"version\":{CosmeticsStore.FormatVersion},", text, StringComparison.Ordinal);

        File.WriteAllText(File_, text.Replace($"\"version\":{CosmeticsStore.FormatVersion},", $"\"version\":{CosmeticsStore.FormatVersion + 1},", StringComparison.Ordinal));
        Assert.Null(new CosmeticsStore(File_).Load());
    }

    [Fact]
    public void A_save_that_fails_throws_and_leaves_no_temp_file()
    {
        // A folder where the file goes: the move onto it fails on every OS.
        Directory.CreateDirectory(File_);
        var store = new CosmeticsStore(File_);

        Assert.ThrowsAny<Exception>(() => store.Save(Sample()));

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task A_temp_file_left_by_a_killed_save_is_removed_at_start_and_by_the_next_save()
    {
        var store = new CosmeticsStore(File_);
        store.Save(Sample());
        File.WriteAllText(store.TempPath, "half the cosmetics");

        var watch = new CosmeticsWatch(store);
        var raised = 0;
        watch.Changed += () => raised++;
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["cosmetics.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.NotNull(watch.Current);
        Assert.False(watch.Current.Confirmed);
        Assert.Equal(1, raised);

        File.WriteAllText(store.TempPath, "half the cosmetics");
        store.Save(Sample(readAt: Noon));
        Assert.Equal(["cosmetics.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal(Noon, store.Load()!.ReadAt);
    }

    [Fact]
    public async Task A_watch_without_a_file_has_nothing_and_raises_nothing()
    {
        var watch = new CosmeticsWatch(new CosmeticsStore(File_));
        var raised = 0;
        watch.Changed += () => raised++;

        watch.Start();
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(watch.Current);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void The_file_holds_the_catalog_the_ids_and_the_time_and_nothing_about_the_account()
    {
        new CosmeticsStore(File_).Save(Sample() with { Confirmed = true });

        using var document = JsonDocument.Parse(File.ReadAllText(File_));
        var root = document.RootElement;
        Assert.Equal(
            ["cardBacks", "coins", "favoriteCardBacks", "favoriteCoins", "ownedCardBacks", "ownedCoins", "readAt", "version"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(root.GetProperty("cardBacks").EnumerateArray(), b =>
            Assert.Equal(["category", "description", "id", "name", "sortOrder"], b.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Assert.All(root.GetProperty("coins").EnumerateArray(), c =>
            Assert.Equal(["cardDbfId", "description", "id", "name"], c.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Assert.Equal([0, 7, 400], root.GetProperty("ownedCardBacks").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal("2026-11-16T12:00:00.1234567Z", root.GetProperty("readAt").GetString());
    }

    [Fact]
    public async Task The_session_loads_the_saved_cosmetics_without_a_client()
    {
        new CosmeticsStore(File_).Save(Sample() with { Confirmed = true });
        using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => Noon,
            IsHearthstoneRunning = () => false,
            ReadsMemory = false,
            CosmeticsWatch = new CosmeticsWatch(new CosmeticsStore(File_)),
        };
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.CosmeticsChanged += () => told.TrySetResult();
        Assert.Null(session.Cosmetics);

        session.OnTick();
        await told.Task.WaitAsync(TimeSpan.FromSeconds(30));

        AssertSame(Sample(), session.Cosmetics!);
        Assert.False(session.Cosmetics!.Confirmed);
    }

    // ------------------------------------------------------------------------------- the rules of a client read

    [Fact]
    public void The_catalog_keeps_enabled_entries_without_the_random_back_and_the_ids_as_read()
    {
        var read = CosmeticsCollection.FromClient(
            [
                new ClientCardBack(1, "Classic", "First", 1, 0, Enabled: true, Random: false),
                new ClientCardBack(2, "Off", "Disabled", 5, 1, Enabled: false, Random: false),
                new ClientCardBack(3, "Random", "Random back", 0, 2, Enabled: true, Random: true),
                new ClientCardBack(4, "Events", "Last enum value", 13, 3, Enabled: true, Random: false),
                new ClientCardBack(5, "Newer", "A group the enum does not have", 14, 4, Enabled: true, Random: false),
                new ClientCardBack(6, "Negative", "A broken group", -1, 5, Enabled: true, Random: false),
            ],
            [1, 2, 99], [1],
            [new ClientCoin(1, 1746, "The Coin", string.Empty, Enabled: true), new ClientCoin(2, 1747, "Off", string.Empty, Enabled: false)],
            [1, 2], [2],
            new DateTime(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(read);
        Assert.Equal([1, 4, 5, 6], read.CardBacks.Select(b => b.Id));
        Assert.Equal(
            [CardBackCategory.Base, CardBackCategory.Events, CardBackCategory.Unknown, CardBackCategory.Unknown],
            read.CardBacks.Select(b => b.Category));
        Assert.Equal([1], read.Coins.Select(c => c.Id));
        // What the player owns is kept as the client holds it, catalog or not.
        Assert.Equal([1, 2, 99], read.OwnedCardBacks.Order());
        Assert.Equal([1, 2], read.OwnedCoins.Order());
        Assert.Equal([2], read.FavoriteCoins);
        Assert.False(read.Confirmed);
    }

    [Fact]
    public void A_client_read_with_a_repeated_id_or_a_bad_value_gives_no_cosmetics()
    {
        static CosmeticsCollection? Read(ClientCardBack[] backs, int[] owned) =>
            CosmeticsCollection.FromClient(backs, owned, [], [], [], [], Noon);

        Assert.NotNull(Read([new ClientCardBack(1, "A", "", 1, 0, true, false)], [1]));
        Assert.Null(Read([new ClientCardBack(1, "A", "", 1, 0, true, false), new ClientCardBack(1, "B", "", 1, 0, true, false)], [1]));
        Assert.Null(Read([new ClientCardBack(1, "A", "", 1, 0, true, false)], [-1]));
        // The client's first card back, "Classic", is number 0.
        Assert.NotNull(Read([new ClientCardBack(0, "Classic", "", 1, 0, true, false)], [0]));
        Assert.Null(Read([new ClientCardBack(1, new string('x', CosmeticsLimits.MaxText + 1), "", 1, 0, true, false)], [1]));
    }

    [Theory]
    [InlineData(0, CardBackCategory.None)]
    [InlineData(5, CardBackCategory.Seasonal)]
    [InlineData(10, CardBackCategory.PrePurchase)]
    [InlineData(13, CardBackCategory.Events)]
    [InlineData(14, CardBackCategory.Unknown)]
    [InlineData(-1, CardBackCategory.Unknown)]
    [InlineData(int.MaxValue, CardBackCategory.Unknown)]
    public void Sort_categories_follow_the_enum_order(int number, CardBackCategory expected) =>
        Assert.Equal(expected, CosmeticsCollection.CategoryOf(number));
}

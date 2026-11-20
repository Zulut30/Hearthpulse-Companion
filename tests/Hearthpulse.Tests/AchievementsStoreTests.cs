using System.Text.Json;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The local file of the achievement book (<see cref="AchievementsStore"/>, docs/collection-cosmetics.md): the made-up
/// book comes back whole (catalog, states, gains) and unconfirmed, a damaged or inconsistent file is "none" and is left
/// alone, no temp file outlives a save or a start, and the file names nothing about the account.
/// </summary>
public sealed class AchievementsStoreTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = TestDataFolder.Scratch("hp-achievements");

    public AchievementsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private string File_ => Path.Combine(_dir, "achievements.json");

    internal static void AssertSame(AchievementBook expected, AchievementBook actual)
    {
        Assert.Equal(expected.Categories, actual.Categories);
        Assert.Equal(expected.Subcategories, actual.Subcategories);
        Assert.Equal(expected.Sections, actual.Sections);
        Assert.Equal(expected.Achievements.OrderBy(p => p.Key), actual.Achievements.OrderBy(p => p.Key));
        Assert.Equal(expected.States.OrderBy(p => p.Key), actual.States.OrderBy(p => p.Key));
        Assert.Equal(expected.Gains.OrderBy(p => p.Key), actual.Gains.OrderBy(p => p.Key));
        Assert.Equal(expected.ReadAt, actual.ReadAt);
    }

    // A small consistent book: one category, subcategory, section, a two-tier chain, a state of each kind and a gain.
    private const string Good = """
        {"version":1,"readAt":"2026-11-16T12:00:00Z",
         "categories":[{"id":1,"name":"Общие","icon":"general","sortOrder":1}],
         "subcategories":[{"id":10,"categoryId":1,"name":"Прогресс","sortOrder":1}],
         "sections":[{"id":100,"subcategoryId":10,"name":"","sortOrder":1}],
         "achievements":[{"id":1000,"sectionId":100,"sortOrder":1,"name":"A I","description":"Do $q","hidden":false,"quota":10,"points":5,"nextTierId":1001},
                         {"id":1001,"sectionId":100,"sortOrder":2,"name":"A II","description":"Do $q","hidden":false,"quota":20,"points":10,"nextTierId":0}],
         "states":[{"id":1000,"status":"Completed","progress":10,"completedAt":"2026-11-01T10:00:00Z"},
                   {"id":1001,"status":"InProgress","progress":12,"completedAt":null}],
         "gains":[{"headId":1000,"tiers":0,"progress":2,"at":"2026-11-16T11:00:00Z"}]}
        """;

    [Fact]
    public void A_saved_book_comes_back_the_same_and_unconfirmed()
    {
        var saved = AchievementsScene.Build(Noon, confirmed: true);
        Assert.NotEmpty(saved.Gains);
        Assert.Contains(saved.States.Values, s => s.CompletedAt is null);

        new AchievementsStore(File_).Save(saved);
        var loaded = new AchievementsStore(File_).Load();

        Assert.NotNull(loaded);
        AssertSame(saved, loaded);
        Assert.Equal(DateTimeKind.Utc, loaded.ReadAt.Kind);
        Assert.All(loaded.States.Values.Where(s => s.CompletedAt is not null), s => Assert.Equal(DateTimeKind.Utc, s.CompletedAt!.Value.Kind));
        Assert.False(loaded.Confirmed);
        // What is worked out from the book is the same too.
        Assert.Equal(saved.Summary.PointsEarned, loaded.Summary.PointsEarned);
        Assert.Equal(saved.Chains.Select(c => (c.HeadId, c.Progress, c.Quota)), loaded.Chains.Select(c => (c.HeadId, c.Progress, c.Quota)));
        Assert.Equal(["achievements.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void The_well_formed_sample_of_the_damage_cases_is_read()
    {
        File.WriteAllText(File_, Good);

        var loaded = new AchievementsStore(File_).Load();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Achievements.Count);
        Assert.Equal(new AchievementState(1001, AchievementStatus.InProgress, 12, null), loaded.States[1001]);
        Assert.Equal(new AchievementGain(0, 2, new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc)), loaded.Gains[1000]);
        Assert.Equal(12, loaded.Chain(1000)!.Progress);
    }

    [Fact]
    public void No_file_is_no_book()
    {
        Assert.Null(new AchievementsStore(File_).Load());
        Assert.Null(new AchievementsStore(Path.Combine(_dir, "missing", "achievements.json")).Load());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("not json at all", "")]
    [InlineData("[]", "")]
    [InlineData("""{"version":1,"readAt":"2026-11-16T12:00:00Z","categories":[""", "")]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"version\":1,", "")]
    [InlineData("\"readAt\":\"2026-11-16T12:00:00Z\"", "\"readAt\":\"soon\"")]
    [InlineData("\"readAt\":\"2026-11-16T12:00:00Z\"", "\"readAt\":\"2101-01-01T00:00:00Z\"")]
    // A list that is missing or of the wrong kind.
    [InlineData("\"gains\":[", "\"gainz\":[")]
    [InlineData("\"categories\":[{\"id\":1,\"name\":\"Общие\",\"icon\":\"general\",\"sortOrder\":1}]", "\"categories\":{}")]
    // Records that point at nothing.
    [InlineData("\"categoryId\":1", "\"categoryId\":2")]
    [InlineData("\"subcategoryId\":10", "\"subcategoryId\":11")]
    [InlineData("\"sectionId\":100,\"sortOrder\":2", "\"sectionId\":101,\"sortOrder\":2")]
    [InlineData("{\"id\":1001,\"status\"", "{\"id\":1002,\"status\"")]
    [InlineData("\"headId\":1000", "\"headId\":999")]
    // Repeated ids.
    [InlineData("{\"id\":1001,\"sectionId\"", "{\"id\":1000,\"sectionId\"")]
    [InlineData("{\"id\":1001,\"status\"", "{\"id\":1000,\"status\"")]
    // Values out of their kind or bounds.
    [InlineData("\"status\":\"InProgress\"", "\"status\":\"Active\"")]
    [InlineData("\"status\":\"InProgress\"", "\"status\":1")]
    [InlineData("\"status\":\"InProgress\"", "\"status\":\"1\"")]
    [InlineData("\"completedAt\":\"2026-11-01T10:00:00Z\"", "\"completedAt\":\"1999-11-01T10:00:00Z\"")]
    [InlineData("\"completedAt\":\"2026-11-01T10:00:00Z\"", "\"completedAt\":0")]
    [InlineData(",\"completedAt\":null", "")]
    [InlineData("\"tiers\":0", "\"tiers\":-1")]
    [InlineData("\"points\":5", "\"points\":-5")]
    [InlineData("\"quota\":10", "\"quota\":10.5")]
    [InlineData("\"quota\":10", "\"quota\":2000000000")]
    [InlineData("\"hidden\":false,\"quota\":10", "\"hidden\":0,\"quota\":10")]
    [InlineData("\"nextTierId\":1001", "\"nextTierId\":-1")]
    [InlineData("\"name\":\"A I\"", "\"name\":null")]
    [InlineData("{\"id\":1,\"name\"", "{\"id\":0,\"name\"")]
    public void A_damaged_file_is_no_book_and_is_left_as_it_is(string find, string replace)
    {
        var content = find.Length > 0 && Good.Contains(find, StringComparison.Ordinal) ? Good.Replace(find, replace, StringComparison.Ordinal) : find;
        Assert.NotEqual(Good, content);
        File.WriteAllText(File_, content);

        Assert.Null(new AchievementsStore(File_).Load());
        Assert.Equal(content, File.ReadAllText(File_));
    }

    [Fact]
    public void A_text_past_the_bound_makes_the_file_unreadable()
    {
        var text = new string('x', AchievementLimits.MaxText);
        File.WriteAllText(File_, Good.Replace("\"Do $q\",\"hidden\":false,\"quota\":10", $"\"{text}\",\"hidden\":false,\"quota\":10", StringComparison.Ordinal));
        Assert.NotNull(new AchievementsStore(File_).Load());

        File.WriteAllText(File_, Good.Replace("\"Do $q\",\"hidden\":false,\"quota\":10", $"\"{text}x\",\"hidden\":false,\"quota\":10", StringComparison.Ordinal));
        Assert.Null(new AchievementsStore(File_).Load());
    }

    [Fact]
    public void A_save_that_fails_throws_and_leaves_no_temp_file()
    {
        Directory.CreateDirectory(File_);
        var store = new AchievementsStore(File_);

        Assert.ThrowsAny<Exception>(() => store.Save(AchievementsScene.Build(Noon, confirmed: true)));

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task A_temp_file_left_by_a_killed_save_is_removed_at_start_and_by_the_next_save()
    {
        var store = new AchievementsStore(File_);
        store.Save(AchievementsScene.Build(Noon, confirmed: true));
        File.WriteAllText(store.TempPath, "half a book");

        var watch = new AchievementsWatch(store);
        var raised = 0;
        watch.Changed += () => raised++;
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["achievements.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.NotNull(watch.Current);
        Assert.False(watch.Current.Confirmed);
        Assert.Equal(1, raised);

        File.WriteAllText(store.TempPath, "half a book");
        store.Save(AchievementsScene.Build(Noon.AddDays(1), confirmed: true));
        Assert.Equal(["achievements.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal(Noon.AddDays(1), store.Load()!.ReadAt);
    }

    [Fact]
    public async Task The_session_loads_the_saved_book_without_a_client()
    {
        var saved = AchievementsScene.Build(Noon, confirmed: true);
        new AchievementsStore(File_).Save(saved);
        using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => Noon,
            IsHearthstoneRunning = () => false,
            ReadsMemory = false,
            AchievementsWatch = new AchievementsWatch(new AchievementsStore(File_)),
        };
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AchievementsChanged += () => told.TrySetResult();
        Assert.Null(session.Achievements);

        session.OnTick();
        await told.Task.WaitAsync(TimeSpan.FromSeconds(30));

        AssertSame(saved, session.Achievements!);
        Assert.False(session.Achievements!.Confirmed);
    }

    [Fact]
    public void The_file_holds_the_catalog_states_gains_and_the_time_and_nothing_about_the_account()
    {
        new AchievementsStore(File_).Save(AchievementsScene.Build(Noon, confirmed: true));

        var text = File.ReadAllText(File_);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(
            ["achievements", "categories", "gains", "readAt", "sections", "states", "subcategories", "version"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["at", "headId", "progress", "tiers"], root.GetProperty("gains")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["completedAt", "id", "progress", "status"], root.GetProperty("states")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        // The client's language is written as it is, not escaped.
        Assert.Contains("\"Коллекционер I\"", text, StringComparison.Ordinal);
    }
}

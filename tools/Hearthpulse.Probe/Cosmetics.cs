using System.Globalization;
using System.Text.Json;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.Probe;

/// <summary>
/// Developer reconnaissance (docs/collection-cosmetics.md): the card backs, coins and achievements the client holds,
/// read field by field through the generic object reader, written to a JSON file of the developer's choosing outside
/// the repository, with a summary on the console. Read-only, like every read of this tool.
/// </summary>
internal static class Cosmetics
{
    private const ulong ArrayData = 32;

    public static int Run(string[] args)
    {
        using var memory = new HearthstoneMemory();
        if (!memory.EnsureAttached() || memory.Runtime is not { } rt)
        {
            Console.WriteLine("Not attached: " + memory.Status);
            return 2;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Dump(rt) is not { } dump)
        {
            Console.WriteLine("GameDbf not found");
            return 3;
        }

        var took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        Summary(dump, took);
        if (args.Length > 0)
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = false }));
            Console.WriteLine("written " + args[0]);
        }

        return 0;
    }

    /// <summary>Everything <c>cosmetics</c> writes, walked field by field through the generic object reader; null without GameDbf.</summary>
    private static Dictionary<string, object?>? Dump(MonoRuntime rt)
    {
        var gameDbf = rt.FindClass("GameDbf");
        if (gameDbf is null)
        {
            return null;
        }

        var cardBacksCache = HearthstoneMemory.FindNetCacheObject(rt, "NetCacheCardBacks");
        var coinsCache = HearthstoneMemory.FindNetCacheObject(rt, "NetCacheCoins");
        var manager = HearthstoneMemory.FindService(rt, "Hearthstone.Progression.AchievementManager");

        var dump = new Dictionary<string, object?>
        {
            ["cardBacks"] = Records(gameDbf, "CardBack", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["name"] = Loc(r, "m_name"),
                ["description"] = Loc(r, "m_description"),
                ["sortCategory"] = r.Int("m_sortCategory"),
                ["sortOrder"] = r.Int("m_sortOrder"),
                ["source"] = r.Int("m_source"),
                ["enabled"] = r.Bool("m_enabled"),
                ["random"] = r.Bool("m_isRandomCardBack"),
            }),
            ["ownedCardBacks"] = IntSet(rt, cardBacksCache["<CardBacks>k__BackingField"]),
            ["favoriteCardBacks"] = IntSet(rt, cardBacksCache["<FavoriteCardBacks>k__BackingField"]),
            ["coins"] = Records(gameDbf, "CosmeticCoin", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["cardDbfId"] = r.Int("m_cardId"),
                ["name"] = Loc(r, "m_name"),
                ["description"] = Loc(r, "m_description"),
                ["enabled"] = r.Bool("m_enabled"),
            }),
            ["ownedCoins"] = IntSet(rt, coinsCache["<Coins>k__BackingField"]),
            ["favoriteCoins"] = IntSet(rt, coinsCache["<FavoriteCoins>k__BackingField"]),
            ["categories"] = Records(gameDbf, "AchievementCategory", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["name"] = Loc(r, "m_name"),
                ["icon"] = r.String("m_icon"),
                ["sortOrder"] = r.Int("m_sortOrder"),
            }),
            ["subcategories"] = Records(gameDbf, "AchievementSubcategory", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["categoryId"] = r.Int("m_achievementCategoryId"),
                ["name"] = Loc(r, "m_name"),
                ["icon"] = r.String("m_icon"),
                ["sortOrder"] = r.Int("m_sortOrder"),
            }),
            ["sections"] = Records(gameDbf, "AchievementSection", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["name"] = Loc(r, "m_name"),
            }),
            ["sectionItems"] = Records(gameDbf, "AchievementSectionItem", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["subcategoryId"] = r.Int("m_achievementSubcategoryId"),
                ["sectionId"] = r.Int("m_achievementSectionId"),
                ["sortOrder"] = r.Int("m_sortOrder"),
            }),
            ["achievements"] = Records(gameDbf, "Achievement", r => new Dictionary<string, object?>
            {
                ["id"] = r.Int("m_ID"),
                ["sectionId"] = r.Int("m_achievementSectionId"),
                ["sortOrder"] = r.Int("m_sortOrder"),
                ["enabled"] = r.Bool("m_enabled"),
                ["retired"] = r.Bool("m_retired"),
                ["name"] = Loc(r, "m_name"),
                ["description"] = Loc(r, "m_description"),
                ["visibility"] = r.Int("m_achievementVisibility"),
                ["quota"] = r.Int("m_quota"),
                ["allowExceedQuota"] = r.Bool("m_allowExceedQuota"),
                ["points"] = r.Int("m_points"),
                ["nextTierId"] = r.Int("m_nextTierId"),
                ["rewardTrackXp"] = r.Int("m_rewardTrackXp"),
            }),
            ["states"] = manager["m_playerState"]["m_playerState"].MapIntEntries().Select(e => new Dictionary<string, object?>
            {
                ["key"] = e.Key,
                ["id"] = e.Value.Int("_AchievementId"),
                ["hasStatus"] = e.Value.Bool("HasStatus"),
                ["status"] = e.Value.Int("_Status"),
                ["hasProgress"] = e.Value.Bool("HasProgress"),
                ["progress"] = e.Value.Int("_Progress"),
                ["hasCompletedDate"] = e.Value.Bool("HasCompletedDate"),
                ["completedDate"] = e.Value.Long("_CompletedDate"),
            }).ToList(),
        };
        return dump;
    }

    /// <summary>
    /// <c>recon-import &lt;cosmetics-dump.json&gt; &lt;folder&gt;</c>: turns a file written by <c>cosmetics</c> into the app's
    /// <c>cosmetics.json</c> and <c>achievements.json</c> in the folder, by the same rules the app's read applies
    /// (<see cref="CosmeticsCollection.FromClient"/>, <see cref="AchievementBook.FromClient"/>), written by its stores and
    /// read back through them. For a copy of a data folder made for renders; prints counts only.
    /// </summary>
    public static int ReconImport(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: recon-import <cosmetics-dump.json> <folder>");
            return 1;
        }

        var readAt = File.GetLastWriteTimeUtc(args[0]);
        using var document = JsonDocument.Parse(File.ReadAllBytes(args[0]));
        var (cosmetics, book) = FromDump(document.RootElement, readAt);
        if (cosmetics is null || book is null)
        {
            Console.WriteLine($"rejected by the rules: cosmetics {(cosmetics is null ? "none" : "ok")}, achievements {(book is null ? "none" : "ok")}");
            return 3;
        }

        Directory.CreateDirectory(args[1]);
        var cosmeticsStore = new CosmeticsStore(Path.Combine(args[1], "cosmetics.json"));
        var achievementsStore = new AchievementsStore(Path.Combine(args[1], "achievements.json"));
        cosmeticsStore.Save(cosmetics);
        achievementsStore.Save(book);
        var cosmeticsBack = cosmeticsStore.Load();
        var bookBack = achievementsStore.Load();
        Console.WriteLine($"read at {readAt:O}");
        Counts(cosmetics, book);
        Console.WriteLine($"loaded back: cosmetics {(cosmeticsBack is null ? "FAILED" : "ok")}, achievements {(bookBack is null ? "FAILED" : "ok")}");
        return cosmeticsBack is null || bookBack is null ? 4 : 0;
    }

    /// <summary>
    /// <c>cosmetics-check &lt;folder&gt;</c> (docs/collection-cosmetics.md, "What was checked"): the cosmetics and the
    /// achievements read by the app's methods (<see cref="HearthstoneMemory.GetCosmetics(Func{bool}?)"/>,
    /// <see cref="HearthstoneMemory.GetAchievements(Func{bool}?, AchievementBook?)"/>: the catalog read and a read of
    /// states only) with their times, then the same walked field by field through the generic object reader, turned into
    /// records by the same rules, and the two compared record by record. The app's reads are written by its stores into
    /// the folder (a copy of a data folder, outside the repository). Prints counts and differences only.
    /// </summary>
    public static int Check(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: cosmetics-check <folder>");
            return 1;
        }

        using var memory = new HearthstoneMemory();
        if (!memory.EnsureAttached() || memory.Runtime is not { } rt)
        {
            Console.WriteLine("Not attached: " + memory.Status);
            return 2;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var cosmetics = memory.GetCosmetics();
        var cosmeticsTime = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        started = System.Diagnostics.Stopwatch.GetTimestamp();
        var book = memory.GetAchievements();
        var bookTime = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        started = System.Diagnostics.Stopwatch.GetTimestamp();
        var states = book is null ? null : memory.GetAchievements(catalog: book);
        var statesTime = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        Console.WriteLine($"app read: cosmetics {cosmeticsTime.TotalMilliseconds:N0} ms ({(cosmetics is null ? "none" : "ok")}), "
            + $"achievements with the catalog {bookTime.TotalMilliseconds:N0} ms ({(book is null ? "none" : "ok")}), "
            + $"states only {statesTime.TotalMilliseconds:N0} ms ({(states is null ? "none" : "ok")})");
        if (cosmetics is null || book is null || states is null)
        {
            Console.WriteLine("see hearthpulse.log of HEARTHPULSE_DATA for the reason");
            return 3;
        }

        started = System.Diagnostics.Stopwatch.GetTimestamp();
        var dump = Dump(rt);
        var walkTime = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        var (walkedCosmetics, walkedBook) = dump is null ? (null, null) : FromDump(JsonSerializer.SerializeToElement(dump), DateTime.UtcNow);
        Console.WriteLine($"generic walk: {walkTime.TotalMilliseconds:N0} ms");
        if (walkedCosmetics is null || walkedBook is null)
        {
            Console.WriteLine("the generic walk gave nothing the rules accept");
            return 3;
        }

        var differences = 0;
        differences += Report("card backs", Differ(cosmetics.CardBacks.ToDictionary(b => b.Id), walkedCosmetics.CardBacks.ToDictionary(b => b.Id)));
        differences += Report("coins", Differ(cosmetics.Coins.ToDictionary(c => c.Id), walkedCosmetics.Coins.ToDictionary(c => c.Id)));
        differences += Report("owned card backs", Differ(cosmetics.OwnedCardBacks, walkedCosmetics.OwnedCardBacks));
        differences += Report("favourite card backs", Differ(cosmetics.FavoriteCardBacks, walkedCosmetics.FavoriteCardBacks));
        differences += Report("owned coins", Differ(cosmetics.OwnedCoins, walkedCosmetics.OwnedCoins));
        differences += Report("favourite coins", Differ(cosmetics.FavoriteCoins, walkedCosmetics.FavoriteCoins));
        differences += Report("categories", Differ(book.Categories.ToDictionary(c => c.Id), walkedBook.Categories.ToDictionary(c => c.Id)));
        differences += Report("subcategories", Differ(book.Subcategories.ToDictionary(c => c.Id), walkedBook.Subcategories.ToDictionary(c => c.Id)));
        differences += Report("sections", Differ(book.Sections.ToDictionary(c => c.Id), walkedBook.Sections.ToDictionary(c => c.Id)));
        differences += Report("achievements", Differ(book.Achievements, walkedBook.Achievements));
        differences += Report("states", Differ(book.States, walkedBook.States));
        differences += Report("states (read of states only)", Differ(states.States, walkedBook.States));
        Console.WriteLine($"differences: {differences}");

        Directory.CreateDirectory(args[0]);
        new CosmeticsStore(Path.Combine(args[0], "cosmetics.json")).Save(cosmetics);
        new AchievementsStore(Path.Combine(args[0], "achievements.json")).Save(states.WithGainsSince(book));
        Console.WriteLine("written by the app's stores to " + args[0]);
        Counts(cosmetics, states);
        return differences == 0 ? 0 : 5;
    }

    /// <summary>The cosmetics and the book from a file of <c>cosmetics</c>, by the rules the app's read applies.</summary>
    private static (CosmeticsCollection? Cosmetics, AchievementBook? Book) FromDump(JsonElement root, DateTime readAt)
    {
        var cosmetics = CosmeticsCollection.FromClient(
            Rows(root, "cardBacks", r => new ClientCardBack(Int(r, "id"), Text(r, "name"), Text(r, "description"), Int(r, "sortCategory"),
                Int(r, "sortOrder"), Bool(r, "enabled"), Bool(r, "random"))),
            Ints(root, "ownedCardBacks"), Ints(root, "favoriteCardBacks"),
            Rows(root, "coins", r => new ClientCoin(Int(r, "id"), Int(r, "cardDbfId"), Text(r, "name"), Text(r, "description"), Bool(r, "enabled"))),
            Ints(root, "ownedCoins"), Ints(root, "favoriteCoins"),
            readAt);
        var book = AchievementBook.FromClient(
            Rows(root, "categories", r => new AchievementCategory(Int(r, "id"), Text(r, "name"), Text(r, "icon"), Int(r, "sortOrder"))),
            Rows(root, "subcategories", r => new AchievementSubcategory(Int(r, "id"), Int(r, "categoryId"), Text(r, "name"), Int(r, "sortOrder"))),
            Rows(root, "sections", r => new ClientSection(Int(r, "id"), Text(r, "name"))),
            Rows(root, "sectionItems", r => new ClientSectionItem(Int(r, "subcategoryId"), Int(r, "sectionId"), Int(r, "sortOrder"))),
            Rows(root, "achievements", r => new ClientAchievement(Int(r, "id"), Int(r, "sectionId"), Int(r, "sortOrder"), Bool(r, "enabled"),
                Bool(r, "retired"), Text(r, "name"), Text(r, "description"), Int(r, "visibility"), Int(r, "quota"), Int(r, "points"), Int(r, "nextTierId"))),
            Rows(root, "states", r => new ClientAchievementState(Int(r, "id"), Int(r, "status"), Int(r, "progress"), r.GetProperty("completedDate").GetInt64())),
            readAt);
        return (cosmetics, book);
    }

    private static int Differ<T>(IReadOnlyDictionary<int, T> app, IReadOnlyDictionary<int, T> walked) =>
        app.Keys.Union(walked.Keys).Count(id => !app.TryGetValue(id, out var a) || !walked.TryGetValue(id, out var w) || !Equals(a, w));

    private static int Differ(IReadOnlySet<int> app, IReadOnlySet<int> walked) => app.Count + walked.Count - (2 * app.Intersect(walked).Count());

    private static int Report(string what, int count)
    {
        Console.WriteLine($"  {what}: {count} differ");
        return count;
    }

    private static void Counts(CosmeticsCollection cosmetics, AchievementBook book)
    {
        Console.WriteLine($"cosmetics: card backs {cosmetics.CardBacks.Count} (owned {cosmetics.OwnedCardBacks.Count}, favourite {cosmetics.FavoriteCardBacks.Count}), "
            + $"coins {cosmetics.Coins.Count} (owned {cosmetics.OwnedCoins.Count}, favourite {cosmetics.FavoriteCoins.Count})");
        var summary = book.Summary;
        Console.WriteLine($"achievements: {book.Achievements.Count} in {book.Chains.Count} chains, states {book.States.Count}, "
            + $"completed {summary.Completed}/{summary.Total}, points {summary.PointsEarned}/{summary.PointsTotal}");
        foreach (var category in summary.Categories)
        {
            Console.WriteLine($"  category {category.Category.Id}: {category.Completed}/{category.Total}, points {category.PointsEarned}/{category.PointsTotal}");
        }
    }

    private static List<T> Rows<T>(JsonElement root, string name, Func<JsonElement, T> map) => root.GetProperty(name).EnumerateArray().Select(map).ToList();

    private static List<int> Ints(JsonElement root, string name) => root.GetProperty(name).EnumerateArray().Select(e => e.GetInt32()).ToList();

    private static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();

    private static bool Bool(JsonElement row, string name) => row.GetProperty(name).GetBoolean();

    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString() ?? string.Empty;

    private static List<Dictionary<string, object?>> Records(MonoClass gameDbf, string table, Func<MonoObject, Dictionary<string, object?>> map) =>
        gameDbf.GetStatic(table)["m_records"].ListItems().Where(r => !r.IsNull).Select(map).ToList();

    private static string? Loc(MonoObject record, string field) => record[field].String("m_currentLocaleValue");

    /// <summary>The values of a HashSet&lt;int&gt;: slots of {hashCode, next, value}, live where hashCode is not negative.</summary>
    private static List<int> IntSet(MonoRuntime rt, MonoObject set)
    {
        var slots = set["_slots"];
        var last = set.Int("_lastIndex");
        var values = new List<int>();
        for (var i = 0; i < last; i++)
        {
            var at = slots.Address + ArrayData + (ulong)(i * 12);
            var hash = rt.Memory.ReadInt32(at);
            var value = rt.Memory.ReadInt32(at + 8);
            if (hash >= 0)
            {
                values.Add(value);
                if (hash != (value & 0x7FFFFFFF))
                {
                    Console.WriteLine($"  slot {i}: hash {hash} != value {value}");
                }
            }
        }

        return values;
    }

    private static void Summary(Dictionary<string, object?> dump, TimeSpan took)
    {
        Console.WriteLine($"read in {took.TotalMilliseconds:N0} ms");
        foreach (var (key, value) in dump)
        {
            if (value is System.Collections.ICollection list)
            {
                Console.WriteLine($"  {key}: {list.Count}");
            }
        }

        var states = (List<Dictionary<string, object?>>)dump["states"]!;
        Console.WriteLine("  status: " + string.Join(", ", states.GroupBy(s => s["status"]).OrderBy(g => g.Key).Select(g =>
            $"{g.Key}: {g.Count()} (with date {g.Count(s => (bool)s["hasCompletedDate"]!)}, progress>0 {g.Count(s => (int)s["progress"]! > 0)})")));
        var achievements = ((List<Dictionary<string, object?>>)dump["achievements"]!).ToDictionary(a => (int)a["id"]!);
        Console.WriteLine($"  states without a record: {states.Count(s => !achievements.ContainsKey((int)s["id"]!))}, key != id: {states.Count(s => (int)s["key"]! != (int)s["id"]!)}");
        var progressOverQuota = states.Count(s => achievements.TryGetValue((int)s["id"]!, out var a) && (int)s["progress"]! > (int)a["quota"]!);
        Console.WriteLine($"  progress over quota: {progressOverQuota}");
        Console.WriteLine("  visibility: " + string.Join(", ", achievements.Values.GroupBy(a => a["visibility"]).Select(g => $"{g.Key}: {g.Count()}")));
        Console.WriteLine("  enabled/retired: " + achievements.Values.Count(a => (bool)a["enabled"]!) + "/" + achievements.Values.Count(a => (bool)a["retired"]!));
        Console.WriteLine("  points total " + achievements.Values.Where(a => (bool)a["enabled"]! && !(bool)a["retired"]!).Sum(a => (int)a["points"]!).ToString(CultureInfo.InvariantCulture));
        var dates = states.Where(s => (bool)s["hasCompletedDate"]!).Select(s => (long)s["completedDate"]!).ToList();
        if (dates.Count > 0)
        {
            Console.WriteLine($"  completed dates {DateTimeOffset.FromUnixTimeSeconds(dates.Min()):yyyy-MM-dd} .. {DateTimeOffset.FromUnixTimeSeconds(dates.Max()):yyyy-MM-dd}");
        }
    }
}

using System.Globalization;
using System.Text.Json;
using Hearthpulse.Core.Memory;

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
        var gameDbf = rt.FindClass("GameDbf");
        if (gameDbf is null)
        {
            Console.WriteLine("GameDbf not found");
            return 3;
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

        var took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        Summary(dump, took);
        if (args.Length > 0)
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = false }));
            Console.WriteLine("written " + args[0]);
        }

        return 0;
    }

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

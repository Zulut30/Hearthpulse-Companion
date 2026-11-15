using System.Globalization;
using System.Text.Json;
using Hearthpulse.Core.Arena;

namespace Hearthpulse.Core.Api;

/// <summary>Arena numbers: ArenaSmith card scores and class results (docs/arena-draft.md).</summary>
public sealed partial class HearthpulseData
{
    /// <summary>
    /// The compact card route: one row per card with only the asked fields (about 100 KB instead of the 1.7 MB dataset),
    /// <c>meta.game_type</c> (underground / normal), <c>meta.time_range</c> and <c>meta.fetched_at</c>. Every row is the
    /// all-classes slice; a card with too few games has <c>score: null</c>. <c>dbfId</c> is asked as well, so a deck
    /// code's card the card data does not know yet is still found.
    /// </summary>
    internal const string ArenaCardsRoute = "v1/arena/cards?fields=id,dbfId,score,tier,cardClass,win_rate";

    /// <summary>The published Arena card dataset, read when the compact route is not available.</summary>
    internal const string ArenaCardsPath = "datasets/hsreplay_arena_cards_advanced";

    /// <summary>Class win rates, pick rates and 7+ win shares.</summary>
    internal const string ArenaClassesPath = "v1/arena/classes?limit=20";

    /// <summary>The legendary packages of the Underground Arena: <c>groups[]</c> with a <c>key_card</c> and its <c>cards[]</c>.</summary>
    internal const string ArenaLegendariesPath = "datasets/hsreplay_arena_legendaries";

    /// <summary>The route answers up to 5000 rows a page (the whole list today); more pages than this are not followed.</summary>
    private const int ArenaRoutePages = 4;

    /// <summary>Card scores and class results; empty parts on unavailable data or a timeout. Explicit caller cancellation propagates.</summary>
    public async Task<ArenaMeta> LoadArenaMetaAsync(CancellationToken cancellationToken = default)
    {
        var cards = LoadArenaCardsAsync(cancellationToken);
        var classes = LoadArenaClassesAsync(cancellationToken);
        var packages = LoadArenaPackagesAsync(cancellationToken);
        var slice = await cards.ConfigureAwait(false);
        return new ArenaMeta(slice.Cards, await classes.ConfigureAwait(false), slice.FetchedAt, slice.Underground, slice.TimeRange,
            await packages.ConfigureAwait(false));
    }

    /// <summary>The compact route first; the dataset when the route gave no card (not deployed, offline with no copy of it).</summary>
    private async Task<ArenaCardSlice> LoadArenaCardsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await LoadArenaRouteAsync(cancellationToken).ConfigureAwait(false) is { Cards.Count: > 0 } route)
            {
                return route;
            }

            using var doc = await FetchAsync(ArenaCardsPath, "arena-cards.json", cancellationToken, ShrinkArenaCards).ConfigureAwait(false);
            return doc is null ? ArenaCardSlice.Empty : Read(() =>
            {
                var (list, fetchedAt) = ParseArenaCards(Data(doc));
                return new ArenaCardSlice(list, list.Count, null, fetchedAt, ParseArenaSlice(Data(doc)), null);
            }, "arena cards", ArenaCardSlice.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ArenaCardSlice.Empty;
        }
    }

    /// <summary>
    /// Reads the route page by page (each page has its own cache file) until the server's <c>meta.count</c> is reached.
    /// A later page that fails leaves the cards read so far: those beyond it show no score.
    /// </summary>
    private async Task<ArenaCardSlice?> LoadArenaRouteAsync(CancellationToken cancellationToken)
    {
        ArenaCardSlice? all = null;
        var rows = 0;
        for (var page = 0; page < ArenaRoutePages; page++)
        {
            var path = page == 0 ? ArenaCardsRoute : ArenaCardsRoute + "&offset=" + rows.ToString(CultureInfo.InvariantCulture);
            var cache = page == 0 ? "arena-cards-route.json" : $"arena-cards-route-{page.ToString(CultureInfo.InvariantCulture)}.json";
            using var doc = await FetchAsync(path, cache, cancellationToken).ConfigureAwait(false);
            var part = doc is null ? null : Read(() => ParseArenaRoute(doc.RootElement), "arena cards route", (ArenaCardSlice?)null);
            if (part is null || part.Rows == 0)
            {
                break;
            }

            rows += part.Rows;
            all = all is null ? part : all with { Cards = [.. all.Cards, .. part.Cards], Rows = rows };
            if (part.Total is not { } total || rows >= total)
            {
                break;
            }
        }

        return all;
    }

    /// <summary>The legendary packages by key card; empty when the server and the cache have none.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadArenaPackagesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> empty = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        try
        {
            using var doc = await FetchAsync(ArenaLegendariesPath, "arena-legendaries.json", cancellationToken, ShrinkArenaLegendaries).ConfigureAwait(false);
            return doc is null ? empty : Read(() => ParseArenaLegendaries(Data(doc)), "arena legendaries", empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return empty;
        }
    }

    private async Task<IReadOnlyList<ArenaClassStat>> LoadArenaClassesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await FetchAsync(ArenaClassesPath, "arena-classes.json", cancellationToken).ConfigureAwait(false);
            return doc is null ? [] : Read(() => ParseArenaClasses(Data(doc)), "arena classes", (IReadOnlyList<ArenaClassStat>)[]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>
    /// The card dataset is about 1.7 MB, most of it repeated (the list is published twice) or unused by the app: the
    /// cache keeps the fetch time and, per card, the id, dbf id, score, win rates (deck, drawn, played), pick rate and games.
    /// </summary>
    internal static object ShrinkArenaCards(JsonElement data)
    {
        var structured = Structured(data);
        if (structured.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?> { ["cards"] = Array.Empty<object>() };
        }

        var cards = Items(structured.TryGetProperty("cards", out var list) ? list : default)
            .Select(c => new Dictionary<string, object?>
            {
                ["id"] = Str(c, "card_id") ?? Str(c, "id"),
                ["dbfId"] = Num(c, "dbfId"),
                ["score"] = Num(c, "score"),
                ["win_rate"] = Num(c, "win_rate"),
                ["pick_rate"] = Num(c, "pick_rate"),
                ["times_played"] = Num(c, "times_played"),
                ["winrate_when_drawn"] = PercentOrNull(c, "winrate_when_drawn"),
                ["winrate_when_played"] = PercentOrNull(c, "winrate_when_played"),
            })
            .ToList();
        var observed = structured.TryGetProperty("upstream_freshness", out var fresh) ? Str(fresh, "observed_at") : null;
        return new Dictionary<string, object?> { ["fetched_at"] = observed, ["game_type"] = GameTypeOf(structured), ["cards"] = cards };
    }

    /// <summary>
    /// Parses the card answer, whole (<c>structured.cards</c>) or as the cache keeps it (<c>cards</c>). Percentages
    /// arrive as 0-100 (the drawn / played win rates as "64.40%" text); a card without an id is skipped.
    /// </summary>
    internal static (IReadOnlyList<ArenaCardScore> Cards, DateTime? FetchedAt) ParseArenaCards(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return ([], null);
        }

        var root = data.TryGetProperty("cards", out _) ? data : Structured(data);
        var fetchedAt = Str(root, "fetched_at")
                        ?? (root.TryGetProperty("upstream_freshness", out var fresh) ? Str(fresh, "observed_at") : null);
        var cards = new List<ArenaCardScore>();
        foreach (var c in Items(root.TryGetProperty("cards", out var list) ? list : default))
        {
            var id = Str(c, "card_id") ?? Str(c, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            cards.Add(new ArenaCardScore(
                id,
                (int)(Num(c, "dbfId") ?? 0),
                Num(c, "score") is { } score && double.IsFinite(score) ? score : null,
                Num(c, "win_rate") / 100,
                Num(c, "pick_rate") / 100,
                (int)(Num(c, "times_played") ?? 0))
            {
                DrawnWinRate = PercentOrNull(c, "winrate_when_drawn") / 100,
                PlayedWinRate = PercentOrNull(c, "winrate_when_played") / 100,
            });
        }

        return (cards, ParseTime(fetchedAt));
    }

    /// <summary>
    /// Parses one page of the compact route as the cache keeps it: <c>data[]</c> (<c>id</c>, <c>dbfId</c>, <c>score</c> or
    /// null, <c>tier</c>, <c>cardClass</c>, <c>win_rate</c> 0-100) and <c>meta</c> (<c>count</c>, <c>game_type</c>,
    /// <c>time_range</c>, <c>fetched_at</c>). A row without an id is skipped but counted, so the next page starts right.
    /// </summary>
    internal static ArenaCardSlice ParseArenaRoute(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return ArenaCardSlice.Empty;
        }

        var meta = root.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var cards = new List<ArenaCardScore>();
        var rows = 0;
        foreach (var c in data.EnumerateArray())
        {
            rows++;
            var id = c.ValueKind == JsonValueKind.Object ? Str(c, "id") ?? Str(c, "card_id") : null;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            cards.Add(new ArenaCardScore(
                id,
                (int)(Num(c, "dbfId") ?? 0),
                // A missing score stays missing: the card is listed without a number, never as 0.
                Num(c, "score") is { } score && double.IsFinite(score) ? score : null,
                Num(c, "win_rate") / 100,
                null,
                0)
            {
                CardClass = Str(c, "cardClass"),
                SourceTier = Str(c, "tier"),
            });
        }

        bool? underground = Str(meta, "game_type") switch
        {
            "underground" => true,
            "normal" => false,
            _ => null,
        };
        return new ArenaCardSlice(cards, rows, Num(meta, "count") is { } count ? (int)count : null, ParseTime(Str(meta, "fetched_at")), underground,
            Str(meta, "time_range"));
    }

    /// <summary>The legendaries dataset is about 600 KB of card details and rates: the cache keeps the key card and its cards' ids.</summary>
    internal static object ShrinkArenaLegendaries(JsonElement data) => new Dictionary<string, object?>
    {
        ["groups"] = ParseArenaLegendaries(data)
            .Select(g => new Dictionary<string, object?> { ["key"] = g.Key, ["cards"] = g.Value })
            .ToList(),
    };

    /// <summary>
    /// Parses the legendary packages, whole (<c>structured.groups[]</c>: <c>key_card.id</c>, <c>cards[]</c> with
    /// <c>id</c> and <c>count</c>) or as the cache keeps them (<c>groups[]</c>: <c>key</c>, <c>cards</c> as ids, a copy
    /// each). A group without a key card or without cards is skipped; the first group of a key card wins.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseArenaLegendaries(JsonElement data)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var root = data.TryGetProperty("groups", out _) ? data : Structured(data);
        foreach (var group in Items(root.TryGetProperty("groups", out var groups) ? groups : default))
        {
            var key = Str(group, "key")
                      ?? (group.TryGetProperty("key_card", out var keyCard) ? Str(keyCard, "id") ?? Str(keyCard, "card_id") : null);
            if (string.IsNullOrEmpty(key) || !group.TryGetProperty("cards", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var cards = new List<string>();
            foreach (var card in list.EnumerateArray())
            {
                if (card.ValueKind == JsonValueKind.String)
                {
                    cards.Add(card.GetString()!);
                }
                else if (card.ValueKind == JsonValueKind.Object && (Str(card, "id") ?? Str(card, "card_id")) is { Length: > 0 } id)
                {
                    cards.AddRange(Enumerable.Repeat(id, Math.Clamp((int)(Num(card, "count") ?? 1), 1, ArenaDraft.DeckSize)));
                }
            }

            if (cards.Count > 0)
            {
                result.TryAdd(key, cards);
            }
        }

        return result;
    }

    /// <summary>Parses <c>v1/arena/classes</c>: <c>class</c>, <c>win_rate</c>, <c>pick_rate</c>, <c>pct_7_plus</c> (0-100), <c>num_drafts</c>.</summary>
    internal static IReadOnlyList<ArenaClassStat> ParseArenaClasses(JsonElement data)
    {
        var result = new List<ArenaClassStat>();
        foreach (var c in Items(data))
        {
            if (ArenaMeta.ClassKey(Str(c, "class") ?? Str(c, "slug")) is not { } key || Num(c, "win_rate") is not { } winRate)
            {
                continue;
            }

            result.Add(new ArenaClassStat(key, winRate / 100, Num(c, "pick_rate") / 100, Num(c, "pct_7_plus") / 100, (int)(Num(c, "num_drafts") ?? 0)));
        }

        return result;
    }

    /// <summary>
    /// Which Arena the card slice is: Underground (true) for <c>BGT_UNDERGROUND_ARENA</c>, normal (false) for
    /// <c>BGT_ARENA</c>, null when the answer does not say. The whole answer names it in the source's request
    /// (<c>ArenaGameTypeFilter=...</c>), the cache keeps it as <c>game_type</c>.
    /// </summary>
    internal static bool? ParseArenaSlice(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = data.TryGetProperty("cards", out _) ? data : Structured(data);
        return (Str(root, "game_type") ?? GameTypeOf(root)) switch
        {
            "BGT_UNDERGROUND_ARENA" => true,
            "BGT_ARENA" => false,
            _ => null,
        };
    }

    private static string? GameTypeOf(JsonElement structured)
    {
        var url = structured.ValueKind == JsonValueKind.Object && structured.TryGetProperty("source", out var source) ? Str(source, "api_url") : null;
        var match = url is null ? null : ArenaGameTypeFilter.Match(url);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"ArenaGameTypeFilter=(BGT_[A-Z_]+)")]
    private static partial System.Text.RegularExpressions.Regex ArenaGameTypeFilter { get; }

    /// <summary>"64.40%" or 64.4 as 64.4; null when the field is missing or not a number.</summary>
    private static double? PercentOrNull(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String
        && (v.ValueKind == JsonValueKind.Number || double.TryParse(v.GetString()!.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            ? Percent(v)
            : null;

    private static JsonElement Structured(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("structured", out var s) && s.ValueKind == JsonValueKind.Object ? s : data;

    private static DateTime? ParseTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}

/// <summary>The Arena card scores of one answer (a page of the route, or the dataset) with what the answer says about itself.</summary>
/// <param name="Rows">Rows the answer held, those without an id included: where the next page starts.</param>
/// <param name="Total">The server's count of rows over all pages, when it says.</param>
/// <param name="Underground">Underground (true) or normal (false) Arena; null when the answer does not say.</param>
/// <param name="TimeRange">The period the numbers cover as the server names it (LAST_4_DAYS), or null.</param>
internal sealed record ArenaCardSlice(IReadOnlyList<ArenaCardScore> Cards, int Rows, int? Total, DateTime? FetchedAt, bool? Underground, string? TimeRange)
{
    public static ArenaCardSlice Empty { get; } = new([], 0, null, null, null, null);
}

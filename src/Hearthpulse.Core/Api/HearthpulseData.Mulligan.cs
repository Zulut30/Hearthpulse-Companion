using System.Globalization;
using System.Text.Json;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Api;

/// <summary>
/// The per-deck mulligan (<c>/v1/hsreplay/mulligan</c>, docs/mulligan-v2.md 2.2): the decks the server collects and one
/// deck's slice by rank, initiative and opponent class. Same disk cache as every other answer (6 h, a 404 remembered).
/// These loaders never throw, a cancelled token included: they answer "nothing" and the archetype guide speaks.
/// </summary>
public sealed partial class HearthpulseData
{
    /// <summary>The ranks the per-deck mulligan is collected for (the server answers 422 for any other).</summary>
    public static readonly IReadOnlyList<string> DeckMulliganRanks = ["legend", "diamond_4to1", "diamond", "bronze_gold"];

    private static readonly HashSet<string> DeckInitiatives = new(StringComparer.Ordinal) { DeckMulliganSlice.AllGames, "first", "coin" };

    /// <summary>Deck ids (HSReplay's short ids, mixed case) that may go into a URL and a cache file name.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9]{1,40}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex DeckIdKey { get; }

    /// <summary>
    /// The decks of a format ("standard", "wild") the server collects the per-deck mulligan for: one row per deck and
    /// rank, with the deck's card list (an answer cached before 2026-11-15 has none). Empty when neither the server nor
    /// the cache answers, or the caller cancelled.
    /// </summary>
    public async Task<IReadOnlyList<MulliganDeckEntry>> LoadHsreplayMulliganDecksAsync(string format, CancellationToken cancellationToken = default)
    {
        if (!SafeKey.IsMatch(format))
        {
            return [];
        }

        try
        {
            using var doc = await FetchAsync($"v1/hsreplay/mulligan/decks?format={format}&limit=500", $"hsreplay-mulligan-decks-{format}.json", cancellationToken).ConfigureAwait(false);
            return doc is null ? [] : Read(() => ParseMulliganDecks(Data(doc)), "mulligan decks", new List<MulliganDeckEntry>());
        }
        catch (OperationCanceledException)
        {
            // The caller gave up (a timeout is "offline" inside the API client): nothing, like any other miss.
            return [];
        }
    }

    /// <summary>
    /// One slice of a collected deck's mulligan (last 30 days): a rank of <see cref="DeckMulliganRanks"/>, the initiative
    /// ("all", "first", "coin") and the opponent's class ("any" or "MAGE"). Null when the server has not collected the
    /// slice (a 404, remembered like any answer), holds no data for it, a parameter is not one of the API's keys, or the
    /// caller cancelled.
    /// </summary>
    public async Task<DeckMulliganSlice?> LoadHsreplayDeckMulliganAsync(string deckId, string format, string rank, string initiative = DeckMulliganSlice.AllGames,
        string opponentClass = MulliganGuide.AnyOpponent, CancellationToken cancellationToken = default)
    {
        var opponent = opponentClass.ToLowerInvariant();
        if (!DeckIdKey.IsMatch(deckId) || !SafeKey.IsMatch(format) || !DeckMulliganRanks.Contains(rank) || !DeckInitiatives.Contains(initiative) || !SafeKey.IsMatch(opponent))
        {
            return null;
        }

        // The server takes "any" or an upper-case class and answers 422 for anything else.
        var server = opponent == MulliganGuide.AnyOpponent ? opponent : opponent.ToUpperInvariant();
        // Deck ids differ by case and Windows file names do not: the hash keeps two such ids apart.
        var file = $"hsreplay-mulligan-{format}-{deckId}-{CaseHash(deckId):x8}-{rank}-{initiative}-{opponent}.json";
        try
        {
            using var doc = await FetchAsync($"v1/hsreplay/mulligan?deck={deckId}&format={format}&rank={rank}&initiative={initiative}&opponent_class={server}", file, cancellationToken).ConfigureAwait(false);
            return doc is null
                ? null
                : Read(() => ParseDeckMulligan(Data(doc), doc.RootElement.TryGetProperty("meta", out var meta) ? meta : default, deckId), "deck mulligan", (DeckMulliganSlice?)null);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>[{"deck_id", "archetype_name", "player_class", "rank", "total_games", "win_rate", "deck_list"}]</c>; win rates
    /// become fractions, <c>deck_list</c> is read in either shape (<see cref="ParseDeckList"/>) and is null when the row
    /// has none. Rows without a usable deck id, class or rank are skipped.
    /// </summary>
    internal static List<MulliganDeckEntry> ParseMulliganDecks(JsonElement root)
    {
        var result = new List<MulliganDeckEntry>();
        foreach (var row in Items(root))
        {
            if (Str(row, "deck_id") is { } id && DeckIdKey.IsMatch(id) && Str(row, "player_class") is { Length: > 0 } playerClass
                && Str(row, "rank") is { } rank && DeckMulliganRanks.Contains(rank))
            {
                result.Add(new MulliganDeckEntry(id, Str(row, "archetype_name") ?? string.Empty, playerClass.ToUpperInvariant(), rank,
                    (int)(Num(row, "total_games") ?? 0), (Num(row, "win_rate") ?? 0) / 100)
                {
                    DeckList = row.TryGetProperty("deck_list", out var listed) && ParseDeckList(listed) is { Count: > 0 } list ? list : null,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// <c>{"rank", "initiative", "opponent_class", "games", "base_winrate", "as_of", "no_data", "deck": {"deck_list",
    /// "archetype_id", "archetype_name", "player_class"}, "rows": [{card_id, dbf_id, keep_percentage,
    /// opening_hand_winrate, winrate_when_drawn, avg_turn_played_on}]}</c>; percentages become fractions. The answer has
    /// no per-card counts: they are estimated from the games (<see cref="DealtShare"/>). Null without rows, games, the
    /// deck's win rate or its list.
    /// </summary>
    internal static DeckMulliganSlice? ParseDeckMulligan(JsonElement root, JsonElement meta, string deckId)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rows", out var rows)
            || (root.TryGetProperty("no_data", out var noData) && noData.ValueKind == JsonValueKind.True)
            || Num(root, "games") is not { } games || games < 1 || Num(root, "base_winrate") is not { } baseWinRate
            || !root.TryGetProperty("deck", out var deck) || deck.ValueKind != JsonValueKind.Object || !deck.TryGetProperty("deck_list", out var listed)
            || ParseDeckList(listed) is not { Count: > 0 } list)
        {
            return null;
        }

        var initiative = Str(root, "initiative") is { } i && DeckInitiatives.Contains(i) ? i : DeckMulliganSlice.AllGames;
        var size = list.Values.Sum();
        var cards = new List<GuideCard>();
        foreach (var row in Items(rows))
        {
            if (Str(row, "card_id") is not { Length: > 0 } id)
            {
                continue;
            }

            var dbf = (int)(Num(row, "dbf_id") ?? 0);
            var dealt = (int)Math.Round(games * DealtShare(list.GetValueOrDefault(dbf, 1), size, initiative));
            cards.Add(new GuideCard(id, dbf, 0, dealt, (Num(row, "keep_percentage") ?? 0) / 100, dealt, Num(row, "opening_hand_winrate") / 100, Num(row, "avg_turn_played_on"))
            {
                Drawn = dealt,
                DrawnWinRate = Num(row, "winrate_when_drawn") / 100,
            });
        }

        if (cards.Count == 0)
        {
            return null;
        }

        var opponent = Str(root, "opponent_class") is { Length: > 0 } o && !string.Equals(o, MulliganGuide.AnyOpponent, StringComparison.OrdinalIgnoreCase)
            ? o.ToUpperInvariant()
            : MulliganGuide.AnyOpponent;
        return new DeckMulliganSlice(deckId, Str(root, "rank") ?? string.Empty, initiative, opponent, (int)games, baseWinRate / 100,
            Time(Str(root, "as_of")) ?? Time(Str(meta, "fetched_at")), list, cards)
        {
            ArchetypeId = (int)(Num(deck, "archetype_id") ?? 0),
            ArchetypeName = Str(deck, "archetype_name") ?? string.Empty,
            PlayerClass = (Str(deck, "player_class") ?? string.Empty).ToUpperInvariant(),
        };
    }

    /// <summary>
    /// <c>[[dbfId, count], ...]</c>, as an array (the rows of the list of decks) or as the JSON text of one (the slice's
    /// <c>deck.deck_list</c>). Null when it is neither.
    /// </summary>
    internal static Dictionary<int, int>? ParseDeckList(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(value.GetString()!);
                return ParseDeckList(doc.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new Dictionary<int, int>();
        foreach (var pair in Items(value, JsonValueKind.Array))
        {
            if (pair.GetArrayLength() == 2 && pair[0].ValueKind == JsonValueKind.Number && pair[1].ValueKind == JsonValueKind.Number
                && pair[0].TryGetInt32(out var dbf) && pair[1].TryGetInt32(out var count) && dbf > 0 && count is > 0 and <= 40)
            {
                result[dbf] = result.GetValueOrDefault(dbf) + count;
            }
        }

        return result;
    }

    /// <summary>
    /// The chance that at least one of <paramref name="copies"/> copies is among the starting cards of a deck of
    /// <paramref name="deckSize"/>: 3 cards going first, 4 on the coin, their mean over all games.
    /// </summary>
    internal static double DealtShare(int copies, int deckSize, string initiative)
    {
        double Share(int dealt)
        {
            var none = 1.0;
            for (var i = 0; i < dealt; i++)
            {
                none *= Math.Max(0, deckSize - copies - i) / (double)Math.Max(1, deckSize - i);
            }

            return 1 - none;
        }

        return initiative switch
        {
            "first" => Share(3),
            "coin" => Share(4),
            _ => (Share(3) + Share(4)) / 2,
        };
    }

    private static DateTime? Time(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;

    /// <summary>A case-sensitive FNV-1a hash of an id, for file names on a case-insensitive disk.</summary>
    private static uint CaseHash(string id)
    {
        var hash = 2166136261u;
        foreach (var ch in id)
        {
            hash = (hash ^ ch) * 16777619u;
        }

        return hash;
    }
}

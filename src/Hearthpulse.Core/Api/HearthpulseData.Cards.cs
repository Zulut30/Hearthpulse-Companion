using System.Text.Json;
using Hearthpulse.Core.Constructed;

namespace Hearthpulse.Core.Api;

/// <summary>Constructed card numbers for the card page (docs/card-page.md).</summary>
public sealed partial class HearthpulseData
{
    /// <summary>
    /// The stored card list of a format. Standard: Gold, last 14 days (the source's name says "legend", its answer says
    /// <c>rank_range: GOLD</c>; <c>…_included_winrate</c> holds the same rows in another order). Wild: Legend, last 14
    /// days. The page names the slice from the answer, never from the source id. Null for a format without a list.
    /// </summary>
    internal static string? CardStatsSource(string? format) => format switch
    {
        "standard" => "hsreplay_cards_legend_included_popularity",
        "wild" => "hsreplay_cards_wild_legend_14d",
        _ => null,
    };

    /// <summary>
    /// Every card's numbers in one format ("standard", "wild"): share of decks, deck win rate, win rate when drawn and
    /// played, mulligan keep rate. One request per format (1.3 MB Standard, 4 MB Wild), kept trimmed
    /// (<see cref="ShrinkCardStats"/>) for six hours; offline the old copy is served with its own date. Null when the
    /// server and the cache have nothing, or the format has no list. Never throws: a cancelled load gives null too.
    /// </summary>
    public async Task<CardStatsSlice?> LoadCardStatsAsync(string format, CancellationToken cancellationToken = default)
    {
        if (CardStatsSource(format) is not { } source)
        {
            return null;
        }

        try
        {
            using var doc = await FetchAsync("datasets/" + source, $"card-stats-{format}.json", cancellationToken, ShrinkCardStats).ConfigureAwait(false);
            return doc is null ? null : Read(() => ParseCardStats(doc.RootElement, format), "card stats", (CardStatsSlice?)null);
        }
        catch (OperationCanceledException)
        {
            // The page that asked is gone (or the server took too long): no numbers, and nothing for the caller to catch.
            return null;
        }
    }

    /// <summary>The cached row's columns, in order; a missing number is null.</summary>
    private static readonly string[] CardStatColumns =
    [
        "deck_popularity", "deck_winrate", "avg_copies", "times_played", "winrate_when_played", "winrate_when_drawn", "keep_percentage",
        "opening_hand_winrate", "avg_turn_played_on",
    ];

    /// <summary>
    /// The list is published twice in the answer (<c>structured</c> and <c>hsreplay_extracted</c>) with card names and
    /// Battlegrounds fields the page does not read: the cache keeps the slice (<c>game_type</c>, <c>rank_range</c>,
    /// <c>time_range</c>) and one array per card - id, dbf id, then <see cref="CardStatColumns"/> as numbers.
    /// </summary>
    internal static object ShrinkCardStats(JsonElement data)
    {
        var structured = Structured(data);
        var rows = new List<object?[]>();
        foreach (var c in Items(structured.ValueKind == JsonValueKind.Object && structured.TryGetProperty("cards", out var list) ? list : default))
        {
            if (Str(c, "id") is not { Length: > 0 } id)
            {
                continue;
            }

            var row = new object?[2 + CardStatColumns.Length];
            row[0] = id;
            row[1] = (int)(Num(c, "dbfId") ?? 0);
            for (var i = 0; i < CardStatColumns.Length; i++)
            {
                row[2 + i] = PercentOrNull(c, CardStatColumns[i]);
            }

            rows.Add(row);
        }

        return new Dictionary<string, object?>
        {
            ["game_type"] = Str(structured, "game_type"),
            ["rank_range"] = Str(structured, "rank_range"),
            ["time_range"] = Str(structured, "time_range"),
            ["cards"] = rows,
        };
    }

    /// <summary>
    /// Parses a cached card list (<c>{"data": ..., "meta": {"fetched_at"}}</c>): <c>data</c> whole
    /// (<c>structured.cards[]</c> objects, percentages as "55.13%") or as the cache keeps it (arrays). Percentages
    /// become fractions; a card played 0 times has no "when played" number, and that stays missing.
    /// </summary>
    internal static CardStatsSlice ParseCardStats(JsonElement root, string format)
    {
        var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : default;
        var body = Structured(data);
        var cards = new Dictionary<string, CardStat>(StringComparer.Ordinal);
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("cards", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in list.EnumerateArray())
            {
                if (ParseCardStat(c) is { } stat)
                {
                    // The first row of a card wins: the list is sorted by the source, most played first.
                    cards.TryAdd(stat.CardId, stat);
                }
            }
        }

        var meta = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("meta", out var m) ? m : default;
        return new CardStatsSlice(format, Str(body, "rank_range"), Str(body, "time_range"), ParseTime(Str(meta, "fetched_at")), cards);
    }

    private static CardStat? ParseCardStat(JsonElement c)
    {
        string? id;
        int dbf;
        var values = new double?[CardStatColumns.Length];
        if (c.ValueKind == JsonValueKind.Array)
        {
            var row = c.EnumerateArray().ToArray();
            if (row.Length < 2 + CardStatColumns.Length || row[0].ValueKind != JsonValueKind.String)
            {
                return null;
            }

            id = row[0].GetString();
            dbf = row[1].ValueKind == JsonValueKind.Number && row[1].TryGetInt32(out var n) ? n : 0;
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = row[2 + i].ValueKind == JsonValueKind.Number ? row[2 + i].GetDouble() : null;
            }
        }
        else if (c.ValueKind == JsonValueKind.Object)
        {
            id = Str(c, "id");
            dbf = (int)(Num(c, "dbfId") ?? 0);
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = PercentOrNull(c, CardStatColumns[i]);
            }
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var played = values[3] is { } times && double.IsFinite(times) && times >= 0 ? (int?)Math.Min(times, int.MaxValue) : null;
        return new CardStat(
            id,
            dbf,
            Share(values[0]),
            Share(values[1]),
            values[2] is { } copies && double.IsFinite(copies) && copies > 0 ? copies : null,
            played,
            // Without a play there is no game to take a win rate from, whatever the row says.
            played is > 0 ? Share(values[4]) : null,
            Share(values[5]),
            Share(values[6]),
            Share(values[7]),
            played is > 0 && values[8] is { } turn && double.IsFinite(turn) && turn > 0 ? turn : null);
    }
}

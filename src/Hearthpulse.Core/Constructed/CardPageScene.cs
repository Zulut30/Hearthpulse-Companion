using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;

namespace Hearthpulse.Core.Constructed;

/// <summary>
/// Made-up data for the card page's render scenes (<c>HEARTHPULSE_RENDER_ONLY=card</c>, docs/card-page.md): the states a
/// live render cannot be asked for - the player's own games with a card, and an older copy of the card list under a
/// late source. Nothing here reaches a page outside the render check.
/// </summary>
public static class CardPageScene
{
    /// <summary>When the scene's older copy of the list was collected.</summary>
    public static DateTime Collected { get; } = new(2026, 11, 13, 6, 4, 0, DateTimeKind.Utc);

    /// <summary>
    /// Twelve Standard games of the player: the card offered in five opening hands and kept in three, drawn in nine
    /// games (6-3), played in seven (5-2); two games without the card and one saved before card data.
    /// </summary>
    public static IReadOnlyList<GameRecord> Games(string cardId, string formatType = "FT_STANDARD")
    {
        // offered, kept, drawn, played, won
        (bool Offered, bool Kept, bool Drawn, bool Played, bool Won)[] games =
        [
            (true, true, true, true, true), (true, true, true, true, true), (true, true, true, true, false), (true, false, true, true, true),
            (true, false, true, false, false), (false, false, true, true, true), (false, false, true, true, true), (false, false, true, true, false),
            (false, false, true, false, true), (false, false, false, false, true), (false, false, false, false, false),
        ];
        var start = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Local);
        var list = games.Select((g, i) => new GameRecord
        {
            Key = "card-scene-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartedAt = start.AddMinutes(i * 12),
            EndedAt = start.AddMinutes(i * 12 + 9),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = formatType,
            Result = g.Won ? "Win" : "Loss",
            Mulligan = GameCards.MulliganJson(new MulliganFacts(g.Offered ? [cardId] : [], g.Offered && !g.Kept ? [cardId] : [], [])),
            CardsDrawn = GameCards.TurnsJson(g.Drawn ? [new CardTurn(cardId, g.Kept ? 0 : 3)] : [new CardTurn("SCENE_OTHER", 1)]),
            CardsPlayed = GameCards.TurnsJson(g.Played ? [new CardTurn(cardId, 4)] : null),
        }).ToList();
        list.Add(new GameRecord { Key = "card-scene-old", StartedAt = start.AddDays(-30), EndedAt = start.AddDays(-30).AddMinutes(8), Mode = "Ranked", Format = formatType, Result = "Win" });
        return list;
    }

    /// <summary>An older copy of a format's list holding the card: collected at <see cref="Collected"/>, Gold or Legend, 14 days.</summary>
    public static CardStatsSlice StaleSlice(string format, Card card) => new(format, format == "wild" ? "LEGEND" : "GOLD", "LAST_14_DAYS", Collected,
        new Dictionary<string, CardStat>(StringComparer.Ordinal)
        {
            [card.Id] = new(card.Id, card.DbfId, 0.119, 0.571, 1.92, 64038, 0.509, 0.523, 0.48, 0.537, 5.1),
        });

    /// <summary>A data summary where the format's card list is late since <see cref="Collected"/> and everything else is unknown.</summary>
    public static DataHealthReport LateReport(string format) => new(
        HearthpulseData.CardStatsSource(format) is { } source ? [new DataSourceHealth(source, DataFreshness.Stale, 78, 30, Collected, null)] : [],
        Collected.AddHours(78));
}

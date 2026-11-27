using System.Globalization;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The deck panel's subtitle: the player's own record with the deck, else its archetype's community win rate, else no
/// line at all (O-7).
/// </summary>
public static class DeckHeader
{
    /// <summary>
    /// Subtitle text, its tooltip, and whether it comes from community stats rather than the player's games; all empty
    /// without either. The meta's rank filter is named in the tooltip only.
    /// </summary>
    public static (string Text, string Tooltip, bool FromMeta) Describe(DeckAggregate? own, DeckMetaStat? meta)
    {
        // Ties and unknown results alone give no win rate: fall through to the meta.
        if (own is not null && own.Wins + own.Losses > 0)
        {
            return (Loc.F("Core.Deck.Record", Num(own.Wins), Num(own.Losses), Percent(own.WinRate)),
                Loc.F("Core.Deck.RecordTip", Num(own.Games)),
                false);
        }

        if (meta is not null)
        {
            return (Loc.F("Core.Deck.Meta", Percent(meta.WinRate)),
                Loc.F("Core.Deck.MetaTip", meta.Archetype, Percent(meta.WinRate), meta.Games.ToString("N0", CultureInfo.InvariantCulture), meta.Format, meta.Rank, meta.Source),
                true);
        }

        return (string.Empty, string.Empty, false);
    }

    // Counts as before localization: invariant, so the text does not depend on the machine's culture.
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Percentages in the culture's pattern, like every other percentage on the overlay ("55%" / "55 %").
    private static string Percent(double rate) => rate.ToString("P0", CultureInfo.CurrentCulture);
}

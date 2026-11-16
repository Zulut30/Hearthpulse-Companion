#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

// What both UIs show of the player's collection (docs/collection.md, "In the interface"): the build label of a deck,
// its missing cards and the collection's status line. Built from DeckCost; a view decides nothing itself.

/// <summary>One missing card of a deck: "2 × Name", its price ("2 × 400", or "can't be crafted") and what makes it an estimate.</summary>
public sealed record MissingCardRow(string CardId, string Name, string Price, string Note) : IImmutableView
{
    public bool HasNote => Note.Length > 0;

    /// <summary>The row as one sentence, for a screen reader and the label's tooltip.</summary>
    public string Line => HasNote ? Loc.F("Collection.Missing.LineNote", Name, Price, Note) : Loc.F("Collection.Missing.Line", Name, Price);
}

/// <summary>
/// The answer to "can I build this deck" as a label: a glyph and words (never a colour alone), what makes it an
/// estimate, and the missing cards behind it. Made only for a known answer: <see cref="DeckBuild.Unknown"/> has no label.
/// </summary>
/// <param name="Kind">The state's name ("Owned", "CraftableNow", "NeedsDust", "NotCraftable").</param>
/// <param name="Glyph">The icon beside the words.</param>
/// <param name="Label">"All cards owned", "Craftable: 1,200 dust", "400 dust short", "Can't be crafted".</param>
/// <param name="Marks">"estimate", "saved collection" or both; empty for a plain answer from a confirmed collection.</param>
/// <param name="Tip">Why the label says so: the dust at hand, why it is an estimate, that the collection is a saved one.</param>
/// <param name="Saved">The collection is not confirmed: the label is shown dimmed and says so in <see cref="Marks"/>.</param>
/// <param name="Missing">The cards the collection lacks.</param>
/// <param name="Total">The sum line under the missing cards; empty with none.</param>
public sealed record DeckBuildView(
    string Kind,
    string Glyph,
    string Label,
    string Marks,
    string Tip,
    bool Saved,
    IReadOnlyList<MissingCardRow> Missing,
    string Total) : IImmutableView
{
    /// <summary>A label by a saved collection is dimmed: it is not a statement about the collection as it is now.</summary>
    public const double SavedOpacity = 0.72;

    public bool HasMarks => Marks.Length > 0;

    public bool HasMissing => Missing.Count > 0;

    public bool HasTip => Tip.Length > 0;

    public double Opacity => Saved ? SavedOpacity : 1;

    /// <summary>The label with its marks: the accessible name of the label's button, starting with the visible words.</summary>
    public string Name => HasMarks ? Loc.F("Collection.Label.WithMarks", Label, Marks) : Label;

    /// <summary>The label, its reason and the missing cards as text: the help text of the label's button.</summary>
    public string Details => string.Join(
        Environment.NewLine,
        new[] { Name, Tip }.Concat(Missing.Select(m => m.Line)).Append(Total).Where(line => line.Length > 0));
}

/// <summary>
/// The collection block of the own deck page: the status line (always), the label and missing cards when the deck
/// could be priced, and otherwise why it could not.
/// </summary>
public sealed record DeckCollectionView(string Status, DeckBuildView? Build, string Note) : IImmutableView
{
    public static DeckCollectionView Empty { get; } = new(string.Empty, null, string.Empty);

    public bool HasBuild => Build is not null;

    public bool HasNote => Note.Length > 0;
}

/// <summary>Turns a <see cref="DeckCost"/> into what the views show.</summary>
internal static class DeckBuildViews
{
    /// <summary>The label of a priced deck; null for <see cref="DeckBuild.Unknown"/> (nothing is shown, never "owned" or "0 dust").</summary>
    public static DeckBuildView? Of(DeckCost cost, CardDb cards) =>
        Of(cost.State, cost.DustNeeded, cost.DustShort, cost.DustAvailable, cost.DustForCraftable, cost.Estimated, cost.Confirmed, cost.Missing, cards);

    /// <summary>
    /// The label from the numbers of a deck cost. A dust number the state needs and does not have gives no label: an
    /// unknown amount is never shown as "0 dust" (Core gives these numbers for these states today; were that to change,
    /// the label goes rather than lie).
    /// </summary>
    internal static DeckBuildView? Of(DeckBuild state, long? dustNeeded, long? dustShort, long? dustAvailable, long? dustForCraftable, bool estimated,
        bool confirmed, IReadOnlyList<MissingCard> missingCards, CardDb cards)
    {
        string glyph;
        string label;
        string reason;
        string total;
        switch (state)
        {
            case DeckBuild.Owned:
                (glyph, label, reason, total) = ("\U000F05E0", Loc.T("Collection.Label.Owned"), string.Empty, string.Empty);
                break;
            case DeckBuild.CraftableNow when dustNeeded is { } needed && dustAvailable is { } available:
                (glyph, label) = ("\U000F08EA", Loc.F("Collection.Label.Craftable", Dust(needed)));
                reason = Loc.F("Collection.Tip.Craftable", Dust(needed), Dust(available));
                total = Loc.F("Collection.Missing.Total", Dust(needed));
                break;
            case DeckBuild.NeedsDust when dustNeeded is { } needed && dustAvailable is { } available && dustShort is { } lacking:
                (glyph, label) = ("\U000F0028", Loc.F("Collection.Label.NeedsDust", Dust(lacking)));
                reason = Loc.F("Collection.Tip.NeedsDust", Dust(needed), Dust(available), Dust(lacking));
                total = Loc.F("Collection.Missing.Total", Dust(needed));
                break;
            case DeckBuild.NotCraftable:
                (glyph, label, reason) = ("\U000F0159", Loc.T("Collection.Label.NotCraftable"), Loc.T("Collection.Tip.NotCraftable"));
                // No dust completes the deck: the sum of the craftable part is named as that, not as the deck's price.
                total = dustForCraftable is > 0 and var part ? Loc.F("Collection.Missing.TotalPartial", Dust(part)) : Loc.T("Collection.Missing.TotalNone");
                break;
            default:
                // Unknown, or a state without the numbers its label states.
                return null;
        }

        var saved = !confirmed;
        var marks = new List<string>(2);
        if (estimated)
        {
            marks.Add(Loc.T("Collection.Mark.Estimate"));
        }

        if (saved)
        {
            marks.Add(Loc.T("Collection.Mark.Saved"));
        }

        var tips = new List<string>(4);
        if (reason.Length > 0)
        {
            tips.Add(reason);
        }

        if (missingCards.Any(m => m.MayOwnOtherPrinting))
        {
            tips.Add(Loc.T("Collection.Tip.OtherPrinting"));
        }

        if (missingCards.Any(m => m.CraftUncertain))
        {
            tips.Add(Loc.T("Collection.Tip.CraftUncertain"));
        }

        if (saved)
        {
            tips.Add(Loc.T("Collection.Tip.Saved"));
        }

        var missing = missingCards.Select(m => Row(m, cards)).ToList();
        return new DeckBuildView(state.ToString(), glyph, label, string.Join(" · ", marks), string.Join(' ', tips), saved, missing, total);
    }

    private static MissingCardRow Row(MissingCard card, CardDb cards)
    {
        var name = cards.NameOf(card.CardId);
        var notes = new List<string>(2);
        if (card.MayOwnOtherPrinting)
        {
            notes.Add(Loc.T("Collection.Missing.OtherPrinting"));
        }

        if (card.CraftUncertain)
        {
            notes.Add(Loc.T("Collection.Missing.CraftUncertain"));
        }

        return new MissingCardRow(
            card.CardId,
            card.Copies > 1 ? Loc.F("Collection.Missing.Copies", card.Copies, name) : name,
            card.DustEach is not { } each ? Loc.T("Collection.Missing.NotCraftable")
                : card.Copies > 1 ? Loc.F("Collection.Missing.PriceEach", card.Copies, Dust(each))
                : Loc.F("Collection.Missing.Price", Dust(each)),
            string.Join(" · ", notes));
    }

    private static string Dust(long dust) => dust.ToString("N0", CultureInfo.CurrentCulture);
}

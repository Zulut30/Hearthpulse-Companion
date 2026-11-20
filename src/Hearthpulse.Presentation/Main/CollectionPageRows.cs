using Hearthpulse.Core;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

// What both UIs show on the Collection page (docs/collection-page.md): ready texts, fractions and flags made by
// MainViewModel.CollectionPage.cs from the collection, the card data and the cosmetics. A view decides nothing itself.

/// <summary>A sum tile above the sets: label, the big value, a caption under it and the tile's tip.</summary>
public sealed record CollectionSumTile(string Label, string Value, string Detail, string Tip) : IImmutableView;

/// <summary>One rarity cell of a set tile: its name (with its colour, never the colour alone), the bar and "40/40".</summary>
/// <param name="Rarity">The card data's rarity ("COMMON"): the views take its colour through <c>KeyBrush</c>.</param>
public sealed record CollectionRarityView(string Rarity, string Name, double Fraction, string Count, string Tip, double ArtOpacity) : IImmutableView;

/// <summary>
/// A set's tile: name, percent of the playable copies, its bar, "playable" and "unique" counts, the dust line (or that the
/// set is complete), what dust cannot buy, the rarities and, once verified, the golden copies.
/// </summary>
/// <param name="Group">"Standard" / "Wild" / "Classic" in words, shown only under the Wild and All filters.</param>
/// <param name="Complete">Every playable copy is owned: the tile's edge and the check glyph say so.</param>
/// <param name="DustGlyph">Diamond for dust, check for a complete set, lock when only uncraftable cards are left.</param>
/// <param name="Estimate">The dust is an estimate (some missing cards are handed out by the game): the pill shows.</param>
/// <param name="Uncraftable">"4 copies can't be crafted", empty when none.</param>
/// <param name="Golden">"Golden: 34 of 290", empty until the split by finish is verified.</param>
/// <param name="A11y">The tile in one sentence for a screen reader.</param>
/// <param name="ArtOpacity">The bars dim with saved data; the text stays readable.</param>
public sealed record CollectionSetView(
    string Set, string Name, string Percent, string Group, double Fraction,
    string Playable, string Unique, string Dust, string DustGlyph, bool Complete, bool Estimate,
    string Uncraftable, IReadOnlyList<CollectionRarityView> Rarities, string Golden, string A11y, double ArtOpacity) : IImmutableView
{
    public bool HasGroup => Group.Length > 0;

    public bool HasUncraftable => Uncraftable.Length > 0;

    public bool HasGolden => Golden.Length > 0;
}

/// <summary>
/// A hero skin or a coin: the card's art (by its card data id, never an id of the collection), its name, owned or not
/// (in words under the name, a lock on the art), a favourite (a heart, and in the tip and the accessible name).
/// </summary>
/// <param name="CardId">The card whose art and full picture show; empty when the card data has no such card (a circle without art).</param>
/// <param name="State">"Not owned" under the name; empty for an owned one.</param>
/// <param name="ArtOpacity">0.4 for one not owned, times the dimming of saved data; the text never dims.</param>
public sealed record CollectionItemView(
    string Key, string CardId, string Name, bool Owned, bool Favourite, string State, string Tip, string A11y, double ArtOpacity) : IImmutableView
{
    public bool HasArt => CardId.Length > 0;

    public bool HasState => State.Length > 0;

    /// <summary>The badge in the art's corner: a heart for a favourite, a lock for one not owned, none otherwise.</summary>
    public string Badge => Favourite ? CollectionGlyphs.Heart : Owned ? string.Empty : CollectionGlyphs.Lock;

    public bool HasBadge => Badge.Length > 0;
}

/// <summary>
/// A card back drawn without art (no open source has the pictures): a framed tile with the theme's motif and a medallion
/// whose shape and turn come from its id, the group's glyph in it; owned or not and favourite as on the other tiles.
/// </summary>
/// <param name="Lobes">The medallion's lobes: 9, 4 or 12 by the id.</param>
/// <param name="Angle">The medallion's turn in degrees: a step of 11.25 by the id.</param>
/// <param name="State">"Not owned" under the name; empty for an owned one.</param>
/// <param name="Standing">Owned, not owned or favourite in words, for the tip under the name and description.</param>
/// <param name="Opacity">The whole frame: 0.4 for one not owned, times the dimming of saved data.</param>
public sealed record CardBackView(
    int Id, string Name, string Description, string Glyph, int Lobes, double Angle, bool Owned, bool Favourite, string State, string Standing, string A11y,
    double Opacity) : IImmutableView
{
    public bool HasState => State.Length > 0;

    public bool HasDescription => Description.Length > 0;

    public string Badge => Favourite ? CollectionGlyphs.Heart : Owned ? string.Empty : CollectionGlyphs.Lock;

    public bool HasBadge => Badge.Length > 0;
}

/// <summary>The head of the shown class or card back group: name, "14 of 58", the bar; a class has its hero's portrait.</summary>
public sealed record CollectionGroupView(string Name, string HeroCardId, string Count, double Fraction, double ArtOpacity) : IImmutableView
{
    public bool HasPortrait => HeroCardId.Length > 0;
}

/// <summary>MDI glyphs of the Collection page (the codes the theme's icon font draws).</summary>
public static class CollectionGlyphs
{
    public const string Heart = "\U000F02D1";
    public const string Lock = "\U000F033E";
    public const string Dust = "\U000F01C8";
    public const string Check = "\U000F05E0";
    public const string Saved = "\U000F02DA";
    public const string Unsupported = "\U000F002A";
    public const string Empty = "\U000F0639";
    public const string Waiting = "\U000F051F";
    public const string Filter = "\U000F0232";
}

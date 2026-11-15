using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Arena;

/// <summary>Why an offered card fits the deck so far better or worse than its score says.</summary>
public enum ArenaFitReason
{
    /// <summary>The card's cost slot already holds more cards than the target curve has by now.</summary>
    CurveCrowded,

    /// <summary>The card's cost slot is short of the target curve even with this card.</summary>
    CurveThin,

    /// <summary>Less than half of the deck is minions and the card is one.</summary>
    FewMinions,

    /// <summary>The deck is short of minions costing 3 or less and the card is one.</summary>
    FewEarlyMinions,

    /// <summary>With this spell more than two fifths of the deck would be spells.</summary>
    ManySpells,

    /// <summary>The deck already has two weapons or more and the card is another.</summary>
    ManyWeapons,

    /// <summary>The deck already has two or more cards of this name.</summary>
    Copies,
}

/// <summary>One term of the adjustment.</summary>
/// <param name="Points">Its share of the adjustment before the overall limit: positive raises the card, negative lowers it.</param>
/// <param name="Slot">Cost slot of a curve reason (<see cref="ArenaDeckFit.SlotOf"/>), else -1.</param>
/// <param name="Count">Cards of the card's name already in the deck for <see cref="ArenaFitReason.Copies"/>, else 0.</param>
public sealed record ArenaFitPart(ArenaFitReason Reason, double Points, int Slot = -1, int Count = 0);

/// <summary>
/// Hearthpulse's own adjustment of an offered card for the deck so far. It is never the source's number: the badge shows
/// the source's score and this next to it, apart.
/// </summary>
/// <param name="Points">The sum of the parts, kept within ±<see cref="ArenaDeckFit.Limit"/>.</param>
/// <param name="Parts">The reasons, the strongest first; empty when nothing applies.</param>
public sealed record ArenaFit(double Points, IReadOnlyList<ArenaFitPart> Parts)
{
    public static ArenaFit None { get; } = new(0, []);
}

/// <summary>
/// How an offered card fits what is already taken: the mana curve, the balance of minions, spells and weapons, and
/// copies of one card. Our own rule from the card data alone (cost, type, name); no synergy table of another tool.
/// Spec with a worked example: docs/arena-draft.md, "Deck fit".
/// <para>
/// With <c>n</c> known cards in the deck (nothing below <see cref="MinPicks"/>: the first picks take the strongest card):
/// </para>
/// <list type="bullet">
/// <item>Curve. Cost slots 0-1, 2, 3, 4, 5, 6, 7+ aim at <see cref="TargetCurve"/> cards of 30. For the card's slot
/// <c>d = (cards in the slot + 1) - target * (n + 1) / 30</c>. With at least two cards already in the slot,
/// <c>d &gt;= 1</c>: minus <c>min(d, 2)</c>;
/// <c>d &lt;= -1</c>: plus <c>min(-d, 2)</c>; in between nothing.</item>
/// <item>Minion: +1 when less than half of the deck is minions; +1 more when it costs 3 or less and
/// <c>(minions costing 3 or less + 1) - 8 * (n + 1) / 30 &lt;= -1</c>.</item>
/// <item>Spell: -1 when <c>spells + 1 &gt; 0.4 * (n + 1)</c>. Weapon: -1 when the deck has two already.</item>
/// <item>Copies: with <c>k &gt;= 2</c> cards of the same name in the deck, minus <c>min(k - 1, 2)</c>.</item>
/// </list>
/// The sum is kept within ±<see cref="Limit"/>.
/// </summary>
public static class ArenaDeckFit
{
    /// <summary>The adjustment never moves a card by more than this many score points.</summary>
    public const double Limit = 4;

    /// <summary>Deck cards before any adjustment is made.</summary>
    public const int MinPicks = 5;

    /// <summary>Cost slots: 0-1, 2, 3, 4, 5, 6, 7+.</summary>
    public const int Slots = 7;

    private const double PartLimit = 2;
    private const int EarlyCost = 3;
    private const int EarlyMinionTarget = 8;
    private const double SpellShare = 0.4;
    private const int WeaponsEnough = 2;

    /// <summary>
    /// Cards per cost slot a finished 30-card deck aims at: 3 at 0-1, 7 at 2, 6 at 3, 5 at 4, 4 at 5, 3 at 6, 2 at 7+.
    /// A plain tempo curve (the bulk at 2-4 mana, a few late cards), set by us; it is no statistic of any source.
    /// </summary>
    public static IReadOnlyList<int> TargetCurve { get; } = [3, 7, 6, 5, 4, 3, 2];

    /// <summary>Cost slot of a mana cost: 0 for 0-1, then one per mana, 6 for 7 or more.</summary>
    public static int SlotOf(int cost) => Math.Clamp(cost - 1, 0, Slots - 1);

    /// <summary>
    /// The adjustment of <paramref name="cardId"/> for a deck given as card ids (a card listed once per copy). A card
    /// the card data does not know gets none.
    /// </summary>
    public static ArenaFit Of(string cardId, IReadOnlyList<string> deckIds, CardDb cards)
    {
        var card = cards.Get(cardId);
        if (card is null || deckIds.Count < MinPicks)
        {
            return ArenaFit.None;
        }

        var slots = new int[Slots];
        int n = 0, minions = 0, earlyMinions = 0, spells = 0, weapons = 0, sameName = 0;
        var name = NameOf(cardId, card);
        foreach (var id in deckIds)
        {
            var have = cards.Get(id);
            if (have is null)
            {
                continue;
            }

            n++;
            if (string.Equals(NameOf(id, have), name, StringComparison.Ordinal))
            {
                sameName++;
            }

            slots[SlotOf(have.Cost)]++;
            switch (have.Type)
            {
                case "MINION":
                    minions++;
                    if (have.Cost <= EarlyCost)
                    {
                        earlyMinions++;
                    }

                    break;
                case "SPELL": spells++; break;
                case "WEAPON": weapons++; break;
            }
        }

        if (n < MinPicks)
        {
            return ArenaFit.None;
        }

        var parts = new List<ArenaFitPart>(4);
        var share = (n + 1) / (double)ArenaDraft.DeckSize;
        var slot = SlotOf(card.Cost);
        var over = slots[slot] + 1 - (TargetCurve[slot] * share);
        if (slots[slot] >= 2 && over >= 1)
        {
            parts.Add(new ArenaFitPart(ArenaFitReason.CurveCrowded, -Math.Min(over, PartLimit), slot));
        }
        else if (over <= -1)
        {
            parts.Add(new ArenaFitPart(ArenaFitReason.CurveThin, Math.Min(-over, PartLimit), slot));
        }

        switch (card.Type)
        {
            case "MINION":
                if (minions * 2 < n)
                {
                    parts.Add(new ArenaFitPart(ArenaFitReason.FewMinions, 1));
                }

                if (card.Cost <= EarlyCost && earlyMinions + 1 - (EarlyMinionTarget * share) <= -1)
                {
                    parts.Add(new ArenaFitPart(ArenaFitReason.FewEarlyMinions, 1));
                }

                break;
            case "SPELL" when spells + 1 > SpellShare * (n + 1):
                parts.Add(new ArenaFitPart(ArenaFitReason.ManySpells, -1));
                break;
            case "WEAPON" when weapons >= WeaponsEnough:
                parts.Add(new ArenaFitPart(ArenaFitReason.ManyWeapons, -1));
                break;
        }

        if (sameName >= 2)
        {
            parts.Add(new ArenaFitPart(ArenaFitReason.Copies, -Math.Min(sameName - 1, PartLimit), Count: sameName));
        }

        if (parts.Count == 0)
        {
            return ArenaFit.None;
        }

        // Stable: equal weights keep the order they were found in (curve, type, copies).
        var ordered = parts.OrderByDescending(p => Math.Abs(p.Points)).ToList();
        return new ArenaFit(Math.Clamp(parts.Sum(p => p.Points), -Limit, Limit), ordered);
    }

    /// <summary>A reprint (CORE_...) is the same card to the player: cards are one by name, by id without the card data.</summary>
    private static string NameOf(string cardId, Card? card) => card is { Name.Length: > 0 } ? card.Name : cardId;
}

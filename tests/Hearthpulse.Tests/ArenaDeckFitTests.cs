using System.Text;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Tests;

/// <summary>
/// The deck-fit adjustment of an offered Arena card (docs/arena-draft.md, "Deck fit"): each case is worked out by hand
/// in its comment from the rule in <see cref="ArenaDeckFit"/>. Target curve of 30 cards by cost slot
/// 0-1 / 2 / 3 / 4 / 5 / 6 / 7+ = 3 / 7 / 6 / 5 / 4 / 3 / 2; with n cards in the deck the target by now is
/// target * (n + 1) / 30.
/// </summary>
public sealed class ArenaDeckFitTests
{
    [Fact]
    public void The_target_curve_is_a_thirty_card_deck()
    {
        Assert.Equal(ArenaDraft.DeckSize, ArenaDeckFit.TargetCurve.Sum());
        Assert.Equal(ArenaDeckFit.Slots, ArenaDeckFit.TargetCurve.Count);
        Assert.Equal([0, 0, 1, 2, 3, 4, 5, 6, 6, 6], new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 }.Select(ArenaDeckFit.SlotOf));
    }

    [Fact]
    public void The_first_picks_are_not_adjusted()
    {
        // Four cards: below MinPicks (5) the strongest card is simply taken.
        Assert.Same(ArenaFit.None, ArenaDeckFit.Of("M2b", Deck(("M2a", 4)), Cards));
        Assert.Equal(0, ArenaFit.None.Points);
    }

    [Fact]
    public void A_crowded_cost_slot_lowers_the_card()
    {
        // n = 9: five 2-cost minions, four 4-cost minions. Offered: another 2-cost minion.
        // Slot "2": (5 + 1) - 7 * 10/30 = 6 - 2.33 = 3.67 >= 1 -> -min(3.67, 2) = -2.
        // Minions 9 of 9: not few. Early minions (5 + 1) - 8 * 10/30 = 3.33: not short. No copies of its name.
        var fit = ArenaDeckFit.Of("M2b", Deck(("M2a", 5), ("M4a", 4)), Cards);

        Assert.Equal(-2, fit.Points);
        var part = Assert.Single(fit.Parts);
        Assert.Equal((ArenaFitReason.CurveCrowded, -2.0, 1), (part.Reason, part.Points, part.Slot));
    }

    [Fact]
    public void A_thin_slot_few_minions_and_few_early_minions_raise_the_card_up_to_the_limit()
    {
        // n = 14: ten 5-cost spells, four 6-cost minions. Offered: a 2-cost minion.
        // Slot "2": (0 + 1) - 7 * 15/30 = 1 - 3.5 = -2.5 <= -1 -> +min(2.5, 2) = +2.
        // Minions 4 * 2 = 8 < 14 -> +1. Early minions (0 + 1) - 8 * 15/30 = -3 <= -1 -> +1. Sum +4 = the limit.
        var fit = ArenaDeckFit.Of("M2b", Deck(("S5a", 10), ("M6a", 4)), Cards);

        Assert.Equal(4, fit.Points);
        Assert.Equal(
            [(ArenaFitReason.CurveThin, 2.0), (ArenaFitReason.FewMinions, 1.0), (ArenaFitReason.FewEarlyMinions, 1.0)],
            fit.Parts.Select(p => (p.Reason, p.Points)));
        Assert.Equal(1, fit.Parts[0].Slot);
    }

    [Fact]
    public void The_sum_is_kept_within_the_limit()
    {
        // n = 14: ten copies of one 5-cost spell, four 6-cost minions. Offered: the same spell again.
        // Slot "5": (10 + 1) - 4 * 15/30 = 9 -> -2. Spells 10 + 1 = 11 > 0.4 * 15 = 6 -> -1.
        // Copies: 10 of its name -> -min(10 - 1, 2) = -2. Sum -5, kept at -4.
        var fit = ArenaDeckFit.Of("S5a", Deck(("S5a", 10), ("M6a", 4)), Cards);

        Assert.Equal(-ArenaDeckFit.Limit, fit.Points);
        // The strongest reasons first; equal ones in the order curve, type, copies.
        Assert.Equal([ArenaFitReason.CurveCrowded, ArenaFitReason.Copies, ArenaFitReason.ManySpells], fit.Parts.Select(p => p.Reason));
        Assert.Equal(10, fit.Parts[1].Count);
    }

    [Fact]
    public void A_third_weapon_is_lowered()
    {
        // n = 6: two 3-cost weapons, four 4-cost minions. Offered: a 2-cost weapon.
        // Slot "2": (0 + 1) - 7 * 7/30 = 1 - 1.63 = -0.63: inside the dead zone. Two weapons already -> -1.
        var fit = ArenaDeckFit.Of("W2a", Deck(("W3a", 2), ("M4a", 4)), Cards);

        Assert.Equal(-1, fit.Points);
        Assert.Equal(ArenaFitReason.ManyWeapons, Assert.Single(fit.Parts).Reason);
        // With one weapon the same card is not touched.
        Assert.Same(ArenaFit.None, ArenaDeckFit.Of("W2a", Deck(("W3a", 1), ("M4a", 5)), Cards));
    }

    [Fact]
    public void A_card_on_the_curve_is_left_alone()
    {
        // n = 5: five 3-cost minions. Offered: a 4-cost minion.
        // Slot "4": (0 + 1) - 5 * 6/30 = 0: nothing. Minions 5 of 5; not an early minion; no copies.
        var fit = ArenaDeckFit.Of("M4a", Deck(("M3a", 5)), Cards);

        Assert.Same(ArenaFit.None, fit);
    }

    [Fact]
    public void Copies_are_counted_by_name_so_a_reprint_is_the_same_card()
    {
        // n = 5: "Twin" as CORE_TWIN and TWIN (3-cost minions), three 5-cost minions. Offered: TWIN.
        // Slot "3": (2 + 1) - 6 * 6/30 = 3 - 1.2 = 1.8 >= 1 -> -1.8. Two of its name already -> -min(2 - 1, 2) = -1.
        // Early minions (2 + 1) - 8 * 6/30 = 1.4: not short. Sum -2.8.
        var fit = ArenaDeckFit.Of("TWIN", Deck(("CORE_TWIN", 1), ("TWIN", 1), ("M5a", 3)), Cards);

        Assert.Equal(-2.8, fit.Points, 9);
        Assert.Equal([ArenaFitReason.CurveCrowded, ArenaFitReason.Copies], fit.Parts.Select(p => p.Reason));
        Assert.Equal(-1.8, fit.Parts[0].Points, 9);
        Assert.Equal((-1.0, 2), (fit.Parts[1].Points, fit.Parts[1].Count));

        // One copy in the deck is no reason: two of a card is an ordinary Arena deck.
        var second = ArenaDeckFit.Of("TWIN", Deck(("TWIN", 1), ("M5a", 2), ("M4a", 2)), Cards);
        Assert.DoesNotContain(second.Parts, p => p.Reason == ArenaFitReason.Copies);
    }

    [Fact]
    public void A_card_the_card_data_does_not_know_is_not_adjusted()
    {
        Assert.Same(ArenaFit.None, ArenaDeckFit.Of("NEW_001", Deck(("M2a", 9)), Cards));
        // Unknown cards of the deck count as cards taken (n) but sit in no slot and have no type.
        // n = 9: five unknown, four 2-cost minions. Offered: a 2-cost minion.
        // Slot "2": (4 + 1) - 7 * 10/30 = 2.67 -> -2. Minions 4 * 2 = 8 < 9 -> +1. Sum -1.
        var fit = ArenaDeckFit.Of("M2b", Deck(("NEW_001", 5), ("M2a", 4)), Cards);
        Assert.Equal(-1, fit.Points);
    }

    /// <summary>
    /// The adjustment is our number and stays apart: the option's score is the source's, the best pick is still chosen
    /// by it, and a card without a score or with a package has no adjustment.
    /// </summary>
    [Fact]
    public void The_view_keeps_the_sources_score_and_carries_the_adjustment_apart()
    {
        var meta = new ArenaMeta(
            [new("M2b", 0, 50, null, null, 0), new("M4b", 0, 49, null, null, 0), new("M6a", 0, null, null, null, 0), new("S5a", 0, 30, null, null, 0)], []);
        var deck = Deck(("M2a", 5), ("M4a", 4));
        var state = new ArenaDraftState("HERO_07", 0, deck, "DRAFTING");

        var view = ArenaDraft.Build(state, new ArenaDraftOffer(["M2b", "M4b", "M6a"]), meta, Cards);

        var crowded = view.Options[0];
        Assert.Equal(50, crowded.Score);
        Assert.Equal(-2, crowded.Fit!.Points);
        // 50 - 2 is below 49, and the best pick is still the source's best.
        Assert.True(crowded.IsBest);
        Assert.False(view.Options[1].IsBest);
        // Slot "4": (4 + 1) - 5 * 10/30 = 3.33 -> -2 for the second card as well.
        Assert.Equal(-2, view.Options[1].Fit!.Points);
        Assert.Null(view.Options[2].Score);
        Assert.Null(view.Options[2].Fit);

        IReadOnlyList<IReadOnlyList<string>> packages = [["S5a"], [], []];
        var packaged = ArenaDraft.Build(state, new ArenaDraftOffer(["M2b", "M4b", "M6a"], true, packages), meta, Cards);
        Assert.Null(packaged.Options[0].Fit);
        Assert.Equal(40, packaged.Options[0].Score);
        Assert.NotNull(packaged.Options[1].Fit);

        // A hero offer has no adjustment at all.
        var heroes = ArenaDraft.Build(ArenaDraftState.Empty, new ArenaDraftOffer(["HERO_07"]), meta, Cards);
        Assert.Null(heroes.Options[0].Fit);
    }

    private static List<string> Deck(params (string Id, int Copies)[] cards) =>
        cards.SelectMany(c => Enumerable.Repeat(c.Id, c.Copies)).ToList();

    private static readonly CardDb Cards = MakeCards();

    /// <summary>Mxa / Mxb: two x-cost minions of different names; Sxa spells, Wxa weapons; "Twin" under two ids.</summary>
    private static CardDb MakeCards()
    {
        var json = new StringBuilder("[");
        json.Append("""{"id":"HERO_07","dbfId":893,"name":"Gul'dan","type":"HERO","cardClass":"WARLOCK","collectible":true}""");
        var dbf = 60000;
        void Card(string id, string name, string type, int cost) =>
            json.Append($$""",{"id":"{{id}}","dbfId":{{++dbf}},"name":"{{name}}","cost":{{cost}},"type":"{{type}}","rarity":"COMMON","cardClass":"NEUTRAL","collectible":true}""");
        for (var cost = 1; cost <= 8; cost++)
        {
            Card($"M{cost}a", $"Minion {cost} a", "MINION", cost);
            Card($"M{cost}b", $"Minion {cost} b", "MINION", cost);
            Card($"S{cost}a", $"Spell {cost}", "SPELL", cost);
            Card($"W{cost}a", $"Weapon {cost}", "WEAPON", cost);
        }

        Card("TWIN", "Twin", "MINION", 3);
        Card("CORE_TWIN", "Twin", "MINION", 3);
        json.Append(']');
        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json.ToString())));
    }
}

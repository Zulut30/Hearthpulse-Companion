using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Murcules (BG27_023): "Whenever this kills a minion, give a minion in your hand +3/+3." Golden: +6/+6. The hand is
/// not on the board, so the buff shows through Bassgill, which summons from it. Expected results are worked out by hand
/// from the card text in each comment.
/// </summary>
public sealed class MurculesTests
{
    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    private static SimSide Ours(bool golden, params SimMinion[] hand) =>
        Side(
            Minion(golden ? 10 : 5, golden ? 6 : 3, cardId: "BG27_023", tribes: Tribe.Murloc, golden: golden),
            Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc, keywords: Keyword.Taunt))
        with { Hand = hand };

    [Fact]
    public void A_kill_gives_the_minion_in_hand_three_and_three_and_bassgill_brings_it_in_buffed()
    {
        // Murcules 5/3 attacks first and kills their 1/1 (Murcules is left at 2 Health): the hand's only minion, a 1/4
        // Murloc, becomes 4/7. Their 6/20 then kills the Taunt Bassgill, whose deathrattle summons that 4/7.
        var result = Fight(Ours(golden: false, Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)),
                           Side(Minion(1, 1, cardId: "E1", keywords: Keyword.Taunt), Minion(6, 20, cardId: "E2")),
                           true, Effects(typeof(Murcules), typeof(Bassgill)));

        Assert.Contains("summoned our M 4/7 at 1", result.Log);
    }

    [Fact]
    public void Golden_murcules_gives_six_and_six()
    {
        // Golden 10/6 kills their 1/1: the 1/4 in hand becomes 7/10, and Bassgill's deathrattle summons it so.
        var result = Fight(Ours(golden: true, Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)),
                           Side(Minion(1, 1, cardId: "E1", keywords: Keyword.Taunt), Minion(6, 40, cardId: "E2")),
                           true, Effects(typeof(Murcules), typeof(Bassgill)));

        Assert.Contains("summoned our M 7/10 at 1", result.Log);
    }

    [Fact]
    public void The_buff_can_change_which_murloc_has_the_most_health()
    {
        // Hand: M1 1/4 and M2 1/6. The kill buffs a random one; the scripted pick is the first (M1 → 4/7), which now has
        // more Health than M2, so Bassgill summons M1 and not M2.
        var result = Fight(Ours(golden: false, Minion(1, 4, cardId: "M1", tribes: Tribe.Murloc), Minion(1, 6, cardId: "M2", tribes: Tribe.Murloc)),
                           Side(Minion(1, 1, cardId: "E1", keywords: Keyword.Taunt), Minion(6, 20, cardId: "E2")),
                           true, Effects(typeof(Murcules), typeof(Bassgill)));

        Assert.Contains("summoned our M1 4/7 at 1", result.Log);
        Assert.Empty(result.Lines("summoned our M2"));
    }

    [Fact]
    public void An_attack_that_kills_nothing_gives_nothing()
    {
        // Murcules 5/3 hits their 1/20 Taunt and kills nothing; their 6/20 kills Bassgill: the Murloc comes in as it was.
        var result = Fight(Ours(golden: false, Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)),
                           Side(Minion(1, 20, cardId: "E1", keywords: Keyword.Taunt), Minion(6, 20, cardId: "E2")),
                           true, Effects(typeof(Murcules), typeof(Bassgill)));

        Assert.Contains("summoned our M 1/4 at 1", result.Log);
    }

    [Fact]
    public void Murcules_changes_nothing_on_the_board_without_a_card_that_summons_from_the_hand()
    {
        // Murcules 5/3 kills the 1/1 and is left a 5/2, nothing else happens.
        var ours = Side(Minion(5, 3, cardId: "BG27_023", tribes: Tribe.Murloc)) with { Hand = [Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)] };
        var result = Fight(ours, Side(Minion(1, 1, cardId: "E")), true, Effects(typeof(Murcules)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(5, 2)], Stats(result.Ours));
    }

    [Fact]
    public void The_opponents_murcules_is_not_simulated_because_their_hand_is_hidden()
    {
        // The rule of every effect that reads the hand: simulated for us, flagged for the opponent.
        Assert.False(EffectRegistry.Default.Implements("BG27_023", golden: false, isOpponent: true));
        Assert.False(EffectRegistry.Default.Implements("BG27_023", golden: true, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements("BG27_023", golden: false, isOpponent: false));

        // As the tracker flags it at capture: combat-relevant, and no effect for this side.
        var flagged = CombatRelevance.IsUnsimulated("BG27_023", golden: false, combatRelevant: true, grantedMechanics: [], enchantments: [],
            isOpponent: true, EffectRegistry.Default);
        Assert.True(flagged);
        Assert.False(CombatRelevance.IsUnsimulated("BG27_023", golden: false, combatRelevant: true, grantedMechanics: [], enchantments: [],
            isOpponent: false, EffectRegistry.Default));

        var murcules = Minion(5, 3, cardId: "BG27_023", tribes: Tribe.Murloc) with { HasUnsimulatedEffect = flagged };
        var item = Assert.Single(CombatSimulator.Explain(Side(murcules), isOpponent: true));
        Assert.Equal(("BG27_023", UnsimulatedReason.MissingInput), (item.CardId, item.Reason));
        var odds = CombatSimulator.Run(Side(Minion(1, 1, cardId: "E")), Side(murcules), true, new SimOptions { Runs = 10 });
        Assert.Equal(1, odds.Unsimulated);
        Assert.True(odds.Approximate);
    }
}

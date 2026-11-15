using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Deaths;

/// <summary>
/// Bassgill (BG26_350): "Deathrattle: Summon the highest-Health Murloc from your hand for this combat only." Golden:
/// "the 2 highest-Health Murlocs". Expected results are worked out by hand from the card text in each comment.
/// </summary>
public sealed class BassgillTests
{
    /// <summary>A hand of a 9/1 Murloc, a 2/6 Murloc, a 1/4 Murloc and a 0/20 Beast.</summary>
    private static readonly SimMinion[] Hand =
    [
        Minion(9, 1, cardId: "M1", tribes: Tribe.Murloc),
        Minion(2, 6, cardId: "M2", tribes: Tribe.Murloc),
        Minion(1, 4, cardId: "M3", tribes: Tribe.Murloc),
        Minion(0, 20, cardId: "B", tribes: Tribe.Beast),
    ];

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void Bassgill_summons_the_highest_health_murloc_of_the_hand_where_it_stood()
    {
        // Their 5/3 attacks first and kills Bassgill 5/2 (and dies to its 5 Attack). The deathrattle takes the Murloc
        // with the most Health, M2 2/6: not the 9/1 with the most Attack, not the 0/20 Beast. It stands in Bassgill's
        // slot, left of our 0/3.
        var ours = Side(Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc), Minion(0, 3, cardId: "W")) with { Hand = Hand };
        var result = Fight(ours, Side(Minion(5, 3, cardId: "E")), false, Effects(typeof(Bassgill)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["M2", "W"], result.Ours.Select(m => m.CardId));
        Assert.Equal([(2, 6), (0, 3)], Stats(result.Ours));
        Assert.Single(result.Lines("summoned our "));
    }

    [Fact]
    public void Golden_bassgill_summons_the_two_highest_health_murlocs_left_to_right()
    {
        // Golden 10/4 dies to their 5/11 (which is left at 1 Health). M2 2/6 first, then M3 1/4 to its right: the two
        // Murlocs with the most Health. M2 then kills the 5/1.
        var ours = Side(Minion(10, 4, cardId: "BG26_350", tribes: Tribe.Murloc, golden: true)) with { Hand = Hand };
        var result = Fight(ours, Side(Minion(5, 11, cardId: "E")), false, Effects(typeof(Bassgill)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["M2", "M3"], result.Ours.Select(m => m.CardId));
        Assert.Equal(2, result.Lines("summoned our ").Count);
    }

    [Fact]
    public void Bassgill_summons_nothing_without_a_murloc_in_hand()
    {
        var ours = Side(Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc)) with { Hand = [Minion(0, 20, cardId: "B", tribes: Tribe.Beast)] };
        var result = Fight(ours, Side(Minion(5, 30, cardId: "E")), false, Effects(typeof(Bassgill)));

        Assert.Equal(-1, result.Outcome);
        Assert.Empty(result.Lines("summoned our "));
    }

    [Fact]
    public void A_murloc_leaves_the_hand_when_summoned_so_a_second_bassgill_finds_none()
    {
        // Seven of ours: two Taunt Bassgills and five 0/1 walls. Their 5/100 kills the first Bassgill, which summons
        // the hand's only Murloc. The second Bassgill then finds no Murloc left: one summon in the whole fight, not two.
        var ours = Side(
            Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc, keywords: Keyword.Taunt),
            Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc, keywords: Keyword.Taunt),
            Minion(0, 1, cardId: "W1"), Minion(0, 1, cardId: "W2"), Minion(0, 1, cardId: "W3"), Minion(0, 1, cardId: "W4"), Minion(0, 1, cardId: "W5"))
            with { Hand = [Minion(1, 4, cardId: "M3", tribes: Tribe.Murloc)] };
        var result = Fight(ours, Side(Minion(5, 100, cardId: "E")), false, Effects(typeof(Bassgill)));

        Assert.Single(result.Lines("summoned our M3"));
    }

    [Fact]
    public void Bassgill_does_nothing_for_the_opponent_whose_hand_is_hidden()
    {
        // Even if a hand were passed for the opponent, the engine never exposes it, and the card stays "not simulated".
        var theirs = Side(Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc)) with { Hand = Hand };
        var result = Fight(Side(Minion(5, 30, cardId: "E")), theirs, true, Effects(typeof(Bassgill)));

        Assert.Equal(1, result.Outcome);
        Assert.Empty(result.Lines("summoned their "));
        Assert.False(EffectRegistry.Default.Implements("BG26_350", golden: false, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements("BG26_350", golden: true, isOpponent: false));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Very Hungry Winterfinner (BG29_300): "Taunt. Whenever this takes damage, give a minion in your hand +2/+1." Golden:
/// +4/+2. The hand shows through Bassgill, which summons from it. Expected results are worked out by hand from the card
/// text in each comment; the real fights are in <see cref="SimReplayResidualTests"/>.
/// </summary>
public sealed class VeryHungryWinterfinnerTests
{
    private static SimSide Ours(SimMinion winterfinner) =>
        Side(winterfinner, Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc))
        with { Hand = [Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)] };

    private static SimSide Theirs() =>
        Side(Minion(1, 2, cardId: "E1", keywords: Keyword.Taunt), Minion(10, 30, cardId: "E2"));

    [Fact]
    public void Every_hit_it_takes_buffs_the_minion_in_hand_and_bassgill_brings_it_in()
    {
        // Winterfinner 2/6 hits their 1/2 Taunt (takes 1: M 1/4 → 3/5; the Taunt dies). Their 10/30 hits our Taunt
        // Winterfinner (takes 10 and dies: M → 5/6). Bassgill attacks the 10/30, dies, and summons M as 5/6.
        var result = Fight(Ours(Minion(2, 6, Keyword.Taunt, cardId: "BG29_300", tribes: Tribe.Murloc)), Theirs(),
                           true, Effects(typeof(VeryHungryWinterfinner), typeof(Bassgill)));

        Assert.Contains("summoned our M 5/6 at 0", result.Log);
    }

    [Fact]
    public void Golden_winterfinner_gives_four_and_two()
    {
        // Golden 4/12 hits the 1/2 Taunt (M → 5/6), then takes 10 from the 10/30 and lives at 1 (M → 9/8). Bassgill attacks
        // the 10/30 and dies: M comes in as 9/8.
        var result = Fight(Ours(Minion(4, 12, Keyword.Taunt, cardId: "BG29_300", tribes: Tribe.Murloc, golden: true)), Theirs(),
                           true, Effects(typeof(VeryHungryWinterfinner), typeof(Bassgill)));

        Assert.Contains("summoned our M 9/8 at 1", result.Log);
    }

    [Fact]
    public void A_hit_its_divine_shield_takes_does_not_count()
    {
        // As in the real log (15.11.2026, game 2 fight 9): the first hit only pops Divine Shield and buffs nothing; the
        // 10 from their 10/30 kills it and buffs M once, to 3/5.
        var result = Fight(Ours(Minion(2, 6, Keyword.Taunt | Keyword.DivineShield, cardId: "BG29_300", tribes: Tribe.Murloc)), Theirs(),
                           true, Effects(typeof(VeryHungryWinterfinner), typeof(Bassgill)));

        Assert.Contains("summoned our M 3/5 at 0", result.Log);
    }

    [Fact]
    public void The_opponents_winterfinner_is_not_simulated_because_their_hand_is_hidden()
    {
        Assert.False(EffectRegistry.Default.Implements("BG29_300", golden: false, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements("BG29_300", golden: true, isOpponent: false));
        Assert.Equal(Relevance.Relevant, CombatRelevance.Find("BG29_300_G")?.Relevance);
    }
}

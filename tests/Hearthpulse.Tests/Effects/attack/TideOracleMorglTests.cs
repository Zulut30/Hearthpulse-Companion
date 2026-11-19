using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Attack;

/// <summary>
/// Tide Oracle Morgl (BG27_513, 1/10): "Poisonous. When this attacks and kills a minion, give its maximum stats to a
/// minion in your hand." Golden: "double its maximum stats". The hand matters through Bassgill (BG26_350, "Deathrattle:
/// Summon the highest-Health Murloc from your hand"). Expected results are worked out by hand in each comment.
/// </summary>
public sealed class TideOracleMorglTests
{
    private const string Morgl = "BG27_513";

    private static EffectRegistry Registry => Effects(typeof(TideOracleMorgl), typeof(Bassgill));

    /// <summary>Our Morgl 1/10 Poisonous and Bassgill 1/1; hand: a 2/2 Murloc and a 0/20 Beast.</summary>
    private static SimSide Ours(bool golden = false) => Side(
            Minion(golden ? 2 : 1, golden ? 20 : 10, Keyword.Poisonous, Morgl, Tribe.Murloc, golden),
            Minion(1, 1, cardId: "BG26_350", tribes: Tribe.Murloc))
        with { Hand = [Minion(2, 2, cardId: "M", tribes: Tribe.Murloc), Minion(0, 20, cardId: "B", tribes: Tribe.Beast)] };

    /// <summary>Their 5/8 Taunt (Morgl must attack it) and a 3/30 that later kills our Bassgill.</summary>
    private static SimSide Theirs => Side(Minion(5, 8, Keyword.Taunt, "T"), Minion(3, 30, cardId: "E"));

    [Fact]
    public void A_kill_on_its_attack_gives_the_victims_maximum_stats_to_a_hand_minion()
    {
        // Morgl attacks the Taunt (pick 0) and poisons it: the hand minion picked (pick 0: M 2/2) gets +5/+8 → 7/10.
        // Their 3/30 attacks Bassgill (pick 1), which dies and summons the highest-Health Murloc of the hand: M 7/10.
        var result = Fight(Ours(), Theirs, oursFirst: true, Registry, 0, 0, 1);

        Assert.Equal(["M in hand gets +5/+8"], result.Lines("M in hand"));
        Assert.Single(result.Lines("summoned our M 7/10"));
    }

    [Fact]
    public void Golden_morgl_gives_double_the_stats()
    {
        // +10/+16 → M 12/18.
        var result = Fight(Ours(golden: true), Theirs, oursFirst: true, Registry, 0, 0, 1);

        Assert.Single(result.Lines("summoned our M 12/18"));
    }

    [Fact]
    public void Maximum_health_counts_not_the_damaged_health()
    {
        // Their Taunt 5/8 was damaged to 5/3 before the fight: its maximum Health is still 8, so M gets +5/+8.
        var theirs = Side(Minion(5, 8, Keyword.Taunt, "T") with { Health = 3 }, Minion(3, 30, cardId: "E"));
        var result = Fight(Ours(), theirs, oursFirst: true, Registry, 0, 0, 1);

        Assert.Equal(["M in hand gets +5/+8"], result.Lines("M in hand"));
    }

    [Fact]
    public void A_kill_while_defending_gives_nothing()
    {
        // Their 5/8 attacks Morgl and dies to its Poisonous: Morgl did not attack, no +5/+8. Morgl then attacks and
        // poisons their 0/30: that kill is its own, +0/+30, the only buff.
        var ours = Side(Minion(1, 10, Keyword.Poisonous, Morgl, Tribe.Murloc)) with { Hand = [Minion(2, 2, cardId: "M", tribes: Tribe.Murloc)] };
        var result = Fight(ours, Side(Minion(5, 8, cardId: "T"), Minion(0, 30, cardId: "W")), oursFirst: false, Registry, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["M in hand gets +0/+30"], result.Lines("M in hand"));
    }

    [Fact]
    public void A_divine_shield_that_survives_is_not_a_kill()
    {
        // Morgl hits a Divine Shield 5/8 Taunt: the shield pops, no kill. The 5/8 attacks Morgl and dies to its Poisonous
        // (a kill while defending: nothing). Morgl then attacks and poisons the 0/30: the only buff, +0/+30.
        // A buffed Morgl 1/11 survives both hits of the 5/8.
        var theirs = Side(Minion(5, 8, Keyword.Taunt | Keyword.DivineShield, "T"), Minion(0, 30, cardId: "W"));
        var ours = Side(Minion(1, 10, Keyword.Poisonous, Morgl, Tribe.Murloc) with { Health = 11, MaxHealth = 11 }) with { Hand = [Minion(2, 2, cardId: "M", tribes: Tribe.Murloc)] };
        var result = Fight(ours, theirs, oursFirst: true, Registry, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["M in hand gets +0/+30"], result.Lines("M in hand"));
    }

    [Fact]
    public void The_opponents_morgl_is_not_simulated_because_their_hand_is_hidden()
    {
        Assert.False(EffectRegistry.Default.Implements(Morgl, golden: false, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements(Morgl, golden: true, isOpponent: false));
        Assert.True(EffectRegistry.Default.Implements("BG27_513_G", golden: true));
    }
}

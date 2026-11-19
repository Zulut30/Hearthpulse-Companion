using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>
/// Timewarped minions that read the hand (Timewarped Bassgill, Murcules, Scourfin): simulated for the local player only.
/// Boards worked out by hand in each comment.
/// </summary>
public sealed class TimewarpedHandTests
{
    private const string Bassgill = "BG34_Giant_071";
    private const string Murcules = "BG34_Giant_207";
    private const string Scourfin = "BG34_Giant_017";

    /// <summary>A 9/1 Murloc, a 2/6 Beast, a 1/4 minion.</summary>
    private static readonly SimMinion[] Hand =
    [
        Minion(9, 1, cardId: "M", tribes: Tribe.Murloc),
        Minion(2, 6, cardId: "H", tribes: Tribe.Beast),
        Minion(1, 4, cardId: "L"),
    ];

    private static SimMinion Dr(int attack, int health, string cardId, Tribe tribe, bool golden = false) =>
        Minion(attack, health, Keyword.Taunt, cardId, tribe, golden, CardMechanic.Deathrattle);

    [Fact]
    public void Bassgill_summons_the_highest_health_minion_of_any_type_with_divine_shield()
    {
        // Killer 7/1 and the 7/4 Bassgill trade. The hand's highest Health is the 2/6 Beast (not a Murloc): it comes in
        // with Divine Shield, then only the Wall is left.
        var result = Fight(Side(Dr(7, 4, Bassgill, Tribe.Murloc)) with { Hand = Hand }, Side(Killer(7, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedBassgill)));

        Assert.Equal(["H 2/6 DivineShield"], Board(result.Ours));
        Assert.Single(result.Lines("summoned our H"));
    }

    [Fact]
    public void Golden_bassgill_summons_the_two_highest_left_to_right()
    {
        var result = Fight(Side(Dr(14, 8, Bassgill, Tribe.Murloc, golden: true)) with { Hand = Hand }, Side(Killer(8, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedBassgill)));

        Assert.Equal(["H 2/6 DivineShield", "L 1/4 DivineShield"], Board(result.Ours));
    }

    [Fact]
    public void Murcules_buffs_the_left_most_minion_in_hand_which_a_bassgill_then_picks()
    {
        // Our Murcules 10/5 attacks first (pick 0: E 1/1) and kills it: the left-most minion in hand, A 1/3, becomes 5/7.
        // Their Killer 7/1 then hits our Taunt Bassgill (pick 0); both die. The Bassgill takes the highest Health in
        // hand: A 5/7 (without the buff it would have been B 1/5). Their board is empty: we win.
        var ours = Side(Minion(10, 5, cardId: Murcules, tribes: Tribe.Murloc), Dr(7, 4, Bassgill, Tribe.Murloc))
            with { Hand = [Minion(1, 3, cardId: "A"), Minion(1, 5, cardId: "B")] };
        var result = Fight(ours, Side(Minion(1, 1, cardId: "E"), Killer(7, 1)), oursFirst: true,
                           Only(typeof(TimewarpedMurcules), typeof(TimewarpedBassgill)), 0, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["BG34_Giant_207 10/4", "A 5/7 DivineShield"], Board(result.Ours));
    }

    [Fact]
    public void Scourfin_buffs_a_random_minion_of_the_hand_and_summons_it()
    {
        // Killer 7/1 (its target is pick 0) and the Scourfin 7/7 trade. The random hand minion is pick 1: B 2/2 gets
        // +7/+7 and comes in as 9/9.
        var ours = Side(Dr(7, 7, Scourfin, Tribe.Murloc)) with { Hand = [Minion(1, 1, cardId: "A"), Minion(2, 2, cardId: "B")] };
        var result = Fight(ours, Side(Killer(7, 1), Wall), oursFirst: false, Only(typeof(TimewarpedScourfin)), 0, 1);

        Assert.Equal(["B 9/9"], Board(result.Ours));
    }

    [Fact]
    public void Golden_scourfin_gives_fourteen()
    {
        var ours = Side(Dr(14, 14, Scourfin, Tribe.Murloc, golden: true)) with { Hand = [Minion(1, 1, cardId: "A")] };
        var result = Fight(ours, Side(Killer(14, 1), Wall), oursFirst: false, Only(typeof(TimewarpedScourfin)));

        Assert.Equal(["A 15/15"], Board(result.Ours));
    }

    [Theory]
    [InlineData(Bassgill)]
    [InlineData(Murcules)]
    [InlineData(Scourfin)]
    public void Hand_cards_are_simulated_for_us_only(string cardId)
    {
        Assert.True(EffectRegistry.Default.Implements(cardId, golden: false, isOpponent: false));
        Assert.True(EffectRegistry.Default.Implements(cardId, golden: true, isOpponent: false));
        Assert.False(EffectRegistry.Default.Implements(cardId, golden: false, isOpponent: true));
    }
}

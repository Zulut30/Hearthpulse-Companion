using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Rot Hide Gnoll (BG25_013, 1/4): "Has +1 Attack for each friendly minion that died this combat." Golden: "+2". Mishmash
/// (TB_BaconShop_HERO_33_Buddy, 4/4): "Whenever your Amalgam gains stats, this gains them too." Golden: "twice". Results
/// are worked out by hand in each comment.
/// </summary>
public sealed class RotHideGnollAndMishmashTests
{
    private const string Gnoll = RotHideGnoll.Id;
    private const string MishmashId = "TB_BaconShop_HERO_33_Buddy";

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void Gnoll_gains_attack_for_each_friendly_death()
    {
        // Their 1/1 attacks our A 1/1 (pick 1): both die, our Gnoll 1/4 → 2/4. Then nothing can kill anything (their 0/100
        // wall): a tie at the attack cap with the Gnoll at 2/4.
        var ours = Side(Minion(1, 4, cardId: Gnoll, tribes: Tribe.Undead), Minion(1, 1, cardId: "A"));
        var result = Fight(ours, Side(Minion(1, 1, cardId: "K"), Minion(0, 100, cardId: "W")), oursFirst: false, Effects(typeof(RotHideGnoll)), 1);

        Assert.Equal((2, 4), Stats(result.Ours)[0]);
    }

    [Fact]
    public void Golden_gnoll_gains_two_and_enemy_deaths_do_not_count()
    {
        // Their K dies too (to our A), but only our A counts: golden 2/8 → 4/8.
        var ours = Side(Minion(2, 8, cardId: Gnoll, tribes: Tribe.Undead, golden: true), Minion(1, 1, cardId: "A"));
        var result = Fight(ours, Side(Minion(1, 1, cardId: "K"), Minion(0, 100, cardId: "W")), oursFirst: false, Effects(typeof(RotHideGnoll)), 1);

        Assert.Equal((4, 8), Stats(result.Ours)[0]);
    }

    [Fact]
    public void A_reborn_gnoll_comes_back_with_every_death_of_the_combat()
    {
        // Their 5/100 kills A first (pick 1): Gnoll 2/4. Our Gnoll attacks (2) and dies to the 5. Its reborn copy (printed
        // 1/1) counts both deaths, A and the Gnoll itself: 3/1, and their 5/97 then attacks it.
        var ours = Side(Minion(1, 4, Keyword.Reborn, Gnoll, Tribe.Undead), Minion(1, 1, cardId: "A"));
        var result = Fight(ours, Side(Minion(5, 100, cardId: "E")), oursFirst: false, Effects(typeof(RotHideGnoll)), 1);

        Assert.Contains(result.Log, l => l.Contains("attacks our BG25_013 3/1", StringComparison.Ordinal));
    }

    [Fact]
    public void Mishmash_copies_what_the_amalgam_gains()
    {
        // Bonker's Rally plays a Blood Gem (+1/+1) on each other friendly minion: the Amalgam 2/2 → 3/3 and Mishmash copies
        // it (+1/+1), then Mishmash's own gem (+1/+1, not copied again): 6/6. Bonker's 2 kills the 0/2.
        var ours = Side(Minion(2, 7, cardId: "BG20_104", tribes: Tribe.Quilboar), Tokens.Amalgam(), Minion(4, 4, cardId: MishmashId));
        var result = Fight(ours, Side(Minion(0, 2, cardId: "W")), oursFirst: true, Effects(typeof(Bonker), typeof(Mishmash)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(2, 7), (3, 3), (6, 6)], Stats(result.Ours));
    }

    [Fact]
    public void Golden_mishmash_gains_it_twice_and_ignores_other_minions()
    {
        // Golden 8/8: +1/+1 twice from the Amalgam's gem, +1/+1 from its own gem: 11/11. The 1/1 X also gets a gem: not
        // an Amalgam, nothing copied.
        var ours = Side(Minion(2, 7, cardId: "BG20_104", tribes: Tribe.Quilboar), Minion(1, 1, cardId: "X"), Tokens.Amalgam(),
                        Minion(8, 8, cardId: MishmashId, golden: true));
        var result = Fight(ours, Side(Minion(0, 2, cardId: "W")), oursFirst: true, Effects(typeof(Bonker), typeof(Mishmash)));

        Assert.Equal([(2, 7), (2, 2), (3, 3), (11, 11)], Stats(result.Ours));
    }

    [Fact]
    public void Gnoll_and_mishmash_are_registered()
    {
        Assert.True(EffectRegistry.Default.Implements(Gnoll, golden: true, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements(MishmashId, golden: false, isOpponent: true));
    }
}

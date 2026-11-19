using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Rewards;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Deaths;

/// <summary>
/// Death's Embrace, a Dark Gift (BG36_MidGameEffect_000t29, enchantment …t29e): "Has +2/+2 for each Deathrattle you've
/// triggered this game." The weaker gift (…t29t, enchantment …t29te): "+1/+1". The captured stats hold the triggers
/// before the fight; each friendly deathrattle trigger in the fight adds the bonus once. Boards worked out by hand in
/// each comment: their Killer attacks first and trades with our Taunt Harmless Bonehead ("Deathrattle: Summon two 1/1
/// Skeletons"), then only their 0-Attack Wall is left and nothing changes our board any more (a tie at the attack cap).
/// </summary>
public sealed class DeathsEmbraceTests
{
    private const string Greater = "BG36_MidGameEffect_000t29e";
    private const string Lesser = "BG36_MidGameEffect_000t29te";
    private const string Skeleton = "BG_ICC_026t 1/1";

    private static SimMinion Bonehead => Minion(1, 1, Keyword.Taunt, Ids.Bonehead, Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    private static SimMinion Gifted(string enchantment) => Minion(2, 3, cardId: "G") with { Enchantments = [enchantment] };

    [Fact]
    public void The_gift_grows_by_two_for_a_friendly_deathrattle()
    {
        // Killer 5/1 hits the Bonehead (Taunt); both die. The Bonehead's deathrattle puts two Skeletons in its slot and
        // is one trigger: G 2/3 becomes 4/5 and attacks the Wall as 4/5 from then on.
        var result = Fight(Side(Bonehead, Gifted(Greater)), Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(DeathsEmbraceGreater)));

        Assert.Equal(0, result.Outcome);
        Assert.Equal([Skeleton, Skeleton, "G 4/5"], Board(result.Ours));
        Assert.Single(result.Lines("deathrattle of our BG28_300"));
        Assert.NotEmpty(result.Lines("our G 4/5 attacks their WALL"));
    }

    [Fact]
    public void The_weaker_gift_grows_by_one()
    {
        // The same fight with …t29te: G 2/3 becomes 3/4.
        var result = Fight(Side(Bonehead, Gifted(Lesser)), Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(DeathsEmbraceLesser)));

        Assert.Equal(0, result.Outcome);
        Assert.Equal([Skeleton, Skeleton, "G 3/4"], Board(result.Ours));
    }

    [Fact]
    public void Each_multiplied_trigger_counts_once()
    {
        // Titus Rivendare (1/7, "Your Deathrattles trigger an extra time"): the Bonehead's deathrattle triggers twice,
        // four Skeletons, and G gains +2/+2 twice: 6/7.
        var result = Fight(Side(Bonehead, Gifted(Greater), Minion(1, 7, cardId: Ids.Titus)), Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(DeathsEmbraceGreater), typeof(TitusRivendare)));

        Assert.Equal([.. Times(4, Skeleton), "G 6/7", "BG25_354 1/7"], Board(result.Ours));
        Assert.Equal(2, result.Lines("deathrattle of our BG28_300").Count);
    }

    [Fact]
    public void A_quest_reward_that_repeats_deathrattles_feeds_the_gift_too()
    {
        // Turbulent Tombs as our extra: two triggers, as with Titus. G 2/3 + 2 × 2/2 = 6/7.
        var result = Fight(Side(Bonehead, Gifted(Greater)) with { Extras = ["BG27_Reward_803"] }, Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(DeathsEmbraceGreater), typeof(TurbulentTombs)));

        Assert.Equal([.. Times(4, Skeleton), "G 6/7"], Board(result.Ours));
    }

    [Fact]
    public void Enemy_deathrattles_do_not_count()
    {
        // Their Bonehead 1/1 attacks G 2/30 first (G 2/29) and dies: their deathrattle, two Skeletons for them. G stays
        // 2/x. G kills one Skeleton (2/28), the other one attacks G and dies (2/27). We win with G 2/27; had their
        // trigger counted, G would be 4/29.
        var theirBonehead = Minion(1, 1, cardId: Ids.Bonehead, tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);
        var gifted = Minion(2, 30, cardId: "G") with { Enchantments = [Greater] };
        var result = Fight(Side(gifted), Side(theirBonehead), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(DeathsEmbraceGreater)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["G 2/27"], Board(result.Ours));
        Assert.Single(result.Lines("deathrattle of their BG28_300"));
    }

    [Fact]
    public void Both_gifts_are_registered_and_count_as_implemented()
    {
        Assert.IsType<DeathsEmbraceGreater>(EffectRegistry.Default.Get(Greater, golden: false));
        Assert.IsType<DeathsEmbraceLesser>(EffectRegistry.Default.Get(Lesser, golden: false));
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify(Greater, CoverageKind.DarkGift).Status);
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify(Lesser, CoverageKind.DarkGift).Status);
    }
}

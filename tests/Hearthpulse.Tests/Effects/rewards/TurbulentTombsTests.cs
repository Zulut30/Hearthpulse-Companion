using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Rewards;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim.Rewards;

/// <summary>
/// Turbulent Tombs, a quest reward (BG27_Reward_803): "Get a copy of '{0}'. Your Deathrattles trigger an extra time."
/// The reward is an extra of its owner's side. Boards worked out by hand in each comment: their Killer attacks first and
/// trades with our Taunt Harmless Bonehead ("Deathrattle: Summon two 1/1 Skeletons"), then only their 0-Attack Wall is
/// left, so the survivors are the board right after the deathrattles (a tie at the attack cap).
/// </summary>
public sealed class TurbulentTombsTests
{
    private const string Tombs = "BG27_Reward_803";
    private const string Skeleton = "BG_ICC_026t 1/1";

    private static SimMinion Bonehead => Minion(1, 1, Keyword.Taunt, Ids.Bonehead, Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    [Fact]
    public void Our_deathrattles_trigger_an_extra_time()
    {
        // The Bonehead's deathrattle runs twice: four Skeletons instead of two.
        var result = Fight(Side(Bonehead) with { Extras = [Tombs] }, Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TurbulentTombs)));

        Assert.Equal(0, result.Outcome);
        Assert.Equal(Times(4, Skeleton), Board(result.Ours));
        Assert.Equal(2, result.Lines("deathrattle of our BG28_300").Count);
        Assert.Equal(4, result.Lines("summoned our BG_ICC_026t").Count);
    }

    [Fact]
    public void The_opponents_reward_does_not_repeat_our_deathrattles()
    {
        // Their Tombs only acts on their side: our Bonehead makes its usual two Skeletons.
        var result = Fight(Side(Bonehead), Side(Killer(5, 1), Wall) with { Extras = [Tombs] }, oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TurbulentTombs)));

        Assert.Equal([Skeleton, Skeleton], Board(result.Ours));
        Assert.Single(result.Lines("deathrattle of our BG28_300"));
    }

    [Fact]
    public void It_adds_to_titus_rivendare()
    {
        // Titus 1/7 (one extra trigger) plus the reward (one more): three triggers, six Skeletons left of Titus, a full
        // board of seven.
        var result = Fight(Side(Bonehead, Minion(1, 7, cardId: Ids.Titus)) with { Extras = [Tombs] }, Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TitusRivendare), typeof(TurbulentTombs)));

        Assert.Equal([.. Times(6, Skeleton), "BG25_354 1/7"], Board(result.Ours));
        Assert.Equal(3, result.Lines("deathrattle of our BG28_300").Count);
    }

    [Fact]
    public void The_reward_no_longer_makes_a_fight_not_simulated()
    {
        // Its generated table row marks it as acting in combat; with the effect registered neither side flags it.
        var side = Side(Minion(1, 1)) with { Extras = [Tombs] };

        Assert.Empty(CombatSimulator.Explain(side, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(side, isOpponent: true));
        Assert.Equal(Relevance.Relevant, CombatRelevance.Find(Tombs)?.Relevance);
    }
}

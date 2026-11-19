using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Deaths;

/// <summary>
/// Fishbait (BG36_205, 0/1): "This can't gain stats. Deathrattle: Give the minion that killed this +5/+5." Golden (0/2):
/// "+10/+10". Expected results are worked out by hand in each comment.
/// </summary>
public sealed class FishbaitTests
{
    private const string Fishbait = "BG36_205";

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void The_minion_that_killed_it_gets_five_five()
    {
        // Their 3/10 kills Fishbait 0/1 (pick 0) and becomes 8/15. It then kills our 0/50 wall in seven attacks
        // (8 × 7 = 56) and is left at 8/15.
        var ours = Side(Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast), Minion(0, 50, cardId: "W"));
        var result = Fight(ours, Side(Minion(3, 10, cardId: "E")), oursFirst: false, Effects(typeof(Fishbait)), 0);

        Assert.Equal(-1, result.Outcome);
        Assert.Equal([(8, 15)], Stats(result.Theirs));
        Assert.Single(result.Lines("deathrattle of our BG36_205"));
    }

    [Fact]
    public void Golden_fishbait_gives_ten_ten()
    {
        // Golden 0/2 dies to their 3/10 (3 ≥ 2): 13/20.
        var ours = Side(Minion(0, 2, cardId: Fishbait, tribes: Tribe.Beast, golden: true), Minion(0, 50, cardId: "W"));
        var result = Fight(ours, Side(Minion(3, 10, cardId: "E")), oursFirst: false, Effects(typeof(Fishbait)), 0);

        Assert.Equal([(13, 20)], Stats(result.Theirs));
    }

    [Fact]
    public void Only_the_killer_is_buffed()
    {
        // Their 1/1 kills Fishbait (pick 0) and becomes 6/6; their 0/9 gets nothing. Our 1/1 R attacks the 6/6 (pick 0):
        // 6/5, and R dies.
        var ours = Side(Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast), Minion(1, 1, cardId: "R"));
        var theirs = Side(Minion(1, 1, cardId: "E"), Minion(0, 9, cardId: "W"));
        var result = Fight(ours, theirs, oursFirst: false, Effects(typeof(Fishbait)), 0, 0);

        Assert.Equal(-1, result.Outcome);
        Assert.Equal([(6, 5), (0, 9)], Stats(result.Theirs));
    }

    [Fact]
    public void It_cannot_gain_stats_from_buffs()
    {
        // Bonker 3/5 (Windfury; Rally: a Blood Gem on each other friendly minion) attacks their 0/6 twice: two gems would
        // make Fishbait 2/3, but it can't gain stats and stays 0/1. Bonker kills the 0/6 with its two hits.
        var ours = Side(Minion(3, 5, Keyword.Windfury, "BG20_104", Tribe.Quilboar), Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast));
        var result = Fight(ours, Side(Minion(0, 6, cardId: "W")), oursFirst: true, Effects(typeof(Fishbait), typeof(Bonker)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(3, 5), (0, 1)], Stats(result.Ours));
    }

    [Fact]
    public void An_aura_it_never_gained_cannot_take_stats_away_when_it_leaves()
    {
        // The engine assumes captured stats include the auras present at capture. Our aura minion (+2/+2 to the others)
        // dies first (their 5/50 picks it): Fishbait must not lose 2/2 it never had and die. Their 5/50 then kills
        // Fishbait in its next attack and becomes 10/54 (it took 1 from the aura minion).
        var ours = Side(Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast), Minion(1, 1, cardId: OtherMinionsAura.Id));
        var result = Fight(ours, Side(Minion(5, 50, cardId: "E")), oursFirst: false, Effects(typeof(Fishbait), typeof(OtherMinionsAura)), 1, 0);

        Assert.Equal(-1, result.Outcome);
        Assert.Equal([(10, 54)], Stats(result.Theirs));
        var deaths = result.Log.Where(l => l.EndsWith(" dies", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, deaths.Count);
        Assert.StartsWith("our TEST_AURA_OTHERS", deaths[0], StringComparison.Ordinal);
        Assert.StartsWith("our BG36_205 0/", deaths[1], StringComparison.Ordinal);
        Assert.Equal(2, result.Lines("their E").Count(l => l.Contains(" attacks ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fishbait_is_simulated_for_both_sides_normal_and_golden()
    {
        Assert.True(EffectRegistry.Default.Implements(Fishbait, golden: false, isOpponent: true));
        Assert.True(EffectRegistry.Default.Implements("BG36_205_G", golden: true));
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify(Fishbait, CoverageKind.Minion).Status);
    }
}

/// <summary>Test aura: the other friendly minions have +2/+2.</summary>
[CardEffect(Id)]
internal sealed class OtherMinionsAura : CombatEffect
{
    public const string Id = "TEST_AURA_OTHERS";

    public override StatDelta AuraFor(Fight fight, Unit self, Unit other) => other == self ? default : new StatDelta(2, 2);
}

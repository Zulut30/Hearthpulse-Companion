using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Rewards;

/// <summary>
/// Quest rewards and quests out of today's pool that return with the rotation (TASK-2026-11-19 E6a). A reward is an extra
/// of its side, a quest an extra with its QUEST_PROGRESS / _TOTAL / reward dbf. Boards worked out by hand in each comment.
/// </summary>
public sealed class RotationRewardsTests
{
    private const int TurbulentTombsDbf = 104670;
    private const string Skeleton = "summoned our BG_ICC_026t";

    private static SimSide With(SimSide side, string cardId, Dictionary<string, int>? tags = null) => side with
    {
        Extras = [.. side.Extras, cardId],
        ExtraTags = tags is null
            ? side.ExtraTags
            : new Dictionary<string, IReadOnlyDictionary<string, int>>(side.ExtraTags) { [cardId] = tags },
    };

    private static SimSide Quest(SimSide side, string questId, int progress, int total) =>
        With(side, questId, new() { [SimInputs.QuestProgress] = progress, [SimInputs.QuestTotal] = total, [SimInputs.QuestReward] = TurbulentTombsDbf });

    private static SimMinion Bonehead => Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    /// <summary>Our R 1/1 with Reborn; their Killer 5/1 attacks it first: both die and R comes back (a summon).</summary>
    private static FightResult Reborn(SimSide ours) => Fight(ours, Side(Killer(5, 1)), oursFirst: false);

    private static SimSide RebornBoard => Side(Minion(1, 1, Keyword.Reborn, "R"));

    [Fact]
    public void Boom_squad_hits_the_highest_health_enemy_after_three_friendly_deaths()
    {
        // Their E 1/9 and H 1/10 kill our three 0/1 minions one by one; the third death fires Avenge (3): 10 damage to H
        // (the highest Health), which dies.
        var ours = Side(Minion(0, 1, cardId: "A"), Minion(0, 1, cardId: "B"), Minion(0, 1, cardId: "C"));
        var theirs = Side(Minion(1, 9, cardId: "E"), Minion(1, 10, cardId: "H"));

        Assert.Equal(["E 1/9", "H 1/10"], Board(Fight(ours, theirs, oursFirst: false).Theirs));
        Assert.Equal(["E 1/9"], Board(Fight(With(ours, "BG27_Reward_502"), theirs, oursFirst: false).Theirs));
    }

    [Fact]
    public void Boom_squad_reads_its_captured_avenge_number()
    {
        // SCORE_VALUE_1 = 1: the first death already fires. H 1/10 dies, then E kills the remaining minions one by one,
        // each death firing again: E (1/9, then the highest) takes 10 and dies as well.
        var ours = Side(Minion(0, 1, cardId: "A"), Minion(0, 1, cardId: "B"));
        var theirs = Side(Minion(1, 9, cardId: "E"), Minion(1, 10, cardId: "H"));
        var result = Fight(With(ours, "BG27_Reward_502", new() { ["SCORE_VALUE_1"] = 1, ["SCORE_VALUE_2"] = 0 }), theirs, oursFirst: false);

        Assert.Empty(result.Theirs);
    }

    [Fact]
    public void Ritual_dagger_buffs_a_dead_deathrattle_minion()
    {
        // Find the Murder Weapon at 0 of 1 stat gains, reward Turbulent Tombs. The Killer kills Bonehead A (two Skeletons);
        // the Dagger then gives A +5/+5, a stat gain: the quest completes and Bonehead B's deathrattle later triggers twice.
        // 2 + 4 Skeletons; without the Dagger 2 + 2.
        var ours = Quest(Side(Bonehead, Bonehead), "BG24_Quest_123", 0, 1);
        var theirs = Side(Killer(10, 100));

        Assert.Equal(4, Fight(ours, theirs, oursFirst: false).Lines(Skeleton).Count);
        Assert.Equal(6, Fight(With(ours, "BG24_Reward_113"), theirs, oursFirst: false).Lines(Skeleton).Count);
    }

    [Fact]
    public void Stable_amalgamation_summons_a_horse_after_seven_deaths()
    {
        // The Killer kills our seven 0/1 minions one by one; the seventh death fires Avenge (7) and the 50/50 Horse (all
        // types) is summoned in the space it left. It then beats the Killer.
        var ours = With(Side([.. Enumerable.Range(0, 7).Select(i => Minion(0, 1, cardId: "M" + i))]), "BG28_Reward_518");
        var result = Fight(ours, Side(Killer(10, 100)), oursFirst: false);

        Assert.Single(result.Lines("summoned our BG28_Reward_518t"));
        var horse = Assert.Single(result.Ours);
        Assert.Equal("BG28_Reward_518t", horse.CardId);
        Assert.Equal(Tribe.All, horse.Tribes);
    }

    [Fact]
    public void The_smoking_gun_gives_summoned_minions_four_attack()
    {
        Assert.Equal(["R 5/1"], Board(Reborn(With(RebornBoard, "BG24_Reward_125")).Ours));
    }

    [Theory]
    [InlineData(null, 0, "R 5/5")]       // printed +4/+4
    [InlineData(6, 0, "R 7/6")]          // captured +6/+5
    [InlineData(null, 1, "R 6/6")]       // Avenge (1) fires on R's death before it comes back: +5/+5
    public void Tumbling_disaster_buffs_summoned_minions_and_improves_on_avenge(int? captured, int threshold, string reborn)
    {
        var tags = new Dictionary<string, int>();
        if (captured is { } attack)
        {
            tags["TAG_SCRIPT_DATA_NUM_1"] = attack;
            tags["TAG_SCRIPT_DATA_NUM_2"] = attack - 1;
        }

        if (threshold > 0)
        {
            tags["SCORE_VALUE_1"] = threshold;
        }

        Assert.Equal([reborn], Board(Reborn(With(RebornBoard, "BG28_Reward_505", tags.Count > 0 ? tags : null)).Ours));
    }

    [Fact]
    public void The_opponents_tumbling_disaster_value_is_an_estimate()
    {
        var theirs = With(Side(Minion(1, 1)), "BG28_Reward_505");
        var estimates = SimEstimates.For(theirs, isOpponent: true, [], local: null);

        Assert.Contains(estimates, e => e.Input == SimInputs.TrinketValue && e.Basis.StartsWith("BG28_Reward_505:", StringComparison.Ordinal));
        // Without the estimate the opponent's number is a missing input, never a silent printed value; ours is captured.
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(theirs, isOpponent: true)).Reason);
        Assert.Empty(CombatSimulator.Explain(theirs with { Estimates = estimates }, isOpponent: true));
    }

    [Fact]
    public void Volatile_venom_gives_summoned_minions_seven_seven()
    {
        Assert.Equal(["R 8/8"], Board(Reborn(With(RebornBoard, "BG24_Reward_364")).Ours));
    }

    [Fact]
    public void Volatile_venom_destroys_a_minion_after_it_attacks()
    {
        // Our A 1/10 attacks their Wall and survives the exchange, then dies: we have nothing left.
        var result = Fight(With(Side(Minion(1, 10, cardId: "A")), "BG24_Reward_364"), Side(Wall), oursFirst: true);

        Assert.Empty(result.Ours);
        Assert.Equal(-1, result.Outcome);
    }

    [Fact]
    public void Exhume_the_bones_counts_friendly_deathrattle_triggers()
    {
        // At 0 of 1: Bonehead A's deathrattle completes it; Turbulent Tombs then makes B's trigger twice. 2 + 4 Skeletons.
        var ours = Quest(Side(Bonehead, Bonehead), "BG24_Quest_320", 0, 1);

        Assert.Equal(6, Fight(ours, Side(Killer(10, 100)), oursFirst: false).Lines(Skeleton).Count);
    }

    [Fact]
    public void Witness_protection_counts_attacks_on_friendly_taunt_minions()
    {
        // At 0 of 1: the Killer attacks our Taunt T (completing it) and kills it; Bonehead B then triggers twice: 4 Skeletons.
        // Without Taunt on T the attack does not count: 2.
        var taunt = Quest(Side(Minion(0, 1, Keyword.Taunt, "T"), Bonehead), "BG24_Quest_318", 0, 1);
        var plain = Quest(Side(Minion(0, 1, cardId: "T"), Bonehead), "BG24_Quest_318", 0, 1);

        Assert.Equal(4, Fight(taunt, Side(Killer(10, 100)), oursFirst: false).Lines(Skeleton).Count);
        Assert.Equal(2, Fight(plain, Side(Killer(10, 100)), oursFirst: false).Lines(Skeleton).Count);
    }

    [Fact]
    public void A_magnanimoose_lists_the_opponent_warband_estimate()
    {
        var side = Side(Minion(8, 2, cardId: SimInputs.Magnanimoose, tribes: Tribe.Beast));

        Assert.Contains(SimEstimates.For(side, isOpponent: false, [], local: null), e => e.Input == SimInputs.OpponentWarband);
        Assert.Contains(SimEstimates.For(side, isOpponent: true, [], local: null), e => e.Input == SimInputs.OpponentWarband);
    }
}

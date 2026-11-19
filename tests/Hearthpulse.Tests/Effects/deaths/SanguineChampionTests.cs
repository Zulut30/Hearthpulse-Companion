using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Deaths;

/// <summary>
/// Sanguine Champion (BG23_017): "Battlecry and Deathrattle: Your Blood Gems give an extra +2/+1 this game." Golden:
/// "+4/+2". Seen through Tusked Camper (BG33_886, "Rally: This plays a Blood Gem on itself"): a Blood Gem gives
/// +(1 + bonus Attack)/+(1 + bonus Health). Expected results are worked out by hand in each comment.
/// </summary>
public sealed class SanguineChampionTests
{
    private const string Champion = "BG23_017";
    private const string Camper = "BG33_886";

    private static EffectRegistry Registry => Effects(typeof(SanguineChampion), typeof(TuskedCamper));

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void Its_deathrattle_makes_blood_gems_played_later_in_the_fight_bigger()
    {
        // Their 9/4 attacks first, trades with our Champion 9/4: our Blood Gems now give +2/+1 extra. Our Camper 2/3 then
        // attacks their 0/5: its Rally gem gives +3/+2 → 5/5, enough to kill the 0/5. Without the deathrattle: +1/+1, 3/4,
        // and the 0/5 would survive at 2 Health.
        var ours = Side(Minion(9, 4, cardId: Champion, tribes: Tribe.Quilboar), Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar));
        var theirs = Side(Minion(9, 4, cardId: "KILLER"), Minion(0, 5, cardId: "W"));
        var result = Fight(ours, theirs, oursFirst: false, Registry, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([Camper], result.Ours.Select(m => m.CardId));
        Assert.Equal([(5, 5)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +2/+1 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void Golden_champion_gives_four_one_and_two_and_adds_to_the_captured_bonus()
    {
        // Captured bonus +1/+1. Golden Champion 18/8 trades with their 18/8: bonus +5/+3. The Camper's gem gives +6/+4 → 8/7.
        var ours = Side(Minion(18, 8, cardId: Champion, tribes: Tribe.Quilboar, golden: true), Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar))
            with { Tags = new Dictionary<string, int> { [BloodGems.AttackTag] = 1, [BloodGems.HealthTag] = 1 } };
        var theirs = Side(Minion(18, 8, cardId: "KILLER"), Minion(0, 8, cardId: "W"));
        var result = Fight(ours, theirs, oursFirst: false, Registry, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(8, 7)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +5/+3 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void The_captured_script_data_wins_over_the_card_text()
    {
        // A balance change shows in TAG_SCRIPT_DATA_NUM_1/2: a Champion carrying 3 and 2 gives +3/+2, not +2/+1. The
        // Camper's gem is then +4/+3 → 6/6, which kills the 0/6.
        var champion = Minion(9, 4, cardId: Champion, tribes: Tribe.Quilboar) with { ScriptData1 = 3, ScriptData2 = 2 };
        var ours = Side(champion, Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(9, 4, cardId: "KILLER"), Minion(0, 6, cardId: "W")), oursFirst: false, Registry, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(6, 6)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +3/+2 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void Each_deathrattle_trigger_adds_the_bonus_again()
    {
        // Titus Rivendare (an extra trigger): the Champion's deathrattle runs twice, +4/+2. Camper's gem +5/+3 → 7/6.
        var ours = Side(
            Minion(9, 4, cardId: Champion, tribes: Tribe.Quilboar),
            Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar),
            Minion(1, 7, cardId: "BG25_354", tribes: Tribe.Undead));
        var theirs = Side(Minion(9, 4, cardId: "KILLER"), Minion(0, 7, cardId: "W"));
        var result = Fight(ours, theirs, oursFirst: false,
                           Effects(typeof(SanguineChampion), typeof(TuskedCamper), typeof(TitusRivendare)), 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(7, 6), (1, 7)], Stats(result.Ours));
        Assert.Equal(2, result.Lines("our Blood Gems").Count);
    }

    [Fact]
    public void The_opponents_champion_raises_only_the_opponents_gems()
    {
        // Our 9/4 trades with their Champion; their Camper's gem is then +3/+2, ours stays at the captured bonus.
        var theirs = Side(Minion(9, 4, cardId: Champion, tribes: Tribe.Quilboar), Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar));
        var result = Fight(Side(Minion(9, 4, cardId: "KILLER"), Minion(0, 5, cardId: "W")), theirs, oursFirst: true, Registry, 0);

        Assert.Equal(-1, result.Outcome);
        Assert.Equal([(5, 5)], Stats(result.Theirs));
        Assert.Equal(["their Blood Gems now +2/+1 extra"], result.Lines("their Blood Gems"));
    }

    [Fact]
    public void Champion_and_refiner_are_simulated_normal_and_golden()
    {
        foreach (var id in new[] { Champion, "BG33_885" })
        {
            Assert.True(EffectRegistry.Default.Implements(id, golden: false));
            Assert.True(EffectRegistry.Default.Implements(id, golden: true));
            Assert.True(EffectRegistry.Default.Implements(id, golden: false, isOpponent: true));
            Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify(id, CoverageKind.Minion).Status);
        }
    }
}

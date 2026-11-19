using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Attack;

/// <summary>
/// Sanguine Refiner (BG33_885): "Rally: Your Blood Gems give an extra +1/+1 this game." Golden: "+2/+2". Seen through
/// Tusked Camper (BG33_886, "Rally: This plays a Blood Gem on itself"): a gem gives +(1 + bonus)/+(1 + bonus). Their
/// minions have 0 Attack and never attack; results are worked out by hand in each comment.
/// </summary>
public sealed class SanguineRefinerTests
{
    private const string Refiner = "BG33_885";
    private const string Camper = "BG33_886";

    private static EffectRegistry Registry => Effects(typeof(SanguineRefiner), typeof(TuskedCamper));

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void Its_rally_raises_the_blood_gems_played_after_it()
    {
        // Refiner 4/9 attacks their 0/8 (→ 0/4): gems +1/+1 extra. Camper 2/3 attacks: gem +2/+2 → 4/5, kills the 0/4.
        var ours = Side(Minion(4, 9, cardId: Refiner, tribes: Tribe.Quilboar), Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(0, 8, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(4, 9), (4, 5)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +1/+1 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void Golden_refiner_gives_two_two_per_attack_and_it_adds_up()
    {
        // Golden Refiner 8/18 attacks a 0/30 (→ 0/22): +2/+2. Camper: gem +3/+3 → 5/6 (→ 0/17). Refiner again: +4/+4
        // (→ 0/9). Camper: gem +5/+5 → 10/11, kills the 0/9.
        var ours = Side(Minion(8, 18, cardId: Refiner, tribes: Tribe.Quilboar, golden: true), Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(0, 30, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(8, 18), (10, 11)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +2/+2 extra", "our Blood Gems now +4/+4 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void The_captured_script_data_wins_over_the_card_text()
    {
        // A Refiner carrying TAG_SCRIPT_DATA_NUM_1/2 = 2/1 gives +2/+1: Camper's gem +3/+2 → 5/5, which kills the 0/4 left.
        var refiner = Minion(4, 9, cardId: Refiner, tribes: Tribe.Quilboar) with { ScriptData1 = 2, ScriptData2 = 1 };
        var result = Fight(Side(refiner, Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar)), Side(Minion(0, 8, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(4, 9), (5, 5)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +2/+1 extra"], result.Lines("our Blood Gems"));
    }

    [Fact]
    public void A_gem_played_before_the_rally_keeps_the_old_value()
    {
        // Camper first (gem +1/+1 → 3/4, 0/7 → 0/4), then the Refiner (+1/+1 extra; 0/4 → dead). Camper stays 3/4.
        var ours = Side(Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar), Minion(4, 9, cardId: Refiner, tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(0, 7, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(3, 4), (4, 9)], Stats(result.Ours));
    }
}

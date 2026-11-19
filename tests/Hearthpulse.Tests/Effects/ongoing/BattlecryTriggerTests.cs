using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Battlecries triggered in combat and what reacts to them: Timewarped Greenskeeper (BG34_Giant_041, "Rally: Trigger your
/// right-most Battlecry and Deathrattle", golden: twice), Brann Bronzebeard (BG_LOE_077, "Your Battlecries trigger
/// twice", golden: three times), Kalecgos (BGS_041, "After you trigger a Battlecry, give your Dragons +2/+2", golden
/// +4/+4) and Dark Paradox t6 ("Has +4/+4 for each Battlecry you've triggered this game"). Sanguine Champion (BG23_017,
/// "Battlecry and Deathrattle: Your Blood Gems give an extra +2/+1") makes each trigger visible in the trace. Their
/// minions have 0 Attack and never attack; results are worked out by hand in each comment.
/// </summary>
public sealed class BattlecryTriggerTests
{
    private const string Greenskeeper = "BG34_Giant_041";
    private const string Champion = "BG23_017";
    private const string Brann = "BG_LOE_077";
    private const string Kalecgos = "BGS_041";
    private const string Camper = "BG33_886";

    private static EffectRegistry Registry => Effects(
        typeof(TimewarpedGreenskeeper), typeof(SanguineChampion), typeof(TuskedCamper), typeof(BrannBronzebeard),
        typeof(KalecgosArcaneAspect), typeof(DarkParadoxBattlecries), typeof(TitusRivendare));

    private static SimMinion Keeper(bool golden = false) => Minion(golden ? 10 : 5, golden ? 18 : 9, cardId: Greenskeeper, tribes: Tribe.Dragon, golden: golden);

    private static SimMinion SanguineChampion() => Minion(9, 4, cardId: Champion, tribes: Tribe.Quilboar, mechanics: CardMechanic.Battlecry | CardMechanic.Deathrattle);

    private static SimMinion TuskedCamper() => Minion(2, 3, cardId: Camper, tribes: Tribe.Quilboar);

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void Greenskeeper_triggers_the_right_most_battlecry_then_the_right_most_deathrattle()
    {
        // Keeper 5/9 attacks a 0/12: Champion's Battlecry (+2/+1), then its Deathrattle (+2/+1): gems +4/+2 extra. Keeper
        // deals 5 (0/7). Camper's gem +5/+3 → 7/6 kills the 0/7.
        var ours = Side(Keeper(), TuskedCamper(), SanguineChampion());
        var result = Fight(ours, Side(Minion(0, 12, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(5, 9), (7, 6), (9, 4)], Stats(result.Ours));
        Assert.Equal(["our Blood Gems now +2/+1 extra", "our Blood Gems now +4/+2 extra"], result.Lines("our Blood Gems"));
        Assert.True(result.IndexOf("battlecry of our BG23_017") < result.IndexOf("deathrattle of our BG23_017"));
    }

    [Fact]
    public void Golden_greenskeeper_triggers_each_twice()
    {
        // Golden Keeper 10/18: Battlecry twice (+4/+2), Deathrattle twice (+4/+2): +8/+4. Keeper deals 10 to a 0/19 (0/9);
        // Camper's gem +9/+5 → 11/8 kills it.
        var ours = Side(Keeper(golden: true), TuskedCamper(), SanguineChampion());
        var result = Fight(ours, Side(Minion(0, 19, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(10, 18), (11, 8), (9, 4)], Stats(result.Ours));
        Assert.Equal(2, result.Lines("battlecry of our BG23_017").Count);
        Assert.Equal(2, result.Lines("deathrattle of our BG23_017").Count);
    }

    [Fact]
    public void Brann_doubles_the_triggered_battlecry_but_not_the_deathrattle()
    {
        // Brann (right of the Champion, no Battlecry): Battlecry twice (+4/+2) and Deathrattle once (+2/+1): +6/+3. Camper's
        // gem +7/+4 → 9/7. Keeper 5 + Camper 9 = 14 kills the 0/14.
        var ours = Side(Keeper(), TuskedCamper(), SanguineChampion(), Minion(2, 4, cardId: Brann));
        var result = Fight(ours, Side(Minion(0, 14, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(5, 9), (9, 7), (9, 4), (2, 4)], Stats(result.Ours));
        Assert.Equal(2, result.Lines("battlecry of our BG23_017").Count);
        Assert.Single(result.Lines("deathrattle of our BG23_017"));
    }

    [Fact]
    public void Golden_brann_triggers_three_times_and_branns_do_not_add_up()
    {
        // A normal and a golden Brann: three times (the largest), not four. Battlecry +6/+3, Deathrattle +2/+1: +8/+4.
        var ours = Side(Keeper(), TuskedCamper(), SanguineChampion(), Minion(2, 4, cardId: Brann), Minion(4, 8, cardId: Brann, golden: true));
        var result = Fight(ours, Side(Minion(0, 16, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(3, result.Lines("battlecry of our BG23_017").Count);
        Assert.Equal("our Blood Gems now +8/+4 extra", result.Lines("our Blood Gems")[^1]);
        Assert.Equal((11, 8), Stats(result.Ours)[1]);

        // Two normal Branns: still twice.
        var two = Side(Keeper(), TuskedCamper(), SanguineChampion(), Minion(2, 4, cardId: Brann), Minion(2, 4, cardId: Brann));
        Assert.Equal(2, Fight(two, Side(Minion(0, 14, cardId: "W")), oursFirst: true, Registry).Lines("battlecry of our BG23_017").Count);
    }

    [Fact]
    public void Kalecgos_buffs_dragons_after_each_battlecry_trigger()
    {
        // Keeper (Dragon) triggers the right-most Battlecry, a 1/1 whose Battlecry does nothing in combat: it still counts.
        // Kalecgos: Dragons +2/+2 → Keeper 7/11, Kalecgos 6/14. The Keeper then deals 7: the 0/7 dies.
        var ours = Side(Keeper(), Minion(4, 12, cardId: Kalecgos, tribes: Tribe.Dragon), Minion(1, 1, cardId: "BC", mechanics: CardMechanic.Battlecry));
        var result = Fight(ours, Side(Minion(0, 7, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(7, 11), (6, 14), (1, 1)], Stats(result.Ours));
        Assert.Single(result.Lines("battlecry of our BC"));
    }

    [Fact]
    public void Golden_kalecgos_reacts_to_every_trigger_brann_makes()
    {
        // Brann doubles the trigger; golden Kalecgos gives +4/+4 per trigger: +8/+8 to each Dragon. Keeper 13/17 kills a 0/13.
        var ours = Side(Keeper(), Minion(8, 24, cardId: Kalecgos, tribes: Tribe.Dragon, golden: true),
                        Minion(1, 1, cardId: "BC", mechanics: CardMechanic.Battlecry), Minion(2, 4, cardId: Brann));
        var result = Fight(ours, Side(Minion(0, 13, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(13, 17), (16, 32), (1, 1), (2, 4)], Stats(result.Ours));
    }

    [Fact]
    public void Kalecgos_reads_its_numbers_from_the_captured_script_data()
    {
        // Kalecgos carrying TAG_SCRIPT_DATA_NUM_1/2 = 3/1 gives +3/+1: Keeper 8/10, Kalecgos 7/13; the Keeper's 8 kills a 0/8.
        var kalecgos = Minion(4, 12, cardId: Kalecgos, tribes: Tribe.Dragon) with { ScriptData1 = 3, ScriptData2 = 1 };
        var ours = Side(Keeper(), kalecgos, Minion(1, 1, cardId: "BC", mechanics: CardMechanic.Battlecry));
        var result = Fight(ours, Side(Minion(0, 8, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(8, 10), (7, 13), (1, 1)], Stats(result.Ours));
    }

    [Fact]
    public void A_multiplier_repeats_each_battlecry_in_a_row()
    {
        // A minion with two Battlecries (its card's A and an enchantment's B) under Brann: A, A, B, B (the order real logs
        // show for deathrattles with extra triggers), each one a trigger Kalecgos reacts to (4 × +2/+2).
        var both = Minion(1, 1, cardId: BattlecryA.Id) with { Enchantments = [BattlecryB.Id] };
        var ours = Side(Keeper(), Minion(4, 12, cardId: Kalecgos, tribes: Tribe.Dragon), both, Minion(2, 4, cardId: Brann));
        var registry = Effects(typeof(TimewarpedGreenskeeper), typeof(BrannBronzebeard), typeof(KalecgosArcaneAspect), typeof(BattlecryA), typeof(BattlecryB));
        var result = Fight(ours, Side(Minion(0, 13, cardId: "W")), oursFirst: true, registry);

        Assert.Equal(["A", "A", "B", "B"], result.Log.Where(l => l is "A" or "B"));
        Assert.Equal((12, 20), Stats(result.Ours)[1]);
    }

    [Fact]
    public void Dark_paradox_with_the_battlecry_gift_grows_per_trigger()
    {
        // The Battlecry form t6 4/4 is the right-most minion without a Battlecry; the 1/1's Battlecry is triggered: t6 → 8/8.
        // Golden t6 8/8: +8/+8 → 16/16.
        var ours = Side(Keeper(), Minion(1, 1, cardId: "BC", mechanics: CardMechanic.Battlecry), Minion(4, 4, cardId: "BG36_360t6"));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), oursFirst: true, Registry);
        Assert.Equal([(5, 9), (1, 1), (8, 8)], Stats(result.Ours));

        var golden = Side(Keeper(), Minion(1, 1, cardId: "BC", mechanics: CardMechanic.Battlecry), Minion(8, 8, cardId: "BG36_360_Gt6", golden: true));
        Assert.Equal((16, 16), Stats(Fight(golden, Side(Minion(0, 5, cardId: "W")), oursFirst: true, Registry).Ours)[2]);
    }

    [Fact]
    public void Greenskeeper_without_battlecries_or_deathrattles_does_nothing_and_titus_multiplies_its_deathrattle()
    {
        // Nothing to trigger: the Keeper just attacks.
        var plain = Fight(Side(Keeper(), Minion(1, 1, cardId: "X")), Side(Minion(0, 5, cardId: "W")), oursFirst: true, Registry);
        Assert.Empty(plain.Lines("battlecry of"));
        Assert.Empty(plain.Lines("deathrattle of"));

        // Titus Rivendare (an extra time) doubles the triggered Deathrattle: Champion's Battlecry once, Deathrattle twice.
        var ours = Side(Keeper(), SanguineChampion(), Minion(1, 7, cardId: "BG25_354", tribes: Tribe.Undead));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), oursFirst: true, Registry);
        Assert.Single(result.Lines("battlecry of our BG23_017"));
        Assert.Equal(2, result.Lines("deathrattle of our BG23_017").Count);
    }

    [Fact]
    public void The_opponents_greenskeeper_works_the_same()
    {
        // Their Keeper attacks our 0/12 first: their Champion's Battlecry and Deathrattle give their gems +4/+2.
        var theirs = Side(Keeper(), TuskedCamper(), SanguineChampion());
        var result = Fight(Side(Minion(0, 12, cardId: "W")), theirs, oursFirst: false, Registry);

        Assert.Equal(-1, result.Outcome);
        Assert.Equal("their Blood Gems now +4/+2 extra", result.Lines("their Blood Gems")[^1]);
    }

    [Fact]
    public void The_new_effects_are_registered_and_dark_paradox_untransformed_is_a_plain_minion()
    {
        foreach (var (id, golden) in new[] { (Greenskeeper, "BG34_Giant_041_G"), (Brann, "TB_BaconUps_045"), (Kalecgos, "TB_BaconUps_109"), ("BG36_360", "BG36_360_G") })
        {
            Assert.True(EffectRegistry.Default.Implements(id, golden: false, isOpponent: true));
            Assert.True(EffectRegistry.Default.Implements(golden, golden: true, isOpponent: true));
        }

        Assert.True(EffectRegistry.Default.Implements("BG36_360t6", golden: false));
        Assert.True(EffectRegistry.Default.Implements("BG36_360_Gt6", golden: true));
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify("BG36_360", CoverageKind.Minion).Status);
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify(Brann, CoverageKind.Minion).Status);

        // The untransformed card has no hooks: a captured one fights with its stats only.
        var result = Fight(Side(Minion(3, 3, cardId: "BG36_360")), Side(Minion(2, 2, cardId: "E")), oursFirst: true);
        Assert.Equal(1, result.Outcome);
        Assert.Equal([(3, 1)], Stats(result.Ours));
    }
}

/// <summary>Test Battlecry A: writes "A" to the trace.</summary>
[CardEffect(Id)]
internal sealed class BattlecryA : CombatEffect
{
    public const string Id = "TEST_BC_A";

    public override void Battlecry(Fight fight, Unit self) => fight.Trace("A");
}

/// <summary>Test Battlecry B (an enchantment's): writes "B" to the trace.</summary>
[CardEffect(Id)]
internal sealed class BattlecryB : CombatEffect
{
    public const string Id = "TEST_BC_B";

    public override void Battlecry(Fight fight, Unit self) => fight.Trace("B");
}

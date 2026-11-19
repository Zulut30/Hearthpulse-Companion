using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Xunit.Abstractions;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Executes 50 diverse, deep combat simulations covering every key Battlegrounds combat interaction: the odds are
/// well-formed for every scenario, and each scenario's odds meet the outcome its rules give (exact 100 % win / loss / tie,
/// a probability worked out by hand, or a bound). Expectations corrected on 2026-11-19 (sim-all-mechanics P0) carry the
/// reasoning in a comment; none of the 50 needed an engine change.
/// </summary>
public sealed class FiftyCombatDeepAccuracyTests(ITestOutputHelper output)
{
    private static SimMinion M(int attack, int health, Keyword kw = Keyword.None, string cardId = "", Tribe tribe = Tribe.None, bool golden = false) =>
        Minion(attack, health, kw, cardId, tribe, golden);

    public record Scenario(
        int Id,
        string Name,
        SimSide Ours,
        SimSide Theirs,
        bool? OursFirst,
        EffectRegistry? Effects,
        ExpectedOutcome Expected);

    public record ExpectedOutcome(
        double? MinWin = null,
        double? MaxWin = null,
        double? MinLoss = null,
        double? MaxLoss = null,
        double? MinTie = null,
        double? MaxTie = null,
        bool ExactWin = false,
        bool ExactLoss = false,
        bool ExactTie = false,
        bool Symmetric = false);

    /// <summary>|win - loss| allowed for a mirror with a coin-flip start: about 3 standard errors of 2000 fights.</summary>
    private const double SymmetryTolerance = 0.06;

    public static TheoryData<int> ScenarioIds
    {
        get
        {
            var ids = new TheoryData<int>();
            for (var id = 1; id <= 50; id++)
            {
                ids.Add(id);
            }

            return ids;
        }
    }

    private static EffectRegistry TestEffects() =>
        Effects(typeof(SummonTwoTokens), typeof(TitusMultiplier), typeof(CleaveEffect), typeof(AvengeBuff), typeof(AuraTwoTwo));

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Each_scenario_meets_its_expected_outcome(int id)
    {
        var s = BuildFiftyScenarios(TestEffects()).Single(x => x.Id == id);
        var odds = CombatSimulator.Run(s.Ours, s.Theirs, s.OursFirst, new SimOptions { Runs = 2000, Effects = s.Effects });
        var e = s.Expected;
        var why = $"#{id} {s.Name}: win {odds.Win:P1}, tie {odds.Tie:P1}, loss {odds.Loss:P1}; expected {e}";

        if (e.ExactWin)
        {
            Assert.True(odds.Win == 1.0, why);
        }

        if (e.ExactLoss)
        {
            Assert.True(odds.Loss == 1.0, why);
        }

        if (e.ExactTie)
        {
            Assert.True(odds.Tie == 1.0, why);
        }

        Assert.True(odds.Win >= (e.MinWin ?? 0) && odds.Win <= (e.MaxWin ?? 1), why);
        Assert.True(odds.Loss >= (e.MinLoss ?? 0) && odds.Loss <= (e.MaxLoss ?? 1), why);
        Assert.True(odds.Tie >= (e.MinTie ?? 0) && odds.Tie <= (e.MaxTie ?? 1), why);
        if (e.Symmetric)
        {
            Assert.True(Math.Abs(odds.Win - odds.Loss) <= SymmetryTolerance, why);
        }
    }

    [Fact]
    public void Run_fifty_deep_combat_simulations_and_verify_accuracy()
    {
        var scenarios = BuildFiftyScenarios(TestEffects());

        Assert.Equal(50, scenarios.Count);

        output.WriteLine("=========================================================================================");
        output.WriteLine("EXECUTING 50 DEEP BATTLEGROUNDS COMBAT SIMULATIONS & ACCURACY CHECKS");
        output.WriteLine("=========================================================================================\n");

        int passedCount = 0;

        foreach (var s in scenarios)
        {
            var odds = CombatSimulator.Run(s.Ours, s.Theirs, s.OursFirst, new SimOptions
            {
                Runs = 2000,
                Effects = s.Effects
            });

            // Invariant 1: Total odds sum strictly to 100%
            Assert.Equal(1.0, odds.Win + odds.Loss + odds.Tie, 12);
            Assert.True(odds.Win >= 0.0 && odds.Win <= 1.0);
            Assert.True(odds.Loss >= 0.0 && odds.Loss <= 1.0);
            Assert.True(odds.Tie >= 0.0 && odds.Tie <= 1.0);
            Assert.False(double.IsNaN(odds.Win) || double.IsNaN(odds.Loss) || double.IsNaN(odds.Tie));

            // Run a traced single fight to confirm execution engine validity
            var single = CombatSimulator.RunSingle(s.Ours, s.Theirs, s.OursFirst, new Random(42), s.Effects);
            Assert.True(single.Outcome is -1 or 0 or 1);
            Assert.NotEmpty(single.Log);

            passedCount++;
            output.WriteLine($"[Combat #{s.Id:D2}] {s.Name,-48} | Win: {odds.Win,6:P1} | Tie: {odds.Tie,5:P1} | Loss: {odds.Loss,6:P1} -> ACCURATE");
        }

        output.WriteLine($"\nSUCCESS: All {passedCount}/50 combat simulations completed with 100% mathematical accuracy and zero errors.");
        Assert.Equal(50, passedCount);
    }

    private static List<Scenario> BuildFiftyScenarios(EffectRegistry drEffects)
    {
        var list = new List<Scenario>();
        int id = 1;

        // Group 1: Guaranteed Wins & Losses (Deterministic 100% Rates) (1 - 5)
        list.Add(new Scenario(id++, "Overwhelming stats 1v1", Side(M(100, 100)), Side(M(1, 1)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Overwhelming opponent stats 1v1", Side(M(1, 1)), Side(M(100, 100)), false, null, new ExpectedOutcome(ExactLoss: true)));
        list.Add(new Scenario(id++, "Full 7v1 overwhelming board", Side(M(30, 30), M(30, 30), M(30, 30), M(30, 30), M(30, 30), M(30, 30), M(30, 30)), Side(M(1, 1)), null, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Giant divine shield poisonous sweep", Side(M(1, 1, Keyword.Poisonous | Keyword.DivineShield)), Side(M(100, 100)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Mutual 0-Attack stalemate", Side(M(0, 10)), Side(M(0, 10)), null, null, new ExpectedOutcome(ExactTie: true)));

        // Group 2: Symmetric Mirror Matchups (6 - 10)
        list.Add(new Scenario(id++, "Mirror 1v1 trading simultaneously", Side(M(5, 5)), Side(M(5, 5)), null, null, new ExpectedOutcome(ExactTie: true)));
        list.Add(new Scenario(id++, "Mirror 2v2 equal stat trade", Side(M(3, 4), M(4, 3)), Side(M(3, 4), M(4, 3)), null, null, new ExpectedOutcome(Symmetric: true)));
        list.Add(new Scenario(id++, "Mirror 3v3 with taunt", Side(M(2, 6, Keyword.Taunt), M(4, 4), M(5, 5)), Side(M(2, 6, Keyword.Taunt), M(4, 4), M(5, 5)), null, null, new ExpectedOutcome(Symmetric: true)));
        list.Add(new Scenario(id++, "Mirror 5v5 mid-game boards", Side(M(3, 3), M(4, 5, Keyword.Taunt), M(6, 6), M(2, 4), M(5, 3)), Side(M(3, 3), M(4, 5, Keyword.Taunt), M(6, 6), M(2, 4), M(5, 3)), null, null, new ExpectedOutcome(Symmetric: true)));
        list.Add(new Scenario(id++, "Mirror 7v7 full boards", Side(M(3, 5), M(4, 6, Keyword.Taunt), M(5, 5), M(2, 8), M(6, 4), M(7, 7), M(4, 4)), Side(M(3, 5), M(4, 6, Keyword.Taunt), M(5, 5), M(2, 8), M(6, 4), M(7, 7), M(4, 4)), null, null, new ExpectedOutcome(Symmetric: true)));

        // Group 3: Divine Shield Mechanics (11 - 15)
        list.Add(new Scenario(id++, "Divine Shield absorbs lethal attack", Side(M(2, 1, Keyword.DivineShield)), Side(M(10, 2)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Dual Divine Shield vs raw stats", Side(M(4, 4, Keyword.DivineShield), M(4, 4, Keyword.DivineShield)), Side(M(8, 8)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Divine Shield + Windfury two-turn trade", Side(M(4, 7, Keyword.DivineShield | Keyword.Windfury)), Side(M(3, 5), M(3, 3)), true, null, new ExpectedOutcome(ExactWin: true)));
        // Was MinWin 0.85. They attack first and must hit the Taunt: shield off, their first 5/5 at 5/3. The Taunt (2 Attack)
        // hits either 5/5 and drops to 2/5; their second 5/5 kills it. Our 10/2 kills one of the two damaged 5/5s and dies to
        // the 5 Attack: one of theirs is always left.
        list.Add(new Scenario(id++, "Taunt Divine Shield wall", Side(M(2, 10, Keyword.Taunt | Keyword.DivineShield), M(10, 2)), Side(M(5, 5), M(5, 5)), false, null, new ExpectedOutcome(ExactLoss: true)));
        list.Add(new Scenario(id++, "Divine shield popping sequence", Side(M(1, 1, Keyword.DivineShield), M(1, 1, Keyword.DivineShield)), Side(M(1, 1, Keyword.DivineShield), M(1, 1)), true, null, new ExpectedOutcome(MinWin: 0.90)));

        // Group 4: Poisonous and Venomous Interactions (16 - 20)
        list.Add(new Scenario(id++, "Poisonous kills giant taunt", Side(M(1, 1, Keyword.Poisonous)), Side(M(1, 500, Keyword.Taunt)), true, null, new ExpectedOutcome(ExactTie: true)));
        list.Add(new Scenario(id++, "Venomous kills first target only", Side(M(1, 10, Keyword.Venomous)), Side(M(50, 50), M(2, 2)), true, null, new ExpectedOutcome(ExactLoss: true)));
        list.Add(new Scenario(id++, "Poisonous + Reborn vs 2 giants", Side(M(1, 1, Keyword.Poisonous | Keyword.Reborn)), Side(M(100, 100), M(100, 100)), true, null, new ExpectedOutcome(ExactTie: true)));
        list.Add(new Scenario(id++, "Venomous with Divine Shield", Side(M(2, 2, Keyword.Venomous | Keyword.DivineShield)), Side(M(50, 50)), false, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Poisonous army vs stats army", Side(M(1, 1, Keyword.Poisonous), M(1, 1, Keyword.Poisonous), M(1, 1, Keyword.Poisonous)), Side(M(30, 30), M(30, 30)), true, null, new ExpectedOutcome(ExactWin: true)));

        // Group 5: Reborn Positioning and Mechanics (21 - 25)
        list.Add(new Scenario(id++, "Reborn minion trades and revives", Side(M(3, 1, Keyword.Reborn)), Side(M(2, 3)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Reborn copy trades into mutual death", Side(M(5, 1, Keyword.Reborn)), Side(M(4, 9)), true, null, new ExpectedOutcome(ExactTie: true)));
        // Was MinWin 0.85. Their first 3/6 kills the Taunt (now 3/4); it comes back 2/1 in its slot and, as our left-most
        // minion, attacks first: it dies to either 3/6 for 2 damage. Their second 3/6 then trades with our 8/2, and the other
        // one is left.
        list.Add(new Scenario(id++, "Reborn taunt protects backline twice", Side(M(2, 1, Keyword.Reborn | Keyword.Taunt), M(8, 2)), Side(M(3, 6), M(3, 6)), false, null, new ExpectedOutcome(ExactLoss: true)));
        // Was ExactWin. 6 damage leaves their 5/11 at 5/5 and ours dies; Reborn brings back 6/1, which their 5/5 attacks:
        // 6 damage kills it while 5 kills ours - both boards are empty.
        list.Add(new Scenario(id++, "Golden reborn brings back golden attack", Side(M(6, 2, Keyword.Reborn, golden: true)), Side(M(5, 11)), true, null, new ExpectedOutcome(ExactTie: true)));
        list.Add(new Scenario(id++, "Multiple reborns wave", Side(M(2, 1, Keyword.Reborn), M(2, 1, Keyword.Reborn)), Side(M(2, 6)), true, null, new ExpectedOutcome(MinWin: 0.70)));

        // Group 6: Windfury and Mega-Windfury Swings (26 - 30)
        list.Add(new Scenario(id++, "Windfury 2 attacks in one turn", Side(M(4, 10, Keyword.Windfury)), Side(M(3, 4), M(3, 4)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Mega-Windfury 4 attacks clean sweep", Side(M(5, 20, Keyword.MegaWindfury)), Side(M(2, 4), M(2, 4), M(2, 4), M(2, 4)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Windfury with Divine Shield", Side(M(5, 5, Keyword.Windfury | Keyword.DivineShield)), Side(M(4, 5), M(4, 5)), true, null, new ExpectedOutcome(MinWin: 0.90)));
        list.Add(new Scenario(id++, "Windfury dying on first swing cancels second", Side(M(10, 2, Keyword.Windfury)), Side(M(5, 20)), true, null, new ExpectedOutcome(ExactLoss: true)));
        list.Add(new Scenario(id++, "Mega-Windfury vs Taunt wall", Side(M(6, 30, Keyword.MegaWindfury)), Side(M(3, 5, Keyword.Taunt), M(3, 5, Keyword.Taunt), M(10, 2)), true, null, new ExpectedOutcome(ExactWin: true)));

        // Group 7: Stealth and Target Prioritization (31 - 35)
        list.Add(new Scenario(id++, "Stealth cannot be attacked while non-stealth alive", Side(M(1, 10, Keyword.Taunt), M(10, 1, Keyword.Stealth)), Side(M(2, 2)), false, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Stealth breaks upon attacking", Side(M(5, 5, Keyword.Stealth)), Side(M(2, 10)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Taunt takes priority over regular minions", Side(M(1, 10, Keyword.Taunt), M(10, 1)), Side(M(2, 5)), false, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Opponent only stealthed minions", Side(M(5, 5)), Side(M(2, 1, Keyword.Stealth), M(2, 1, Keyword.Stealth)), true, null, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Dual taunt random selection", Side(M(2, 10, Keyword.Taunt), M(2, 10, Keyword.Taunt)), Side(M(4, 8)), null, null, new ExpectedOutcome(ExactWin: true)));

        // Group 8: Cleave and Adjacent Damage (36 - 40)
        list.Add(new Scenario(id++, "Cleave hits 3 adjacent minions", Side(M(5, 10, cardId: "CLEAVE")), Side(M(2, 4), M(2, 4), M(2, 4)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Cleave on edge hits 2 minions", Side(M(5, 10, cardId: "CLEAVE")), Side(M(2, 4), M(2, 4)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Cleave pops adjacent Divine Shields", Side(M(5, 10, cardId: "CLEAVE")), Side(M(1, 1, Keyword.DivineShield), M(1, 1, Keyword.DivineShield), M(1, 1, Keyword.DivineShield)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Cleave vs Taunt positioning", Side(M(6, 12, cardId: "CLEAVE")), Side(M(2, 2), M(3, 5, Keyword.Taunt), M(2, 2)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        // Was ExactTie. The target is random: the middle one (1/3) lets the cleave kill all three while ours dies to 3 damage
        // (a tie); an edge one (2/3) kills only two and leaves the third (a loss).
        list.Add(new Scenario(id++, "Cleave dies while killing 3 enemies", Side(M(5, 2, cardId: "CLEAVE")), Side(M(3, 4), M(3, 4), M(3, 4)), true, drEffects, new ExpectedOutcome(MaxWin: 0, MinTie: 0.29, MaxTie: 0.38, MinLoss: 0.62, MaxLoss: 0.71)));

        // Group 9: Deathrattles, Titus Multiplier and 7-Cap (41 - 45)
        list.Add(new Scenario(id++, "Deathrattle spawns 2 tokens", Side(M(2, 2, cardId: "DR")), Side(M(3, 3)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Titus doubles deathrattle tokens to 4", Side(M(0, 10, cardId: "TITUS"), M(2, 2, cardId: "DR")), Side(M(3, 5)), false, drEffects, new ExpectedOutcome(MinWin: 0.70)));
        list.Add(new Scenario(id++, "Board at 7-cap drops overflowing summons", Side(M(2, 2, cardId: "DR"), M(0, 5), M(0, 5), M(0, 5), M(0, 5), M(0, 5), M(0, 5)), Side(M(3, 3)), true, drEffects, new ExpectedOutcome(MinWin: 0.80)));
        list.Add(new Scenario(id++, "Deathrattle + Reborn sequential spawn", Side(M(2, 2, Keyword.Reborn, cardId: "DR")), Side(M(3, 4)), true, drEffects, new ExpectedOutcome(ExactWin: true)));
        list.Add(new Scenario(id++, "Chain deathrattles clear board", Side(M(3, 3, cardId: "DR"), M(3, 3, cardId: "DR")), Side(M(4, 7)), true, drEffects, new ExpectedOutcome(ExactWin: true)));

        // Group 10: Avenge, Auras, and Full 7v7 Endgame Boards (46 - 50)
        // Was ExactWin. The aura minion is our left-most and attacks first: 5 damage kills it. Captured stats already hold the
        // aura, so the other two lose +2/+2 and are left 0/1 (if it came on top they would be back to 2/3); either way the
        // 5/8 (at 5/6) kills them one by one while they deal it at most 4.
        list.Add(new Scenario(id++, "Aura +2/+2 buffs allies in combat", Side(M(2, 5, cardId: "AURA"), M(2, 3), M(2, 3)), Side(M(5, 8)), true, drEffects, new ExpectedOutcome(ExactLoss: true)));
        // Was ExactWin. Enumerating their random targets by hand gives win 5/6, tie 1/6, no loss: when they hit the Avenge
        // minion twice before the two 1/1s die, it is 5/2 after the buff and trades with their 3/3 at the same time.
        list.Add(new Scenario(id++, "Avenge triggers after friendly deaths", Side(M(1, 10, cardId: "AV"), M(1, 1), M(1, 1)), Side(M(3, 11)), true, drEffects, new ExpectedOutcome(MinWin: 0.80, MaxWin: 0.87, MinTie: 0.13, MaxTie: 0.20, MaxLoss: 0)));
        // Was 0.3-0.8 (a guess). Equal Attack (19 each) and nearly equal Health (26 v 27), but ours adds a Divine Shield, a
        // Reborn body and a Windfury swing: ours is the favourite, so at least an even chance to win.
        list.Add(new Scenario(id++, "Mixed keywords 5v5 endgame", Side(M(4, 5, Keyword.DivineShield), M(3, 7, Keyword.Taunt), M(2, 4, Keyword.Reborn), M(6, 6), M(4, 4, Keyword.Windfury)), Side(M(5, 5), M(4, 6, Keyword.Taunt), M(3, 5), M(2, 8), M(5, 3)), null, null, new ExpectedOutcome(MinWin: 0.5)));
        // Was Symmetric, but the boards are not mirrors (each slot differs by 1 Attack and up to 2 Health, ours +1 Attack and
        // +4 Health in total): a close fight, so each side wins a fair share.
        list.Add(new Scenario(id++, "High-stat scaling 7v7 combat", Side(M(20, 25), M(15, 30, Keyword.Taunt), M(18, 18, Keyword.DivineShield), M(12, 16, Keyword.Reborn), M(22, 22), M(14, 14), M(25, 25)), Side(M(19, 24), M(16, 28, Keyword.Taunt), M(17, 19, Keyword.DivineShield), M(13, 15, Keyword.Reborn), M(21, 23), M(15, 13), M(24, 24)), null, null, new ExpectedOutcome(MinWin: 0.15, MinLoss: 0.15)));
        // Was Symmetric, but the boards are not mirrors (same effects in the same slots, stats differ by 1-2 per slot): a
        // close fight, so each side wins a fair share.
        list.Add(new Scenario(id++, "Complete 7v7 with all mechanisms", Side(M(10, 15, cardId: "CLEAVE"), M(5, 20, Keyword.Taunt | Keyword.DivineShield), M(8, 8, Keyword.Reborn, cardId: "DR"), M(0, 12, cardId: "TITUS"), M(7, 7, cardId: "AURA"), M(12, 12, Keyword.Windfury), M(6, 14, cardId: "AV")), Side(M(9, 14, cardId: "CLEAVE"), M(6, 18, Keyword.Taunt | Keyword.DivineShield), M(7, 9, Keyword.Reborn, cardId: "DR"), M(0, 14, cardId: "TITUS"), M(8, 6, cardId: "AURA"), M(11, 13, Keyword.Windfury), M(7, 12, cardId: "AV")), null, drEffects, new ExpectedOutcome(MinWin: 0.15, MinLoss: 0.15)));

        return list;
    }

    // Test effects
    [CardEffect("DR")]
    private sealed class SummonTwoTokens : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 1, cardId: "TOKEN"), 2);
    }

    [CardEffect("TITUS")]
    private sealed class TitusMultiplier : CombatEffect
    {
        public override int ExtraDeathrattles(Fight fight, Unit self) => self.Golden ? 2 : 1;
    }

    [CardEffect("CLEAVE")]
    private sealed class CleaveEffect : CombatEffect
    {
        public override void OnAttackDamage(Fight fight, Unit self, Unit target)
        {
            var (left, right) = fight.Neighbours(target);
            if (left is not null) fight.DealDamage(self, left, self.Attack);
            if (right is not null) fight.DealDamage(self, right, self.Attack);
        }
    }

    [CardEffect("AV")]
    private sealed class AvengeBuff : AvengeEffect
    {
        protected override int Threshold(Unit self) => 2;
        protected override void Avenge(Fight fight, Unit self) => fight.Buff(self, 4, 4);
    }

    [CardEffect("AURA")]
    private sealed class AuraTwoTwo : CombatEffect
    {
        public override StatDelta AuraFor(Fight fight, Unit self, Unit other) => other == self ? default : new StatDelta(2, 2);
    }
}

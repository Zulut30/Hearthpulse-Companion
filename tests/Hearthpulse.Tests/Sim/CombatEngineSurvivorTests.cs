using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Engine rules a Stryker run on Simulation/* (main db901379, 2026-11-19, stryker-sim.md in the task folder, package
/// TESTS2) changed without a test failing: where a summon of a minion that died beside a dead last attacker stands,
/// the forced target of an attack, tie-breaks, a dead minion asked to attack, Start of Combat deaths and "attacks
/// immediately", summon slots, a full board, and a minion's "after you summon" trigger on its own summon. Boards are
/// worked out by hand in each comment; picks are the scripted random choices in order (one per target choice, also when
/// there is a single candidate).
/// </summary>
public sealed class CombatEngineSurvivorTests
{
    private static readonly EffectRegistry Rules = Effects(
        typeof(BladeCollector), typeof(JailbirdJuggernaut), typeof(DeVolitionist), typeof(BananaSlamma),
        typeof(SummonOne), typeof(SummonTwo), typeof(SummonBeast), typeof(KillThenAttack), typeof(BuffAnyFriendly),
        typeof(DestroyAtStart), typeof(TraceDeathrattle), typeof(AttackAtStart), typeof(SummonAtSlot0), typeof(SummonAtSlot1), typeof(SummonAtEnd), typeof(RallySummonTwo), typeof(GiveGains), typeof(SeeGains), typeof(AttackAura), typeof(SummonAura), typeof(RallySummonAtEnd),
        typeof(GainDeathrattle), typeof(OwnAndGainedDeathrattle), typeof(GainWatcher));

    private static List<string> Attacks(FightResult result, string side) =>
        result.Lines(side + " ").Where(l => l.Contains(" attacks ", StringComparison.Ordinal)).ToList();

    // ------------------------------------------------------------------- the gap: a summon beside a dead last attacker

    /// <summary>
    /// Our W attacks, their Blade Collector (BG26_817, Cleave) hits H and T, our T attacks and is the last attacker, then
    /// the Collector kills T and, by Cleave, W beside it. W stood left of the pointer but was not the last attacker: what
    /// it leaves waits for the next round, and H, right of T, attacks next (rules-evidence 2b, control case g3c8 TURN=16).
    /// Picks: W → C; C → H (2); T → C; C → T (1), Cleave on W and H; next attacker → C.
    /// </summary>
    private static FightResult BesideTheDeadLastAttacker(SimMinion w)
    {
        var t = Minion(1, 3, cardId: "T");
        var h = Minion(1, 30, cardId: "H");
        return Fight(Side(w, t, h), Side(Minion(1, 100, cardId: "BG26_817")), oursFirst: true, Rules, picks: [0, 2, 0, 1, 0]);
    }

    [Fact]
    public void A_reborn_copy_beside_the_dead_last_attacker_waits_for_the_next_round()
    {
        var result = BesideTheDeadLastAttacker(Minion(1, 2, Keyword.Reborn, "W"));

        Assert.Contains("reborn our W 1/1 at 0", result.Log);
        var ours = Attacks(result, "our");
        Assert.StartsWith("our W 1/2 Reborn attacks", ours[0]);
        Assert.StartsWith("our T ", ours[1]);
        Assert.StartsWith("our H ", ours[2]);
    }

    [Fact]
    public void A_deathrattle_summon_beside_the_dead_last_attacker_waits_for_the_next_round()
    {
        var result = BesideTheDeadLastAttacker(Minion(1, 2, cardId: "S2_ONE"));

        Assert.Contains("summoned our X 1/1 at 0", result.Log);
        var ours = Attacks(result, "our");
        Assert.StartsWith("our S2_ONE ", ours[0]);
        Assert.StartsWith("our T ", ours[1]);
        Assert.StartsWith("our H ", ours[2]);
    }

    [Fact]
    public void Two_deathrattle_summons_beside_the_dead_last_attacker_stand_in_its_slot_in_order_and_wait()
    {
        var result = BesideTheDeadLastAttacker(Minion(1, 2, cardId: "S2_TWO"));

        Assert.Contains("summoned our X 1/1 at 0", result.Log);
        Assert.Contains("summoned our X 1/1 at 1", result.Log);
        var ours = Attacks(result, "our");
        Assert.StartsWith("our T ", ours[1]);
        Assert.StartsWith("our H ", ours[2]);
    }

    // --------------------------------------------------------------------------------------------- attack targets

    [Fact]
    public void The_juggernauts_golem_attacks_the_juggernauts_target_and_not_a_random_enemy()
    {
        // Jailbird Juggernaut (BG36_333): "Rally: Summon a Golem ... to attack the target first." Pick 1: the Juggernaut
        // attacks E1; a random pick for the Golem would be E0 (the script has no more picks, so 0).
        var result = Fight(Side(Minion(2, 10, cardId: "BG36_333")), Side(Minion(1, 50, cardId: "E0"), Minion(1, 50, cardId: "E1")), oursFirst: true, Rules, picks: [1]);

        var ours = Attacks(result, "our");
        Assert.Contains("attacks their E1 ", ours[0]);
        Assert.StartsWith("our BG29_864t ", ours[1]);
        Assert.Contains("attacks their E1 ", ours[1]);
    }

    [Theory]
    [InlineData(0, "Y")]
    [InlineData(1, "X")]
    public void A_tie_for_the_highest_health_is_a_choice_among_the_tied_minions(int pick, string victim)
    {
        // De-volition-ist (BG36_102): after it attacks, damage to the highest-Health enemy. It kills the Taunt Z; X and Y
        // tie at 10 Health, and the tie-break decides which is hit (RandomChooser keeps the newest tie on a 0 draw, so 0
        // is Y and 1 is X); a strict "highest" that let a later equal value win would always hit Y.
        var result = Fight(
            Side(Minion(1, 50, cardId: "BG36_102")),
            Side(Minion(0, 1, Keyword.Taunt, "Z"), Minion(0, 10, cardId: "X"), Minion(0, 10, cardId: "Y")),
            oursFirst: true, Rules, picks: [0, pick]);

        var hit = result.Log.First(l => l.Contains(" takes 1 from our BG36_102", StringComparison.Ordinal) && !l.StartsWith("their Z ", StringComparison.Ordinal));
        Assert.StartsWith("their " + victim + " ", hit);
    }

    [Fact]
    public void A_minion_that_died_never_attacks_when_an_effect_asks_it_to()
    {
        // K's Rally destroys V, then asks V to attack now: a dead minion does not attack.
        var result = Fight(Side(Minion(1, 50, cardId: "S2_KILL"), Minion(1, 5, cardId: "V")), Side(Minion(0, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Contains("our V 1/5 is destroyed", result.Log);
        Assert.DoesNotContain(Attacks(result, "our"), l => l.StartsWith("our V ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_random_friendly_minion_that_may_be_itself_is_itself_when_it_is_alone()
    {
        // "Give a friendly minion +5 Attack" from a minion alone on its board: the only candidate is itself.
        var result = Fight(Side(Minion(1, 50, cardId: "S2_SELF")), Side(Minion(0, 6, cardId: "E")), oursFirst: true, Rules);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(6, Assert.Single(result.Ours).Attack);
    }

    // ------------------------------------------------------------------------------------------- start of combat

    [Fact]
    public void Deaths_at_the_start_of_combat_resolve_before_the_first_attack()
    {
        // Our S destroys their D at the start of combat (R 4: deaths inside Start of Combat resolve in their own phase).
        var result = Fight(Side(Minion(1, 10, cardId: "S2_SOC_KILL")), Side(Minion(1, 10, cardId: "D"), Minion(1, 10, cardId: "E")), oursFirst: true, Rules);

        var deathrattle = result.IndexOf("DR-D");
        var firstAttack = result.Log.ToList().FindIndex(l => l.Contains(" attacks ", StringComparison.Ordinal));
        Assert.True(deathrattle >= 0 && deathrattle < firstAttack, string.Join(" | ", result.Log));
    }

    [Fact]
    public void An_attack_queued_at_the_start_of_combat_comes_before_the_first_regular_attack()
    {
        // Our Q "attacks immediately" at the start of combat; they attack first in turn order (R 3: such attacks were
        // seen in the Start of Combat phase).
        var result = Fight(Side(Minion(1, 10, cardId: "S2_SOC_ATTACK")), Side(Minion(1, 10, cardId: "E")), oursFirst: false, Rules);

        var first = result.Log.First(l => l.Contains(" attacks ", StringComparison.Ordinal));
        Assert.StartsWith("our S2_SOC_ATTACK ", first);
    }

    // ---------------------------------------------------------------------------------------------------- summons

    [Theory]
    [InlineData("S2_SLOT0", 0)]
    [InlineData("S2_SLOT1", 1)]
    [InlineData("S2_SLOT_END", 2)]
    public void A_summon_at_a_slot_stands_there_and_minus_one_is_the_right_end(string summoner, int position)
    {
        // At the start of combat the summoner (slot 0) summons X at slot 0, at slot 1, or at -1 (the right end, after B).
        var result = Fight(Side(Minion(1, 50, cardId: summoner), Minion(1, 50, cardId: "B")), Side(Minion(0, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Contains($"summoned our X 1/1 at {position}", result.Log);
    }

    [Fact]
    public void Two_summons_with_room_for_one_summon_one()
    {
        // Seven minions; when the summoner dies the first X fills the board again and the second has no room.
        var seven = Enumerable.Range(0, 6).Select(i => Minion(1, 50, cardId: "F" + i)).Prepend(Minion(1, 1, cardId: "S2_TWO")).ToArray();
        var result = Fight(Side(seven), Side(Minion(5, 100, cardId: "E")), oursFirst: true, Rules);

        Assert.Contains("summoned our X 1/1 at 0", result.Log);
        Assert.Contains("no room to summon X for us", result.Log);
    }

    [Fact]
    public void Two_summons_beside_a_living_minion_on_a_full_board_summon_none()
    {
        // R attacks with six others beside it: no room for the first X, and the summon stops there.
        var seven = Enumerable.Range(0, 6).Select(i => Minion(1, 50, cardId: "F" + i)).Prepend(Minion(1, 50, cardId: "S2_RALLY_TWO")).ToArray();
        var result = Fight(Side(seven), Side(Minion(0, 100, cardId: "E")), oursFirst: true, Rules);

        Assert.Contains("no room to summon X for us", result.Log);
        Assert.DoesNotContain(result.Log, l => l.StartsWith("summoned our X", StringComparison.Ordinal));
        Assert.Contains(result.Log, l => l.StartsWith("our F0 ", StringComparison.Ordinal) && l.Contains(" attacks ", StringComparison.Ordinal));
    }

    [Fact]
    public void An_after_you_summon_minion_does_not_trigger_on_its_own_summon()
    {
        // Banana Slamma (BG26_802): "After you summon a Beast in combat, double its Attack." Summoned by K's deathrattle, it
        // keeps its own 3 Attack. K dies to E; the Slamma (3/3) kills E (1/3) and takes 1.
        var result = Fight(Side(Minion(1, 1, cardId: "S2_BEAST")), Side(Minion(1, 4, cardId: "E")), oursFirst: true, Rules);

        Assert.Equal(1, result.Outcome);
        var slamma = Assert.Single(result.Ours);
        Assert.Equal("BG26_802", slamma.CardId);
        Assert.Equal(3, slamma.Attack);
    }

    // ------------------------------------------------------------------------------------- stats, auras, effects

    [Fact]
    public void Only_a_real_increase_counts_as_a_minion_gaining_stats()
    {
        // G gives W +0/+0, +2/+0 and +0/+3 at the start of combat: W sees two gains ("increase a friendly minion's stats").
        var result = Fight(Side(Minion(1, 10, cardId: "S2_GAINS"), Minion(1, 10, cardId: "S2_SEE_GAINS")), Side(Minion(0, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Equal(["gain 2/0", "gain 0/3"], result.Lines("gain "));
    }

    [Fact]
    public void An_aura_of_attack_alone_raises_the_attack()
    {
        // K dies and summons A, which gives the other friendly minions +2 Attack: B (1/10) hits back and attacks with 3. (Auras on the
        // board at the start are already in the captured stats.) Picks: K → E; E → B (1).
        var result = Fight(Side(Minion(1, 1, cardId: "S2_SUMMON_AURA"), Minion(1, 10, cardId: "B")), Side(Minion(1, 50, cardId: "E")), oursFirst: true, Rules, picks: [0, 1]);

        Assert.StartsWith("our B 3/", Attacks(result, "our").First(l => l.StartsWith("our B ", StringComparison.Ordinal)));
        Assert.Contains("their E 1/46 takes 3 from our B 3/9", result.Log);
    }

    [Fact]
    public void A_summon_right_of_the_next_attacker_does_not_skip_it()
    {
        // R attacks and summons X at the right end: B, right of R, still attacks next for us (rules-evidence 2).
        var result = Fight(Side(Minion(1, 50, cardId: "S2_RALLY_END"), Minion(1, 50, cardId: "B")), Side(Minion(0, 100, cardId: "E")), oursFirst: true, Rules);

        Assert.Contains("summoned our X 1/1 at 2", result.Log);
        var ours = Attacks(result, "our");
        Assert.StartsWith("our B ", ours[1]);
        Assert.StartsWith("our X ", ours[2]);
    }

    [Fact]
    public void A_gained_deathrattle_triggers_on_a_minion_without_one()
    {
        var result = Fight(Side(Minion(1, 1, cardId: "S2_GAIN_DR")), Side(Minion(1, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Single(result.Lines("DR-GAINED"));
    }

    [Fact]
    public void A_gained_deathrattle_triggers_beside_the_minions_own()
    {
        var result = Fight(Side(Minion(1, 1, cardId: "S2_OWN_AND_GAIN")), Side(Minion(1, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Equal(["DR-OWN", "DR-GAINED"], result.Log.Where(l => l.StartsWith("DR-", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_gained_after_a_friendly_minion_dies_trigger_sees_the_next_death()
    {
        // W gains "after a friendly minion dies" at the start of combat; F dies to E.
        var result = Fight(Side(Minion(1, 1, cardId: "F"), Minion(0, 50, cardId: "S2_GAIN_WATCH")), Side(Minion(1, 50, cardId: "E")), oursFirst: true, Rules);

        Assert.Single(result.Lines("seen F"));
    }

    // ------------------------------------------------------------------------------------------- test effects

    [CardEffect("S2_ONE")]
    private sealed class SummonOne : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 1, cardId: "X"));
    }

    [CardEffect("S2_TWO")]
    private sealed class SummonTwo : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 1, cardId: "X"), 2);
    }

    [CardEffect("S2_RALLY_TWO")]
    private sealed class RallySummonTwo : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target) => fight.SummonAt(self, Minion(1, 1, cardId: "X"), 2);
    }

    [CardEffect("S2_BEAST")]
    private sealed class SummonBeast : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(3, 3, cardId: "BG26_802", tribes: Tribe.Beast));
    }

    [CardEffect("S2_KILL")]
    private sealed class KillThenAttack : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target)
        {
            if (fight.Alive(self.Side, except: self).FirstOrDefault(u => u.CardId == "V") is { } victim)
            {
                fight.Destroy(victim, self);
                fight.ResolveDeaths();
                fight.AttackNow(victim);
            }
        }
    }

    [CardEffect("S2_SELF")]
    private sealed class BuffAnyFriendly : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target)
        {
            if (self.Counter == 0 && fight.RandomFriendly(self, includeSelf: true) is { } unit)
            {
                self.Counter = 1;
                fight.Buff(unit, 5, 0);
            }
        }
    }

    [CardEffect("S2_SOC_KILL")]
    private sealed class DestroyAtStart : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            if (fight.Alive(self.Enemy).FirstOrDefault(u => u.CardId == "D") is { } victim)
            {
                fight.Destroy(victim, self);
            }
        }
    }

    [CardEffect("D")]
    private sealed class TraceDeathrattle : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.Trace("DR-D");
    }

    [CardEffect("S2_SOC_ATTACK")]
    private sealed class AttackAtStart : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self) => fight.AttackSoon(self);
    }

    private abstract class SummonAtSlot(int slot) : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self) => fight.Summon(self.Side, "X", 1, 1, slot: slot);
    }

    [CardEffect("S2_SLOT0")]
    private sealed class SummonAtSlot0() : SummonAtSlot(0);

    [CardEffect("S2_SLOT1")]
    private sealed class SummonAtSlot1() : SummonAtSlot(1);

    [CardEffect("S2_SLOT_END")]
    private sealed class SummonAtEnd() : SummonAtSlot(-1);

    [CardEffect("S2_GAINS")]
    private sealed class GiveGains : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            var watcher = fight.Alive(self.Side, except: self)[0];
            fight.Buff(watcher, 0, 0);
            fight.Buff(watcher, 2, 0);
            fight.Buff(watcher, 0, 3);
        }
    }

    [CardEffect("S2_SEE_GAINS")]
    private sealed class SeeGains : CombatEffect
    {
        public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health) => fight.Trace($"gain {attack}/{health}");
    }

    [CardEffect("S2_AURA")]
    private sealed class AttackAura : CombatEffect
    {
        public override StatDelta AuraFor(Fight fight, Unit self, Unit other) => other == self ? default : new StatDelta(2, 0);
    }

    [CardEffect("S2_SUMMON_AURA")]
    private sealed class SummonAura : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 10, cardId: "S2_AURA"));
    }

    [CardEffect("S2_RALLY_END")]
    private sealed class RallySummonAtEnd : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target)
        {
            if (self.Counter == 0)
            {
                self.Counter = 1;
                fight.Summon(self.Side, "X", 1, 1);
            }
        }
    }

    private sealed class GainedDeathrattle : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.Trace("DR-GAINED");
    }

    private sealed class GainedWatcher : CombatEffect
    {
        public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => fight.Trace($"seen {dead.CardId}");
    }

    [CardEffect("S2_GAIN_DR")]
    private sealed class GainDeathrattle : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self) => self.AddEffect(new GainedDeathrattle());
    }

    [CardEffect("S2_OWN_AND_GAIN")]
    private sealed class OwnAndGainedDeathrattle : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self) => self.AddEffect(new GainedDeathrattle());

        public override void Deathrattle(Fight fight, Unit self) => fight.Trace("DR-OWN");
    }

    [CardEffect("S2_GAIN_WATCH")]
    private sealed class GainWatcher : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self) => self.AddEffect(new GainedWatcher());
    }
}

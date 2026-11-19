using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Engine rules as real fights show them. Each test encodes a sequence from the owner's Power.log of 15.11.2026
/// (<c>Hearthstone_2026_11_15_20_36_45</c>, build 255790): the line numbers and the gNcM fight names come from
/// <c>reference/tasks/TASK-2026-11-19-sim-all-mechanics/rules-evidence.md</c> (R) and <c>effects-audit.md</c> (A).
/// </summary>
public sealed class CombatRulesEvidenceTests
{
    private static readonly EffectRegistry Rules = Effects(
        typeof(TraceA), typeof(TraceB), typeof(SummonFive), typeof(SummonPlain), typeof(DestroyEnemies), typeof(AvengeBuffOthers), typeof(AvengeThree),
        typeof(EngineTests.ExtraTrigger), typeof(BuffFriendlyAttacker), typeof(TraceRally), typeof(EndlessDeathrattle), typeof(HugeAura));

    private static List<string> Deaths(FightResult result) =>
        result.Log.Where(l => l.EndsWith(" dies", StringComparison.Ordinal)).ToList();

    private static List<string> Attacks(FightResult result, string side) =>
        result.Lines(side + " ").Where(l => l.Contains(" attacks ", StringComparison.Ordinal)).ToList();

    // ---------------------------------------------------------------------------------------- 2b: who attacks next

    [Fact]
    public void Deathrattle_summon_of_the_last_attacker_that_died_later_attacks_next()
    {
        // R 2b, g2c13 TURN=26: our last attacker Dark Paradox died later, not in its own attack (line 435535); its
        // BG29_864t (435610) attacked next (435641). Here A attacks and survives, E kills it, A's token attacks before B.
        var a = Minion(1, 20, cardId: "R_SUMMON5");
        var b = Minion(1, 30, cardId: "B");
        // Picks: our A → E; their E → A (index 0); our next attacker's target.
        var result = Fight(Side(a, b), Side(Minion(10, 100, cardId: "E")), oursFirst: true, Rules, picks: [0, 0, 0]);

        var ours = Attacks(result, "our");
        Assert.StartsWith("our R_SUMMON5 ", ours[0]);
        Assert.StartsWith("our R_A ", ours[1]);
        Assert.StartsWith("our B ", ours[2]);
    }

    [Fact]
    public void Reborn_copy_of_the_last_attacker_that_died_later_attacks_next()
    {
        // R 2b, g2c4: Bronze Warden died after its own attack (line 155766), its reborn copy (155807) attacked next (156038).
        var w = Minion(1, 20, Keyword.Reborn, "W");
        var b = Minion(1, 30, cardId: "B");
        var result = Fight(Side(w, b), Side(Minion(10, 100, cardId: "E")), oursFirst: true, Rules, picks: [0, 0, 0]);

        var ours = Attacks(result, "our");
        Assert.StartsWith("our W 1/20 Reborn attacks", ours[0]);
        Assert.StartsWith("our W 1/1 attacks", ours[1]);
        Assert.StartsWith("our B ", ours[2]);
    }

    [Fact]
    public void Reborn_copy_of_a_minion_left_of_the_last_attacker_waits_for_the_next_round()
    {
        // R 2b control case, g3c8 TURN=16: our last attacker was Bronze Timewalker; Bronze Warden left of it died (630944)
        // and came back in slot 0 (630986); Hired Mount, right of the Timewalker, attacked next (631005).
        var w = Minion(1, 2, Keyword.Reborn, "W");
        var t = Minion(1, 30, cardId: "T");
        var h = Minion(1, 30, cardId: "H");
        // Picks: our W → E; their E → T (1); our T → E; their E → W (0), which dies; our next attacker → E.
        var result = Fight(Side(w, t, h), Side(Minion(1, 100, cardId: "E")), oursFirst: true, Rules, picks: [0, 1, 0, 0, 0]);

        Assert.Contains("reborn our W 1/1 at 0", result.Log);
        var ours = Attacks(result, "our");
        Assert.StartsWith("our W 1/2 Reborn attacks", ours[0]);
        Assert.StartsWith("our T ", ours[1]);
        Assert.StartsWith("our H ", ours[2]);
    }

    // ------------------------------------------------------------------------------- 4a: order of simultaneous deaths

    [Fact]
    public void Simultaneous_deaths_take_the_lower_player_id_side_first_when_it_is_not_attacking()
    {
        // R 4a, g5c12 TURN=24: we (PLAYER_ID 8) attack, the opponent (3): their N'raqi Sapper is removed (line 1421143)
        // before our Runic Arcanist (1421267), and deathrattles follow the same order.
        var ours = Side(Minion(5, 1, cardId: "R_A")) with { PlayerId = 8 };
        var theirs = Side(Minion(5, 1, cardId: "R_B")) with { PlayerId = 3 };
        var result = Fight(ours, theirs, oursFirst: true, Rules);

        var deaths = Deaths(result);
        Assert.StartsWith("their R_B ", deaths[0]);
        Assert.StartsWith("our R_A ", deaths[1]);
        Assert.Equal(["DR-B", "DR-A"], result.Lines("DR-"));
    }

    [Fact]
    public void Simultaneous_deaths_take_the_lower_player_id_side_first_when_it_is_attacking()
    {
        // R 4a, g3c6 TURN=12: their BG34_856_G#4351 attacks (line 562248); our Imp-lusionist#3739 (PLAYER_ID 1) is removed
        // (562439) before it (6, 562488).
        var ours = Side(Minion(5, 1, cardId: "R_A")) with { PlayerId = 1 };
        var theirs = Side(Minion(5, 1, cardId: "R_B")) with { PlayerId = 6 };
        var result = Fight(ours, theirs, oursFirst: false, Rules);

        var deaths = Deaths(result);
        Assert.StartsWith("our R_A ", deaths[0]);
        Assert.StartsWith("their R_B ", deaths[1]);
    }

    [Fact]
    public void A_start_of_combat_summon_dies_after_the_other_sides_starting_minion()
    {
        // R 4a, g2c4: our Flighty Scout#2145, summoned from hand at Start of Combat, is removed (line 156414) after their
        // Wrath Weaver (156326) although our PLAYER_ID (1) is the lower one: 17 of 17 such phases.
        var scout = Minion(5, 1, cardId: "R_A") with { SummonedInCombat = true, EntityId = 2145 };
        var ours = Side(scout) with { PlayerId = 1 };
        var theirs = Side(Minion(5, 1, cardId: "R_B") with { EntityId = 2073 }) with { PlayerId = 6 };
        var result = Fight(ours, theirs, oursFirst: true, Rules);

        var deaths = Deaths(result);
        Assert.StartsWith("their R_B ", deaths[0]);
        Assert.StartsWith("our R_A ", deaths[1]);
    }

    [Fact]
    public void A_minion_summoned_in_the_fight_dies_after_the_other_sides_starting_minion()
    {
        // R 4a: minions summoned in combat die after every starting minion (g2c10: their Sanguine Champion, line 307944,
        // before our Twilight Tidehunter#13895, 308121). Their K kills our X, whose 5/1 token then trades with their B.
        var ours = Side(Minion(1, 1, cardId: "R_SUMMON5")) with { PlayerId = 1 };
        var theirs = Side(Minion(1, 1, cardId: "K"), Minion(5, 1, cardId: "R_B")) with { PlayerId = 2 };
        var result = Fight(ours, theirs, oursFirst: false, Rules);

        var deaths = Deaths(result);
        Assert.Equal(4, deaths.Count);
        Assert.StartsWith("their R_B ", deaths[2]);
        Assert.StartsWith("our R_A ", deaths[3]);
    }

    [Fact]
    public void Without_player_ids_the_runs_mix_both_death_orders_evenly()
    {
        // R 4a: "attacking side first" was right in 47 of 89 phases, no better than a coin. Our deathrattle summons a
        // minion, theirs destroys our minions: whose resolves first decides the fight.
        var ours = Side(Minion(1, 1, cardId: "R_SUMMON"));
        var theirs = Side(Minion(1, 1, cardId: "R_NUKE"));
        var options = new SimOptions { Runs = 200, Effects = Rules };

        var unknown = CombatSimulator.Run(ours, theirs, true, options);
        var oursLower = CombatSimulator.Run(ours with { PlayerId = 1 }, theirs with { PlayerId = 2 }, true, options);
        var theirsLower = CombatSimulator.Run(ours with { PlayerId = 2 }, theirs with { PlayerId = 1 }, true, options);

        Assert.Equal((0.5, 0.5), (unknown.Win, unknown.Tie));
        Assert.Equal(1.0, oursLower.Tie);   // our summon first, then their deathrattle destroys it
        Assert.Equal(1.0, theirsLower.Win); // their deathrattle finds nothing, our summon survives
    }

    // --------------------------------------------------------------------------------------------- 4d and 4e: Reborn

    [Fact]
    public void Reborn_comes_after_avenge()
    {
        // R 4d, g4c9 TURN=18: Conveyor Construct#10040 dies (line 997492), its deathrattles (997524..997604), then
        // Relentless Deflector's AVENGE (997634), and only then the reborn copy (997669). Our Avenge (1) buffs every other
        // friendly minion: the copy is not there yet, so it comes back 2/1, not 3/2.
        var r = Minion(2, 1, Keyword.Reborn, "R");
        var v = Minion(0, 30, cardId: "R_AVENGE");
        // Picks: our R → E; their E → V (index 1); the copy's target.
        var result = Fight(Side(r, v), Side(Minion(1, 100, cardId: "E")), oursFirst: true, Rules, picks: [0, 1, 0]);

        var avenge = result.IndexOf("avenge");
        var reborn = result.IndexOf("reborn our R 2/1");
        Assert.True(avenge >= 0 && reborn > avenge, string.Join('\n', result.Log));
        Assert.StartsWith("our R 2/1 attacks", Attacks(result, "our")[1]);
    }

    [Fact]
    public void A_reborn_copy_takes_the_free_slot_before_a_when_you_have_space_summon()
    {
        // simreplay #4/11 (15.11): on a full board our Bubble Gunner dies and its copy comes back (line 1086990); Diremuck
        // Forager's pending Murloc waits, and comes only after the copy died (1087143), at the right end.
        var forager = Minion(0, 5, cardId: "BG27_556", tribes: Tribe.Murloc);
        var reborn = Minion(1, 1, Keyword.Reborn, "R");
        var ours = Side(forager, reborn, Minion(0, 5), Minion(0, 5), Minion(0, 5), Minion(0, 5), Minion(0, 5))
            with { Hand = [Minion(5, 2, cardId: "MUR", tribes: Tribe.Murloc)] };
        // Picks: their E → R (index 1); the copy's target; their E's next target.
        var result = Fight(ours, Side(Minion(1, 100, cardId: "E")), oursFirst: false, Effects(typeof(DiremuckForager)), picks: [1, 0, 2]);

        var reborned = result.IndexOf("reborn our R 1/1 at 1");
        var summoned = result.IndexOf("summoned our MUR 5/2 at 6");
        Assert.True(reborned >= 0 && summoned > reborned, string.Join(Environment.NewLine, result.Log));
    }

    [Fact]
    public void Reborn_copy_has_one_health_of_its_printed_maximum()
    {
        // R 4e, 12 copies: printed Health, damaged to 1 (g4c10 Bubble Gunner 136/143 comes back 2/3, then 2/1).
        var gunner = SimMinion.Create("G", 2, 3, keywords: Keyword.Reborn) with { Attack = 136, Health = 143, MaxHealth = 143 };
        var copy = gunner.RebornCopy();
        Assert.Equal((2, 1, 3), (copy.Attack, copy.Health, copy.MaxHealth));

        // In a fight: our buffed G and their 143/136 trade; the copy is left as a damaged 2/1 of 3.
        var result = Fight(Side(gunner), Side(Minion(143, 136, cardId: "E")), oursFirst: true, Rules);
        var survivor = Assert.Single(result.Ours);
        Assert.Equal((2, 1, 3), (survivor.Attack, survivor.Health, survivor.MaxHealth));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    public void Avenge_counts_on_from_the_friendly_deaths_of_start_of_combat(int deathsBefore, int firesAfterOurDeaths)
    {
        // R 4f, g4c9: Avenge (3) counts every friendly death and fires on the third (Relentless Deflector: AVENGE blocks
        // 997634, 997912, Divine Shield at 998047), then starts again. Deaths during Start of Combat already count
        // (package I: SimMinion.FriendlyDeathsBeforeCapture); full threes of them fired before the capture.
        var v = Minion(0, 30, cardId: "R_AVENGE3") with { FriendlyDeathsBeforeCapture = deathsBefore };
        var result = Fight(Side(Minion(1, 1, cardId: "D"), Minion(1, 1, cardId: "D"), v), Side(Minion(5, 100, cardId: "E")), oursFirst: true, Rules);

        var fired = result.IndexOf("avenge 3");
        var ourDeaths = fired < 0 ? -1
            : result.Log.Take(fired).Count(l => l.StartsWith("our D ", StringComparison.Ordinal) && l.EndsWith(" dies", StringComparison.Ordinal));
        Assert.Equal(firesAfterOurDeaths, ourDeaths);
    }

    [Fact]
    public void A_fresh_copy_keeps_a_fixed_script_data_value_and_drops_a_grown_one()
    {
        // Power.log 15.11: a card's script data took one value all game for Underrot Spawn (1, golden 2) and Sha of Fear (3/3);
        // Y'Shaarj's copy of Sha of Fear carries 3/3 (line 751229), its copy of a The Last One Standing improved to 20/30
        // carries the base 10/10 (751156), which the effect takes from the card text.
        var underrot = SimMinion.Create("BG36_116", 3, 4, keywords: Keyword.Reborn) with { ScriptData1 = 2 };
        var lastOneStanding = SimMinion.Create("BG34_320", 5, 5, keywords: Keyword.Reborn) with { ScriptData1 = 30, ScriptData2 = 30 };

        Assert.Equal((2, 0), (underrot.RebornCopy().ScriptData1, underrot.RebornCopy().ScriptData2));
        Assert.Equal((0, 0), (lastOneStanding.RebornCopy().ScriptData1, lastOneStanding.RebornCopy().ScriptData2));
    }

    // ---------------------------------------------------------------------------------- A E2: Titus repeats in place

    [Fact]
    public void Extra_deathrattle_triggers_repeat_each_deathrattle_before_the_next()
    {
        // A E2, G4 under Titus Rivendare: Ravaging Scorpid's deathrattle twice (lines 1389939, 1390019), then the
        // Golemancy deathrattle twice (1390099, 1390176): A, A, B, B, not A, B, A, B.
        var two = Minion(1, 1, cardId: "R_A") with { Enchantments = ["R_B"] };
        var titus = Minion(0, 10, cardId: "TITUS");
        var result = Fight(Side(two, titus), Side(Minion(5, 100, cardId: "E")), oursFirst: true, Rules);

        Assert.Equal(["DR-A", "DR-A", "DR-B", "DR-B"], result.Lines("DR-"));
    }

    // ------------------------------------------------------------------------- A E1: "whenever a friendly X attacks"

    [Fact]
    public void Whenever_a_friendly_minion_attacks_comes_before_the_attackers_rally()
    {
        // A E1, G1: Prodigious Tusker's gem lands on Bonker (line 236025) before Bonker's Rally (236080); G4: Faerie
        // Dragon Scale's Divine Shield (1331318) before Glim Guardian's Rally (1331351). Our watcher gives the attacker
        // +3 Attack, so the Rally already sees 5.
        var result = Fight(Side(Minion(2, 50, cardId: "R_RALLY"), Minion(0, 50, cardId: "R_WATCH")), Side(Minion(0, 100, cardId: "E")),
                           oursFirst: true, Rules);

        var friendly = result.IndexOf("friendly attacks");
        var rally = result.IndexOf("rally at ");
        Assert.True(friendly >= 0 && rally > friendly);
        Assert.Equal("rally at 5", result.Log[rally]);
    }

    // ------------------------------------------------------------------------------------------- engine limits

    [Fact]
    public void A_trigger_chain_deeper_than_the_engine_follows_is_not_simulated()
    {
        // The nesting limit used to drop the deathrattle silently; now the fight says it was cut short.
        var result = Fight(Side(Minion(1, 1, cardId: "R_LOOP")), Side(Minion(5, 5, cardId: "E")), oursFirst: true, Rules);
        Assert.True(result.Truncated);

        var odds = CombatSimulator.Run(Side(Minion(1, 1, cardId: "R_LOOP")), Side(Minion(5, 5, cardId: "E")), true,
                                       new SimOptions { Runs = 100, Effects = Rules });
        Assert.Equal(100, odds.Truncated);
        Assert.Equal(1, odds.Unsimulated);
        Assert.True(odds.Approximate);
    }

    [Fact]
    public void Fights_without_a_loop_never_reach_the_engine_limits()
    {
        var odds = CombatSimulator.Run(
            Side(Minion(1, 1, cardId: "R_SUMMON5"), Minion(2, 3, Keyword.Reborn, "R_AVENGE"), Minion(0, 10, cardId: "TITUS"), Minion(3, 3, cardId: "R_WATCH")),
            Side(Minion(2, 2, cardId: "R_NUKE"), Minion(4, 4, Keyword.DivineShield, "R_A"), Minion(1, 5, Keyword.Taunt | Keyword.Reborn, "R_B")),
            null, new SimOptions { Runs = 500, Effects = Rules });

        Assert.Equal(0, odds.Truncated);
        Assert.Equal(0, odds.Unsimulated);
    }

    [Fact]
    public void Every_registered_effect_runs_without_reaching_the_engine_limits()
    {
        // Each built-in effect on both boards, golden and normal, beside Titus Rivendare (BG25_354), every tribe.
        var problems = new List<string>();
        foreach (var id in EffectRegistry.Default.NormalCardIds.Order(StringComparer.Ordinal))
        {
            SimMinion Card(bool golden, Keyword keywords = Keyword.None) => Minion(3, 4, keywords, id, Tribe.All, golden);
            var titus = Minion(1, 7, cardId: "BG25_354", tribes: Tribe.Undead);
            var ours = Side(Card(false), Card(true), titus, Card(false, Keyword.Reborn));
            var theirs = Side(Card(false), titus, Card(true, Keyword.DivineShield), Card(false, Keyword.Taunt));
            var odds = CombatSimulator.Run(ours, theirs, null, new SimOptions { Runs = 20 });
            if (odds.Truncated > 0)
            {
                problems.Add($"{id}: {odds.Truncated} of {odds.Runs}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Auras_summing_past_int_max_are_clamped_before_they_change()
    {
        // Three auras of int.MaxValue / 2 used to wrap around when summed: the 2^31-1 Attack minion lost a billion when
        // one source died. Clamped, it loses exactly the 1 that no longer fits.
        const int Big = int.MaxValue / 2;
        var auras = Enumerable.Range(0, 3).Select(_ => Minion(0, 1, cardId: "R_HUGE")).ToArray();
        var v = Minion(int.MaxValue, int.MaxValue, cardId: "V");
        // Their E attacks the first aura source (pick 0); then our V attacks E.
        var result = Fight(Side([.. auras, v]), Side(Minion(1, 100, cardId: "E")), oursFirst: false, Rules, picks: [0, 0]);

        Assert.Equal(1, result.Outcome);
        var survivor = Assert.Single(result.Ours, m => m.CardId == "V");
        Assert.Equal(2 * Big, survivor.Attack);
        Assert.Equal(2 * Big, survivor.MaxHealth);
    }

    // ------------------------------------------------------------------------------------------- 8b: Ice Block

    [Fact]
    public void Ice_block_prevents_only_fatal_hero_damage()
    {
        // R 8b, g4c9 TURN=18: the opponent's hero attacks ours (line 1000169), our Ice Block triggers (1000193): no damage.
        Assert.Equal(0, DamageRules.Taken(12, 10, iceBlock: true));
        Assert.Equal(12, DamageRules.Taken(12, 10, iceBlock: false));
        Assert.Equal(5, DamageRules.Taken(5, 10, iceBlock: true));
        Assert.Equal(12, DamageRules.Taken(12, 0, iceBlock: true)); // unknown health is never fatal
        Assert.True(DamageRules.HasIceBlock(new SimSide([]) { Secrets = ["TB_Bacon_Secrets_12"] }));
    }

    [Fact]
    public void Ice_block_turns_a_lethal_loss_into_no_damage()
    {
        var ours = Side(Minion(1, 1)) with { Health = 5, TavernTier = 3 };
        var theirs = Side(Minion(10, 10) with { TechLevel = 6 }) with { Health = 30, TavernTier = 4 };

        var plain = CombatSimulator.Run(ours, theirs, true, new SimOptions { Runs = 50, Effects = Rules });
        var blocked = CombatSimulator.Run(ours with { Secrets = [DamageRules.IceBlock] }, theirs, true, new SimOptions { Runs = 50, Effects = Rules });

        Assert.Equal(1.0, plain.Loss);
        Assert.Equal(1.0, plain.LethalTaken);
        Assert.Equal(10.0, plain.DamageTaken!.Mean);
        Assert.Equal(0.0, blocked.LethalTaken);
        Assert.Equal(0.0, blocked.DamageTaken!.Mean);
    }

    // ------------------------------------------------------------------------------------------- test effects

    [CardEffect("R_A")]
    private sealed class TraceA : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.Trace("DR-A");
    }

    [CardEffect("R_B")]
    private sealed class TraceB : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.Trace("DR-B");
    }

    [CardEffect("R_SUMMON5")]
    private sealed class SummonFive : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(5, 1, cardId: "R_A"));
    }

    [CardEffect("R_SUMMON")]
    private sealed class SummonPlain : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 1, cardId: "T"));
    }

    [CardEffect("R_NUKE")]
    private sealed class DestroyEnemies : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Enemy))
            {
                fight.Destroy(enemy, self);
            }
        }
    }

    [CardEffect("R_AVENGE")]
    private sealed class AvengeBuffOthers : AvengeEffect
    {
        protected override int Threshold(Unit self) => 1;

        protected override void Avenge(Fight fight, Unit self)
        {
            fight.Trace("avenge");
            foreach (var friend in fight.Alive(self.Side, except: self))
            {
                fight.Buff(friend, 1, 1);
            }
        }
    }

    [CardEffect("R_AVENGE3")]
    private sealed class AvengeThree : AvengeEffect
    {
        protected override int Threshold(Unit self) => 3;

        protected override void Avenge(Fight fight, Unit self) => fight.Trace("avenge 3");
    }

    [CardEffect("R_WATCH")]
    private sealed class BuffFriendlyAttacker : CombatEffect
    {
        public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
        {
            if (attacker != self)
            {
                fight.Trace("friendly attacks");
                fight.Buff(attacker, 3, 0);
            }
        }
    }

    [CardEffect("R_RALLY")]
    private sealed class TraceRally : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target) => fight.Trace($"rally at {self.Attack}");
    }

    [CardEffect("R_LOOP")]
    private sealed class EndlessDeathrattle : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.TriggerDeathrattles(self, 1, withMultiplier: false);
    }

    [CardEffect("R_HUGE")]
    private sealed class HugeAura : CombatEffect
    {
        public override StatDelta AuraFor(Fight fight, Unit self, Unit other) =>
            other.CardId == "R_HUGE" ? default : new StatDelta(int.MaxValue / 2, int.MaxValue / 2);
    }
}

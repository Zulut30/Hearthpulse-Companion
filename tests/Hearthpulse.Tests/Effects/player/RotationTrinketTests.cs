using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Trinkets out of today's pool that come back with a rotation (package E6b), from their card text (HearthstoneJSON latest):
/// Battle Horn, Belcher Portrait, Blood Amulet, Boom Controller, Crocheted Sungill, Dazzling Dagger, Impulsive Portrait (with
/// Impulsive Trickster), Kaboom Bot Portrait (with Kaboom Bot), Mug of the Sire, Scrapsmith Portrait, Tiger Carving and Twin
/// Sky Lanterns. Their stats show in the attack lines of the fight log ("our X a/h attacks").
/// </summary>
public sealed class RotationTrinketTests
{
    private static SimMinion Wall(int attack = 1) => Minion(attack, 1000, cardId: "E");

    private static string Log(FightResult result) => string.Join('\n', result.Log);

    private static void Has(FightResult result, string text) => Assert.True(result.IndexOf(text) >= 0, text + "\n" + Log(result));

    private static void HasNot(FightResult result, string text) => Assert.True(result.IndexOf(text) < 0, text + "\n" + Log(result));

    // ---- Battle Horn: "Avenge (2): Trigger a friendly Battlecry." ------------------------------------------------------

    [Fact]
    public void Battle_horn_triggers_a_friendly_battlecry_after_two_deaths()
    {
        // Their 5/1000 kills F1, then F2 dies attacking it: Avenge (2) triggers BC's Battlecry (+10/+10 here).
        var ours = Side(Minion(1, 1, cardId: "F1"), Minion(1, 1, cardId: "F2"), Minion(1, 50, cardId: "TEST_BC"));
        var effects = Effects(typeof(BattleHorn), typeof(SelfBuffBattlecry));

        Has(Fight(WithCard(ours, "BG32_MagicItem_415"), Side(Wall(5)), oursFirst: false, effects), "our TEST_BC 11/55 attacks");
        Has(Fight(ours, Side(Wall(5)), oursFirst: false, effects), "our TEST_BC 1/45 attacks");
    }

    [Fact]
    public void Battle_horn_reads_its_avenge_number_from_the_capture()
    {
        // Captured Avenge (1): the first death already triggers it.
        var ours = WithCard(Side(Minion(1, 1, cardId: "F1"), Minion(1, 50, cardId: "TEST_BC")), "BG32_MagicItem_415",
                            new Dictionary<string, int> { ["SCORE_VALUE_1"] = 1, ["SCORE_VALUE_2"] = 0 });

        Has(Fight(ours, Side(Wall(5)), oursFirst: false, Effects(typeof(BattleHorn), typeof(SelfBuffBattlecry))), "our TEST_BC 11/60 attacks");
    }

    // ---- Belcher Portrait: "Whenever a friendly minion loses Venomous, it gains +4/+4 permanently." -----------------------

    [Theory]
    [InlineData("BG30_MagicItem_432", null, "our V 5/12 attacks")]
    [InlineData("BG30_MagicItem_432t", null, "our V 15/22 attacks")]
    [InlineData("BG30_MagicItem_432", 6, "our V 7/14 attacks")]
    public void Belcher_portrait_rewards_a_used_venomous(string trinket, int? captured, string expected)
    {
        // V kills E1 with Venomous and gains the stats, then E2 hits it for 1 before V attacks again.
        var ours = Side(Minion(1, 10, Keyword.Venomous, "V"));
        var theirs = Side(Minion(1, 100, cardId: "E1"), Minion(1, 100, cardId: "E2"));
        var side = WithCard(ours, trinket, captured is { } n ? Numbers(n, n) : null);

        Has(Fight(side, theirs, oursFirst: true, Effects(typeof(BelcherPortrait))), expected);
    }

    [Fact]
    public void Belcher_portrait_ignores_poisonous()
    {
        // Poisonous is not used up: nothing is lost, nothing is gained.
        var ours = WithCard(Side(Minion(1, 10, Keyword.Poisonous, "P")), "BG30_MagicItem_432");
        var theirs = Side(Minion(1, 100, cardId: "E1"), Minion(1, 100, cardId: "E2"));

        var result = Fight(ours, theirs, oursFirst: true, Effects(typeof(BelcherPortrait)));
        Has(result, "attacks our P 1/9 Poisonous");
        HasNot(result, "our P 5/");
    }

    // ---- Blood Amulet and Kaboom Bot Portrait ---------------------------------------------------------------------------

    [Fact]
    public void Blood_amulet_plays_a_blood_gem_after_a_deathrattle()
    {
        // The Kaboom Bot dies attacking, its deathrattle triggers: A gets a Blood Gem (+1/+1), then E hits it for 1.
        var ours = Side(Minion(1, 1, cardId: "BG_BOT_606"), Minion(1, 50, cardId: "A"));
        var effects = Effects(typeof(BloodAmulet), typeof(KaboomBot));

        Has(Fight(WithCard(ours, "BG35_MagicItem_432"), Side(Wall()), oursFirst: true, effects), "our A 2/50 attacks");
        Has(Fight(ours, Side(Wall()), oursFirst: true, effects), "our A 1/49 attacks");
    }

    [Fact]
    public void Blood_amulet_reaches_three_different_minions()
    {
        var ours = WithCard(Side(Minion(1, 1, cardId: "BG_BOT_606"), Minion(1, 50, cardId: "A"), Minion(1, 50, cardId: "B"),
                                 Minion(1, 50, cardId: "C"), Minion(1, 50, cardId: "D")), "BG35_MagicItem_432");
        var result = Fight(ours, Side(Minion(1, 30, cardId: "E")), oursFirst: true, Effects(typeof(BloodAmulet), typeof(KaboomBot)));

        // Their 1/30 takes 1 Health off a minion now and then, never Attack: three of A-D first attack with 2 Attack, one with 1.
        var first = result.Log.Where(l => l.StartsWith("our ", StringComparison.Ordinal) && l.Contains(" attacks ", StringComparison.Ordinal))
            .Select(l => l.Split(' ')).GroupBy(w => w[1]).Where(g => g.Key != "BG_BOT_606").ToDictionary(g => g.Key, g => g.First()[2]);
        Assert.Equal(4, first.Count);
        Assert.Equal(3, first.Values.Count(stats => stats.StartsWith("2/", StringComparison.Ordinal)));
        Assert.Equal(1, first.Values.Count(stats => stats.StartsWith("1/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Kaboom_bot_portrait_adds_ten_damage_to_each_hit()
    {
        // K dies attacking their 1/15 (now 1/14): 4 damage leaves it alive, 4 + 10 kills it.
        var ours = Side(Minion(1, 1, cardId: "BG_BOT_606"), Minion(1, 50, cardId: "A"));
        var theirs = Side(Minion(1, 15, cardId: "E"));
        var effects = Effects(typeof(KaboomBot), typeof(KaboomBotPortrait));

        Assert.Empty(Fight(WithCard(ours, "BG30_MagicItem_803"), theirs, oursFirst: true, effects).Theirs);
        Has(Fight(ours, theirs, oursFirst: true, effects), "their E 1/10 attacks");
        Has(Fight(WithCard(ours, "BG30_MagicItem_803", Numbers(20)), Side(Minion(1, 25, cardId: "E")), oursFirst: true, effects),
            "their E 1/0");
    }

    [Fact]
    public void A_golden_kaboom_bot_hits_twice()
    {
        var ours = Side(Minion(1, 1, cardId: "BG_BOT_606", golden: true), Minion(1, 50, cardId: "A"));

        Has(Fight(ours, Side(Minion(1, 15, cardId: "E")), oursFirst: true, Effects(typeof(KaboomBot))), "their E 1/6 attacks");
    }

    // ---- Impulsive Portrait: "Your Impulsive Tricksters' Deathrattles target adjacent minions." ---------------------------

    [Fact]
    public void Impulsive_portrait_gives_the_trickster_health_to_both_neighbours()
    {
        // Their 5/1000 kills the 1/5 Trickster in the middle: L and R both get +5 Health.
        var ours = WithCard(Side(Minion(1, 100, cardId: "L"), Minion(1, 5, cardId: "BG21_006"), Minion(1, 100, cardId: "R")),
                            "BG32_MagicItem_820");
        var result = Fight(ours, Side(Wall(5)), oursFirst: false, Effects(typeof(ImpulsiveTrickster), typeof(ImpulsivePortrait)), 1, 0);

        Has(result, "our L 1/105 attacks");
        Has(result, "our R 1/105 attacks");
    }

    [Fact]
    public void Without_the_portrait_the_trickster_buffs_one_random_minion()
    {
        var ours = Side(Minion(1, 100, cardId: "L"), Minion(1, 5, cardId: "BG21_006"), Minion(1, 100, cardId: "R"));
        var result = Fight(ours, Side(Wall(5)), oursFirst: false, Effects(typeof(ImpulsiveTrickster)), 1, 1);

        Has(result, "our L 1/100 attacks");
    }

    // ---- Mug of the Sire: "Whenever you would summon a minion that doesn't fit in your warband, give your minions +5 Attack."

    [Fact]
    public void Mug_of_the_sire_gives_attack_when_a_summon_does_not_fit()
    {
        // A full board: TEST_TWO dies, its first token fits, the second does not: +5 Attack to every minion.
        var board = new List<SimMinion> { Minion(1, 1, cardId: "TEST_TWO") };
        board.AddRange(Enumerable.Range(1, 6).Select(i => Minion(1, 50, cardId: "M" + i)));
        var ours = WithCard(Side([.. board]), "BG30_MagicItem_438t");
        var result = Fight(ours, Side(Wall()), oursFirst: true, Effects(typeof(MugOfTheSire), typeof(SummonTwo)));

        Has(result, "attacks our TEST_TOKEN 6/1");
        Has(result, "our M1 6/50 attacks");
        HasNot(Fight(Side([.. board]), Side(Wall()), oursFirst: true, Effects(typeof(SummonTwo))), "our M1 6/");
    }

    // ---- Scrapsmith Portrait: "After a friendly minion with Taunt dies, play a permanent Blood Gem on your Scrapsmiths." ---

    [Fact]
    public void Scrapsmith_portrait_gems_scrapsmiths_after_a_taunt_dies()
    {
        var withTaunt = WithCard(Side(Minion(1, 1, Keyword.Taunt, "T"), Minion(1, 50, cardId: "BG24_707")), "BG35_MagicItem_430");
        var without = WithCard(Side(Minion(1, 1, cardId: "T"), Minion(1, 50, cardId: "BG24_707")), "BG35_MagicItem_430");

        Has(Fight(withTaunt, Side(Wall()), oursFirst: false, Effects(typeof(ScrapsmithPortrait))), "our BG24_707 2/51 attacks");
        Has(Fight(without, Side(Wall()), oursFirst: false, Effects(typeof(ScrapsmithPortrait))), "our BG24_707 1/50 attacks");
    }

    // ---- Tiger Carving: "Whenever a friendly minion takes damage, give another friendly minion +3/+1 permanently." --------

    [Fact]
    public void Tiger_carving_buffs_another_minion_for_each_damage()
    {
        // A takes 1 three times (E hits it, A hits E, E hits it again): B gets +3/+1 each time before it attacks.
        var ours = Side(Minion(1, 50, cardId: "A"), Minion(1, 50, cardId: "B"));

        Has(Fight(WithCard(ours, "BG30_MagicItem_427"), Side(Wall()), oursFirst: false, Effects(typeof(TigerCarving))), "our B 10/53 attacks");
        Has(Fight(WithCard(ours, "BG30_MagicItem_427t"), Side(Wall()), oursFirst: false, Effects(typeof(TigerCarving))), "our B 19/56 attacks");
    }

    [Fact]
    public void Tiger_carving_does_not_count_a_divine_shield_pop()
    {
        var ours = WithCard(Side(Minion(1, 50, Keyword.DivineShield, "A"), Minion(1, 50, cardId: "B")), "BG30_MagicItem_427");

        // E pops A's shield (no damage), A hits E (1 damage): B is 4/51 when it attacks after E's next hit on A (7/52).
        Has(Fight(ours, Side(Wall()), oursFirst: false, Effects(typeof(TigerCarving))), "our B 7/52 attacks");
    }

    // ---- Twin Sky Lanterns: "When you have space, summon a copy of the first minion you summon each combat." -------------

    [Fact]
    public void Twin_sky_lanterns_copy_the_first_summoned_minion()
    {
        var ours = Side(Minion(1, 1, Keyword.Reborn, "R"), Minion(1, 50, cardId: "A"));

        var one = Fight(WithCard(ours, "BG30_MagicItem_822"), Side(Wall()), oursFirst: true, Effects(typeof(TwinSkyLanterns)));
        Assert.Single(one.Lines("summoned our R 1/1"));
        var two = Fight(WithCard(ours, "BG30_MagicItem_822t2"), Side(Wall()), oursFirst: true, Effects(typeof(TwinSkyLanterns)));
        Assert.Equal(2, two.Lines("summoned our R 1/1").Count);
    }

    [Fact]
    public void Twin_sky_lanterns_wait_for_space()
    {
        // A full board: their 5/1000 kills R, the reborn R fills the slot it left, so its copy comes only after the next death
        // (the reborn R attacking into their 5/1000).
        var board = new List<SimMinion> { Minion(1, 1, Keyword.Reborn, "R") };
        board.AddRange(Enumerable.Range(1, 6).Select(i => Minion(1, 1, cardId: "M" + i)));
        var result = Fight(WithCard(Side([.. board]), "BG30_MagicItem_822"), Side(Wall(5)), oursFirst: false, Effects(typeof(TwinSkyLanterns)));

        var reborn = SimTestKit.IndexOf(result, "reborn our R");
        var copy = SimTestKit.IndexOf(result, "summoned our R 1/1");
        Assert.True(reborn >= 0 && copy > reborn, Log(result));
        Assert.Contains(result.Log.Skip(reborn).Take(copy - reborn), l => l.EndsWith(" dies", StringComparison.Ordinal));
    }

    // ---- Boom Controller: "When you have space, summon an exact copy of your first Mech that died each combat." ----------

    [Fact]
    public void Boom_controller_summons_an_exact_copy_of_the_first_dead_mech_once()
    {
        // The Mech's shield popped before it died: the exact copy has it again. A second Mech death brings no copy.
        var ours = WithCard(Side(Minion(1, 1, Keyword.DivineShield, "MECH", Tribe.Mech), Minion(1, 1, cardId: "MECH2", tribes: Tribe.Mech),
                                 Minion(1, 50, cardId: "A")), "BG30_MagicItem_440");
        var result = Fight(ours, Side(Wall(5)), oursFirst: true, Effects(typeof(BoomController)));

        Assert.StartsWith("summoned our MECH 1/1 DivineShield", Assert.Single(result.Lines("summoned our ")), StringComparison.Ordinal);
    }

    [Fact]
    public void Boom_controller_ignores_other_tribes()
    {
        var ours = WithCard(Side(Minion(1, 1, cardId: "B", tribes: Tribe.Beast), Minion(1, 50, cardId: "A")), "BG30_MagicItem_440");

        Assert.Empty(Fight(ours, Side(Wall(5)), oursFirst: true, Effects(typeof(BoomController))).Lines("summoned our "));
    }

    // ---- Crocheted Sungill: "Start of Combat: … When you have space, summon it for this combat only." --------------------

    [Fact]
    public void Crocheted_sungill_summons_the_highest_health_hand_minion_when_room_comes()
    {
        var board = Enumerable.Range(1, 7).Select(i => Minion(1, 1, cardId: "M" + i)).ToArray();
        var ours = WithCard(Side(board), "BG32_MagicItem_960") with { Hand = [Minion(5, 2, cardId: "H1"), Minion(3, 9, cardId: "H2")] };
        var result = Fight(ours, Side(Wall(5)), oursFirst: false, Effects(typeof(CrochetedSungill)));

        Assert.StartsWith("summoned our H2 3/9", Assert.Single(result.Lines("summoned our ")), StringComparison.Ordinal);
    }

    [Fact]
    public void Crocheted_sungill_owes_nothing_when_it_already_summoned()
    {
        // Room at capture: the summon happened at Start of Combat (the minion is on the board).
        var ours = WithCard(Side(Minion(1, 1, cardId: "M1")), "BG32_MagicItem_960") with { Hand = [Minion(3, 9, cardId: "H2")] };

        Assert.Empty(Fight(ours, Side(Wall(5)), oursFirst: false, Effects(typeof(CrochetedSungill))).Lines("summoned our "));
    }

    [Fact]
    public void Crocheted_sungill_of_the_opponent_needs_their_hidden_hand_only_with_a_full_board()
    {
        var full = WithCard(Side(Enumerable.Range(1, 7).Select(i => Minion(1, 1, cardId: "M" + i)).ToArray()), "BG32_MagicItem_960");
        var registry = Effects(typeof(CrochetedSungill));

        Assert.Equal(UnsimulatedReason.MissingInput,
                     Assert.Single(CombatSimulator.Explain(full, isOpponent: true, registry), i => i.CardId == "BG32_MagicItem_960").Reason);
        Assert.DoesNotContain(CombatSimulator.Explain(WithCard(Side(Minion(1, 1)), "BG32_MagicItem_960"), isOpponent: true, registry),
                              i => i.CardId == "BG32_MagicItem_960");
    }

    // ---- Dazzling Dagger: "Your minions have +1 Attack. (Improved by every 4 spells you've cast this game!)" ---------------

    [Fact]
    public void Dazzling_dagger_reaches_minions_summoned_in_combat()
    {
        var reborn = Side(Minion(1, 1, Keyword.Reborn, "R"));

        Has(Fight(WithCard(reborn, "BG32_MagicItem_934"), Side(Minion(1, 10, cardId: "E")), oursFirst: false, Effects(typeof(DazzlingDagger))),
            "our R 2/1 attacks");
        Has(Fight(WithCard(reborn, "BG32_MagicItem_934", Numbers(4)), Side(Minion(1, 10, cardId: "E")), oursFirst: false, Effects(typeof(DazzlingDagger))),
            "our R 5/1 attacks");
    }

    [Fact]
    public void Dazzling_dagger_needs_its_number_or_an_estimate()
    {
        var side = WithCard(Side(Minion(1, 1)), "BG32_MagicItem_934");

        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(side, isOpponent: true)).Reason);
        Assert.Empty(CombatSimulator.Explain(WithCard(Side(Minion(1, 1)), "BG32_MagicItem_934", Numbers(2)), isOpponent: false));
        var estimated = side with
        {
            Estimates = [new SimEstimate(SimInputs.TrinketValue, 0, 0, "BG32_MagicItem_934: its number is hidden from the player: the printed card")],
        };
        Assert.Empty(CombatSimulator.Explain(estimated, isOpponent: true));
    }

    [CardEffect("TEST_BC")]
    private sealed class SelfBuffBattlecry : CombatEffect
    {
        public override void Battlecry(Fight fight, Unit self) => fight.Buff(self, 10, 10);
    }

    [CardEffect("TEST_TWO")]
    private sealed class SummonTwo : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Minion(1, 1, cardId: "TEST_TOKEN"), 2);
    }
}

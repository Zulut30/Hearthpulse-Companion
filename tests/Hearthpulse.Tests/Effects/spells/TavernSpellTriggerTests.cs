using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// "Whenever / after you cast a Tavern spell" minions, set off by Crimson Vindicator's Rally (Mighty Dragonbreath: "Give
/// your minions +3/+2. Repeat for your Dragons. Repeat for your minions with Divine Shield."). Card texts in each test;
/// stats worked out by hand. The Vindicator is an 8/9 Divine Shield Dragon here.
/// </summary>
public sealed class TavernSpellTriggerTests
{
    [Fact]
    public void Timecapn_hooktail_gives_your_minions_one_attack_before_the_spell()
    {
        // "Whenever you cast a Tavern spell, give your minions +1 Attack." Hooktail 1/4 Dragon/Pirate.
        // +1 Attack: Vindicator 9/9, Hooktail 2/4. Dragonbreath: Vindicator +9/+6 (all, Dragon, shield) → 18/15,
        // Hooktail +6/+4 (all, Dragon) → 8/8. The Vindicator kills the 0/18.
        var result = Fight(Side(VindicatorMinion(), Minion(1, 4, cardId: "BG27_005", tribes: Tribe.Dragon | Tribe.Pirate)),
            Wall(18), true, Effects(typeof(CrimsonVindicator), typeof(TimecapnHooktail)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(18, 15), (8, 8)], Stats(result.Ours));
    }

    [Fact]
    public void Golden_timecapn_hooktail_gives_one_attack_twice()
    {
        // Golden 2/8: +1 Attack twice → Vindicator 10/9, Hooktail 4/8; then Dragonbreath → 19/15 and 10/12.
        var result = Fight(Side(VindicatorMinion(), Minion(2, 8, cardId: "BG27_005", tribes: Tribe.Dragon | Tribe.Pirate, golden: true)),
            Wall(19), true, Effects(typeof(CrimsonVindicator), typeof(TimecapnHooktail)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(19, 15), (10, 12)], Stats(result.Ours));
    }

    [Fact]
    public void Hooktail_ignores_a_spell_that_is_not_a_tavern_spell()
    {
        // The old simple CastSpell overload marks a Tavern spell; a SpellCast with IsTavernSpell = false does not count.
        var result = Fight(Side(Minion(1, 1, cardId: "NONTAVERN"), Minion(1, 4, cardId: "BG27_005", tribes: Tribe.Pirate)),
            Wall(1), true, Effects(typeof(NonTavernCaster), typeof(TimecapnHooktail)));

        Assert.Equal([(1, 1), (1, 4)], Stats(result.Ours));
    }

    [Fact]
    public void Charging_czarina_gives_shielded_minions_four_attack_before_the_spell()
    {
        // "Divine Shield. Whenever you cast a Tavern spell, give your minions with Divine Shield +4 Attack."
        // Czarina 4/1 Mech with shield, P 1/1 without. +4: Vindicator 12/9, Czarina 8/1. Dragonbreath: Vindicator
        // +9/+6 → 21/15; Czarina +6/+4 (all, shield) → 14/5; P +3/+2 → 4/3.
        var result = Fight(
            Side(VindicatorMinion(), Minion(4, 1, Keyword.DivineShield, "BG28_741", Tribe.Mech), Minion(1, 1, cardId: "P")),
            Wall(21), true, Effects(typeof(CrimsonVindicator), typeof(ChargingCzarina)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(21, 15), (14, 5), (4, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Golden_charging_czarina_gives_eight_attack()
    {
        // Golden 8/2: +8 → Vindicator 16/9, Czarina 16/2; Dragonbreath → 25/15 and 22/6.
        var result = Fight(Side(VindicatorMinion(), Minion(8, 2, Keyword.DivineShield, "BG28_741", Tribe.Mech, golden: true)),
            Wall(25), true, Effects(typeof(CrimsonVindicator), typeof(ChargingCzarina)));

        Assert.Equal([(25, 15), (22, 6)], Stats(result.Ours));
    }

    [Fact]
    public void Sha_of_fear_gives_your_minions_and_deity_three_three()
    {
        // "Whenever you cast a Tavern spell, give your minions and Deity +3/+3." Sha 10/13 Aberration; the sleeping
        // Y'Shaarj is 3/2. +3/+3: Vindicator 11/12, Sha 13/16, Deity 6/5. Dragonbreath: Vindicator 20/18, Sha 16/18.
        var ours = WithDeity(Side(VindicatorMinion(), Minion(10, 13, cardId: "BG36_111", tribes: Tribe.Aberration)), 3, 2);
        var result = Fight(ours, Wall(20), true, Effects(typeof(CrimsonVindicator), typeof(ShaOfFear), typeof(YShaarj)));

        Assert.Equal([(20, 18), (16, 18)], Stats(result.Ours));
        Assert.Single(result.Lines("our Deity gets +3/+3 (6/5)"));
    }

    [Fact]
    public void Golden_sha_of_fear_gives_six_six()
    {
        // Golden 20/26: +6/+6 → Vindicator 14/15, Sha 26/32, Deity 9/8; Dragonbreath → 23/21 and 29/34.
        var ours = WithDeity(Side(VindicatorMinion(), Minion(20, 26, cardId: "BG36_111", tribes: Tribe.Aberration, golden: true)), 3, 2);
        var result = Fight(ours, Wall(23), true, Effects(typeof(CrimsonVindicator), typeof(ShaOfFear), typeof(YShaarj)));

        Assert.Equal([(23, 21), (29, 34)], Stats(result.Ours));
        Assert.Single(result.Lines("our Deity gets +6/+6 (9/8)"));
    }

    [Fact]
    public void The_live_numbers_in_the_captured_script_data_win_over_the_printed_ones()
    {
        // A server-side balance change: the captured Sha of Fear shows +5/+4 (TAG_SCRIPT_DATA_NUM_1/2). +5/+4:
        // Vindicator 13/13, Sha 15/17, Deity 3/2 → 8/6. Dragonbreath: Vindicator 22/19, Sha 18/19.
        var sha = Minion(10, 13, cardId: "BG36_111", tribes: Tribe.Aberration) with { ScriptData1 = 5, ScriptData2 = 4 };
        var result = Fight(WithDeity(Side(VindicatorMinion(), sha), 3, 2), Wall(22), true,
            Effects(typeof(CrimsonVindicator), typeof(ShaOfFear), typeof(YShaarj)));

        Assert.Equal([(22, 19), (18, 19)], Stats(result.Ours));
        Assert.Single(result.Lines("our Deity gets +5/+4 (8/6)"));
    }

    [Fact]
    public void Vicious_mindslasher_buffs_itself_and_the_deity()
    {
        // "Whenever you cast a Tavern spell, give this and your Deity +1/+2." Mindslasher 1/2 → 2/4, Deity 3/2 → 4/4;
        // Dragonbreath: Vindicator 17/15, Mindslasher 5/6.
        var ours = WithDeity(Side(VindicatorMinion(), Minion(1, 2, cardId: "BG36_108", tribes: Tribe.Aberration)), 3, 2);
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(ViciousMindslasher), typeof(YShaarj)));

        Assert.Equal([(17, 15), (5, 6)], Stats(result.Ours));
        Assert.Single(result.Lines("our Deity gets +1/+2 (4/4)"));
    }

    [Fact]
    public void Golden_vicious_mindslasher_gives_two_four()
    {
        // Golden 2/4 → 4/8, Deity 3/2 → 5/6; Dragonbreath → 7/10.
        var ours = WithDeity(Side(VindicatorMinion(), Minion(2, 4, cardId: "BG36_108", tribes: Tribe.Aberration, golden: true)), 3, 2);
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(ViciousMindslasher), typeof(YShaarj)));

        Assert.Equal([(17, 15), (7, 10)], Stats(result.Ours));
        Assert.Single(result.Lines("our Deity gets +2/+4 (5/6)"));
    }

    [Fact]
    public void Forsaken_weaver_gives_undead_three_attack_after_the_spell()
    {
        // "After you cast a Tavern spell, your Undead have +3 Attack this game (wherever they are)." Weaver 3/8 Undead,
        // U 1/1 Undead. Dragonbreath: Vindicator 17/15, Weaver 6/10, U 4/3; then +3 Attack: Weaver 9/10, U 7/3.
        var result = Fight(
            Side(VindicatorMinion(), Minion(3, 8, cardId: "BG34_692", tribes: Tribe.Undead), Minion(1, 1, cardId: "U", tribes: Tribe.Undead)),
            Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(ForsakenWeaver)));

        Assert.Equal([(17, 15), (9, 10), (7, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Forsaken_weaver_bonus_reaches_undead_summoned_later_this_fight()
    {
        // Golden Weaver 6/16: +6 Attack. Our Vindicator hits their 10/30 first (17 damage, the shield takes the 10) and the
        // Undead get +6. Then their 10/30 kills our U (pick 2 of 3) and U comes back with Reborn: printed 1 Attack + 6.
        var ours = Side(VindicatorMinion(), Minion(6, 16, cardId: "BG34_692", tribes: Tribe.Undead, golden: true),
            Minion(1, 1, Keyword.Reborn, "U", Tribe.Undead));
        var result = Fight(ours, Side(Minion(10, 30, cardId: "K")), true, Effects(typeof(CrimsonVindicator), typeof(ForsakenWeaver)), 0, 2);

        Assert.Single(result.Lines("reborn our U 7/1"));
    }

    [Fact]
    public void Abyssal_bruiser_grows_with_each_tavern_spell()
    {
        // "Has +2/+1 for each Tavern spell you've cast this game." Bruiser 2/1 Naga with shield: Dragonbreath +6/+4 (all,
        // shield) → 8/5, then +2/+1 → 10/6. Golden 4/2: → 10/6 + 4/2 → 14/8.
        var normal = Fight(Side(VindicatorMinion(), Minion(2, 1, Keyword.DivineShield, "BG35_921", Tribe.Naga)),
            Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(AbyssalBruiser)));
        var golden = Fight(Side(VindicatorMinion(), Minion(4, 2, Keyword.DivineShield, "BG35_921", Tribe.Naga, golden: true)),
            Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(AbyssalBruiser)));

        Assert.Equal((10, 6), (normal.Ours[1].Attack, normal.Ours[1].Health));
        Assert.Equal((14, 8), (golden.Ours[1].Attack, golden.Ours[1].Health));
    }

    [Fact]
    public void Two_vindicator_casts_set_each_trigger_off_twice()
    {
        // Golden Vindicator 16/18 casts twice: Hooktail's +1 Attack before each cast. Hooktail 1/4: +1 → 2/4, +6/+4 → 8/8,
        // +1 → 9/8, +6/+4 → 15/12. Vindicator: 17/18 → 26/24 → 27/24 → 36/30.
        var result = Fight(
            Side(Minion(16, 18, Keyword.DivineShield, Vindicator, Tribe.Dragon, golden: true, mechanics: CardMechanic.Rally),
                Minion(1, 4, cardId: "BG27_005", tribes: Tribe.Dragon | Tribe.Pirate)),
            Wall(36), true, Effects(typeof(CrimsonVindicator), typeof(TimecapnHooktail)));

        Assert.Equal(2, result.Lines("we cast BG36_246").Count);
        Assert.Equal([(36, 30), (15, 12)], Stats(result.Ours));
    }

    [CardEffect("NONTAVERN")]
    private sealed class NonTavernCaster : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target) =>
            fight.CastSpell(new SpellCast(self.Side, "TEST_SPELL", null, self, null, IsTavernSpell: false));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// The effects the relevance audit (AUDIT2, 2026-11-19) added for cards the matrix had called "no effect" or "resolved":
/// Timewarped minions, buddies, minions of hero powers, trinkets and the enchantments Start of Combat cards give. One fight
/// per behaviour with every registered effect (<see cref="EffectRegistry.Default"/>); results worked out by hand in each
/// comment. Enemies "W" have no effect; 0-Attack enemies never hurt.
/// </summary>
public sealed class RelevanceAuditEffectsTests
{
    private const string Peggy = "BG34_Giant_327";

    private static SimMinion Pirate => Minion(0, 5, cardId: "P", tribes: Tribe.Pirate);

    private static (int, int) Stats(IEnumerable<SimMinion> side, string cardId) =>
        side.Where(m => m.CardId == cardId).Select(m => (m.Attack, m.Health)).Single();

    private static SimMinion Enchanted(int attack, int health, string enchantment) =>
        Minion(attack, health, cardId: "E") with { Enchantments = [enchantment] };

    [Theory]
    [InlineData(false, 2, 3)]
    [InlineData(true, 4, 5)]
    public void Timewarped_bonker_plays_two_gems_on_each_other_minion(bool golden, int attack, int health)
    {
        // Rally before the hit: 2 Blood Gems (golden 4) of +1/+1 on the 0/1; then it kills the 0/1 enemy.
        var result = Fight(Side(Minion(1, 5, cardId: "BG34_Giant_102", golden: golden), Minion(0, 1, cardId: "A")), Side(Minion(0, 1, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((attack, health), Stats(result.Ours, "A"));
    }

    [Fact]
    public void Timewarped_sea_glass_doubles_its_stats_twice_per_combat()
    {
        // 2/3 against 0/30: 4/6 hits (26 left), 8/12 hits (18), then no more doubling: 8 (10), 8 (2), 8 → dead.
        var result = Fight(Side(Minion(2, 3, cardId: "BG34_Giant_110")), Side(Minion(0, 30, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((8, 12), Stats(result.Ours, "BG34_Giant_110"));
    }

    [Theory]
    [InlineData("BG34_Giant_609", 3, 22)] // Ghoul-acabra 0/20: "give your minions +3/+2"
    [InlineData("BG34_Giant_202", 3, 22)] // Saurolisk 0/20: "gain +3/+2"
    public void After_a_friendly_deathrattle_the_timewarped_minion_grows(string cardId, int attack, int health)
    {
        // The Sewer Rat 1/1 trades with the 1/1: its Deathrattle triggers (a Half-Shell) and the minion grows once.
        var rat = Minion(1, 1, cardId: Tokens.SewerRatId, tribes: Tribe.Beast, mechanics: CardMechanic.Deathrattle);
        var result = Fight(Side(rat, Minion(0, 20, cardId: cardId)), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((attack, health), Stats(result.Ours, cardId));
    }

    [Fact]
    public void Timewarped_whelp_smuggler_gives_health_to_a_minion_that_gains_attack()
    {
        // Bonker's gem gives the Smuggler 0/8 +1/+1 (Attack gained) → +3 Health: 1/12.
        var result = Fight(Side(Minion(1, 5, cardId: "BG20_104"), Minion(0, 8, cardId: "BG34_Giant_064")), Side(Minion(0, 1, cardId: "W")));
        Assert.Equal((1, 12), Stats(result.Ours, "BG34_Giant_064"));
    }

    [Fact]
    public void A_card_got_in_combat_makes_timewarped_peggy_buff_the_pirates()
    {
        // Timewarped Kil'rek trades and its Deathrattle gets a card: Peggy gives the Pirate +1/+1, not herself (not a Pirate).
        var result = Fight(Side(Minion(1, 1, cardId: "BG34_Giant_584"), Minion(0, 5, cardId: Peggy), Pirate), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((1, 6), Stats(result.Ours, "P"));
        Assert.Equal((0, 5), Stats(result.Ours, Peggy));
    }

    [Fact]
    public void Timewarped_henchman_gets_a_card_for_the_second_kill_only()
    {
        // 10/10 kills three 0/1s; the second kill adds a card (Pirate +1/+1 by Peggy), the first and third do not.
        var result = Fight(Side(Minion(10, 10, cardId: "BG34_Giant_593"), Minion(0, 5, cardId: Peggy), Pirate),
            Side(Minion(0, 1, cardId: "W"), Minion(0, 1, cardId: "W"), Minion(0, 1, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((1, 6), Stats(result.Ours, "P"));
    }

    [Fact]
    public void Artisanal_urn_gives_undead_summoned_in_combat_its_attack()
    {
        // The Undead 1/1 Reborn trades with the 1/1 and comes back 1/1 + 20 Attack.
        var undead = Minion(1, 1, Keyword.Reborn, cardId: "U", tribes: Tribe.Undead);
        var result = Fight(Side(undead) with { Extras = ["BG30_MagicItem_989t"] }, Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((21, 1), Stats(result.Ours, "U"));
    }

    [Fact]
    public void Automaton_portrait_summons_once_the_full_board_has_room()
    {
        // Seven 1/1s: the first trades with the 1/1 enemy; the room left gets an Ancestral Automaton 3/4 at the right end.
        var full = Enumerable.Range(0, 7).Select(_ => Minion(1, 1, cardId: "A")).ToArray();
        var result = Fight(Side(full) with { Extras = ["BG30_MagicItem_303"] }, Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((3, 4), Stats(result.Ours, "BG_TTN_401"));

        // With room at Start of Combat the Automaton was summoned before the capture: nothing is owed.
        var six = Fight(Side(full[..6]) with { Extras = ["BG30_MagicItem_303"] }, Side(Minion(1, 1, cardId: "W")));
        Assert.DoesNotContain(six.Ours, m => m.CardId == "BG_TTN_401");
    }

    [Fact]
    public void The_eye_of_dalaran_gets_a_card_when_a_minion_with_no_type_dies()
    {
        var result = Fight(Side(Minion(1, 1, cardId: "A"), Minion(0, 5, cardId: Peggy), Pirate) with { Extras = ["BG30_MagicItem_981"] },
            Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((1, 6), Stats(result.Ours, "P"));
    }

    [Fact]
    public void Funeral_wreath_gets_a_copy_of_a_reborn_minion()
    {
        var result = Fight(Side(Minion(1, 1, Keyword.Reborn, cardId: "R"), Minion(0, 5, cardId: Peggy), Pirate) with { Extras = ["BG36_MagicItem_217"] },
            Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((1, 6), Stats(result.Ours, "P"));
    }

    [Fact]
    public void Element_earth_summons_a_stone_elemental()
    {
        var result = Fight(Side(Enchanted(1, 1, "BG22_HERO_001p_t1e")), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((1, 1), Stats(result.Ours, "BG22_HERO_001p_t1et"));
    }

    [Fact]
    public void Frosty_globe_summons_a_flourishing_frostling()
    {
        var result = Fight(Side(Enchanted(1, 1, "BG30_MagicItem_952e")), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((2, 1), Stats(result.Ours, "BG26_537"));
    }

    [Fact]
    public void Skyward_gives_the_other_minions_three_three()
    {
        var result = Fight(Side(Enchanted(1, 1, "BG35_MagicItem_740e2"), Minion(0, 5, cardId: "B")), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((3, 8), Stats(result.Ours, "B"));
    }

    [Fact]
    public void Fragrant_gives_the_other_minions_its_stats()
    {
        // 4/2 trades with the 2/2: the 0/5 gets +4/+2.
        var result = Fight(Side(Enchanted(4, 2, "BG20_HERO_282pe"), Minion(0, 5, cardId: "B")), Side(Minion(2, 2, cardId: "W")));
        Assert.Equal((4, 7), Stats(result.Ours, "B"));
    }

    [Fact]
    public void Hoggy_bank_deathrattle_gets_two_blood_gems()
    {
        var result = Fight(Side(Enchanted(1, 1, "BG30_MagicItem_411e"), Minion(0, 5, cardId: Peggy), Pirate), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((2, 7), Stats(result.Ours, "P"));
    }

    [Fact]
    public void Fish_of_nzoth_gains_a_deathrattle_of_a_friendly_minion_that_died()
    {
        // A 1/1 with Skyward trades with W: the Fish 0/1 and B 0/10 get +3/+3 (3/4, 3/13) and the Fish gains Skyward. X 9/1
        // kills the Fish (both die): the gained Skyward gives B +3/+3 more → 6/16.
        var result = Fight(Side(Enchanted(1, 1, "BG35_MagicItem_740e2"), Minion(0, 1, cardId: "TB_BaconShop_HP_105t"), Minion(0, 10, cardId: "B")),
            Side(Minion(1, 1, cardId: "W"), Minion(9, 1, cardId: "X")), oursFirst: true, effects: null, 0, 0);
        Assert.Equal((6, 16), Stats(result.Ours, "B"));
    }

    [Fact]
    public void Sneeds_shredder_summons_the_highest_health_minion_from_the_hand_with_divine_shield()
    {
        var ours = Side(Minion(1, 1, cardId: "BG21_HERO_030t")) with { Hand = [Minion(5, 2, cardId: "S"), Minion(3, 9, cardId: "H")] };
        var result = Fight(ours, Side(Minion(1, 1, cardId: "W")));
        var summoned = Assert.Single(result.Ours);
        Assert.Equal(("H", 3, 9, true), (summoned.CardId, summoned.Attack, summoned.Health, summoned.DivineShield));
    }

    [Fact]
    public void Hydralisk_gains_attack_equal_to_the_tier_on_each_attack()
    {
        // Tier 4: 1/10 attacks the 0/20 as 5 (15 left), 9 (6 left), 13 → dead.
        var result = Fight(Side(Minion(1, 10, cardId: "BG31_HERO_811t4")) with { TavernTier = 4 }, Side(Minion(0, 20, cardId: "W")));
        Assert.Equal((13, 10), Stats(result.Ours, "BG31_HERO_811t4"));
    }

    [Fact]
    public void Baneling_deals_its_attack_to_an_enemy_when_it_dies()
    {
        // 5/1 trades with the 1/1 (pick 0), its Deathrattle deals 5 to the 0/5: both boards are empty → a tie.
        var result = Fight(Side(Minion(5, 1, cardId: "BG31_HERO_811t5")), Side(Minion(1, 1, cardId: "W"), Minion(0, 5, cardId: "X")),
            oursFirst: true, effects: null, 0);
        Assert.Equal(0, result.Outcome);
    }

    [Fact]
    public void Viper_is_immune_while_attacking()
    {
        var result = Fight(Side(Minion(2, 1, Keyword.Venomous, cardId: "BG31_HERO_811t8")), Side(Minion(5, 2, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((2, 1), Stats(result.Ours, "BG31_HERO_811t8"));
    }

    [Fact]
    public void Void_ray_gives_an_attacker_and_itself_five_attack()
    {
        var result = Fight(Side(Minion(1, 10, cardId: "A"), Minion(0, 10, cardId: "BG31_HERO_802pt5")), Side(Minion(0, 3, cardId: "W")));
        Assert.Equal((6, 10), Stats(result.Ours, "A"));
        Assert.Equal((5, 10), Stats(result.Ours, "BG31_HERO_802pt5"));
    }

    [Fact]
    public void Monstrosity_gains_the_attack_of_a_friendly_minion_that_dies()
    {
        var result = Fight(Side(Minion(3, 1, cardId: "A"), Minion(0, 10, cardId: "BG20_HERO_282_Buddy")), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((3, 10), Stats(result.Ours, "BG20_HERO_282_Buddy"));
    }

    [Fact]
    public void Tamuzo_doubles_a_minion_summoned_in_combat()
    {
        var rat = Minion(1, 1, cardId: Tokens.SewerRatId, tribes: Tribe.Beast, mechanics: CardMechanic.Deathrattle);
        var result = Fight(Side(rat, Minion(0, 10, cardId: "BG23_HERO_201_Buddy")), Side(Minion(1, 1, cardId: "W")));
        Assert.Equal((4, 6), Stats(result.Ours, Tokens.HalfShellId));
    }

    [Fact]
    public void Wandering_treant_buffs_the_board_when_a_taunt_is_attacked()
    {
        // W 1/1 attacks the Taunt 0/5 first: +1/+1 to both before the hit → 1/6 and 1/11; the hit leaves 1/5, W dies.
        var result = Fight(Side(Minion(0, 5, Keyword.Taunt, cardId: "T"), Minion(0, 10, cardId: "TB_BaconShop_HERO_95_Buddy")),
            Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal((1, 5), Stats(result.Ours, "T"));
        Assert.Equal((1, 11), Stats(result.Ours, "TB_BaconShop_HERO_95_Buddy"));
    }

    [Fact]
    public void Titanic_guardian_gains_the_health_another_minion_gains()
    {
        // Bonker's gems: +1/+1 on the Guardian itself (1/3), +1/+1 on C (1/2) → the Guardian also gains 1 Health: 1/4.
        var result = Fight(Side(Minion(1, 5, cardId: "BG20_104"), Minion(0, 2, cardId: "TB_BaconShop_HERO_39_Buddy"), Minion(0, 1, cardId: "C")),
            Side(Minion(0, 1, cardId: "W")));
        Assert.Equal((1, 4), Stats(result.Ours, "TB_BaconShop_HERO_39_Buddy"));
        Assert.Equal((1, 2), Stats(result.Ours, "C"));
    }
}

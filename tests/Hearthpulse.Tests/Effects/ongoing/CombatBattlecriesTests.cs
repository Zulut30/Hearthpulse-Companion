using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Battlecries that destroy, make Golden or gain a random Bonus Keyword, triggered in combat by Timewarped Greenskeeper 5/9
/// ("Rally: Trigger your right-most Battlecry and Deathrattle"), which attacks first into their 0-Attack wall. Results are
/// worked out by hand from the card text in each comment.
/// </summary>
public sealed class CombatBattlecriesTests
{
    /// <summary>The Greenskeeper, with a tier above every Battlecry's limit so it is never a "make Golden" candidate.</summary>
    private static SimMinion Keeper() => Minion(5, 9, cardId: "BG34_Giant_041", tribes: Tribe.Dragon) with { TechLevel = 7 };

    private static SimMinion With(string cardId, int attack, int health, Tribe tribes = Tribe.None, bool golden = false, Keyword keywords = Keyword.None) =>
        Minion(attack, health, keywords, cardId, tribes, golden, CardMechanic.Battlecry);

    private static EffectRegistry Registry(Type effect) => Effects(typeof(TimewarpedGreenskeeper), effect);

    /// <summary>Their 0/5 wall: the Keeper's first attack kills it, so its Rally triggers the Battlecry exactly once.</summary>
    private static SimSide Wall() => Side(Minion(0, 5, cardId: "W"));

    private static SimMinion Survivor(FightResult result, string cardId) => result.Ours.Single(m => m.CardId == cardId);

    [Fact]
    public void Bubble_gunner_gains_a_bonus_keyword_it_lacks()
    {
        // Picks 0: the first Bonus Keyword it lacks. A plain Gunner gets Taunt; one with Taunt gets Divine Shield.
        var plain = Fight(Side(Keeper(), With("BG31_149", 2, 3, Tribe.Murloc)), Wall(), true, Registry(typeof(BubbleGunner)));
        Assert.True(Survivor(plain, "BG31_149").Taunt);
        Assert.False(Survivor(plain, "BG31_149").DivineShield);

        var taunted = Fight(Side(Keeper(), With("BG31_149", 2, 3, Tribe.Murloc, keywords: Keyword.Taunt)), Wall(), true, Registry(typeof(BubbleGunner)));
        Assert.True(Survivor(taunted, "BG31_149").DivineShield);
    }

    [Fact]
    public void Golden_bubble_gunner_gains_two_different_keywords_and_none_when_it_has_all()
    {
        var golden = Survivor(Fight(Side(Keeper(), With("BG31_149", 4, 6, Tribe.Murloc, golden: true)), Wall(), true, Registry(typeof(BubbleGunner))), "BG31_149");
        Assert.True(golden.Taunt && golden.DivineShield);
        Assert.False(golden.Venomous);

        const Keyword all = Keyword.Taunt | Keyword.DivineShield | Keyword.Windfury | Keyword.Venomous | Keyword.Reborn;
        var full = Survivor(Fight(Side(Keeper(), With("BG31_149", 2, 3, Tribe.Murloc, keywords: all)), Wall(), true, Registry(typeof(BubbleGunner))), "BG31_149");
        Assert.Equal(all, full.Keywords & all);
        Assert.False(full.Stealth);
    }

    [Fact]
    public void Captain_sanders_makes_another_friendly_minion_golden_with_its_printed_stats_added()
    {
        // A is printed 2/3, buffed to 5/6. Golden: 5+2 / 6+3 = 7/9, golden from now on. Sanders itself and the Keeper (tier 7)
        // are not candidates.
        var a = Minion(2, 3, cardId: "A") with { Attack = 5, Health = 6, MaxHealth = 6, TechLevel = 3 };
        var result = Fight(Side(Keeper(), a, With("BG25_034", 9, 9, Tribe.Pirate) with { TechLevel = 7 }), Wall(), true, Registry(typeof(CaptainSanders)));

        var golden = Survivor(result, "A");
        Assert.True(golden.Golden);
        Assert.Equal((7, 9), (golden.Attack, golden.Health));
        Assert.False(Survivor(result, "BG25_034").Golden);
    }

    [Fact]
    public void Captain_sanders_skips_golden_and_tier_seven_minions()
    {
        var goldenAlready = Minion(4, 6, cardId: "G", golden: true) with { TechLevel = 2 };
        var tierSeven = Minion(3, 3, cardId: "T7") with { TechLevel = 7 };
        var result = Fight(Side(Keeper(), goldenAlready, tierSeven, With("BG25_034", 9, 9, Tribe.Pirate) with { TechLevel = 7 }), Wall(), true,
                           Registry(typeof(CaptainSanders)));

        Assert.Equal((4, 6), (Survivor(result, "G").Attack, Survivor(result, "G").Health));
        Assert.False(Survivor(result, "T7").Golden);
    }

    [Fact]
    public void Elite_navigator_makes_only_a_pirate_from_tier_four_or_below_golden()
    {
        // P4 (Pirate, tier 4) is the only candidate: P5 is tier 5, B is a Beast.
        var p4 = Minion(3, 3, cardId: "P4", tribes: Tribe.Pirate) with { TechLevel = 4 };
        var p5 = Minion(3, 3, cardId: "P5", tribes: Tribe.Pirate) with { TechLevel = 5 };
        var beast = Minion(3, 3, cardId: "B", tribes: Tribe.Beast) with { TechLevel = 1 };
        var result = Fight(Side(Keeper(), p5, beast, p4, With("BG32_231", 5, 5, Tribe.Pirate) with { TechLevel = 5 }), Wall(), true,
                           Registry(typeof(EliteNavigator)));

        Assert.True(Survivor(result, "P4").Golden);
        Assert.Equal((6, 6), (Survivor(result, "P4").Attack, Survivor(result, "P4").Health));
        Assert.False(Survivor(result, "P5").Golden);
        Assert.False(Survivor(result, "B").Golden);
    }

    [Fact]
    public void Disguised_graverobber_destroys_a_friendly_undead_and_gets_plain_copies()
    {
        // U (Undead, printed 2/2, buffed to 9/9) is destroyed; golden Graverobber gets two plain 2/2 copies.
        var undead = Minion(2, 2, cardId: "U", tribes: Tribe.Undead) with { Attack = 9, Health = 9, MaxHealth = 9 };
        var result = Fight(Side(Keeper(), undead, With("BG28_303", 8, 8, golden: true)), Wall(), true, Registry(typeof(DisguisedGraverobber)));

        Assert.DoesNotContain(result.Ours, m => m.CardId == "U");
        Assert.Equal(2, result.Log.Count(l => l == "we get U"));
    }

    [Fact]
    public void Maw_caster_destroys_a_friendly_undead_to_get_a_card_and_does_nothing_without_one()
    {
        var undead = Minion(2, 2, cardId: "U", tribes: Tribe.Undead);
        var result = Fight(Side(Keeper(), undead, With("BG32_340", 4, 5, Tribe.Undead)), Wall(), true, Registry(typeof(MawCaster)));
        Assert.DoesNotContain(result.Ours, m => m.CardId == "U");
        Assert.Contains("we get a random card", result.Log);

        // Its own Undead tribe does not make it a target.
        var alone = Fight(Side(Keeper(), With("BG32_340", 4, 5, Tribe.Undead)), Wall(), true, Registry(typeof(MawCaster)));
        Assert.Contains(alone.Ours, m => m.CardId == "BG32_340");
        Assert.DoesNotContain("we get a random card", alone.Log);
    }
}

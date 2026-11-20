using Hearthpulse.Core.Battlegrounds;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Hero damage after the fight, the combat damage cap and lethal: the simulator alone, so the file travels to BGSim.
/// How the overlay writes the damage and how saved predictions are checked against history: <c>CombatPredictionTests</c>.
/// </summary>
public sealed class DamageAndLethalTests
{
    private static SimMinion Tiered(int attack, int health, int tier) => Minion(attack, health) with { TechLevel = tier };

    // Tier 4 with a tier 3 and a tier 2 minion that cannot die: 4 + 3 + 2 = 9 every fight.
    private static SimSide Winner(int health = 0) => Side(Tiered(50, 50, 3), Tiered(50, 50, 2)) with { TavernTier = 4, Health = health };

    private static SimSide Loser(int health = 0) => Side(Tiered(1, 1, 1)) with { TavernTier = 2, Health = health };

    [Fact]
    public void Damage_is_the_winners_tier_plus_the_tiers_of_its_survivors()
    {
        var odds = CombatSimulator.Run(Winner(), Loser(), firstAttackerIsOurs: true, new SimOptions { Runs = 300 });

        Assert.Equal(1.0, odds.Win);
        Assert.Null(odds.DamageTaken);
        var dealt = Assert.IsType<DamageSpread>(odds.DamageDealt);
        Assert.Equal(9, dealt.Mean);
        Assert.Equal((9, 9, 9), (dealt.Low, dealt.High, dealt.Max));
        Assert.Equal(300, dealt.Runs);
        Assert.Equal(300, dealt.Counts[9]);
    }

    [Fact]
    public void Damage_taken_counts_the_opponents_side_when_we_lose()
    {
        var odds = CombatSimulator.Run(Loser(), Winner(), firstAttackerIsOurs: false, new SimOptions { Runs = 200 });

        Assert.Equal(1.0, odds.Loss);
        Assert.Null(odds.DamageDealt);
        Assert.Equal(9, odds.DamageTaken!.Max);
        Assert.Equal(9, odds.DamageTaken.Low);
    }

    [Fact]
    public void Tokens_without_a_tier_count_as_tier_one()
    {
        var ours = Side(Tiered(50, 50, 0), Tiered(50, 50, 0)) with { TavernTier = 1 };

        var odds = CombatSimulator.Run(ours, Loser(), firstAttackerIsOurs: true, new SimOptions { Runs = 50 });

        Assert.Equal(3, odds.DamageDealt!.Max);
        Assert.Equal(3, DamageRules.Damage(1, [0, 0], cap: 0));
    }

    [Fact]
    public void Dead_minions_add_no_damage()
    {
        // Their 1/1 attacks first, must hit our 3/1 taunt, and both die; only the 50/50 is left.
        var ours = Side(Tiered(50, 50, 2), Tiered(3, 1, 5) with { Taunt = true }) with { TavernTier = 3 };
        var theirs = Side(Tiered(1, 1, 1)) with { TavernTier = 1 };

        var odds = CombatSimulator.Run(ours, theirs, firstAttackerIsOurs: false, new SimOptions { Runs = 100 });

        // 3 (tier) + 2 (the 50/50) = 5.
        Assert.Equal(1.0, odds.Win);
        Assert.Equal(5, odds.DamageDealt!.Max);
        Assert.Equal(5, odds.DamageDealt.Low);
    }

    [Fact]
    public void Damage_cap_limits_the_damage_while_it_is_in_force()
    {
        var capped = CombatSimulator.Run(Winner(), Loser(), true, new SimOptions { Runs = 100, DamageCap = 5 });
        var open = CombatSimulator.Run(Winner(), Loser(), true, new SimOptions { Runs = 100, DamageCap = 0 });

        Assert.Equal(5, capped.DamageDealt!.Max);
        Assert.Equal(5, capped.DamageCap);
        Assert.Equal(9, open.DamageDealt!.Max);
        Assert.Equal(0, open.DamageCap);
    }

    [Theory]
    [InlineData(15, null, 15)] // 2022 builds: the cap itself...
    [InlineData(0, null, 0)] // ...drops to 0 once lifted
    [InlineData(5, 1, 5)] // newer builds switch it with BACON_COMBAT_DAMAGE_CAP_ENABLED
    [InlineData(5, 0, 0)]
    [InlineData(0, 1, 0)]
    public void Cap_follows_the_game_entity_tags(int cap, int? enabled, int expected) =>
        Assert.Equal(expected, DamageRules.Cap(cap, enabled));

    [Fact]
    public void Lethal_counts_damage_that_leaves_exactly_zero_health()
    {
        // We take 9; 9 health + armor is exactly lethal, 10 survives at 1.
        var dead = CombatSimulator.Run(Loser(health: 9), Winner(), false, new SimOptions { Runs = 100 });
        var alive = CombatSimulator.Run(Loser(health: 10), Winner(), false, new SimOptions { Runs = 100 });
        var unknown = CombatSimulator.Run(Loser(health: 0), Winner(), false, new SimOptions { Runs = 100 });

        Assert.Equal(1.0, dead.LethalTaken);
        Assert.Equal(0.0, alive.LethalTaken);
        Assert.Null(unknown.LethalTaken);
        Assert.True(DamageRules.IsLethal(9, 9));
        Assert.False(DamageRules.IsLethal(8, 9));
        Assert.False(DamageRules.IsLethal(9, 0));
    }

    [Fact]
    public void Kill_chance_uses_the_opponents_health()
    {
        var kill = CombatSimulator.Run(Winner(), Loser(health: 7), true, new SimOptions { Runs = 100 });
        var cappedKill = CombatSimulator.Run(Winner(), Loser(health: 7), true, new SimOptions { Runs = 100, DamageCap = 5 });

        Assert.Equal(1.0, kill.LethalDealt);
        Assert.Equal(0.0, kill.LethalTaken ?? 0);
        Assert.Equal(0.0, cappedKill.LethalDealt);
    }

    [Fact]
    public void Empty_board_gives_certain_damage_without_simulating()
    {
        var odds = CombatSimulator.Run(Side() with { Health = 4 }, Winner(), null);

        Assert.Equal(0, odds.Runs);
        Assert.Equal(1.0, odds.Loss);
        Assert.Equal(9, odds.DamageTaken!.Mean);
        Assert.Equal(1.0, odds.LethalTaken);
    }

    [Fact]
    public void Fights_with_damage_off_count_none()
    {
        var odds = CombatSimulator.Run(Winner(health: 30), Loser(health: 5), true, new SimOptions { Runs = 50, CountDamage = false });

        Assert.Null(odds.DamageDealt);
        Assert.Null(odds.LethalDealt);
        Assert.Null(odds.LethalTaken);
    }

    [Fact]
    public void A_close_fight_spreads_damage_between_both_sides()
    {
        var ours = Side(Tiered(3, 3, 1), Tiered(2, 4, 2), Tiered(4, 2, 3)) with { TavernTier = 3, Health = 30 };
        var theirs = Side(Tiered(3, 3, 1), Tiered(2, 4, 2), Tiered(4, 2, 3)) with { TavernTier = 3, Health = 30 };

        var odds = CombatSimulator.Run(ours, theirs, null, new SimOptions { Runs = 4000 });

        foreach (var spread in new[] { odds.DamageTaken!, odds.DamageDealt! })
        {
            Assert.InRange(spread.Low, 4, spread.High);
            Assert.InRange(spread.Max, spread.High, 3 + 1 + 2 + 3);
            Assert.InRange(spread.Mean, spread.Low, spread.High);
            Assert.Equal(spread.Runs, spread.Counts.Sum());
        }

        Assert.Equal(odds.Runs, odds.DamageTaken!.Runs + odds.DamageDealt!.Runs + (int)Math.Round(odds.Tie * odds.Runs));
        Assert.Equal(0.0, odds.LethalTaken);
        // Same boards, same numbers (the distribution compares by value).
        Assert.Equal(odds, CombatSimulator.Run(ours, theirs, null, new SimOptions { Runs = 4000 }));
    }

    [Fact]
    public void Spread_takes_the_10th_and_90th_percentiles()
    {
        // One fight each at 2..11 damage.
        var spread = DamageSpread.From([0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1])!;

        Assert.Equal(6.5, spread.Mean);
        Assert.Equal((2, 10, 11), (spread.Low, spread.High, spread.Max));
        Assert.True(spread.Covers(2) && spread.Covers(10));
        Assert.False(spread.Covers(11));
        Assert.Null(DamageSpread.From([0, 0, 0]));
    }
}

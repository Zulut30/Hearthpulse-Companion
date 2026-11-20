using Hearthpulse.Core.Battlegrounds;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Hero damage and lethal in Duos (TASK-2026-11-19 ACC2, <see cref="DamageRules.TeamHealth"/>): the losing team's shared
/// health takes the tier of the winning board left standing plus its survivors' tiers. By the general Duos rules; no
/// Duos Power.log was available to check them.
/// </summary>
public sealed class DuosDamageTests
{
    private static SimMinion Tiered(int attack, int health, int tier, string cardId = "") =>
        Minion(attack, health, cardId: cardId) with { TechLevel = tier };

    [Fact]
    public void A_teammate_that_wins_deals_its_own_tier_plus_its_survivors()
    {
        // Our first board (tier 2) dies to the 3/3; our teammate (tier 5) enters and wins with one tier-4 survivor.
        var ours = Side(Tiered(1, 1, 1)) with
        {
            TavernTier = 2,
            Teammate = Side(Tiered(50, 50, 4, "STRONG")) with { TavernTier = 5 },
        };
        var theirs = Side(Tiered(3, 3, 3)) with { TavernTier = 3, Health = 30 };

        var odds = CombatSimulator.Run(ours, theirs, firstAttackerIsOurs: true, new SimOptions { Runs = 200 });

        Assert.Equal(1.0, odds.Win);
        var dealt = Assert.IsType<DamageSpread>(odds.DamageDealt);
        Assert.Equal((9, 9), (dealt.Low, dealt.High));
        Assert.Equal(0.0, odds.LethalDealt);
    }

    [Fact]
    public void A_first_board_that_wins_without_its_teammate_deals_its_own_tier()
    {
        var ours = Side(Tiered(50, 50, 4)) with { TavernTier = 2, Teammate = Side(Tiered(1, 1, 1)) with { TavernTier = 6 } };
        var theirs = Side(Tiered(1, 1, 1)) with { TavernTier = 3, Health = 30 };

        var odds = CombatSimulator.Run(ours, theirs, firstAttackerIsOurs: true, new SimOptions { Runs = 200 });

        Assert.Equal(6, odds.DamageDealt!.Mean);
    }

    [Fact]
    public void The_opponents_teammate_that_wins_deals_its_tier_to_our_team_and_can_be_lethal()
    {
        // Their first board dies; their teammate (tier 6) wins with a tier-5 survivor: 11 against our team's 10.
        var ours = Side(Tiered(4, 4, 2)) with { TavernTier = 2, Health = 10 };
        var theirs = Side(Tiered(1, 1, 1)) with { TavernTier = 3, Teammate = Side(Tiered(50, 50, 5)) with { TavernTier = 6 } };

        var odds = CombatSimulator.Run(ours, theirs, firstAttackerIsOurs: true, new SimOptions { Runs = 200 });

        Assert.Equal(1.0, odds.Loss);
        Assert.Equal(11, odds.DamageTaken!.Mean);
        Assert.Equal(1.0, odds.LethalTaken);
    }

    [Fact]
    public void Team_health_falls_back_to_the_teammates_when_the_first_boards_is_unknown()
    {
        var theirs = Side(Tiered(1, 1, 1)) with { Teammate = Side(Tiered(1, 1, 1)) with { Health = 7 } };

        Assert.Equal(7, DamageRules.TeamHealth(theirs));
        Assert.Equal(12, DamageRules.TeamHealth(theirs with { Health = 12 }));
        Assert.Equal(0, DamageRules.TeamHealth(Side()));
    }

    [Fact]
    public void A_winning_team_whose_first_board_is_empty_deals_the_teammates_tier()
    {
        // Our first board is empty at the start: the teammate fights the whole fight (CombatSimulator.StartingBoard).
        var ours = Side() with { TavernTier = 1, Teammate = Side(Tiered(50, 50, 3)) with { TavernTier = 4 } };
        var theirs = Side(Tiered(1, 1, 1)) with { TavernTier = 2, Health = 20 };

        var odds = CombatSimulator.Run(ours, theirs, firstAttackerIsOurs: true, new SimOptions { Runs = 100 });

        Assert.Equal(7, odds.DamageDealt!.Mean);
    }

    [Fact]
    public void Ice_block_of_either_member_guards_the_team()
    {
        var withIceBlock = Side(Tiered(1, 1, 1)) with { Secrets = [DamageRules.IceBlock] };
        var team = Side(Tiered(1, 1, 1)) with { Teammate = withIceBlock };

        Assert.True(DamageRules.TeamHasIceBlock(team));
        Assert.True(DamageRules.TeamHasIceBlock(withIceBlock));
        Assert.False(DamageRules.TeamHasIceBlock(Side(Tiered(1, 1, 1))));
    }
}

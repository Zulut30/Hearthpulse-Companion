using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests;

/// <summary>
/// What the player sees of the simulator's damage prediction outside the simulator: the overlay's damage text, the
/// render scene, and saved predictions checked against history and My Stats. Kept apart from
/// <c>Sim/DamageAndLethalTests</c> so that file needs only the simulator (tools/bgsim/sync.ps1 copies it to BGSim).
/// </summary>
public sealed class CombatPredictionTests
{
    private static SimMinion Tiered(int attack, int health, int tier) => Minion(attack, health) with { TechLevel = tier };

    // Tier 4 with a tier 3 and a tier 2 minion that cannot die: 4 + 3 + 2 = 9 every fight.
    private static SimSide Winner() => Side(Tiered(50, 50, 3), Tiered(50, 50, 2)) with { TavernTier = 4 };

    private static SimSide Loser() => Side(Tiered(1, 1, 1)) with { TavernTier = 2 };

    [Fact]
    public void Damage_text_shows_the_10th_to_90th_percentile_or_one_number()
    {
        // One fight each at 2..11 damage: 2-10; all fights at 5: one number.
        Assert.Equal("2–10", CombatOddsText.Damage(DamageSpread.From([0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1])));
        Assert.Equal("5", CombatOddsText.Damage(DamageSpread.From([0, 0, 0, 0, 0, 3])));
    }

    [Fact]
    public void Render_scene_shows_damage_both_ways_and_a_lethal_risk()
    {
        var odds = CombatOddsScene.Synthetic().CombatOdds!;

        Assert.True(odds.Win > 0 && odds.Loss > 0);
        Assert.NotNull(odds.DamageTaken);
        Assert.NotNull(odds.DamageDealt);
        Assert.True(odds.LethalTaken > 0);
        Assert.Equal(15, odds.DamageCap);
    }

    [Fact]
    public void Saved_predictions_are_checked_against_what_happened()
    {
        var win = CombatSimulator.Run(Winner(), Loser(), true, new SimOptions { Runs = 50 }); // dealt 9..9
        var loss = CombatSimulator.Run(Loser(), Winner(), false, new SimOptions { Runs = 50 }); // taken 9..9
        IReadOnlyList<CombatRecord> combats =
        [
            new(1, 5, "H1", "Win", 0, 9, win), // result and damage right
            new(2, 6, "H2", "Loss", 4, 0, loss), // result right, damage outside 9..9
            new(3, 7, "H3", "Loss", 9, 0, win), // predicted a win: both wrong
            new(4, 8, "H4", "Tie", 0, 0, loss), // a tie: result wrong, no damage to check
            new(5, 9, "H5", "Win", 0, 3, null), // nothing simulated: skipped
        ];
        var game = new GameRecord
        {
            Key = "bg",
            Mode = "Battlegrounds",
            CombatResults = System.Text.Json.JsonSerializer.Serialize(combats.Select(c => new
                { turn = c.Turn, result = c.Result, opponent = c.OpponentHeroCardId, taken = c.DamageTaken, dealt = c.DamageDealt })),
            CombatOdds = GameRecords.CombatOddsJson(combats),
        };

        var predictions = BattlegroundsStats.Predictions(game);
        Assert.Equal(5, predictions.Count);
        Assert.Null(predictions[4]);
        Assert.Equal(new BgDamageRange(9, 9, 9, 9), predictions[0]!.Dealt);
        Assert.Equal("Win", predictions[0]!.Likely);

        var accuracy = BattlegroundsStats.SimAccuracy([game]);
        Assert.Equal(new BgSimAccuracy(Fights: 4, ResultHits: 2, DamageFights: 3, DamageHits: 1), accuracy);
        Assert.Equal(0.5, accuracy.ResultRate);
        Assert.Null(BattlegroundsStats.SimAccuracy([]).DamageRate);
    }
}

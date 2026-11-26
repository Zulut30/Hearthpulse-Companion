using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Every registered effect in the places a live capture can put it besides the board (TASK-2026-11-26 AC5: odds for
/// every fight, no exception): a player-level card of either side (hero power, trinket, reward, anomaly, Deity:
/// <see cref="SimSide.Extras"/>) and a Secret or objective (<see cref="SimSide.Secrets"/>), with its captured values or
/// without them, the card in our hand, and on a Duos team's second board (<see cref="SimSide.Teammate"/>) that enters
/// with its own player cards. A throw here is a fight the overlay shows no odds for ("Combat simulation failed"). The
/// board itself is <c>CombatRulesEvidenceTests.Every_registered_effect_runs_without_reaching_the_engine_limits</c>.
/// </summary>
public sealed class EffectPlacementTests
{
    private static readonly string[] Ids = [.. EffectRegistry.Default.NormalCardIds.Order(StringComparer.Ordinal)];

    /// <summary>The values a player card's effect may read from its capture (Avenge progress, a grown number).</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, int>> Values(string id) => new(StringComparer.Ordinal)
    {
        [id] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["TAG_SCRIPT_DATA_NUM_1"] = 2,
            ["TAG_SCRIPT_DATA_NUM_2"] = 2,
            ["SCORE_VALUE_1"] = 3,
            ["SCORE_VALUE_2"] = 1,
        },
    };

    private static SimMinion[] Board(string id) =>
    [
        Minion(3, 4, tribes: Tribe.All),
        Minion(2, 2, Keyword.Taunt | Keyword.DivineShield, tribes: Tribe.Beast),
        Minion(5, 1, Keyword.Reborn, tribes: Tribe.Undead),
        Minion(1, 6, cardId: id, tribes: Tribe.All),
    ];

    [Fact]
    public void Every_registered_effect_runs_as_a_player_card_of_either_side()
    {
        var problems = new List<string>();
        foreach (var id in Ids)
        {
            var ours = Side(Board(id)) with
            {
                Extras = [id],
                Secrets = [id],
                ExtraTags = Values(id),
                Hand = [Minion(2, 2, cardId: id, tribes: Tribe.All), Minion(4, 4, cardId: id, tribes: Tribe.All, golden: true), Minion(1, 1, tribes: Tribe.Murloc)],
                TavernTier = 4,
                Health = 20,
                Turn = 8,
            };
            var theirs = Side(Board(id)) with { Extras = [id], Secrets = [id], TavernTier = 5, Health = 12, Turn = 8 };
            Run(id, "player card", ours, theirs, problems);
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Every_registered_effect_runs_on_a_duos_second_board()
    {
        var problems = new List<string>();
        foreach (var id in Ids)
        {
            // Weak first boards, so both teams' second boards come in with the effect on a minion and as a player card.
            SimSide Second(bool ours) => Side(Minion(3, 3, cardId: id, tribes: Tribe.All), Minion(6, 6, cardId: id, tribes: Tribe.All, golden: true), Minion(2, 5, Keyword.Taunt))
                with { Extras = [id], ExtraTags = ours ? Values(id) : SimSide.EmptyExtraTags, TavernTier = 3, Health = 0, Turn = 6 };
            var ours = Side(Minion(1, 1)) with { Teammate = Second(ours: true), TavernTier = 2, Health = 25, Turn = 6 };
            var theirs = Side(Minion(1, 1, cardId: id)) with { Teammate = Second(ours: false), TavernTier = 2, Health = 25, Turn = 6 };
            Run(id, "duos", ours, theirs, problems);
        }

        Assert.Empty(problems);
    }

    private static void Run(string id, string where, SimSide ours, SimSide theirs, List<string> problems)
    {
        try
        {
            var odds = CombatSimulator.Run(ours, theirs, null, new SimOptions { Runs = 20, DamageCap = 15 });
            if (Math.Abs(odds.Win + odds.Tie + odds.Loss - 1) > 1e-9 || odds.Truncated > 0)
            {
                problems.Add($"{id} ({where}): odds {odds.Win}/{odds.Tie}/{odds.Loss}, truncated {odds.Truncated}");
            }
        }
        catch (Exception ex)
        {
            problems.Add($"{id} ({where}): {ex.GetType().Name}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        }
    }
}

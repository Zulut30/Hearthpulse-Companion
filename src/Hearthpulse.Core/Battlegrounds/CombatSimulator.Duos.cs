using System.Text.Json.Serialization;

namespace Hearthpulse.Core.Battlegrounds;

// Duos team combat (TASK-2026-11-19 package D): the second board of each team (Simulation/Fight.Duos.cs).

public sealed partial record SimSide
{
    /// <summary>
    /// Duos: the teammate's board that enters this side's fight once this board has no minions left (with the teammate's
    /// hero, hero power and player-level cards); null in a solo fight, and when the team has no second board to bring in.
    /// The opponent team's second board is the one the player last saw, so it comes with an estimate in its
    /// <see cref="Estimates"/>; it never holds a teammate of its own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimSide? Teammate { get; init; }
}

public static partial class CombatSimulator
{
    /// <summary>The side a fight starts with: in Duos a first board without minions gives way to its teammate's at once.</summary>
    internal static SimSide StartingBoard(SimSide side) =>
        side.Minions.Count == 0 && side.Teammate is { } teammate
            ? teammate with { Estimates = [.. side.Estimates, .. teammate.Estimates] }
            : side;

    /// <summary>Estimated inputs of a side and of its Duos teammate.</summary>
    private static int EstimateCount(SimSide side) => side.Estimates.Count + (side.Teammate?.Estimates.Count ?? 0);

    /// <summary><see cref="SimInputs.PostGame"/> for a side and its Duos teammate.</summary>
    private static SimSide PostGameTeam(SimSide side)
    {
        var post = SimInputs.PostGame(side);
        return side.Teammate is { } teammate ? post with { Teammate = SimInputs.PostGame(teammate) } : post;
    }
}

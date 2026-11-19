using System.Text.Json.Serialization;

namespace Hearthpulse.Core.Battlegrounds;

// Inputs the engine's combat rules read (TASK-2026-11-19 package R, rules from real Power.log fights in
// rules-evidence.md). Not saved with captures yet: the capture format is package P1's to change.

public sealed partial record SimMinion
{
    /// <summary>
    /// Summoned during this combat before capture (Start of Combat), not brought to the fight: in a death phase it dies
    /// after every starting minion of both sides, in the order summoned (Power.log, rules-evidence 4a: 17 of 17 phases).
    /// </summary>
    [JsonIgnore]
    public bool SummonedInCombat { get; init; }
}

public sealed partial record SimSide
{
    /// <summary>
    /// The hero's lobby PLAYER_ID; 0 when unknown. The side with the lower one entered play first, so its starting
    /// minions die first in a death phase (Power.log, rules-evidence 4a: 83 of 83 phases).
    /// </summary>
    [JsonIgnore]
    public int PlayerId { get; init; }

    /// <summary>
    /// The local player's own secrets in play (card ids), which are on their screen; always empty for the opponent,
    /// whose secrets are hidden. Read for hero damage: Ice Block (<see cref="DamageRules.IceBlock"/>).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Secrets { get; init; } = Array.Empty<string>();
}

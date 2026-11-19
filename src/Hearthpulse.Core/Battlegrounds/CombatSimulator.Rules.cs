using System.Collections.Frozen;
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

    /// <summary>
    /// Cards (normal ids) whose TAG_SCRIPT_DATA_NUM_1/2 is a balance value that does not change during a game. A fresh
    /// copy of a card (Reborn, Y'Shaarj) carries the card's base script data: for these it is what the captured minion
    /// shows, so <see cref="RebornCopy"/> keeps it. Cards whose value grows (The Last One Standing BG34_320 10/20/30, Faceless
    /// Converter BG36_318, BG31_148 3/6/9...) come back with their base value, which the effects take from the card text.
    /// Power.log 15.11, every value of the five games: one non-zero value per card (and golden card) for these; Y'Shaarj's
    /// copy of a The Last One Standing improved to 20/30 carries 10/10 (line 751156), of Sha of Fear 3/3 (751229).
    /// </summary>
    internal static readonly FrozenSet<string> FixedScriptDataCards = FrozenSet.ToFrozenSet(
    [
        "BG23_017", "BG27_005", "BG28_741", "BG33_828", "BG33_885", "BG36_108", "BG36_109", "BG36_110", "BG36_111", "BG36_113",
        "BG36_116", "BG36_703", "BG36_704", "BG36_853", "BGS_008", "BGS_018", "BGS_041",
    ], StringComparer.Ordinal);
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

using System.Text.Json.Serialization;

namespace Hearthpulse.Core.Battlegrounds;

// Inputs besides the boards (TASK-2026-11-19 package I): exact values the log shows the player, and estimates for the
// opponent's values the player cannot see (SimInputs, Tracking/SimEstimates).

public sealed partial record SimMinion
{
    /// <summary>
    /// The enchantments of <see cref="Enchantments"/> with the numbers they carry at capture (a Blood Gem total, a granted
    /// buff's size), in the same order. Empty in captures saved before it was recorded.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SimEnchantment> EnchantmentValues { get; init; } = Array.Empty<SimEnchantment>();

    /// <summary>
    /// Friendly minions that died during Start of Combat (between the combat's start and the capture) while this one was
    /// on the board: the Avenge progress and "after a friendly minion dies" counts it brings into the fight.
    /// </summary>
    [JsonIgnore]
    public int FriendlyDeathsBeforeCapture { get; init; }
}

public sealed partial record SimSide
{
    /// <summary>
    /// Enchantments on the player entity at capture (Diremuck Forager's pending summons, the Beetle Army bonus...): the
    /// local player's only; whether the client shows the opponent's is not established.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SimEnchantment> PlayerEnchantments { get; init; } = Array.Empty<SimEnchantment>();

    /// <summary>The player enchantment <paramref name="cardId"/> (made by <paramref name="creatorEntityId"/> when not 0), or null.</summary>
    public SimEnchantment? PlayerEnchantment(string cardId, int creatorEntityId = 0)
    {
        foreach (var enchantment in PlayerEnchantments)
        {
            if (enchantment.CardId == cardId && (creatorEntityId == 0 || enchantment.CreatorEntityId == creatorEntityId))
            {
                return enchantment;
            }
        }

        return null;
    }

    /// <summary>
    /// Post-game only: the opponent's player tags effects read that the client does not show the player (Blood Gem and
    /// tavern spell values, the Deity's stats; <see cref="SimInputs.HiddenPlayerTags"/>). During the game this is empty and
    /// <see cref="Tags"/> holds an estimate instead (docs/adr/0005-hidden-information.md); the tracker fills it only in the
    /// captures it hands out once the game is over, and a simulation uses it only with <see cref="SimOptions.PostGame"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int> HiddenTags { get; init; } = EmptyTags;

    /// <summary>
    /// This side's minions that died during Start of Combat, before the capture, in the order they died. Public for both
    /// sides: the player saw them die. Deaths that count for the Deity (the opponent's awakening progress is exact for
    /// that reason) or Avenge before the fight is simulated.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SimMinion> DeathsBeforeCapture { get; init; } = Array.Empty<SimMinion>();

    /// <summary>
    /// The lobby's tribes (random summons draw only from them; minions without a tribe always can be drawn);
    /// <see cref="Tribe.None"/> when unknown.
    /// </summary>
    [JsonIgnore]
    public Tribe LobbyTribes { get; init; }

    /// <summary>
    /// Inputs this side's effects read that the log does not show the player (the opponent's) or that were not read,
    /// with the estimate used in their place (<see cref="SimInputs"/>). Not the same as "not simulated": the effect runs.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SimEstimate> Estimates { get; init; } = Array.Empty<SimEstimate>();

    /// <summary>
    /// The side's sleeping Deity at capture as its "Secret Deity" objective (<see cref="SimInputs.DeityObjective"/> in the
    /// SECRET zone) shows it: Aberration deaths counted so far and the count that awakens it. Both sides': the client
    /// reveals the opponent's objective when the combat starts (I2, docs/bg-combat-sim.md). Null when the capture has no
    /// objective (older captures, lobbies without a Deity): the effect falls back to its printed four.
    /// </summary>
    [JsonIgnore]
    public DeityAwakening? DeityAwakening { get; init; }
}

/// <summary>
/// A Deity's awakening progress: <paramref name="Progress"/> is the objective's QUEST_PROGRESS (friendly Aberration
/// deaths counted, Start of Combat included; it can start above 0), <paramref name="Total"/> its QUEST_PROGRESS_TOTAL.
/// </summary>
public sealed record DeityAwakening(int Progress, int Total);

/// <summary>An enchantment as the log shows it at capture: its card id and the numbers it carries (0 when a tag is absent).</summary>
public sealed record SimEnchantment(string CardId)
{
    /// <summary>TAG_SCRIPT_DATA_NUM_1/2: the numbers its text shows (a Blood Gem total, a counter).</summary>
    public int ScriptData1 { get; init; }

    public int ScriptData2 { get; init; }

    /// <summary>The ATK / HEALTH tags of the enchantment entity.</summary>
    public int Attack { get; init; }

    public int Health { get; init; }

    /// <summary>Entity id of what made it (its CREATOR tag), e.g. the Diremuck Forager whose summon is pending.</summary>
    public int CreatorEntityId { get; init; }
}

/// <summary>
/// An input the simulator estimated instead of reading it: a value of the opponent the player cannot see, or one the
/// tracker could not read. The effect runs with the estimate; the fight is not "not simulated".
/// </summary>
/// <param name="Input">Which input (the names in <see cref="SimInputs"/>).</param>
/// <param name="Value1">The estimate (an Attack, a count).</param>
/// <param name="Value2">A second number (a Health), 0 when the input has one.</param>
/// <param name="Basis">What the estimate rests on (visible information only), for diagnostics.</param>
public sealed record SimEstimate(string Input, int Value1, int Value2, string Basis)
{
    /// <summary>The player tags the estimate stands in for in <see cref="SimSide.Tags"/> (empty when it is not a tag).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>One estimated input of a side (<see cref="CombatSimulator.ExplainEstimates"/>).</summary>
public sealed record EstimatedItem(string Input, int Value1, int Value2, string Basis, bool IsOpponent);

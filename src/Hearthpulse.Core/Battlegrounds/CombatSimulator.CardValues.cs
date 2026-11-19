using System.Text.Json.Serialization;

namespace Hearthpulse.Core.Battlegrounds;

// Card values effects read besides the boards (TASK-2026-11-19 package E5 sim-leftovers): the third and fourth script
// numbers a card's text shows, and the crew of a Timewarped Nellie's Ship.

public sealed partial record SimMinion
{
    /// <summary>
    /// TAG_SCRIPT_DATA_NUM_3/4 at capture: a second pair of numbers the card text shows (Turquoise Skitterer and Ravaging
    /// Scorpid: the "+X/+Y" of "your Beetles have +X/+Y this game", 5/5 and 4/4 in a real log of 2026-11-15, while NUM_1/2
    /// hold the Beetle's 2/2). 0 when the log sent none.
    /// </summary>
    public int ScriptData3 { get; init; }

    public int ScriptData4 { get; init; }

    /// <summary>
    /// Timewarped Nellie's Ship (BG34_Giant_074t): the Pirates the local player Discovered for its crew on the latest turn
    /// (one, golden two), as fresh cards. The local player's only (the opponent's pick is hidden); empty when the ship has
    /// no crew yet or the capture did not record it.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SimMinion> Crew { get; init; } = Array.Empty<SimMinion>();
}

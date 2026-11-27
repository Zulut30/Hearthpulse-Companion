using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.SourceCapture;

public sealed partial class SourceCaptureRecorder
{
    private int _pendingInputGaps;

    /// <summary>
    /// Classifies Power.log grammar only. Raw text is never retained, sanitized or serialized; supported facts still come
    /// exclusively from IPowerListener. Known grammar the format leaves out on purpose (source v4: option lists, display
    /// metadata, sub-spells, power-list headers, deck shuffles, tags cached for a dormant minion, what the client sends)
    /// is neither an observation nor a gap; only grammar the recorder does not know is unsupported. PowerTaskList display
    /// replay is intentionally outside the format.
    /// </summary>
    internal void ObservePowerLine(string line)
    {
        if (!_enabled || _catchingUp || _stopped || _game is null) return;
        var supported = false;
        if (PowerLine.TrySplit(line, out _, out var methodRange, out var bodyRange))
        {
            var method = line.AsSpan(methodRange);
            var body = line[bodyRange].Trim();
            supported = method switch
            {
                "DebugPrintPower" => body is "CREATE_GAME" or "BLOCK_END" || body.StartsWith("GameEntity EntityID=", StringComparison.Ordinal)
                    || body.StartsWith("Player EntityID=", StringComparison.Ordinal) || body.StartsWith("FULL_ENTITY - ", StringComparison.Ordinal)
                    || body.StartsWith("SHOW_ENTITY - ", StringComparison.Ordinal) || body.StartsWith("CHANGE_ENTITY - ", StringComparison.Ordinal)
                    || body.StartsWith("HIDE_ENTITY - ", StringComparison.Ordinal) || body.StartsWith("BLOCK_START ", StringComparison.Ordinal)
                    || body.StartsWith("TAG_CHANGE ", StringComparison.Ordinal) && PowerLine.TryTagChange(body, out _, out _, out _)
                    || body.StartsWith("tag=", StringComparison.Ordinal) && PowerLine.TryTagValue(body, out _, out _)
                    || LeftOut(body),
                // The options the client lists for the player and the headers of power lists: what the player could
                // do, never what happened, which arrives as Power lines.
                "DebugPrintGame" or "DebugPrintOptions" or "DebugPrintPowerList" => true,
                "DebugPrintEntityChoices" or "DebugPrintEntitiesChosen" => body.StartsWith("id=", StringComparison.Ordinal)
                    || body.StartsWith("Source=", StringComparison.Ordinal) || body.StartsWith("Entities[", StringComparison.Ordinal),
                _ => false,
            };
        }
        else
        {
            var at = line.IndexOf("GameState.", StringComparison.Ordinal);
            if (at < 0) return;
            supported = Sent(line.AsSpan(at + "GameState.".Length));
        }
        if (supported) return;
        if (_capture is null) _pendingInputGaps++;
        else NoteGap("unsupported");
    }

    // Display metadata (META_DATA and its Info lines), sub-spell effects (SUB_SPELL_START with its Source and Targets lines,
    // SUB_SPELL_END), deck shuffles and the tags cached for a dormant minion: the state they accompany arrives as tag changes.
    private static bool LeftOut(string body) =>
        body == "SUB_SPELL_END" || body.StartsWith("META_DATA - ", StringComparison.Ordinal) || body.StartsWith("Info[", StringComparison.Ordinal)
        || body.StartsWith("SUB_SPELL_START - ", StringComparison.Ordinal) || body.StartsWith("Source = ", StringComparison.Ordinal)
        || body.StartsWith("Targets[", StringComparison.Ordinal) || body.StartsWith("SHUFFLE_DECK ", StringComparison.Ordinal)
        || body.StartsWith("CACHED_TAG_FOR_DORMANT_CHANGE ", StringComparison.Ordinal);

    // The option or choice the client sends, and a choice it queues: the player's input, whose outcome is logged as Power
    // lines and chosen entities.
    private static bool Sent(ReadOnlySpan<char> method) =>
        method.StartsWith("SendOption() - ", StringComparison.Ordinal) || method.StartsWith("SendChoices() - ", StringComparison.Ordinal)
        || method.StartsWith("OnEntityChoices() - ", StringComparison.Ordinal);
}

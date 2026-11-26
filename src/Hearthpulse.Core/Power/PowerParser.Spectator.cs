namespace Hearthpulse.Core.Power;

/// <summary>
/// Spectator mode: the games the client shows while the player watches a friend are somebody else's and are never
/// recorded (docs/local-data.md). The client marks it with lines of their own; the marker text is read from the log
/// format and not yet confirmed by a kept real log (hearthstone-context, reference/power-log.md).
/// </summary>
public sealed partial class PowerParser
{
    /// <summary>Text every spectator marker line holds: one cheap search before the markers themselves.</summary>
    internal const string SpectatorHint = "Spectat";

    /// <summary>
    /// True from a spectator marker line ("Begin Spectating", "Start Spectator") until "End Spectator": the games the client
    /// shows meanwhile are somebody else's. It outlives CREATE_GAME, and each new game takes it as
    /// <see cref="GameState.IsSpectated"/>.
    /// </summary>
    public bool Spectating { get; private set; }

    /// <summary>
    /// Which spectator marker <paramref name="line"/> is, for a line that is not a GameState print. The client writes the
    /// markers as lines of their own; the prints of game lines (GameState, PowerTaskList) carry player and card names, so
    /// a "Spectat" inside them is never a marker.
    /// </summary>
    internal static SpectatorMarker SpectatorMarkerOf(string line)
    {
        if (!line.Contains(SpectatorHint, StringComparison.Ordinal) || line.Contains("DebugPrint", StringComparison.Ordinal))
        {
            return SpectatorMarker.None;
        }

        if (line.Contains("Begin Spectating", StringComparison.Ordinal) || line.Contains("Start Spectator", StringComparison.Ordinal))
        {
            return SpectatorMarker.Begin;
        }

        return line.Contains("End Spectator", StringComparison.Ordinal) ? SpectatorMarker.End : SpectatorMarker.None;
    }

    /// <summary>A line that is not a GameState print: a spectator marker, or the client's on-screen replay.</summary>
    private void ReadOtherLine(string line)
    {
        NoteSpectatorMarker(line);
        TrackCombatOnScreen(line);
    }

    private void NoteSpectatorMarker(string line)
    {
        switch (SpectatorMarkerOf(line))
        {
            case SpectatorMarker.Begin:
                Spectating = true;
                // Written after the game's CREATE_GAME: that game is watched too, unless it was already over for us (our
                // own Battlegrounds game we left to watch a friend's).
                if (Game is { IsComplete: false } game && !game.LocalPlayerFinished)
                {
                    game.IsSpectated = true;
                }

                break;
            case SpectatorMarker.End:
                // The game being watched stays somebody else's; only the next one is ours again.
                Spectating = false;
                break;
        }
    }
}

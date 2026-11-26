using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI;

/// <summary>
/// The "Read data from the game client" switch (Settings → Data, <c>AppSettings.ReadGameMemory</c>,
/// docs/adr/0006-client-read-only.md): the session follows it the moment it moves, not only on the next settings change
/// (Avalonia writes a switch back to its setting after the change event the window passes on).
/// </summary>
public sealed partial class AppHost
{
    partial void FollowGameMemory() => Main.GameMemoryChanged += () =>
    {
        Session.SetGameMemoryEnabled(Settings.ReadGameMemory);
        _settingsDebounce.Kick();
    };
}

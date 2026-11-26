using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App;

/// <summary>
/// The "Read data from the game client" switch (Settings → Data, <c>AppSettings.ReadGameMemory</c>,
/// docs/adr/0006-client-read-only.md): the session follows it the moment it moves, and the settings are saved.
/// </summary>
public partial class App
{
    partial void FollowGameMemory() => _mainViewModel.GameMemoryChanged += () =>
    {
        _session.SetGameMemoryEnabled(_settings.ReadGameMemory);
        _settingsDebounce.Kick();
    };
}

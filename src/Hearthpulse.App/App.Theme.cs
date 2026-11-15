using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;

namespace Hearthpulse.App;

/// <summary>The theme in the host: building it from the settings, and rebuilding the windows after a change.</summary>
public partial class App
{
    private ThemeSpec CurrentTheme()
    {
        // Off-screen renders can preview a theme without touching the saved settings.
        var name = _renderDir is not null && Environment.GetEnvironmentVariable("HEARTHPULSE_THEME") is { Length: > 0 } forced ? forced : _settings.Theme;
        return ThemePalette.FromSettings(_settings, name);
    }

    private bool ApplyTheme()
    {
        try
        {
            Motion.Reduced = _settings.ReduceMotion;
            ThemeEngine.Apply(this, CurrentTheme(), ThemeLook.FromSettings(_settings));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diag.Error("Theme could not be built; keeping the current colours", ex);
            return false;
        }
    }

    /// <summary>Windows take their colours when they are created, so a new theme rebuilds both in place.</summary>
    private void OnThemeChanged()
    {
        _settings.Save();
        if (!ApplyTheme())
        {
            return;
        }

        // A window hidden in the tray (or not built yet) takes the new colours when it is next opened.
        if (_main is { } oldMain)
        {
            _releaseMain.Stop();
            _mainLife.Release();
            if (oldMain.IsVisible)
            {
                ShowMain();
            }

            oldMain.CloseForReal();
            SyncWindowInUse();
            _session.RequestMemoryTrimWhenWindowAway(windowLetGo: true);
        }

        var oldOverlay = _overlay;
        _overlay = CreateOverlay();
        _overlay.Suppressed = oldOverlay.Suppressed;
        oldOverlay.Close();
        _overlay.Show();
        _stream?.Recreate();
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main;
using Hearthpulse.Core;

namespace Hearthpulse.App;

/// <summary>The theme in the host: building it from the settings, and rebuilding the windows after a change.</summary>
public partial class App
{
    /// <summary>
    /// A theme change whose new main window is shown but not on the screen yet (cloaked until its first frames are
    /// drawn); the windows it replaces stay on the screen until then. Null otherwise.
    /// </summary>
    private ThemeSwap? _themeSwap;

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

    /// <summary>
    /// Windows take their colours when they are created, so a new theme rebuilds both. The main window on the screen is
    /// swapped without a seam (<see cref="SwapMain"/>); one hidden in the tray (or not built yet) takes the new colours
    /// when it is next opened.
    /// </summary>
    private void OnThemeChanged()
    {
        _settings.Save();
        Motion.Reduced = _settings.ReduceMotion;
        // Taken before the theme changes: a few brushes are dynamic and would change under the picture. A change made
        // before the last one reached the screen keeps what the player still sees: the last one's view.
        var view = _themeSwap?.View ?? TakeShownView();
        if (!ApplyTheme())
        {
            if (_themeSwap is null && view is not null)
            {
                _main.Uncover(fade: false);
            }

            return;
        }

        if (_main is { } oldMain)
        {
            _releaseMain.Stop();
            _mainLife.Release();
            if (view is not null)
            {
                SwapMain(oldMain, view);
            }
            else
            {
                if (oldMain.IsVisible)
                {
                    ShowMain();
                }

                oldMain.CloseForReal();
                SyncWindowInUse();
                _session.RequestMemoryTrimWhenWindowAway(windowLetGo: true);
            }
        }

        var oldOverlay = _overlay;
        _overlay = CreateOverlay();
        _overlay.Suppressed = oldOverlay.Suppressed;
        oldOverlay.Close();
        _overlay.Show();
        _stream?.Recreate();
    }

    /// <summary>
    /// The main window as the player sees it now, covered with its own picture (with motion on) so nothing changes on it
    /// until it goes; null when it is not on the screen (hidden in the tray, minimized, or an off-screen render).
    /// </summary>
    private MainWindowView? TakeShownView()
    {
        if (_renderDir is not null || _main is not { IsVisible: true, WindowState: not WindowState.Minimized } main)
        {
            return null;
        }

        var view = main.TakeView(Motion.On, Keyboard.FocusedElement);
        if (view.Picture is { } picture)
        {
            main.Cover(picture);
        }

        return view;
    }

    /// <summary>
    /// Builds the new main window where <paramref name="leaving"/> is and shows it off the screen, on the same page, tab,
    /// scroll and state, under the old picture; <see cref="RevealSwapped"/> puts it on the screen once it has drawn.
    /// </summary>
    private void SwapMain(MainWindow leaving, MainWindowView view)
    {
        // The window of a change that never reached the screen goes with the ones it was to replace.
        var outgoing = _themeSwap?.Leaving ?? [];
        _themeSwap?.Stop();
        outgoing.Add(leaving);
        leaving.LeaveWithoutAnimation();

        // The page it opens on is the one on the screen: no entrance plays under the picture.
        var reduced = Motion.Reduced;
        Motion.Reduced = true;
        MainWindow main;
        try
        {
            main = _mainLife.Open(CreateMainWindow);
            main.ShowSwapped(_mainLife.Place is { Maximized: true }, view);
        }
        finally
        {
            Motion.Reduced = reduced;
        }

        var swap = new ThemeSwap(main, view, outgoing);
        _themeSwap = swap;
        swap.Start(() => RevealSwapped(swap));
    }

    /// <summary>The new window has drawn: it takes the screen, the old ones close, and the picture fades out.</summary>
    private void RevealSwapped(ThemeSwap swap)
    {
        if (!ReferenceEquals(_themeSwap, swap))
        {
            return;
        }

        _themeSwap = null;
        swap.Stop();
        swap.Incoming.RevealSwapped(swap.View);
        foreach (var old in swap.Leaving)
        {
            old.CloseForReal();
        }

        SyncWindowInUse();
        _session.RequestMemoryTrimWhenWindowAway(windowLetGo: true);
        swap.Incoming.Uncover(fade: Motion.On);
    }

    /// <summary>A rebuilt main window waiting for its first frames, the view it opens on, and the windows it replaces.</summary>
    private sealed class ThemeSwap(MainWindow incoming, MainWindowView view, List<MainWindow> leaving)
    {
        /// <summary>
        /// Frames WPF draws after the first one before the window is shown (WPF paces its frames by what Windows took, so
        /// by then the first is on the window's surface; <see cref="MainWindow.RevealSwapped"/> waits one composed frame
        /// more). The live probe measured 15-60 ms from the first frame to the reveal.
        /// </summary>
        private const int FramesToWait = 2;

        /// <summary>If the frames never come (they take a few hundredths of a second), the window is shown anyway.</summary>
        private readonly DispatcherTimer _patience = new() { Interval = TimeSpan.FromSeconds(2) };
        private Action? _reveal;
        private int _frames;
        private TimeSpan _lastFrame = TimeSpan.MinValue;

        public MainWindow Incoming => incoming;

        public MainWindowView View => view;

        public List<MainWindow> Leaving => leaving;

        public void Start(Action reveal)
        {
            _reveal = reveal;
            incoming.ContentRendered += OnRendered;
            _patience.Tick += OnPatience;
            _patience.Start();
        }

        public void Stop()
        {
            _patience.Stop();
            _patience.Tick -= OnPatience;
            incoming.ContentRendered -= OnRendered;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnRendered(object? sender, EventArgs e)
        {
            incoming.ContentRendered -= OnRendered;
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            // Rendering can be raised more than once for one frame: a frame counts once, by its time.
            if (e is not RenderingEventArgs { RenderingTime: var time } || time == _lastFrame)
            {
                return;
            }

            _lastFrame = time;
            if (++_frames >= FramesToWait)
            {
                _reveal?.Invoke();
            }
        }

        private void OnPatience(object? sender, EventArgs e) => _reveal?.Invoke();
    }
}

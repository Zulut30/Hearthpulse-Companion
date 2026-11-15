using System.Windows;
using System.Windows.Threading;
using Hearthpulse.App.Main;
using Hearthpulse.Core;

namespace Hearthpulse.App;

/// <summary>The main window's life in the host: built when first shown, let go while it waits in the tray, built again where it was.</summary>
public partial class App
{
    /// <summary>
    /// The main window: built when first shown (start minimized: from the tray), let go after a minute hidden in the tray
    /// (<see cref="ReleaseMain"/>), forgotten once closed for good; <see cref="ShowMain"/> builds it again where it was.
    /// </summary>
    private readonly MainWindowLife<MainWindow, MainPlace> _mainLife = new(main => main.Place);

    /// <summary>Null while there is no window to show (see <see cref="_mainLife"/>). Render scenes always have one.</summary>
    private MainWindow _main => _mainLife.Window!;

    /// <summary>While the window waits in the tray: is it time to let it go? (A game in progress puts it off.)</summary>
    private readonly DispatcherTimer _releaseMain = new() { Interval = TimeSpan.FromSeconds(10) };

    /// <summary>A new main window, on the page, tabs and bounds of <paramref name="place"/> when given.</summary>
    private MainWindow CreateMainWindow(MainPlace? place = null)
    {
        var main = new MainWindow(_mainViewModel);
        main.SettingsChanged += OnSettingsChanged;
        main.ReimportRequested += () => ImportLogsAsync(force: true).Forget("Log import");
        main.ArrangeOverlayRequested += () => _overlayViewModel.LayoutEditing = !_overlayViewModel.LayoutEditing;
        main.IsVisibleChanged += OnMainVisibleChanged;
        main.Closing += (_, e) =>
        {
            if (!e.Cancel)
            {
                _mainLife.Closing(main);
            }
        };
        main.Closed += (_, _) => OnMainClosed(main);
        main.StateChanged += (_, _) =>
        {
            if (!ReferenceEquals(main, _main))
            {
                return;
            }

            SyncWindowInUse();
            // Building and leaving pages leaves garbage an idle app would never collect.
            if (main.WindowState == WindowState.Minimized)
            {
                _session.RequestMemoryTrimWhenWindowAway();
            }
        };
        if (place is not null)
        {
            main.MoveTo(place);
        }

        return main;
    }

    private void OnMainVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _main) || _renderDir is not null)
        {
            return;
        }

        SyncWindowInUse();
        // Hidden in the tray: no collection of its own; the one after the release takes the pages and the rest.
        if (_main.IsVisible)
        {
            _mainLife.Shown(_main);
            _releaseMain.Stop();
        }
        else
        {
            _mainLife.Hidden(_main, DateTime.UtcNow);
            _releaseMain.Start();
        }
    }

    /// <summary>
    /// Closed for good (no tray to hide it in): forgotten, so the tray's Open builds a new one instead of showing a closed
    /// window (which threw). Its pages are garbage now.
    /// </summary>
    private void OnMainClosed(MainWindow main)
    {
        if (!ReferenceEquals(main, _main))
        {
            return;
        }

        _releaseMain.Stop();
        _mainLife.Closed(main);
        SyncWindowInUse();
        _session.RequestMemoryTrimWhenWindowAway(windowLetGo: true);
    }

    /// <summary>A collection asked for by a meta load waits while the window is on screen or waiting in the tray.</summary>
    private void SyncWindowInUse() => _session.MainWindowInUse = _main is { WindowState: not WindowState.Minimized };

    /// <summary>
    /// The window stayed hidden in the tray for a minute, and no game is being played: it is let go with every page it
    /// built, and built again on the same page when opened (about half a second). Bench, all pages and tabs open, then
    /// the memory trim: private 217–222 → 154–157 MB.
    /// </summary>
    private void ReleaseMain()
    {
        if (!_mainLife.Waiting)
        {
            _releaseMain.Stop();
            return;
        }

        if (!_mainLife.ReleaseDue(DateTime.UtcNow, _mainViewModel.InGame) || _main is not { IsVisible: false })
        {
            return;
        }

        _releaseMain.Stop();
        var main = _mainLife.Release()!;
        main.CloseForReal();
        Diag.Info("Main window let go while hidden in the tray");
        SyncWindowInUse();
        _session.RequestMemoryTrimWhenWindowAway(windowLetGo: true);
    }

    private void ShowMain()
    {
        _releaseMain.Stop();
        var built = _main is null;
        var main = _mainLife.Open(CreateMainWindow);
        main.Show();
        if (built && _mainLife.Place is { Maximized: true })
        {
            main.WindowState = WindowState.Maximized;
        }
        else if (!built && main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
    }
}

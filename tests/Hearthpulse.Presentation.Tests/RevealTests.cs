using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF reveal in place (<see cref="Motion.RevealOnShowProperty"/>, <see cref="Motion.Reveal"/>): a view stacked
/// with others that comes is held unseen until laid out and then fades in, the one it replaces goes at once; however
/// fast the toggles come, one view is left, opaque, in place and clickable; and no reveal plays on the window's first
/// show, under a parent shown or held unseen, or with motion off. The real window's Settings and Battlegrounds tabs
/// are clicked through fast. The Avalonia twin is in Hearthpulse.Tests (RevealTests).
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class RevealTests
{
    [Fact]
    public void A_view_that_comes_is_held_until_laid_out_then_fades_in_and_the_one_it_replaces_goes_at_once() => Run(() =>
    {
        var (window, _, views) = Stage();
        try
        {
            Toggle(views, 1);
            // Before the layout: the old view gone, the new one drawn unseen and low, no motion started yet.
            Assert.Equal(Visibility.Collapsed, views[0].Visibility);
            Assert.Equal(1, views[0].Opacity);
            Assert.Equal(Visibility.Visible, views[1].Visibility);
            Assert.Equal(0, views[1].Opacity);
            Assert.Equal(Motion.TabRise, ((TranslateTransform)views[1].RenderTransform).Y);
            Assert.False(views[1].HasAnimatedProperties);

            // After it (Loaded priority, behind the layout and render pass): the fade runs.
            Pump(DispatcherPriority.Background);
            Assert.True(views[1].HasAnimatedProperties);
            Settle();
            AssertOnly(views, views[1]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Fast_toggles_settle_on_one_whole_clickable_view() => Run(() =>
    {
        var (window, _, views) = Stage();
        try
        {
            // A -> B -> A before any motion starts.
            Toggle(views, 1);
            Toggle(views, 0);
            Settle();
            AssertOnly(views, views[0]);

            // A -> B, C while B fades in, then back to A mid-fade, and D -> A in one turn.
            Toggle(views, 1);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(60));
            Toggle(views, 2);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(40));
            Toggle(views, 0);
            Toggle(views, 3);
            Toggle(views, 0);
            Assert.All(views, v => Assert.True(v.IsHitTestVisible));
            Settle();
            AssertOnly(views, views[0]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void No_reveal_on_the_first_show_under_a_parent_shown_or_held_or_with_motion_off() => Run(() =>
    {
        // Changed before the window is on screen (a window opening on a saved tab): shown as it is.
        var (window, cell, views) = Stage(show: false);
        try
        {
            Toggle(views, 2);
            window.Show();
            Pump(DispatcherPriority.ApplicationIdle);
            AssertAtRest(views[2]);

            // A view shown while its parent is hidden, and the parent shown after: the parent's own motion carries it.
            cell.Visibility = Visibility.Collapsed;
            Toggle(views, 3);
            cell.Visibility = Visibility.Visible;
            Pump(DispatcherPriority.ApplicationIdle);
            AssertAtRest(views[3]);

            // Under a parent held unseen (a page coming in): no motion of its own.
            cell.Opacity = 0;
            Toggle(views, 1);
            AssertAtRest(views[1]);
            cell.Opacity = 1;

            // Motion off: at once.
            Motion.RevealMotion = false;
            Toggle(views, 0);
            AssertAtRest(views[0]);
            Settle();
            AssertOnly(views, views[0]);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The real window: the Settings tabs and the Battlegrounds tabs (My stats and the tier lists stacked, the tier
    /// cards cross-fading in their cell) clicked through fast settle on the tab the window reads back, whole.
    /// </summary>
    [Fact]
    public void The_wpf_window_tabs_settle_on_one_whole_view_after_fast_clicks() => Run(() =>
    {
        var model = new WpfMain(new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db")), new AppSettings(), TestApp.Data);
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump(DispatcherPriority.ApplicationIdle);
        try
        {
            window.ShowPage("Settings");
            Settle();
            FrameworkElement[] settings = [window.SettingsGeneral, window.SettingsAppearance, window.SettingsOverlay, window.SettingsTracker, window.SettingsStream, window.SettingsAccount, window.SettingsData, window.SettingsAbout];
            foreach (var tab in new[] { "Overlay", "Tracker", "General", "Data", "About", "Appearance", "Overlay" })
            {
                window.ShowSettingsTab(tab);
                Pump(DispatcherPriority.Background);
            }

            window.ShowSettingsTab("Account");
            window.ShowSettingsTab("Data");
            Settle();
            AssertOnly(settings, window.SettingsData);
            Assert.Equal("Data", model.Settings.SettingsTab);

            window.ShowPage("Battlegrounds");
            Settle();
            var page = window.PageBattlegrounds;
            foreach (var tab in new[] { "Heroes", "Comps", "Mine", "Trinkets", "Cards", "Heroes", "Comps" })
            {
                window.BattlegroundsTab = tab;
                Pump(DispatcherPriority.Background);
                Wait(TimeSpan.FromMilliseconds(30));
            }

            window.BattlegroundsTab = "Mine";
            window.BattlegroundsTab = "Trinkets";
            Settle();
            Assert.Equal("Trinkets", window.BattlegroundsTab);
            AssertOnly([page.BgStats, page.BgTiers], page.BgTiers);
            var cards = ((Grid)page.BgTiers.Content).Children.OfType<FrameworkElement>().ToList();
            Assert.Equal(4, cards.Count);
            AssertOnly(cards, Assert.Single(cards, c => c.Name == "TrinketTierCard"));

            window.BattlegroundsTab = "Mine";
            Settle();
            AssertOnly([page.BgStats, page.BgTiers], page.BgStats);
        }
        finally
        {
            window.CloseForReal();
            Pump(DispatcherPriority.ApplicationIdle);
        }
    });

    /// <summary>Runs <paramref name="body"/> on a fresh WPF thread with reveals animated there.</summary>
    private static void Run(Action body) => WpfThread.RunFresh(() =>
    {
        Motion.RevealMotion = true;
        try
        {
            body();
        }
        finally
        {
            Motion.RevealMotion = null;
        }
    });

    /// <summary>Four views stacked in one panel, the first shown, each revealing itself.</summary>
    private static (Window Window, StackPanel Cell, FrameworkElement[] Views) Stage(bool show = true)
    {
        FrameworkElement[] views = [.. Enumerable.Range(0, 4).Select(i => new Border { Height = 40, Background = Brushes.Gray, Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed })];
        var cell = new StackPanel();
        foreach (var view in views)
        {
            Motion.SetRevealOnShow(view, true);
            cell.Children.Add(view);
        }

        var window = new Window
        {
            Content = cell, Width = 400, Height = 300,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
        };
        if (show)
        {
            window.Show();
            Pump(DispatcherPriority.ApplicationIdle);
        }

        return (window, cell, views);
    }

    /// <summary>Shows view <paramref name="index"/> and hides the others, the new one first, as the tab handlers do.</summary>
    private static void Toggle(FrameworkElement[] views, int index)
    {
        views[index].Visibility = Visibility.Visible;
        foreach (var other in views.Where((_, i) => i != index))
        {
            other.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Exactly one view shown, at rest: opaque, in place and clickable; the others collapsed and at rest.</summary>
    private static void AssertOnly(IReadOnlyList<FrameworkElement> views, FrameworkElement shown)
    {
        foreach (var view in views)
        {
            Assert.Equal(view == shown ? Visibility.Visible : Visibility.Collapsed, view.Visibility);
            Assert.Equal(1, view.Opacity);
            Assert.True(view.IsHitTestVisible);
            Assert.Equal(0, (view.RenderTransform as TranslateTransform)?.Y ?? 0);
        }
    }

    /// <summary>Shown with no motion on it, none to come.</summary>
    private static void AssertAtRest(FrameworkElement view)
    {
        Assert.Equal(Visibility.Visible, view.Visibility);
        Assert.Equal(1, view.Opacity);
        Assert.Equal(0, (view.RenderTransform as TranslateTransform)?.Y ?? 0);
        Pump(DispatcherPriority.Background);
        Assert.False(view.HasAnimatedProperties);
    }

    /// <summary>Lets every reveal finish: its layout, its fade (Motion.Enter) and some room.</summary>
    private static void Settle()
    {
        Pump(DispatcherPriority.ApplicationIdle);
        Wait(Motion.Enter + TimeSpan.FromMilliseconds(400));
        Pump(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Runs the dispatcher (layout, render, animation ticks) for <paramref name="time"/>.</summary>
    private static void Wait(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(time, DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    /// <summary>Runs the dispatcher down to <paramref name="priority"/>.</summary>
    private static void Pump(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

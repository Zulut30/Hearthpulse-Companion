using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia reveal in place (<see cref="Motion.RevealOnShowProperty"/>, <see cref="Motion.Reveal"/>, the twin of the
/// WPF RevealTests): a view stacked with others that comes is held unseen until laid out and then fades in, the one it
/// replaces goes at once; however fast the toggles come, one view is left, opaque, in place and clickable; and no reveal
/// plays on the view's first show, under a parent shown or held unseen, or with motion off. Reveals are switched on for
/// the test (the headless platform turns the system's motion off, PoolInput.NoAnimationsSwitch).
/// </summary>
public sealed class RevealTests
{
    [Fact]
    public void A_view_that_comes_is_held_until_laid_out_and_fast_toggles_settle_on_one_whole_view() => Run(() =>
    {
        var (window, _, views) = Stage();
        try
        {
            Toggle(views, 1);
            // Before the layout: the old view gone, the new one held unseen and low.
            Assert.False(views[0].IsVisible);
            Assert.Equal(1, views[0].Opacity);
            Assert.True(views[1].IsVisible);
            Assert.Equal(0, views[1].Opacity);
            Assert.Equal(Motion.TabRise, ((TranslateTransform)views[1].RenderTransform!).Y);
            Settle();
            AssertOnly(views, views[1]);

            // B -> C -> B before any motion starts.
            Toggle(views, 2);
            Toggle(views, 1);
            Settle();
            AssertOnly(views, views[1]);

            // B -> C, D while C fades in, then back to B mid-fade, and A -> B in one turn.
            Toggle(views, 2);
            Wait(TimeSpan.FromMilliseconds(60));
            Toggle(views, 3);
            Wait(TimeSpan.FromMilliseconds(40));
            Toggle(views, 1);
            Toggle(views, 0);
            Toggle(views, 1);
            Assert.All(views, v => Assert.True(v.IsHitTestVisible));
            Settle();
            AssertOnly(views, views[1]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void No_reveal_on_the_first_show_under_a_parent_shown_or_held_or_with_motion_off() => Run(() =>
    {
        // Changed before the view is on screen (a window opening on a saved tab): shown as it is.
        var (window, cell, views) = Stage(show: false);
        try
        {
            Toggle(views, 2);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AssertAtRest(views[2]);

            // A view shown while its parent is hidden, and the parent shown after: the parent's own motion carries it.
            cell.IsVisible = false;
            Toggle(views, 3);
            cell.IsVisible = true;
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
            AssertOnly(views, views[0]);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The real view: the Settings tabs, the Battlegrounds tabs (My stats and the tier lists stacked) and the Standard
    /// tabs clicked through fast settle on the tab the view reads back, whole.
    /// </summary>
    [Fact]
    public void The_view_tabs_settle_on_one_whole_view_after_fast_clicks() => Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-reveal") + ".db"), new AppSettings(), TestDataFolder.App);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 800 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Settings");
            PageRowsPanelAvaloniaTests.Settle(window);
            Control[] settings = [view.SettingsGeneral, view.SettingsAppearance, view.SettingsOverlay, view.SettingsTracker, view.SettingsStream, view.SettingsAccount, view.SettingsData, view.SettingsAbout];
            foreach (var tab in new[] { "Overlay", "Tracker", "General", "Data", "About", "Appearance", "Overlay" })
            {
                view.ShowSettingsTab(tab);
                Wait(TimeSpan.FromMilliseconds(30));
            }

            view.ShowSettingsTab("Account");
            view.ShowSettingsTab("Data");
            Settle();
            AssertOnly(settings, view.SettingsData);
            Assert.Equal("Data", model.Settings.SettingsTab);

            view.ShowPage("Battlegrounds");
            PageRowsPanelAvaloniaTests.Settle(window);
            var page = view.PageBattlegrounds;
            foreach (var tab in new[] { "Heroes", "Comps", "Mine", "Trinkets", "Cards", "Mine", "Heroes" })
            {
                view.BattlegroundsTab = tab;
                Wait(TimeSpan.FromMilliseconds(30));
            }

            view.BattlegroundsTab = "Mine";
            Settle();
            AssertOnly([page.BgStats, page.BgTiers], page.BgStats);
            view.BattlegroundsTab = "Comps";
            Settle();
            AssertOnly([page.BgStats, page.BgTiers], page.BgTiers);

            view.ShowPage(ConstructedPage.Standard);
            PageRowsPanelAvaloniaTests.Settle(window);
            var constructed = view.PageConstructed;
            foreach (var tab in new[] { "Tiers", "Decks", "Top", "Matchups", "Decks" })
            {
                view.ShowConstructedTab(tab);
                Wait(TimeSpan.FromMilliseconds(30));
            }

            Settle();
            Assert.Equal("Decks", view.ConstructedTab);
            Assert.All(new Control[] { constructed.DeckColumns, constructed.CtStats }, AssertWhole);
            Assert.False(constructed.CtMeta.IsVisible);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Runs <paramref name="body"/> on the headless UI thread with reveals animated.</summary>
    private static void Run(Action body) => LocBindingTests.Headless.Run(() =>
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
    private static (Window Window, StackPanel Cell, Control[] Views) Stage(bool show = true)
    {
        Control[] views = [.. Enumerable.Range(0, 4).Select(i => new Border { Height = 40, Background = Brushes.Gray, IsVisible = i == 0 })];
        var cell = new StackPanel();
        foreach (var view in views)
        {
            Motion.SetRevealOnShow(view, true);
            cell.Children.Add(view);
        }

        var window = new Window { Content = cell, Width = 400, Height = 300 };
        if (show)
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        return (window, cell, views);
    }

    /// <summary>Shows view <paramref name="index"/> and hides the others, the new one first, as the tab handlers do.</summary>
    private static void Toggle(Control[] views, int index)
    {
        views[index].IsVisible = true;
        foreach (var other in views.Where((_, i) => i != index))
        {
            other.IsVisible = false;
        }
    }

    /// <summary>Exactly one view shown, at rest: opaque, in place and clickable; the others hidden and at rest.</summary>
    private static void AssertOnly(IReadOnlyList<Control> views, Control shown)
    {
        foreach (var view in views)
        {
            Assert.Equal(view == shown, view.IsVisible);
            Assert.Equal(1, view.Opacity);
            Assert.True(view.IsHitTestVisible);
            Assert.Equal(0, (view.RenderTransform as TranslateTransform)?.Y ?? 0);
        }
    }

    /// <summary>Shown, opaque, in place and clickable.</summary>
    private static void AssertWhole(Control view)
    {
        Assert.True(view.IsVisible);
        Assert.Equal(1, view.Opacity);
        Assert.True(view.IsHitTestVisible);
        Assert.Equal(0, (view.RenderTransform as TranslateTransform)?.Y ?? 0);
    }

    /// <summary>Shown with no motion on it, and none to come.</summary>
    private static void AssertAtRest(Control view)
    {
        AssertWhole(view);
        Wait(TimeSpan.FromMilliseconds(50));
        AssertWhole(view);
    }

    /// <summary>Lets every reveal finish: its layout, its fade (Motion.Enter) and some room.</summary>
    private static void Settle() => Wait(Motion.Enter + TimeSpan.FromMilliseconds(400));

    /// <summary>Runs the dispatcher, its timers and the animation clock with it, for <paramref name="time"/>.</summary>
    private static void Wait(TimeSpan time)
    {
        using var stop = new CancellationTokenSource();
        DispatcherTimer.RunOnce(stop.Cancel, time);
        Dispatcher.UIThread.MainLoop(stop.Token);
    }
}

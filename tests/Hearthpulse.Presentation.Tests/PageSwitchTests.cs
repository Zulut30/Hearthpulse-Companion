using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF page cross-fade (<see cref="PageSwitch"/>): the leaving page stays drawn but takes no clicks, the motion
/// starts only after the new page's layout, and however fast the switches come, the pages settle on one page, opaque,
/// in place and clickable. The Avalonia twin is in Hearthpulse.Tests (PageSwitchTests).
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class PageSwitchTests
{
    [Fact]
    public void The_leaving_page_stays_drawn_without_clicks_and_the_motion_waits_for_the_new_page_layout() => WpfThread.RunFresh(() =>
    {
        var (window, pages) = Stage();
        try
        {
            var flip = new PageSwitch(motion: () => true);
            flip.Show(pages[0], pages);
            Settle();
            Assert.Equal(1, pages[0].Opacity);

            flip.Show(pages[1], pages);
            // Before the layout: the old page fully drawn, the new one held unseen and low, no motion started yet.
            Assert.Equal(Visibility.Visible, pages[0].Visibility);
            Assert.Equal(1, pages[0].Opacity);
            Assert.False(pages[0].IsHitTestVisible);
            Assert.Equal(Visibility.Visible, pages[1].Visibility);
            Assert.Equal(0, pages[1].Opacity);
            Assert.Equal(Motion.Rise, ((TranslateTransform)pages[1].RenderTransform).Y);
            Assert.False(pages[0].HasAnimatedProperties);
            Assert.False(pages[1].HasAnimatedProperties);

            // After it (Loaded priority, behind the layout and render pass): both fades run.
            Pump(DispatcherPriority.Background);
            Assert.True(pages[0].HasAnimatedProperties);
            Assert.True(pages[1].HasAnimatedProperties);

            Settle();
            AssertOnly(pages, pages[1]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Fast_switches_back_and_forth_and_mid_fade_settle_on_one_whole_page() => WpfThread.RunFresh(() =>
    {
        var (window, pages) = Stage();
        try
        {
            var flip = new PageSwitch(motion: () => true);
            flip.Show(pages[0], pages);
            Settle();

            // A -> B -> A before any motion starts.
            flip.Show(pages[1], pages);
            flip.Show(pages[0], pages);
            Settle();
            AssertOnly(pages, pages[0]);

            // A -> B, then C while B comes in and A goes out, then back to A mid-fade.
            flip.Show(pages[1], pages);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(60));
            flip.Show(pages[2], pages);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(40));
            flip.Show(pages[0], pages);
            Assert.True(pages[0].IsHitTestVisible);
            Assert.All(pages.Where(p => p != pages[0]), p => Assert.False(p.IsHitTestVisible && p.Visibility == Visibility.Visible));
            Settle();
            AssertOnly(pages, pages[0]);
            Assert.Same(pages[0], flip.Current);

            // The same page again changes nothing; the started callback still runs.
            var started = 0;
            flip.Show(pages[0], pages, () => started++);
            Assert.Equal(1, started);
            AssertOnly(pages, pages[0]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_motion_off_a_switch_is_instant() => WpfThread.RunFresh(() =>
    {
        var (window, pages) = Stage();
        try
        {
            var flip = new PageSwitch(motion: () => false);
            flip.Show(pages[0], pages);
            var started = 0;
            flip.Show(pages[2], pages, () => started++);
            Assert.Equal(1, started);
            AssertOnly(pages, pages[2]);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The real window, its pages and the deck and match pages standing in, clicked through fast: the pages settle on
    /// the one the window reads back, with the motion the PC has (on where Windows animates).
    /// </summary>
    [Fact]
    public void The_wpf_window_settles_on_one_page_after_fast_navigation() => WpfThread.RunFresh(() =>
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        Assert.True(history.Add(new GameRecord
        {
            Key = "page-switch-game",
            StartedAt = new DateTime(2026, 11, 18, 10, 0, 0),
            EndedAt = new DateTime(2026, 11, 18, 10, 5, 0),
            Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
            PlayerClass = "MAGE", OpponentClass = "WARRIOR", DeckName = "First deck",
        }));
        var model = new WpfMain(history, new AppSettings(), TestApp.Data);
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
            foreach (var page in new[] { "Battlegrounds", "Overview", "Battlegrounds", "Settings", "History", ConstructedPage.Wild, "Arena", "Overview" })
            {
                window.ShowPage(page);
            }

            Settle();
            AssertWindowOn(window, "PageOverview");

            // Mid-fade back and forth, then a deck page standing in for Standard and Back from it.
            window.ShowPage("History");
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(50));
            window.ShowPage(ConstructedPage.Standard);
            Pump(DispatcherPriority.Background);
            window.ShowPage("History");
            window.ShowPage(ConstructedPage.Standard);
            Settle();
            AssertWindowOn(window, "PageConstructed");

            model.OpenSampleDeck(CollectionContractTests.SampleDeck());
            Assert.True(model.DeckOpen);
            Pump(DispatcherPriority.Background);
            model.CloseDeck();
            model.OpenSampleDeck(CollectionContractTests.SampleDeck());
            Settle();
            AssertWindowOn(window, "PageDeck");

            model.CloseDeck();
            window.ShowPage("Settings");
            window.ShowPage(ConstructedPage.Standard);
            Settle();
            AssertWindowOn(window, "PageConstructed");
            Assert.Equal(ConstructedPage.Standard, window.CurrentPage);
        }
        finally
        {
            window.CloseForReal();
            Pump(DispatcherPriority.ApplicationIdle);
        }
    });

    private static (Window Window, FrameworkElement[] Pages) Stage()
    {
        FrameworkElement[] pages = [.. Enumerable.Range(0, 4).Select(i => new Border { Background = Brushes.Gray, Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed })];
        var cell = new Grid();
        foreach (var page in pages)
        {
            cell.Children.Add(page);
        }

        var window = new Window
        {
            Content = cell, Width = 400, Height = 300,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump(DispatcherPriority.ApplicationIdle);
        return (window, pages);
    }

    /// <summary>Exactly one page shown, at rest: opaque, in place and clickable; the others collapsed and at rest.</summary>
    private static void AssertOnly(IReadOnlyList<FrameworkElement> pages, FrameworkElement shown)
    {
        foreach (var page in pages)
        {
            Assert.Equal(page == shown ? Visibility.Visible : Visibility.Collapsed, page.Visibility);
            Assert.Equal(1, page.Opacity);
            Assert.True(page.IsHitTestVisible);
            Assert.Equal(0, (page.RenderTransform as TranslateTransform)?.Y ?? 0);
        }
    }

    private static void AssertWindowOn(WpfWindow window, string name)
    {
        string[] names = ["PageOverview", "PageBattlegrounds", "PageConstructed", "PageDeck", "PageCard", "PageArena", "PageBrawl", "PageHistory", "PageMatch", "PageSettings"];
        var pages = names.Select(n => (Name: n, Page: Page(window, n))).Where(p => p.Page is not null).ToList();
        var shown = Assert.Single(pages, p => p.Page!.Visibility == Visibility.Visible);
        Assert.Equal(name, shown.Name);
        Assert.Equal(1, shown.Page!.Opacity);
        Assert.True(shown.Page.IsHitTestVisible);
        Assert.Equal(0, (shown.Page.RenderTransform as TranslateTransform)?.Y ?? 0);
    }

    /// <summary>A page by name: the window's own, or a page control built on first use (null while never built).</summary>
    private static FrameworkElement? Page(WpfWindow window, string name) =>
        window.FindName(name) as FrameworkElement
        ?? LogicalTreeHelper.FindLogicalNode(window, name) as FrameworkElement;

    /// <summary>
    /// Lets every page switch finish: its layout, its fades (Motion.Enter at most) and the fades' completions. For tests
    /// that read which page is shown right after a switch: with motion on the old page is still fading out.
    /// </summary>
    internal static void Settle()
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

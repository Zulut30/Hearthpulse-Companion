using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia page cross-fade (<see cref="PageSwitch"/>, the twin of the WPF PageSwitchTests): the leaving page stays
/// drawn but takes no clicks, the motion starts only after the new page's layout, and however fast the switches come,
/// the pages settle on one page, opaque, in place and clickable. Motion is switched on for the switch itself: the
/// headless platform turns the system's off (PoolInput.NoAnimationsSwitch).
/// </summary>
public sealed class PageSwitchTests
{
    [Fact]
    public void The_leaving_page_stays_drawn_without_clicks_and_fast_switches_settle_on_one_whole_page() => LocBindingTests.Headless.Run(() =>
    {
        var (window, pages) = Stage();
        try
        {
            var flip = new PageSwitch(motion: () => true);
            flip.Show(pages[0], pages);
            Wait(TimeSpan.FromMilliseconds(50));
            AssertOnly(pages, pages[0]);

            flip.Show(pages[1], pages);
            // Before the layout: the old page fully drawn but not clickable, the new one held unseen and low.
            Assert.True(pages[0].IsVisible);
            Assert.Equal(1, pages[0].Opacity);
            Assert.False(pages[0].IsHitTestVisible);
            Assert.True(pages[1].IsVisible);
            Assert.Equal(0, pages[1].Opacity);
            Assert.Equal(Motion.Rise, ((TranslateTransform)pages[1].RenderTransform!).Y);
            Settle();
            AssertOnly(pages, pages[1]);

            // B -> C -> B before any motion starts.
            flip.Show(pages[2], pages);
            flip.Show(pages[1], pages);
            Settle();
            AssertOnly(pages, pages[1]);

            // B -> C, then D while C comes in, then back to B mid-fade.
            flip.Show(pages[2], pages);
            Wait(TimeSpan.FromMilliseconds(60));
            flip.Show(pages[3], pages);
            Wait(TimeSpan.FromMilliseconds(40));
            flip.Show(pages[1], pages);
            Assert.True(pages[1].IsHitTestVisible);
            Assert.All(pages.Where(p => p != pages[1]), p => Assert.False(p.IsHitTestVisible && p.IsVisible));
            Settle();
            AssertOnly(pages, pages[1]);
            Assert.Same(pages[1], flip.Current);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_motion_off_a_switch_is_instant() => LocBindingTests.Headless.Run(() =>
    {
        var (window, pages) = Stage();
        try
        {
            var flip = new PageSwitch(motion: () => false);
            flip.Show(pages[0], pages);
            flip.Show(pages[2], pages);
            AssertOnly(pages, pages[2]);
        }
        finally
        {
            window.Close();
        }
    });

    private static (Window Window, Control[] Pages) Stage()
    {
        Control[] pages = [.. Enumerable.Range(0, 4).Select(i => new Border { Background = Brushes.Gray, IsVisible = i == 0 })];
        var cell = new Grid();
        cell.Children.AddRange(pages);
        var window = new Window { Content = cell, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, pages);
    }

    /// <summary>Exactly one page shown, at rest: opaque, in place and clickable; the others hidden and at rest.</summary>
    private static void AssertOnly(IReadOnlyList<Control> pages, Control shown)
    {
        foreach (var page in pages)
        {
            Assert.Equal(page == shown, page.IsVisible);
            Assert.Equal(1, page.Opacity);
            Assert.True(page.IsHitTestVisible);
            Assert.Equal(0, (page.RenderTransform as TranslateTransform)?.Y ?? 0);
        }
    }

    /// <summary>Lets every switch finish: its layout, its fades (Motion.Enter at most) and the fades' completions.</summary>
    private static void Settle() => Wait(Motion.Enter + TimeSpan.FromMilliseconds(400));

    /// <summary>Runs the dispatcher, its timers and the animation clock with it, for <paramref name="time"/>.</summary>
    private static void Wait(TimeSpan time)
    {
        using var stop = new CancellationTokenSource();
        DispatcherTimer.RunOnce(stop.Cancel, time);
        Dispatcher.UIThread.MainLoop(stop.Token);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Controls;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// How card art comes in, in the WPF app (<see cref="ArtMotion"/>): art the memory cache holds is drawn at once without
/// motion, art that arrives later fades in on screen (never in a transparent window such as the overlay), motion off is
/// instant, and an element given another card never shows the old one or a stale fade. The hover card never opens on an
/// empty frame, swaps in place from row to row and fades out after the grace period. The page hero cross-fades and
/// settles on the last hero. The Avalonia twin is in Hearthpulse.Tests (ArtMotionTests).
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class ArtMotionTests
{
    /// <summary>A 4x4 PNG, the art every test card gets on disk (decoded off the UI thread like any cached art).</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAK0lEQVR4nBXIMQEAMAzDsAArMJ8FFX6be+hRkn0jVCVjCHUuMITKRQ2h6gM5yCMxNa2/WQAAAABJRU5ErkJggg==";

    [Fact]
    public void Art_in_memory_is_drawn_at_once_and_art_that_comes_later_fades_in() => OnUiThread(() =>
    {
        using var motion = new MotionSwitch(on: true);
        var (window, panel) = Stage();
        try
        {
            var id = Card();
            var late = Add(panel, new Image());
            CardArt.SetCard(late, id);
            Assert.Null(late.Source);
            Arrive(ArtKind.Card, id);
            Assert.NotNull(late.Source);
            Assert.True(late.HasAnimatedProperties);
            Wait(Motion.Fast + TimeSpan.FromMilliseconds(300));
            Assert.False(late.HasAnimatedProperties);
            Assert.Equal(1, late.Opacity);

            // The same card again (a list shown again, a recycled row): drawn on the call, no motion.
            var seen = Add(panel, new Image { Opacity = 0.5 });
            CardArt.SetCard(seen, id);
            Assert.Same(late.Source, seen.Source);
            Assert.False(seen.HasAnimatedProperties);
            Assert.Equal(0.5, seen.Opacity);

            // A border's art: the art brush fades in, the border itself does not.
            var other = Card();
            var border = Add(panel, new Border { Background = Brushes.Gray });
            CardArt.SetCard(border, other);
            Arrive(ArtKind.Card, other);
            var brush = Assert.IsType<ImageBrush>(border.Background);
            Assert.True(brush.HasAnimatedProperties);
            Assert.False(border.HasAnimatedProperties);
            Wait(Motion.Fast + TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, brush.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_recycled_element_shows_its_new_card_at_once_and_never_the_old_one() => OnUiThread(() =>
    {
        using var motion = new MotionSwitch(on: true);
        var (window, panel) = Stage();
        try
        {
            var (old, current) = (Card(), Card());
            Arrive(ArtKind.Card, current);
            var row = Add(panel, new Image());
            CardArt.SetCard(row, old);
            CardArt.SetCard(row, current);
            var shown = row.Source;
            Assert.NotNull(shown);
            Assert.False(row.HasAnimatedProperties);

            // The old card's art comes after: it is not drawn, and nothing fades.
            Arrive(ArtKind.Card, old);
            Assert.Same(shown, row.Source);
            Assert.False(row.HasAnimatedProperties);

            // Mid-fade, the element is given a card in memory: the fade stops, the card is whole at once.
            var next = Card();
            CardArt.SetCard(row, next);
            Arrive(ArtKind.Card, next);
            Assert.True(row.HasAnimatedProperties);
            CardArt.SetCard(row, current);
            Assert.Same(shown, row.Source);
            Assert.False(row.HasAnimatedProperties);
            Assert.Equal(1, row.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_motion_off_and_in_a_transparent_window_late_art_is_drawn_without_motion() => OnUiThread(() =>
    {
        var (window, panel) = Stage();
        var (overlay, overlayPanel) = Stage(transparent: true);
        try
        {
            using (new MotionSwitch(on: false))
            {
                var id = Card();
                var image = Add(panel, new Image());
                CardArt.SetCard(image, id);
                Arrive(ArtKind.Card, id);
                Assert.NotNull(image.Source);
                Assert.False(image.HasAnimatedProperties);
            }

            using (new MotionSwitch(on: true))
            {
                var id = Card();
                var image = Add(overlayPanel, new Image());
                CardArt.SetCard(image, id);
                Arrive(ArtKind.Card, id);
                Assert.NotNull(image.Source);
                Assert.False(image.HasAnimatedProperties);
            }
        }
        finally
        {
            window.Close();
            overlay.Close();
        }
    });

    [Fact]
    public void The_hover_card_never_opens_empty_swaps_in_place_and_fades_out_after_the_grace() => OnUiThread(() =>
    {
        using var motion = new MotionSwitch(on: true);
        var (window, panel) = Stage();
        var host = new CardPreviewHost();
        ((Grid)window.Content).Children.Add(host);
        try
        {
            var (first, second, late) = (Card(), Card(), Card());
            Arrive(ArtKind.Card, first);
            Arrive(ArtKind.Card, second);
            var rows = Enumerable.Range(0, 3).Select(_ => Add(panel, new Border { Height = 24, Background = Brushes.Transparent })).ToArray();
            CardPreview.SetCardId(rows[0], first);
            CardPreview.SetCardId(rows[1], second);
            CardPreview.SetCardId(rows[2], late);
            Pump(DispatcherPriority.Loaded);

            // Art in memory: the card is there on the call, and the new preview fades in.
            host.Show(rows[0]);
            Assert.False(host.Waiting);
            Assert.NotNull(host.CardShown);
            Assert.True(host.HasAnimatedProperties);

            // Row to row: the card swaps in place at full strength.
            var shown = host.CardShown;
            host.Show(rows[1]);
            Assert.NotSame(shown, host.CardShown);
            Assert.NotNull(host.CardShown);
            Assert.False(host.HasAnimatedProperties);
            Assert.Equal(1, host.Opacity);

            // Art not in memory: the preview stays as it was while it waits, then shows the card.
            shown = host.CardShown;
            host.Show(rows[2]);
            Assert.True(host.Waiting);
            Assert.Same(shown, host.CardShown);
            Arrive(ArtKind.Card, late);
            Assert.False(host.Waiting);
            CardArt.TryGetCached(ArtKind.Card, late, out var lateArt);
            Assert.Same(lateArt, host.CardShown);

            // The pointer leaves for good: after the grace period the preview fades out and is gone.
            host.HideSoon(rows[2]);
            Wait(TimeSpan.FromMilliseconds(140) + Motion.Fast + TimeSpan.FromMilliseconds(400));
            Assert.Null(host.CardShown);
            Assert.Equal(1, host.Opacity);
            Assert.False(host.HasAnimatedProperties);

            // From hidden, a card in memory opens with it at once.
            host.Show(rows[0]);
            Assert.NotNull(host.CardShown);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_motion_off_the_hover_card_opens_and_closes_without_motion() => OnUiThread(() =>
    {
        using var motion = new MotionSwitch(on: false);
        var (window, panel) = Stage();
        var host = new CardPreviewHost();
        ((Grid)window.Content).Children.Add(host);
        try
        {
            var id = Card();
            Arrive(ArtKind.Card, id);
            var row = Add(panel, new Border { Height = 24, Background = Brushes.Transparent });
            CardPreview.SetCardId(row, id);
            Pump(DispatcherPriority.Loaded);

            host.Show(row);
            Assert.NotNull(host.CardShown);
            Assert.False(host.HasAnimatedProperties);
            host.HideSoon(row);
            Wait(TimeSpan.FromMilliseconds(140) + TimeSpan.FromMilliseconds(200));
            Assert.Null(host.CardShown);
            Assert.False(host.HasAnimatedProperties);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_page_hero_cross_fades_and_settles_on_the_last_hero() => OnUiThread(() =>
    {
        var (window, panel) = Stage();
        try
        {
            var layers = Enumerable.Range(0, 4).Select(_ => new Border()).ToArray();
            var cell = Add(panel, new Grid { Height = 100 });
            foreach (var layer in layers)
            {
                cell.Children.Add(layer);
            }

            var hero = new HeroSwap(layers[0], layers[1], layers[2], layers[3], motion: () => true);
            var (first, second) = (Card(ArtKind.Splash), Card(ArtKind.Splash));
            hero.Show(first);
            Arrive(ArtKind.Splash, first);
            Pump(DispatcherPriority.Background);
            Assert.Equal(first, hero.Front);
            Settle();
            AssertSettled(hero, first);

            // A hero in memory: the cross-fade starts after the layout, both slots moving.
            Arrive(ArtKind.Splash, second);
            hero.Show(second);
            Assert.Equal(first, hero.Front);
            Pump(DispatcherPriority.Background);
            Assert.Equal(second, hero.Front);
            Assert.All(layers, l => Assert.True(l.HasAnimatedProperties));
            Settle();
            AssertSettled(hero, second);

            // Back and forth mid-fade: the slots settle on the last hero, whole, the other slot empty.
            hero.Show(first);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(60));
            hero.Show(second);
            Pump(DispatcherPriority.Background);
            Wait(TimeSpan.FromMilliseconds(40));
            hero.Show(first);
            Pump(DispatcherPriority.Background);
            Settle();
            AssertSettled(hero, first);

            // With motion off the hero changes on the call.
            var still = new HeroSwap(layers[0], layers[1], layers[2], layers[3], motion: () => false);
            still.Show(second);
            AssertSettled(still, second);
            Assert.All(layers, l => Assert.False(l.HasAnimatedProperties));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// Runs <paramref name="body"/> on a WPF thread as the app's own code runs: with the dispatcher's synchronization
    /// context, so an awaiting load comes back to the UI thread (a bare test thread has none).
    /// </summary>
    private static void OnUiThread(Action body) => WpfThread.RunFresh(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        body();
    });

    /// <summary>The hero shown whole in one slot, the other slot transparent and empty.</summary>
    private static void AssertSettled(HeroSwap hero, string shown)
    {
        Assert.Equal(shown, hero.Front);
        var slots = hero.Slots;
        Assert.Single(slots, s => s.Hero == shown && s.Opacity == 1);
        Assert.Single(slots, s => s.Hero is null && s.Opacity == 0);
    }

    /// <summary>A new card id with art on disk (not in memory yet).</summary>
    private static string Card(ArtKind kind = ArtKind.Card)
    {
        var id = "ARTMOTION_" + Guid.NewGuid().ToString("N");
        var (_, file) = CardImageFiles.Art(kind, id, CardArt.Locale, AppPaths.Images);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, Convert.FromBase64String(Png));
        return id;
    }

    /// <summary>Lets the card's art load and the UI take it (the continuations of every element waiting for it).</summary>
    private static void Arrive(ArtKind kind, string cardId)
    {
        var load = CardArt.GetAsync(kind, cardId);
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        _ = load.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false), TaskScheduler.Default);
        // Only bounds a hang; the load's end decides.
        var backstop = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => frame.Continue = false, dispatcher);
        Dispatcher.PushFrame(frame);
        backstop.Stop();
        Assert.True(load.IsCompletedSuccessfully);
        Assert.NotNull(load.Result);
        Pump(DispatcherPriority.Background);
    }

    private static T Add<T>(Panel panel, T element)
        where T : FrameworkElement
    {
        if (element is Image or Border && double.IsNaN(element.Height))
        {
            element.Width = 40;
            element.Height = 40;
        }

        panel.Children.Add(element);
        Pump(DispatcherPriority.Loaded);
        return element;
    }

    /// <summary>An off-screen window (shown, so its elements count as on screen), optionally transparent like the overlay.</summary>
    private static (Window Window, StackPanel Panel) Stage(bool transparent = false)
    {
        var panel = new StackPanel();
        var window = new Window
        {
            Content = new Grid { Children = { panel } }, Width = 600, Height = 500,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
        };
        if (transparent)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
        }

        window.Show();
        Pump(DispatcherPriority.ApplicationIdle);
        return (window, panel);
    }

    /// <summary>Lets a cross-fade finish: its layout, its fades (Motion.Enter) and their completions.</summary>
    private static void Settle()
    {
        Pump(DispatcherPriority.ApplicationIdle);
        Wait(Motion.Enter + TimeSpan.FromMilliseconds(400));
        Pump(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Runs the dispatcher (layout, render, animation ticks, timers) for <paramref name="time"/>.</summary>
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

    /// <summary>Card art motion switched on or off for the test, whatever the machine's animation setting.</summary>
    private sealed class MotionSwitch : IDisposable
    {
        private readonly Func<bool> _saved = CardArt.Animate;

        public MotionSwitch(bool on) => CardArt.Animate = on ? static () => true : static () => false;

        public void Dispose() => CardArt.Animate = _saved;
    }
}

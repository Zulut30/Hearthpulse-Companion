using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.Tests;

/// <summary>
/// How card art comes in, in the Avalonia app (<see cref="ArtMotion"/>, the twin of the WPF ArtMotionTests): art the
/// memory cache holds is drawn at once without motion, art that arrives later fades in, motion off is instant, and a
/// control given another card never shows the old one or a stale fade. The hover card never opens on an empty frame,
/// swaps in place from row to row and fades out after the grace period. The page hero cross-fades and settles on the
/// last hero. Motion is switched on through <see cref="CardArt.Animate"/>: the headless platform turns the system's
/// off (PoolInput.NoAnimationsSwitch). The transparent-window rule (the overlay) is checked in the WPF suite only: the
/// headless platform has no transparent windows.
/// </summary>
public sealed class ArtMotionTests
{
    /// <summary>A 4x4 PNG, the art every test card gets on disk.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAK0lEQVR4nBXIMQEAMAzDsAArMJ8FFX6be+hRkn0jVCVjCHUuMITKRQ2h6gM5yCMxNa2/WQAAAABJRU5ErkJggg==";

    [Fact]
    public void Art_in_memory_is_drawn_at_once_and_art_that_comes_later_fades_in() => LocBindingTests.Headless.Run(() =>
    {
        using var motion = new MotionSwitch(on: true);
        var (window, panel) = Stage();
        try
        {
            var id = Card();
            var late = Add(panel, new Image());
            CardArt.SetCard(late, id);
            Assert.Null(late.Source);
            Arrive(ArtKind.Card, id, () => late.Source is not null, late);
            Assert.NotNull(late.Source);
            Assert.True(CardArt.Fading(late));
            Until(() => !CardArt.Fading(late) && late.Opacity == 1, late);
            Assert.False(CardArt.Fading(late));
            Assert.Equal(1, late.Opacity);

            // The same card again (a list shown again, a recycled row): drawn on the call, no motion.
            var seen = Add(panel, new Image { Opacity = 0.5 });
            CardArt.SetCard(seen, id);
            Assert.Same(late.Source, seen.Source);
            Assert.False(CardArt.Fading(seen));
            Assert.Equal(0.5, seen.Opacity);

            // A border's art: the art brush fades in, the border itself stays as it is.
            var other = Card();
            var border = Add(panel, new Border { Background = Brushes.Gray });
            CardArt.SetCard(border, other);
            Arrive(ArtKind.Card, other, () => border.Background is ImageBrush, border);
            var brush = Assert.IsType<ImageBrush>(border.Background);
            Assert.True(CardArt.Fading(border));
            Assert.Equal(1, border.Opacity);
            Until(() => !CardArt.Fading(border) && brush.Opacity == 1, border, brush);
            Assert.False(CardArt.Fading(border));
            Assert.Equal(1, brush.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_recycled_control_shows_its_new_card_at_once_and_never_the_old_one() => LocBindingTests.Headless.Run(() =>
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
            Assert.False(CardArt.Fading(row));

            // The old card's art comes after: it is not drawn, and nothing fades.
            Arrive(ArtKind.Card, old);
            Assert.Same(shown, row.Source);
            Assert.False(CardArt.Fading(row));

            // Mid-fade, the control is given a card in memory: the fade stops, the card is whole at once. The fade runs on
            // the wall clock and could end under load before a wait saw it, so the card is given in the job right after the
            // fade starts (Send outranks every animation frame).
            var next = Card();
            CardArt.SetCard(row, next);
            bool? fadingWhenGiven = null;
            void Give(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (!CardArt.Fading(row))
                {
                    return;
                }

                row.PropertyChanged -= Give;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        fadingWhenGiven = CardArt.Fading(row);
                        CardArt.SetCard(row, current);
                    },
                    DispatcherPriority.Send);
            }

            row.PropertyChanged += Give;
            try
            {
                Arrive(ArtKind.Card, next, () => fadingWhenGiven is not null, row);
            }
            finally
            {
                row.PropertyChanged -= Give;
            }

            Assert.True(fadingWhenGiven);
            Assert.Same(shown, row.Source);
            Assert.False(CardArt.Fading(row));
            Assert.Equal(1, row.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void With_motion_off_late_art_and_the_hover_card_come_without_motion() => LocBindingTests.Headless.Run(() =>
    {
        using var motion = new MotionSwitch(on: false);
        var (window, panel) = Stage();
        var host = new CardPreviewHost();
        ((Grid)window.Content!).Children.Add(host);
        try
        {
            var id = Card();
            var image = Add(panel, new Image());
            CardArt.SetCard(image, id);
            Arrive(ArtKind.Card, id, () => image.Source is not null, image);
            Assert.NotNull(image.Source);
            Assert.False(CardArt.Fading(image));

            var row = Add(panel, new Border { Height = 24, Background = Brushes.Transparent });
            CardPreview.SetCardId(row, id);
            Dispatcher.UIThread.RunJobs();
            host.Show(row);
            Assert.NotNull(host.CardShown);
            Assert.False(host.Fading);
            host.HideSoon(row);
            WaitUntilHidden(host);
            Assert.Null(host.CardShown);
            Assert.False(host.Fading);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_hover_card_never_opens_empty_swaps_in_place_and_fades_out_after_the_grace() => LocBindingTests.Headless.Run(() =>
    {
        using var motion = new MotionSwitch(on: true);
        var (window, panel) = Stage();
        var host = new CardPreviewHost();
        ((Grid)window.Content!).Children.Add(host);
        try
        {
            var (first, second, late) = (Card(), Card(), Card());
            Arrive(ArtKind.Card, first);
            Arrive(ArtKind.Card, second);
            var rows = Enumerable.Range(0, 3).Select(_ => Add(panel, new Border { Height = 24, Background = Brushes.Transparent })).ToArray();
            CardPreview.SetCardId(rows[0], first);
            CardPreview.SetCardId(rows[1], second);
            CardPreview.SetCardId(rows[2], late);
            Dispatcher.UIThread.RunJobs();

            // Art in memory: the card is there on the call, and the new preview fades in.
            host.Show(rows[0]);
            Assert.False(host.Waiting);
            Assert.NotNull(host.CardShown);
            Assert.True(host.Fading);

            // Row to row: the card swaps in place at full strength.
            var shown = host.CardShown;
            host.Show(rows[1]);
            Assert.NotSame(shown, host.CardShown);
            Assert.NotNull(host.CardShown);
            Assert.False(host.Fading);
            Assert.Equal(1, host.Opacity);

            // Art not in memory: the preview stays as it was while it waits, then shows the card.
            shown = host.CardShown;
            host.Show(rows[2]);
            Assert.True(host.Waiting);
            Assert.Same(shown, host.CardShown);
            Arrive(ArtKind.Card, late, () => !host.Waiting && Shows(host, late), [host, .. host.GetVisualDescendants()]);
            Assert.False(host.Waiting);
            CardArt.TryGetCached(ArtKind.Card, late, out var lateArt);
            Assert.Same(lateArt, host.CardShown);

            // The pointer leaves for good: after the grace period the preview fades out and is gone.
            host.HideSoon(rows[2]);
            Assert.NotNull(host.CardShown);
            WaitUntilHidden(host);
            Assert.Null(host.CardShown);
            Assert.False(host.Fading);
            Assert.Equal(1, host.Opacity);

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
    public void The_page_hero_cross_fades_and_settles_on_the_last_hero() => LocBindingTests.Headless.Run(() =>
    {
        var (window, panel) = Stage();
        try
        {
            var layers = Enumerable.Range(0, 4).Select(_ => new Border()).ToArray();
            var cell = Add(panel, new Grid { Height = 100 });
            cell.Children.AddRange(layers);
            Dispatcher.UIThread.RunJobs();

            var hero = new HeroSwap(layers[0], layers[1], layers[2], layers[3], motion: () => true);
            var (first, second) = (Card(ArtKind.Splash), Card(ArtKind.Splash));
            hero.Show(first);
            Arrive(ArtKind.Splash, first, () => hero.Front == first, layers);
            Assert.Equal(first, hero.Front);
            Settle(hero, first, layers);
            AssertSettled(hero, first);

            // A hero in memory: the cross-fade starts after the layout.
            Arrive(ArtKind.Splash, second);
            hero.Show(second);
            Assert.Equal(first, hero.Front);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second, hero.Front);
            Settle(hero, second, layers);
            AssertSettled(hero, second);

            // Back and forth mid-fade: the slots settle on the last hero, whole, the other slot empty.
            hero.Show(first);
            MidFade(hero, first, layers);
            hero.Show(second);
            MidFade(hero, second, layers);
            hero.Show(first);
            Settle(hero, first, layers);
            AssertSettled(hero, first);

            // With motion off the hero changes on the call.
            var still = new HeroSwap(layers[0], layers[1], layers[2], layers[3], motion: () => false);
            still.Show(second);
            AssertSettled(still, second);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The hero shown whole in one slot, the other slot transparent and empty.</summary>
    private static void AssertSettled(HeroSwap hero, string shown)
    {
        Assert.Equal(shown, hero.Front);
        var slots = hero.Slots;
        Assert.Single(slots, s => s.Hero == shown && s.Opacity == 1);
        Assert.Single(slots, s => s.Hero is null && s.Opacity == 0);
    }

    /// <summary>Whether the hero is shown whole in one slot and the other slot is transparent and empty.</summary>
    private static bool Settled(HeroSwap hero, string shown) =>
        hero.Front == shown
        && hero.Slots.Count(s => s.Hero == shown && s.Opacity == 1) == 1
        && hero.Slots.Count(s => s.Hero is null && s.Opacity == 0) == 1;

    /// <summary>Whether the hover card shows the art the memory cache holds for <paramref name="cardId"/>.</summary>
    private static bool Shows(CardPreviewHost host, string cardId) =>
        CardArt.TryGetCached(ArtKind.Card, cardId, out var art) && ReferenceEquals(art, host.CardShown);

    /// <summary>A new card id with art on disk (not in memory yet).</summary>
    private static string Card(ArtKind kind = ArtKind.Card)
    {
        var id = "ARTMOTION_" + Guid.NewGuid().ToString("N");
        var (_, file) = CardImageFiles.Art(kind, id, CardArt.Locale, AppPaths.Images);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, Convert.FromBase64String(Png));
        return id;
    }

    /// <summary>
    /// Lets the card's art load and the UI take it: the load's end, then <paramref name="taken"/> (what the control
    /// waiting for it shows once it has), checked on every property change of <paramref name="watched"/>. The shared load
    /// leaves the memory cache holding the art a moment before its task ends on a pool thread, and the controls'
    /// continuations come only then: a load asked for in that moment ends at once, and the end of the load alone ran
    /// ahead of them under load.
    /// </summary>
    private static void Arrive(ArtKind kind, string cardId, Func<bool>? taken = null, params AvaloniaObject[] watched)
    {
        var load = CardArt.GetAsync(kind, cardId);
        using var stop = new CancellationTokenSource();
        _ = load.ContinueWith(_ => Dispatcher.UIThread.Post(stop.Cancel, DispatcherPriority.Background), TaskScheduler.Default);
        // Only bounds a hang; the load's end decides.
        DispatcherTimer.RunOnce(stop.Cancel, TimeSpan.FromSeconds(10));
        Dispatcher.UIThread.MainLoop(stop.Token);
        Dispatcher.UIThread.RunJobs();
        Assert.True(load.IsCompletedSuccessfully);
        Assert.NotNull(load.Result);
        if (taken is not null)
        {
            Until(taken, watched);
        }
    }

    private static T Add<T>(StackPanel panel, T control)
        where T : Control
    {
        if (control is Image or Border && double.IsNaN(control.Height))
        {
            control.Width = 40;
            control.Height = 40;
        }

        panel.Children.Add(control);
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static (Window Window, StackPanel Panel) Stage()
    {
        var panel = new StackPanel();
        var window = new Window { Content = new Grid { Children = { panel } }, Width = 600, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, panel);
    }

    /// <summary>Lets a cross-fade finish (its layout, its fades and their completions) on <paramref name="shown"/>.</summary>
    private static void Settle(HeroSwap hero, string shown, Border[] layers) => Until(() => Settled(hero, shown), layers);

    /// <summary>
    /// Lets the cross-fade to <paramref name="hero"/> start and take its first step, so the next change lands mid-fade:
    /// its slot partly in. A machine too slow to show a step settles first, and the next change starts from there.
    /// </summary>
    private static void MidFade(HeroSwap swap, string hero, Border[] layers) =>
        Until(() => swap.Slots.Any(s => s.Hero == hero && s.Opacity is > 0 and < 1) || Settled(swap, hero), layers);

    /// <summary>
    /// Runs the dispatcher, its timers and the animation clock with it until <paramref name="done"/> holds, checked on
    /// every property change of <paramref name="watched"/> (each step of a fade, its end). The fades run on the wall
    /// clock: a fixed wait ran out under load while a fade was still on, so the state ends the wait; 10 s only bounds a
    /// hang, and the state is asserted after it.
    /// </summary>
    private static void Until(Func<bool> done, params AvaloniaObject[] watched)
    {
        if (done())
        {
            return;
        }

        using var stop = new CancellationTokenSource();
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (done())
            {
                stop.Cancel();
            }
        }

        foreach (var item in watched)
        {
            item.PropertyChanged += Changed;
        }

        try
        {
            using var backstop = DispatcherTimer.RunOnce(stop.Cancel, TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.MainLoop(stop.Token);
        }
        finally
        {
            foreach (var item in watched)
            {
                item.PropertyChanged -= Changed;
            }
        }

        Assert.True(done(), "The awaited state did not come within 10 s.");
    }

    /// <summary>
    /// Runs the dispatcher until the hover card hides its frame at the end of the fade-out. The grace timer and the fade
    /// run on the wall clock: a fixed wait from <see cref="CardPreviewHost.HideSoon"/> ran out under load while the fade
    /// was still on (the stop and the grace came due together), so the frame's change ends the wait; 10 s only bounds a hang.
    /// </summary>
    private static void WaitUntilHidden(CardPreviewHost host)
    {
        var frame = (Control)host.Children[0];
        using var stop = new CancellationTokenSource();
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty && host.CardShown is null)
            {
                stop.Cancel();
            }
        }

        frame.PropertyChanged += Changed;
        try
        {
            using var backstop = DispatcherTimer.RunOnce(stop.Cancel, TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.MainLoop(stop.Token);
        }
        finally
        {
            frame.PropertyChanged -= Changed;
        }
    }

    /// <summary>Card art motion switched on or off for the test, whatever the platform's animation setting.</summary>
    private sealed class MotionSwitch : IDisposable
    {
        private readonly Func<bool> _saved = CardArt.Animate;

        public MotionSwitch(bool on) => CardArt.Animate = on ? static () => true : static () => false;

        public void Dispose() => CardArt.Animate = _saved;
    }
}

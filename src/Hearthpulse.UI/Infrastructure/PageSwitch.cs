using Avalonia.Controls;
using Avalonia.Media;
using Hearthpulse.Core;

namespace Hearthpulse.UI.Infrastructure;

/// <summary>
/// Cross-fades the pages that share one grid cell (the main view's pages), as the WPF app does. The page that leaves
/// stays drawn under the new one but takes no clicks, fades out over <see cref="Motion.Fast"/> and is then hidden; the
/// page that comes in fades in and rises <see cref="Motion.Rise"/> over <see cref="Motion.Enter"/>. Both start after
/// the new page's first layout and render (<see cref="Motion.AfterLayout"/>), the new page held unseen and the old one
/// fully drawn meanwhile, so a page that takes long to lay out the first time eats none of the motion.
/// Every switch and every page has a turn: a fade that ends after a later switch never hides a page that switch shows,
/// and a superseded switch never starts its motion. With motion off, or before the view is on screen, the switch is
/// instant.
/// </summary>
public sealed class PageSwitch
{
    private readonly Func<bool> _motion;
    private readonly Dictionary<Control, PageMotion> _pages = [];
    private int _switches;

    /// <param name="motion">Whether to animate; <see cref="Motion.On"/> unless a test says otherwise.</param>
    public PageSwitch(Func<bool>? motion = null) => _motion = motion ?? (() => Motion.On);

    /// <summary>The page shown last (it may still be coming in).</summary>
    public Control? Current { get; private set; }

    /// <summary>Shows <paramref name="page"/>; every other visible page of <paramref name="pages"/> leaves.</summary>
    public void Show(Control page, IEnumerable<Control> pages)
    {
        if (ReferenceEquals(page, Current) && page.IsVisible)
        {
            // Already shown or coming in: its own switch goes on.
            return;
        }

        var switchTurn = ++_switches;
        Current = page;
        Stop(page);
        page.IsHitTestVisible = true;
        var leaving = pages.Where(p => !ReferenceEquals(p, page) && p.IsVisible).Distinct().ToList();
        // Nothing on screen to fade from (the view is not shown yet, as when it opens on its saved page): no motion.
        if (!_motion() || !leaving.Exists(OnScreen))
        {
            foreach (var other in leaving)
            {
                Stop(other);
                Collapse(other);
            }

            page.IsVisible = true;
            page.Opacity = 1;
            Of(page).Shift.Y = 0;
            return;
        }

        if (!page.IsVisible)
        {
            page.Opacity = 0;
            Of(page).Shift.Y = Motion.Rise;
            page.IsVisible = true;
        }

        foreach (var other in leaving)
        {
            Stop(other);
            other.IsHitTestVisible = false;
            // Held unseen and never shown (a switch overtaken before its motion started): nothing to fade.
            if (other.Opacity <= 0)
            {
                Collapse(other);
            }
        }

        leaving.RemoveAll(p => !p.IsVisible);
        Motion.AfterLayout(() =>
        {
            // A later switch took over: it starts the motion of the pages as they are then.
            if (switchTurn != _switches)
            {
                return;
            }

            Enter(page);
            foreach (var other in leaving)
            {
                LeaveAsync(other).Forget("Page leave");
            }
        });
    }

    private static bool OnScreen(Control page) => page.IsEffectivelyVisible && TopLevel.GetTopLevel(page) is { IsVisible: true };

    /// <summary>The page fades in and rises from wherever it stands, the end values set under the motion.</summary>
    private void Enter(Control page)
    {
        var motion = Of(page);
        var (opacity, y) = (page.Opacity, motion.Shift.Y);
        page.Opacity = 1;
        motion.Shift.Y = 0;
        var cancel = motion.Start();
        Motion.FadeAsync(page, opacity, 1, Motion.Enter, cancel).Forget("Page enter");
        Motion.LiftAsync(page, y, cancel).Forget("Page rise");
    }

    /// <summary>The page fades out from wherever it stands, then hides unless a later switch claimed it meanwhile.</summary>
    private async Task LeaveAsync(Control page)
    {
        var motion = Of(page);
        var (opacity, turn) = (page.Opacity, motion.Turn);
        page.Opacity = 0;
        await Motion.FadeAsync(page, opacity, 0, Motion.Fast, motion.Start());
        if (motion.Turn == turn)
        {
            Collapse(page);
        }
    }

    /// <summary>
    /// A new turn for the page: its running motion stops where it stands (kept as the page's own values) and a fade
    /// still to end no longer hides it.
    /// </summary>
    private void Stop(Control page)
    {
        var motion = Of(page);
        var (opacity, y) = (page.Opacity, motion.Shift.Y);
        motion.Turn++;
        motion.Cancel();
        page.Opacity = opacity;
        motion.Shift.Y = y;
    }

    /// <summary>A page out of sight: hidden, at rest and clickable again for when it comes back.</summary>
    private void Collapse(Control page)
    {
        page.IsVisible = false;
        page.IsHitTestVisible = true;
        page.Opacity = 1;
        Of(page).Shift.Y = 0;
    }

    private PageMotion Of(Control page)
    {
        if (!_pages.TryGetValue(page, out var motion))
        {
            if (page.RenderTransform is not TranslateTransform shift)
            {
                shift = new TranslateTransform();
                page.RenderTransform = shift;
            }

            motion = new PageMotion(shift);
            _pages[page] = motion;
        }

        return motion;
    }

    /// <summary>One page's turn, its rise transform and the motion running on it.</summary>
    private sealed class PageMotion(TranslateTransform shift)
    {
        private CancellationTokenSource? _run;

        public TranslateTransform Shift { get; } = shift;

        public int Turn { get; set; }

        /// <summary>Stops the running motion and returns the token of the next one.</summary>
        public CancellationToken Start()
        {
            Cancel();
            _run = new CancellationTokenSource();
            return _run.Token;
        }

        public void Cancel()
        {
            _run?.Cancel();
            _run?.Dispose();
            _run = null;
        }
    }
}

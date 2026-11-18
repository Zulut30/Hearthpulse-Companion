using System.Windows;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// Cross-fades the pages that share one grid cell (the main window's pages). The page that leaves stays drawn under
/// the new one but takes no clicks, fades out over <see cref="Motion.Fast"/> and is then collapsed; the page that comes
/// in fades in with its rise over <see cref="Motion.Enter"/> (<see cref="Motion.FadeIn"/>). Both start after the new
/// page's first layout and render (<see cref="Motion.AfterLayout"/>), the new page held unseen and the old one fully
/// drawn meanwhile, so a page that takes long to lay out the first time eats none of the motion.
/// Every switch and every page has a turn: a fade that ends after a later switch never collapses a page that switch
/// shows, and a superseded switch never starts its motion. With motion off, or before the window is on screen, the
/// switch is instant.
/// </summary>
public sealed class PageSwitch
{
    private readonly Func<bool> _motion;
    private readonly Dictionary<UIElement, int> _turns = [];
    private int _switches;

    /// <param name="motion">Whether to animate; <see cref="Motion.On"/> unless a test says otherwise.</param>
    public PageSwitch(Func<bool>? motion = null) => _motion = motion ?? (() => Motion.On);

    /// <summary>The page shown last (it may still be coming in).</summary>
    public UIElement? Current { get; private set; }

    /// <summary>
    /// Shows <paramref name="page"/>; every other visible page of <paramref name="pages"/> leaves.
    /// <paramref name="started"/> runs when the motion starts (at once when there is none), for motion that goes with
    /// the page, such as the sidebar's selection pill.
    /// </summary>
    public void Show(UIElement page, IEnumerable<UIElement> pages, Action? started = null)
    {
        if (ReferenceEquals(page, Current) && page.Visibility == Visibility.Visible)
        {
            // Already shown or coming in: its own switch goes on.
            started?.Invoke();
            return;
        }

        var switchTurn = ++_switches;
        Current = page;
        Turn(page);
        page.IsHitTestVisible = true;
        var leaving = pages.Where(p => !ReferenceEquals(p, page) && p.Visibility == Visibility.Visible).Distinct().ToList();
        // Nothing on screen to fade from (the window is not shown yet, as when it opens on its saved page): no motion.
        if (!_motion() || !leaving.Exists(p => p.IsVisible))
        {
            leaving.ForEach(Collapse);
            page.Visibility = Visibility.Visible;
            Motion.Still(page);
            started?.Invoke();
            return;
        }

        if (page.Visibility != Visibility.Visible)
        {
            Motion.Hold(page, Motion.Rise);
            page.Visibility = Visibility.Visible;
        }

        foreach (var other in leaving)
        {
            Turn(other);
            other.IsHitTestVisible = false;
            // Held unseen and never shown (a switch overtaken before its motion started): nothing to fade.
            if (other.Opacity <= 0)
            {
                Collapse(other);
            }
        }

        leaving.RemoveAll(p => p.Visibility != Visibility.Visible);
        Motion.AfterLayout(page, () =>
        {
            // A later switch took over: it starts the motion of the pages as they are then.
            if (switchTurn != _switches)
            {
                return;
            }

            Motion.FadeIn(page);
            foreach (var other in leaving)
            {
                var turn = TurnOf(other);
                Motion.FadeOut(other, () =>
                {
                    if (TurnOf(other) == turn)
                    {
                        Collapse(other);
                    }
                });
            }

            started?.Invoke();
        });
    }

    /// <summary>A page out of sight: collapsed, at rest and clickable again for when it comes back.</summary>
    private static void Collapse(UIElement page)
    {
        page.Visibility = Visibility.Collapsed;
        page.IsHitTestVisible = true;
        Motion.Still(page);
    }

    private void Turn(UIElement page) => _turns[page] = TurnOf(page) + 1;

    private int TurnOf(UIElement page) => _turns.TryGetValue(page, out var turn) ? turn : 0;
}

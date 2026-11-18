using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Hearthpulse.App.Infrastructure;

/// <summary>WPF side of <see cref="Motion"/>: the Windows setting and the animations views start from code.</summary>
public static partial class Motion
{
    /// <summary>Cubic ease-out for state feedback (hover, press, toggles), shared and frozen.</summary>
    public static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Quintic ease-out for travel and entrances (page rise, the selection pill).</summary>
    public static readonly IEasingFunction Travel = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });

    private static bool SystemAnimations() => SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// The page coming in fades in and rises <see cref="Rise"/> DIP over <see cref="Enter"/>: opacity and a render
    /// transform only, so nothing is laid out again. The motion starts once the page has been laid out and drawn
    /// (<see cref="AfterLayout"/>), the page held unseen until then: a first opening can lay out for hundreds of ms,
    /// and motion started before it would be over by its first frame. With motion off the page is simply shown.
    /// </summary>
    public static void Reveal(UIElement page, double rise = Rise)
    {
        if (!On)
        {
            Still(page);
            return;
        }

        Hold(page, rise);
        AfterLayout(page, () =>
        {
            if (On)
            {
                FadeIn(page);
            }
            else
            {
                Still(page);
            }
        });
    }

    /// <summary>Moves <paramref name="shift"/> to <paramref name="y"/> over <see cref="Settle"/>, or at once with motion off.</summary>
    public static void Glide(TranslateTransform shift, double y, bool animate = true)
    {
        if (!animate || !On)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = y;
            return;
        }

        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Settle) { EasingFunction = Travel });
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the layout and render pass queued now are done: at Loaded priority, which
    /// the dispatcher reaches only after its Render work. WPF ticks animation clocks before it lays out, so motion
    /// started in the same pass as a long layout would begin before the stall and be over by its first frame.
    /// </summary>
    internal static void AfterLayout(DispatcherObject owner, Action action) =>
        _ = owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, action);

    /// <summary>The page at rest: no motion on it, fully opaque, not shifted.</summary>
    internal static void Still(UIElement page)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null);
        page.Opacity = 1;
        if (page.RenderTransform is TranslateTransform { IsFrozen: false } still)
        {
            still.BeginAnimation(TranslateTransform.YProperty, null);
            still.Y = 0;
        }
    }

    /// <summary>Holds a page about to come in unseen, <paramref name="rise"/> DIP low, until <see cref="FadeIn"/>.</summary>
    internal static void Hold(UIElement page, double rise)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null);
        page.Opacity = 0;
        var shift = ShiftOf(page);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.Y = rise;
    }

    /// <summary>
    /// Fades the page in and lifts it to its place over <see cref="Enter"/>, from wherever it is (no <c>From</c>: a page
    /// that was fading out turns back without a jump).
    /// </summary>
    internal static void FadeIn(UIElement page)
    {
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { To = 1, Duration = Enter, EasingFunction = Ease });
        ShiftOf(page).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation { To = 0, Duration = Enter, EasingFunction = Travel });
    }

    /// <summary>Fades the page out over <see cref="Fast"/> from wherever it is, then runs <paramref name="done"/>.</summary>
    internal static void FadeOut(UIElement page, Action done)
    {
        var fade = new DoubleAnimation { To = 0, Duration = Fast, EasingFunction = Ease };
        fade.Completed += (_, _) => done();
        page.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>The page's own translate transform (a frozen or foreign one is replaced).</summary>
    private static TranslateTransform ShiftOf(UIElement page)
    {
        if (page.RenderTransform is not TranslateTransform shift || shift.IsFrozen)
        {
            shift = new TranslateTransform();
            page.RenderTransform = shift;
        }

        return shift;
    }

    private static IEasingFunction Frozen(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}

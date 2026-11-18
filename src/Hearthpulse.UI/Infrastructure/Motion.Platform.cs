using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.UI.Infrastructure;

/// <summary>Avalonia side of <see cref="Motion"/>: the Windows setting, the theme's transitions and the motion of page switches.</summary>
public static partial class Motion
{
    private static Styles? _still;

    private static bool SystemAnimations() => PoolInput.AnimationsOn();

    /// <summary>
    /// Takes the app's reduce-motion setting and, with motion off, turns the theme's transitions off: one style per
    /// element type that carries them, at style-trigger priority (the <c>:not</c> makes it one) so it outranks the
    /// templates' own <c>Transitions</c>. With motion on nothing is added, so styling costs nothing extra.
    /// </summary>
    public static void Apply(bool reduced)
    {
        Reduced = reduced;
        if (Application.Current is not { } app)
        {
            return;
        }

        if (On)
        {
            if (_still is not null)
            {
                app.Styles.Remove(_still);
                _still = null;
            }

            return;
        }

        if (_still is not null)
        {
            return;
        }

        _still = [Still<Border>(), Still<Panel>(), Still<ContentPresenter>()];
        app.Styles.Add(_still);
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the layout and render pass queued now are done: at Loaded priority, below
    /// Render. Avalonia pulses its animation clock before it lays out, so motion started in the same pass as a long
    /// layout would begin before the stall and be over by its first frame.
    /// </summary>
    internal static void AfterLayout(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Loaded);

    /// <summary>
    /// Fades <paramref name="target"/> from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/>
    /// (cubic ease-out); ends when done or cancelled. The caller sets the end value itself: the motion is drawn over it.
    /// </summary>
    internal static Task FadeAsync(Visual target, double from, double to, TimeSpan duration, CancellationToken cancel) =>
        Run(target, Visual.OpacityProperty, from, to, duration, new CubicEaseOut(), cancel);

    /// <summary>
    /// Lifts <paramref name="target"/> from <paramref name="from"/> DIP low to its place over <see cref="Enter"/> (quintic
    /// ease-out). The motion runs on the element's own <see cref="TranslateTransform"/> render transform: Avalonia animates
    /// a transform through the visual that holds it.
    /// </summary>
    internal static Task LiftAsync(Visual target, double from, CancellationToken cancel) =>
        Run(target, TranslateTransform.YProperty, from, 0d, Enter, new QuinticEaseOut(), cancel);

    private static Task Run(Animatable target, AvaloniaProperty property, double from, double to, TimeSpan duration, Easing easing, CancellationToken cancel) =>
        new Animation
        {
            Duration = duration,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        }.RunAsync(target, cancel);

    private static Style Still<T>() where T : Visual =>
        new(x => x.OfType<T>().Not(y => y.Class("motion-keep")))
        {
            Setters = { new Setter(Animatable.TransitionsProperty, null) },
        };
}

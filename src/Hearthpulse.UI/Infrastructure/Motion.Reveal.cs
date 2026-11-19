using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;

namespace Hearthpulse.UI.Infrastructure;

/// <summary>
/// The reveal in place, as the WPF app does it: a view that comes into a place it shares with views stacked beside it
/// (a tab's view, a section a toggle opens, the content that replaces "Loading") fades in and rises over
/// <see cref="Enter"/> once it has been laid out and drawn, held unseen until then; the view that goes hides at once.
/// Opacity and a render transform only, so nothing is laid out again. A view on <see cref="RevealOnShowProperty"/>
/// does it by itself whenever its own <c>IsVisible</c> turns true; code calls <see cref="Reveal"/>.
/// It plays only where it would be seen: not with motion off, not before the view is on screen (its first show, a
/// window opening on a saved tab), not under a parent hidden or held unseen (a page coming in carries its views with
/// it). Every reveal of a view has a turn: hidden or shown again before its motion started, the older reveal never
/// starts, and a running one stops where it stands, so fast toggling leaves the view at rest.
/// </summary>
public static partial class Motion
{
    /// <summary>How far a tab's view or a section opened in place rises as it comes in, in DIP (a page rises <see cref="Rise"/>).</summary>
    public const double TabRise = 8;

    /// <summary>
    /// True: the element reveals itself (<see cref="Reveal"/>, <see cref="TabRise"/>) each time its own
    /// <c>IsVisible</c> turns true, and rests at once when it turns false. A parent shown or hidden does not count.
    /// </summary>
    public static readonly AttachedProperty<bool> RevealOnShowProperty = AvaloniaProperty.RegisterAttached<Visual, bool>("RevealOnShow", typeof(Motion));

    /// <summary>The element's reveal: its turn and the motion running on it.</summary>
    private static readonly AttachedProperty<RevealRun?> RevealRunProperty = AvaloniaProperty.RegisterAttached<Visual, RevealRun?>("RevealRun", typeof(Motion));

    static Motion() => RevealOnShowProperty.Changed.AddClassHandler<Visual>(OnRevealOnShowChanged);

    [ThreadStatic]
    private static bool? _revealMotion;

    /// <summary>
    /// A test's say on whether reveals animate on its thread (null: <see cref="On"/>): one thread per WPF test window,
    /// so a test never changes another's.
    /// </summary>
    internal static bool? RevealMotion { get => _revealMotion; set => _revealMotion = value; }

    private static bool Reveals => _revealMotion ?? On;

    public static bool GetRevealOnShow(Visual element) => element.GetValue(RevealOnShowProperty);

    public static void SetRevealOnShow(Visual element, bool value) => element.SetValue(RevealOnShowProperty, value);

    /// <summary>
    /// The element coming in fades in and rises <paramref name="rise"/> DIP over <see cref="Enter"/>, starting once it
    /// has been laid out and drawn (<see cref="AfterLayout"/>): a first opening can lay out for hundreds of ms, and
    /// motion started before it would be over by its first frame. Where it would not be seen, or with motion off, it is
    /// simply shown at rest.
    /// </summary>
    public static void Reveal(Visual element, double rise = TabRise)
    {
        var run = Rest(element);
        if (!Reveals || !Seen(element))
        {
            return;
        }

        var shift = ShiftOf(element);
        element.Opacity = 0;
        shift.Y = rise;
        var turn = run.Turn;
        AfterLayout(() =>
        {
            // Hidden, shown or revealed again since: that change decides (and has set the element's values).
            if (run.Turn != turn)
            {
                return;
            }

            // The end values under the motion: cancelled or done, the element is at rest.
            element.Opacity = 1;
            shift.Y = 0;
            if (Reveals && element.IsVisible)
            {
                var cancel = run.Start();
                FadeAsync(element, 0, 1, Enter, cancel).Forget("Reveal");
                LiftAsync(element, rise, cancel).Forget("Reveal rise");
            }
        });
    }

    /// <summary>A new turn for the element: its reveal still to start never does, a running one stops, and it is at rest.</summary>
    private static RevealRun Rest(Visual element)
    {
        var run = element.GetValue(RevealRunProperty);
        if (run is null)
        {
            run = new RevealRun();
            element.SetValue(RevealRunProperty, run);
        }

        run.Turn++;
        run.Stop();
        element.Opacity = 1;
        if (element.RenderTransform is TranslateTransform shift)
        {
            shift.Y = 0;
        }

        return run;
    }

    /// <summary>
    /// Whether a reveal of <paramref name="element"/> would be seen: its parent is on screen and nothing above it is held
    /// unseen (a page or section coming in reveals what it holds with it).
    /// </summary>
    private static bool Seen(Visual element)
    {
        if (element.GetVisualParent() is not { IsEffectivelyVisible: true } parent || TopLevel.GetTopLevel(element) is not { IsVisible: true })
        {
            return false;
        }

        for (var up = parent; up is not null; up = up.GetVisualParent())
        {
            if (up.Opacity <= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The element's own translate transform (another one is replaced).</summary>
    private static TranslateTransform ShiftOf(Visual element)
    {
        if (element.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            element.RenderTransform = shift;
        }

        return shift;
    }

    private static void OnRevealOnShowChanged(Visual element, AvaloniaPropertyChangedEventArgs e)
    {
        // The element holds the handler, a static method: nothing outside it is kept alive, nothing to unsubscribe.
        element.PropertyChanged -= OnOwnVisibility;
        if (e.NewValue is true)
        {
            element.PropertyChanged += OnOwnVisibility;
        }
    }

    private static void OnOwnVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Visual element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            Reveal(element, TabRise);
        }
        else
        {
            // Gone at once, and at rest for when it comes back.
            Rest(element);
        }
    }

    /// <summary>One element's reveal turn and the motion running on it.</summary>
    private sealed class RevealRun
    {
        private CancellationTokenSource? _motion;

        public int Turn { get; set; }

        /// <summary>Stops the running motion and returns the token of the next one.</summary>
        public CancellationToken Start()
        {
            Stop();
            _motion = new CancellationTokenSource();
            return _motion.Token;
        }

        public void Stop()
        {
            _motion?.Cancel();
            _motion?.Dispose();
            _motion = null;
        }
    }
}

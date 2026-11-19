using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// The reveal in place: a view that comes into a place it shares with views stacked beside it (a tab's view, a section
/// a toggle opens, the content that replaces "Loading") fades in and rises over <see cref="Enter"/> once it has been
/// laid out and drawn, held unseen until then; the view that goes collapses at once. Opacity and a render transform
/// only, so nothing is laid out again. A view on <see cref="RevealOnShowProperty"/> does it by itself whenever its own
/// <c>Visibility</c> turns visible; code calls <see cref="Reveal"/>.
/// It plays only where it would be seen: not with motion off, not before the window is on screen (its first show, a
/// window opening on a saved tab), not under a parent hidden or held unseen (a page coming in carries its views with
/// it, and a section shown with its page has no motion of its own). Every reveal of a view has a turn: hidden or shown
/// again before its motion started, the older reveal never starts, so fast toggling leaves the view at rest.
/// </summary>
public static partial class Motion
{
    /// <summary>How far a tab's view or a section opened in place rises as it comes in, in DIP (a page rises <see cref="Rise"/>).</summary>
    public const double TabRise = 8;

    /// <summary>
    /// True: the element reveals itself (<see cref="Reveal"/>, <see cref="TabRise"/>) each time its own visibility
    /// turns visible, and rests at once when it is hidden. A parent shown or hidden does not count.
    /// </summary>
    public static readonly DependencyProperty RevealOnShowProperty = DependencyProperty.RegisterAttached(
        "RevealOnShow", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRevealOnShowChanged));

    /// <summary>
    /// The element's own Visibility, followed through a binding to the element itself: no handler outside it holds the
    /// element, so nothing is left to unsubscribe. Untyped so its first value (from null) tells the build from a change.
    /// </summary>
    private static readonly DependencyProperty OwnVisibilityProperty = DependencyProperty.RegisterAttached(
        "OwnVisibility", typeof(object), typeof(Motion), new PropertyMetadata(null, OnOwnVisibilityChanged));

    /// <summary>The element's reveal turn: a reveal whose turn has passed never starts its motion.</summary>
    private static readonly DependencyProperty RevealTurnProperty = DependencyProperty.RegisterAttached(
        "RevealTurn", typeof(int), typeof(Motion), new PropertyMetadata(0));

    [ThreadStatic]
    private static bool? _revealMotion;

    /// <summary>
    /// A test's say on whether reveals animate on its thread (null: <see cref="On"/>): one thread per WPF test window,
    /// so a test never changes another's.
    /// </summary>
    internal static bool? RevealMotion { get => _revealMotion; set => _revealMotion = value; }

    private static bool Reveals => _revealMotion ?? On;

    public static bool GetRevealOnShow(UIElement element) => (bool)element.GetValue(RevealOnShowProperty);

    public static void SetRevealOnShow(UIElement element, bool value) => element.SetValue(RevealOnShowProperty, value);

    /// <summary>
    /// The element coming in fades in and rises <paramref name="rise"/> DIP over <see cref="Enter"/>, starting once it
    /// has been laid out and drawn (<see cref="AfterLayout"/>): a first opening can lay out for hundreds of ms, and
    /// motion started before it would be over by its first frame. Where it would not be seen, or with motion off, it is
    /// simply shown at rest.
    /// </summary>
    public static void Reveal(UIElement element, double rise = Rise)
    {
        var turn = NextRevealTurn(element);
        if (!Reveals || !Seen(element))
        {
            Still(element);
            return;
        }

        Hold(element, rise);
        AfterLayout(element, () =>
        {
            // Hidden, shown or revealed again since: that change decides.
            if ((int)element.GetValue(RevealTurnProperty) != turn)
            {
                return;
            }

            if (Reveals && element.Visibility == Visibility.Visible)
            {
                FadeIn(element);
            }
            else
            {
                Still(element);
            }
        });
    }

    /// <summary>
    /// Whether a reveal of <paramref name="element"/> would be seen: its parent is on screen and nothing above it is held
    /// unseen (a page or section coming in reveals what it holds with it).
    /// </summary>
    private static bool Seen(UIElement element)
    {
        if (VisualTreeHelper.GetParent(element) is not UIElement { IsVisible: true } parent)
        {
            return false;
        }

        for (DependencyObject? up = parent; up is not null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is UIElement { Opacity: <= 0 })
            {
                return false;
            }
        }

        return true;
    }

    private static int NextRevealTurn(UIElement element)
    {
        var turn = (int)element.GetValue(RevealTurnProperty) + 1;
        element.SetValue(RevealTurnProperty, turn);
        return turn;
    }

    private static void OnRevealOnShowChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not UIElement element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            BindingOperations.SetBinding(element, OwnVisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = element, Mode = BindingMode.OneWay });
        }
        else
        {
            BindingOperations.ClearBinding(element, OwnVisibilityProperty);
        }
    }

    private static void OnOwnVisibilityChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        // The first value is the element's as built, not a change.
        if (e.OldValue is null || target is not UIElement element)
        {
            return;
        }

        if (e.NewValue is Visibility.Visible)
        {
            Reveal(element, TabRise);
        }
        else
        {
            // Gone at once, and at rest for when it comes back; a reveal still to start never does.
            NextRevealTurn(element);
            Still(element);
        }
    }
}

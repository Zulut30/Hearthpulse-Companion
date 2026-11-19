using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// Calm content changes inside a page, set from XAML; the twin of the Avalonia helper of the same name. Content that
/// waits for new data stays where it stands, dimmed (<see cref="DimProperty"/>), and a busy cue keeps its room at all
/// times and shows by its opacity (<see cref="LitProperty"/>), so neither moves the layout. A part that comes in
/// (<see cref="ShownProperty"/>) or whose content was replaced (<see cref="RefreshProperty"/>) fades in once it has been
/// laid out (<see cref="Motion.AfterLayout"/>), so a long layout eats none of the motion; a section that loads keeps its
/// height until its content is in (<see cref="PendingProperty"/>). Opacity and a render transform only, eased out; the
/// first value a part gets is shown as it is, and with motion off every change is instant.
/// </summary>
public static class ContentMotion
{
    /// <summary>The opacity of content that waits for new data: still readable, plainly not current.</summary>
    public const double DimOpacity = 0.45;

    /// <summary>Content that waits for new data: dimmed at once, back to full once the new content is laid out.</summary>
    public static readonly DependencyProperty DimProperty =
        DependencyProperty.RegisterAttached("Dim", typeof(bool), typeof(ContentMotion), new PropertyMetadata(false, OnDim));

    /// <summary>
    /// A busy cue that keeps its room: shown by opacity, hidden (<see cref="Visibility.Hidden"/>, still laid out) when
    /// off. Unset, the element is left as it is; the first value is shown at once.
    /// </summary>
    public static readonly DependencyProperty LitProperty =
        DependencyProperty.RegisterAttached("Lit", typeof(bool?), typeof(ContentMotion), new PropertyMetadata(null, OnLit));

    /// <summary>
    /// The element's visibility, in place of a <c>Visibility</c> binding: shown, it fades in (rising <see cref="RiseProperty"/>
    /// DIP) once laid out; hidden, it is collapsed at once. The first value is applied without motion.
    /// </summary>
    public static readonly DependencyProperty ShownProperty =
        DependencyProperty.RegisterAttached("Shown", typeof(bool?), typeof(ContentMotion), new PropertyMetadata(null, OnShown));

    /// <summary>How far an element that comes in (<see cref="ShownProperty"/>) rises to its place, in DIP; 0 fades only.</summary>
    public static readonly DependencyProperty RiseProperty =
        DependencyProperty.RegisterAttached("Rise", typeof(double), typeof(ContentMotion), new PropertyMetadata(0d));

    /// <summary>
    /// What the element's content stands for (an archetype, a tab): when it changes, the new content fades in once laid
    /// out. The first value is shown as it is.
    /// </summary>
    public static readonly DependencyProperty RefreshProperty =
        DependencyProperty.RegisterAttached("Refresh", typeof(object), typeof(ContentMotion), new PropertyMetadata(Unset.Value, OnRefresh));

    /// <summary>
    /// A section whose content is loading: it keeps the height it had (at least <see cref="ReserveProperty"/>) and is
    /// dimmed; once loaded it lets the height go and comes back to full after its content is laid out.
    /// </summary>
    public static readonly DependencyProperty PendingProperty =
        DependencyProperty.RegisterAttached("Pending", typeof(bool?), typeof(ContentMotion), new PropertyMetadata(null, OnPending));

    /// <summary>The least height a loading section (<see cref="PendingProperty"/>) keeps, for its first load.</summary>
    public static readonly DependencyProperty ReserveProperty =
        DependencyProperty.RegisterAttached("Reserve", typeof(double), typeof(ContentMotion), new PropertyMetadata(0d));

    /// <summary>Each element's turn: a deferred motion started for an older turn does not run.</summary>
    private static readonly DependencyProperty TurnProperty =
        DependencyProperty.RegisterAttached("Turn", typeof(int), typeof(ContentMotion), new PropertyMetadata(0));

    /// <summary>The opacity a content change starts from: the new content fades in from nothing.</summary>
    private const double Start = 0;

    public static bool GetDim(DependencyObject element) => (bool)element.GetValue(DimProperty);

    public static void SetDim(DependencyObject element, bool value) => element.SetValue(DimProperty, value);

    public static bool? GetLit(DependencyObject element) => (bool?)element.GetValue(LitProperty);

    public static void SetLit(DependencyObject element, bool? value) => element.SetValue(LitProperty, value);

    public static bool? GetShown(DependencyObject element) => (bool?)element.GetValue(ShownProperty);

    public static void SetShown(DependencyObject element, bool? value) => element.SetValue(ShownProperty, value);

    public static double GetRise(DependencyObject element) => (double)element.GetValue(RiseProperty);

    public static void SetRise(DependencyObject element, double value) => element.SetValue(RiseProperty, value);

    public static object? GetRefresh(DependencyObject element) => element.GetValue(RefreshProperty);

    public static void SetRefresh(DependencyObject element, object? value) => element.SetValue(RefreshProperty, value);

    public static bool? GetPending(DependencyObject element) => (bool?)element.GetValue(PendingProperty);

    public static void SetPending(DependencyObject element, bool? value) => element.SetValue(PendingProperty, value);

    public static double GetReserve(DependencyObject element) => (double)element.GetValue(ReserveProperty);

    public static void SetReserve(DependencyObject element, double value) => element.SetValue(ReserveProperty, value);

    /// <summary>The opacity the element rests at: dimmed while it waits, else full.</summary>
    private static double Rest(UIElement element) => GetDim(element) || GetPending(element) == true ? DimOpacity : 1;

    private static void OnDim(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            // Dimmed at once, where the reader looks; the way back waits for the new content's layout.
            Settle(element, (bool)e.NewValue ? Motion.Fast : Motion.Enter, afterLayout: !(bool)e.NewValue);
        }
    }

    private static void OnLit(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not bool lit)
        {
            return;
        }

        var turn = NextTurn(element);
        var target = lit ? 1d : 0d;
        if (lit)
        {
            element.Visibility = Visibility.Visible;
        }

        if (e.OldValue is null || !Moving(element))
        {
            Place(element, target);
            if (!lit)
            {
                element.Visibility = Visibility.Hidden;
            }

            return;
        }

        Fade(element, target, lit ? Motion.Fast : Motion.Settle, () =>
        {
            // Hidden only when still off: a cue lit again meanwhile stays.
            if (GetTurn(element) == turn && GetLit(element) == false)
            {
                element.Visibility = Visibility.Hidden;
            }
        });
    }

    private static void OnShown(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not bool shown)
        {
            return;
        }

        var turn = NextTurn(element);
        var parentShown = VisualTreeHelper.GetParent(element) is UIElement { IsVisible: true };
        if (!shown || e.OldValue is null || element.Visibility == Visibility.Visible || !Motion.On || !parentShown)
        {
            element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            Place(element, Rest(element));
            Shift(element, 0);
            return;
        }

        // Held unseen and low until laid out, then in: the reader never sees it jump into place.
        Place(element, 0);
        Shift(element, GetRise(element));
        element.Visibility = Visibility.Visible;
        Motion.AfterLayout(element, () =>
        {
            if (GetTurn(element) == turn)
            {
                Enter(element);
            }
        });
    }

    private static void OnRefresh(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.OldValue == Unset.Value || Equals(e.OldValue, e.NewValue) || !Moving(element))
        {
            return;
        }

        var turn = NextTurn(element);
        Place(element, Start);
        Motion.AfterLayout(element, () =>
        {
            if (GetTurn(element) == turn)
            {
                Fade(element, Rest(element), Motion.Enter);
            }
        });
    }

    private static void OnPending(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not bool pending)
        {
            return;
        }

        if (pending)
        {
            // The height it stands at now (the last content's), or its reserve the first time.
            element.MinHeight = Math.Max(element.ActualHeight, GetReserve(element));
            Settle(element, Motion.Fast, afterLayout: false, instant: e.OldValue is null);
            return;
        }

        // The content is in: the height goes with this layout pass, and the section comes back to full after it.
        element.ClearValue(FrameworkElement.MinHeightProperty);
        Settle(element, Motion.Enter, afterLayout: true, instant: e.OldValue is null);
    }

    /// <summary>Brings the element to its resting opacity over <paramref name="duration"/>, or at once.</summary>
    private static void Settle(UIElement element, TimeSpan duration, bool afterLayout, bool instant = false)
    {
        var turn = NextTurn(element);
        if (instant || !Moving(element))
        {
            Place(element, Rest(element));
            return;
        }

        if (!afterLayout)
        {
            Fade(element, Rest(element), duration);
            return;
        }

        Motion.AfterLayout(element, () =>
        {
            if (GetTurn(element) == turn)
            {
                Fade(element, Rest(element), duration);
            }
        });
    }

    /// <summary>Fades in and rises to its place over <see cref="Motion.Enter"/>, from wherever it stands.</summary>
    private static void Enter(UIElement element)
    {
        if (!Motion.On)
        {
            Place(element, Rest(element));
            Shift(element, 0);
            return;
        }

        Fade(element, Rest(element), Motion.Enter);
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } shift)
        {
            var rise = new DoubleAnimation { From = shift.Y, To = 0, Duration = Motion.Enter, EasingFunction = Motion.Travel };
            shift.Y = 0;
            shift.BeginAnimation(TranslateTransform.YProperty, rise);
        }
    }

    /// <summary>Animates the opacity from where it stands to <paramref name="to"/>; the end value is the element's own.</summary>
    private static void Fade(UIElement element, double to, TimeSpan duration, Action? done = null)
    {
        var from = element.Opacity;
        var fade = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = Motion.Ease };
        if (done is not null)
        {
            fade.Completed += (_, _) => done();
        }

        element.Opacity = to;
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Sets the opacity with no motion on it.</summary>
    private static void Place(UIElement element, double opacity)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
    }

    /// <summary>Sets the rise offset with no motion on it; an element with a transform of its own is not shifted.</summary>
    private static void Shift(UIElement element, double y)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = y;
        }
        else if (y != 0 && (element.RenderTransform is null || element.RenderTransform == Transform.Identity))
        {
            element.RenderTransform = new TranslateTransform(0, y);
        }
    }

    /// <summary>Motion only on screen: an element out of sight takes its end state at once.</summary>
    private static bool Moving(UIElement element) => Motion.On && element.IsVisible;

    private static int GetTurn(UIElement element) => (int)element.GetValue(TurnProperty);

    private static int NextTurn(UIElement element)
    {
        var turn = GetTurn(element) + 1;
        element.SetValue(TurnProperty, turn);
        return turn;
    }

    /// <summary>The default of <see cref="RefreshProperty"/>: no value seen yet, so the first one is not a change.</summary>
    private sealed class Unset
    {
        public static readonly Unset Value = new();
    }
}

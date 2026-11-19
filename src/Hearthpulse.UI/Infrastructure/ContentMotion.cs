using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;

namespace Hearthpulse.UI.Infrastructure;

/// <summary>
/// Calm content changes inside a page, set from XAML; the twin of the WPF helper of the same name. Content that waits
/// for new data stays where it stands, dimmed (<see cref="DimProperty"/>), and a busy cue keeps its room at all times and
/// shows by its opacity (<see cref="LitProperty"/>), so neither moves the layout. A part that comes in
/// (<see cref="ShownProperty"/>, <see cref="Reveal"/>) or whose content was replaced (<see cref="RefreshProperty"/>) fades
/// in once it has been laid out (<see cref="Motion.AfterLayout"/>), so a long layout eats none of the motion; a section
/// that loads keeps its height until its content is in (<see cref="PendingProperty"/>). Opacity and a render transform
/// only, eased out; the first value a part gets is shown as it is, and with motion off every change is instant.
/// </summary>
public static class ContentMotion
{
    /// <summary>The opacity of content that waits for new data: still readable, plainly not current.</summary>
    public const double DimOpacity = 0.45;

    /// <summary>Content that waits for new data: dimmed at once, back to full once the new content is laid out.</summary>
    public static readonly AttachedProperty<bool> DimProperty = AvaloniaProperty.RegisterAttached<Control, bool>("Dim", typeof(ContentMotion));

    /// <summary>A busy cue that keeps its room, shown by opacity. Unset, the element is left as it is; the first value is shown at once.</summary>
    public static readonly AttachedProperty<bool?> LitProperty = AvaloniaProperty.RegisterAttached<Control, bool?>("Lit", typeof(ContentMotion));

    /// <summary>
    /// The element's visibility, in place of an <c>IsVisible</c> binding: shown, it fades in (rising <see cref="RiseProperty"/>
    /// DIP) once laid out; hidden, it goes at once. The first value is applied without motion.
    /// </summary>
    public static readonly AttachedProperty<bool?> ShownProperty = AvaloniaProperty.RegisterAttached<Control, bool?>("Shown", typeof(ContentMotion));

    /// <summary>How far an element that comes in (<see cref="ShownProperty"/>) rises to its place, in DIP; 0 fades only.</summary>
    public static readonly AttachedProperty<double> RiseProperty = AvaloniaProperty.RegisterAttached<Control, double>("Rise", typeof(ContentMotion));

    /// <summary>
    /// What the element's content stands for (an archetype, a tab): when it changes, the new content fades in once laid
    /// out. The first value is shown as it is.
    /// </summary>
    public static readonly AttachedProperty<object?> RefreshProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("Refresh", typeof(ContentMotion), Unset.Value);

    /// <summary>
    /// A section whose content is loading: it keeps the height it had (at least <see cref="ReserveProperty"/>) and is
    /// dimmed; once loaded it lets the height go and comes back to full after its content is laid out.
    /// </summary>
    public static readonly AttachedProperty<bool?> PendingProperty = AvaloniaProperty.RegisterAttached<Control, bool?>("Pending", typeof(ContentMotion));

    /// <summary>The least height a loading section (<see cref="PendingProperty"/>) keeps, for its first load.</summary>
    public static readonly AttachedProperty<double> ReserveProperty = AvaloniaProperty.RegisterAttached<Control, double>("Reserve", typeof(ContentMotion));

    /// <summary>The opacity a content change starts from: the new content fades in from nothing.</summary>
    private const double Start = 0;

    /// <summary>Each element's turn and running motion.</summary>
    private static readonly ConditionalWeakTable<Control, State> States = new();

    static ContentMotion()
    {
        DimProperty.Changed.AddClassHandler<Control>(OnDim);
        LitProperty.Changed.AddClassHandler<Control>(OnLit);
        ShownProperty.Changed.AddClassHandler<Control>(OnShown);
        RefreshProperty.Changed.AddClassHandler<Control>(OnRefresh);
        PendingProperty.Changed.AddClassHandler<Control>(OnPending);
    }

    public static bool GetDim(Control element) => element.GetValue(DimProperty);

    public static void SetDim(Control element, bool value) => element.SetValue(DimProperty, value);

    public static bool? GetLit(Control element) => element.GetValue(LitProperty);

    public static void SetLit(Control element, bool? value) => element.SetValue(LitProperty, value);

    public static bool? GetShown(Control element) => element.GetValue(ShownProperty);

    public static void SetShown(Control element, bool? value) => element.SetValue(ShownProperty, value);

    public static double GetRise(Control element) => element.GetValue(RiseProperty);

    public static void SetRise(Control element, double value) => element.SetValue(RiseProperty, value);

    public static object? GetRefresh(Control element) => element.GetValue(RefreshProperty);

    public static void SetRefresh(Control element, object? value) => element.SetValue(RefreshProperty, value);

    public static bool? GetPending(Control element) => element.GetValue(PendingProperty);

    public static void SetPending(Control element, bool? value) => element.SetValue(PendingProperty, value);

    public static double GetReserve(Control element) => element.GetValue(ReserveProperty);

    public static void SetReserve(Control element, double value) => element.SetValue(ReserveProperty, value);

    /// <summary>
    /// A part that has just been shown in place of another (a tab of a page) fades in and rises <paramref name="rise"/> DIP
    /// once it has been laid out, as the WPF app's <c>Motion.Reveal</c> does; at once with motion off or off screen.
    /// </summary>
    public static void Reveal(Control element, double rise)
    {
        var turn = Of(element).Next();
        if (!Moving(element))
        {
            Place(element, Rest(element));
            Shift(element, 0);
            return;
        }

        Place(element, 0);
        Shift(element, rise);
        Motion.AfterLayout(() =>
        {
            if (Of(element).Turn == turn)
            {
                Enter(element);
            }
        });
    }

    /// <summary>The opacity the element rests at: dimmed while it waits, else full.</summary>
    private static double Rest(Control element) => GetDim(element) || GetPending(element) == true ? DimOpacity : 1;

    private static void OnDim(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        // Dimmed at once, where the reader looks; the way back waits for the new content's layout.
        var dim = e.GetNewValue<bool>();
        Settle(element, dim ? Motion.Fast : Motion.Enter, afterLayout: !dim);
    }

    private static void OnLit(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool?>() is not { } lit)
        {
            return;
        }

        Of(element).Next();
        if (e.GetOldValue<bool?>() is null || !Moving(element))
        {
            Place(element, lit ? 1 : 0);
            return;
        }

        Fade(element, lit ? 1 : 0, lit ? Motion.Fast : Motion.Settle);
    }

    private static void OnShown(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool?>() is not { } shown)
        {
            return;
        }

        Of(element).Next();
        var parentShown = element.GetVisualParent() is Visual { IsEffectivelyVisible: true };
        if (!shown || e.GetOldValue<bool?>() is null || element.IsVisible || !Motion.On || !parentShown)
        {
            element.IsVisible = shown;
            Place(element, Rest(element));
            Shift(element, 0);
            return;
        }

        // Held unseen and low until laid out, then in: the reader never sees it jump into place.
        element.IsVisible = true;
        Reveal(element, GetRise(element));
    }

    private static void OnRefresh(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        var (old, now) = (e.GetOldValue<object?>(), e.GetNewValue<object?>());
        if (old == Unset.Value || Equals(old, now) || !Moving(element))
        {
            return;
        }

        var turn = Of(element).Next();
        Place(element, Start);
        Motion.AfterLayout(() =>
        {
            if (Of(element).Turn == turn)
            {
                Fade(element, Rest(element), Motion.Enter);
            }
        });
    }

    private static void OnPending(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool?>() is not { } pending)
        {
            return;
        }

        var first = e.GetOldValue<bool?>() is null;
        if (pending)
        {
            // The height it stands at now (the last content's), or its reserve the first time.
            element.MinHeight = Math.Max(element.Bounds.Height, GetReserve(element));
            Settle(element, Motion.Fast, afterLayout: false, instant: first);
            return;
        }

        // The content is in: the height goes with this layout pass, and the section comes back to full after it.
        element.ClearValue(Layoutable.MinHeightProperty);
        Settle(element, Motion.Enter, afterLayout: true, instant: first);
    }

    /// <summary>Brings the element to its resting opacity over <paramref name="duration"/>, or at once.</summary>
    private static void Settle(Control element, TimeSpan duration, bool afterLayout, bool instant = false)
    {
        var turn = Of(element).Next();
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

        Motion.AfterLayout(() =>
        {
            if (Of(element).Turn == turn)
            {
                Fade(element, Rest(element), duration);
            }
        });
    }

    /// <summary>Fades in and rises to its place over <see cref="Motion.Enter"/>, from wherever it stands.</summary>
    private static void Enter(Control element)
    {
        if (!Motion.On)
        {
            Place(element, Rest(element));
            Shift(element, 0);
            return;
        }

        var cancel = Fade(element, Rest(element), Motion.Enter);
        if (element.RenderTransform is TranslateTransform shift && shift.Y != 0)
        {
            var from = shift.Y;
            shift.Y = 0;
            Motion.LiftAsync(element, from, cancel).Forget("Content rise");
        }
    }

    /// <summary>
    /// Animates the opacity from where it stands to <paramref name="to"/>, stopping the element's running motion; the end
    /// value is set under the motion. Returns the token that stops this motion.
    /// </summary>
    private static CancellationToken Fade(Control element, double to, TimeSpan duration)
    {
        var from = element.Opacity;
        var cancel = Of(element).Start();
        element.Opacity = to;
        Motion.FadeAsync(element, from, to, duration, cancel).Forget("Content fade");
        return cancel;
    }

    /// <summary>Sets the opacity with no motion on it.</summary>
    private static void Place(Control element, double opacity)
    {
        Of(element).Stop();
        element.Opacity = opacity;
    }

    /// <summary>Sets the rise offset; an element with a transform of its own is not shifted.</summary>
    private static void Shift(Control element, double y)
    {
        if (element.RenderTransform is TranslateTransform shift)
        {
            shift.Y = y;
        }
        else if (y != 0 && element.RenderTransform is null)
        {
            element.RenderTransform = new TranslateTransform(0, y);
        }
    }

    /// <summary>Motion only on screen: an element out of sight takes its end state at once.</summary>
    private static bool Moving(Control element) => Motion.On && element.IsEffectivelyVisible;

    private static State Of(Control element) => States.GetValue(element, _ => new State());

    /// <summary>One element's turn and the motion running on it.</summary>
    private sealed class State
    {
        private CancellationTokenSource? _run;

        public int Turn { get; private set; }

        /// <summary>A new turn: a deferred motion of an older one does not run.</summary>
        public int Next() => ++Turn;

        /// <summary>Stops the running motion and returns the token of the next one.</summary>
        public CancellationToken Start()
        {
            Stop();
            _run = new CancellationTokenSource();
            return _run.Token;
        }

        public void Stop()
        {
            _run?.Cancel();
            _run?.Dispose();
            _run = null;
        }
    }

    /// <summary>The default of <see cref="RefreshProperty"/>: no value seen yet, so the first one is not a change.</summary>
    private sealed class Unset
    {
        public static readonly Unset Value = new();
    }
}

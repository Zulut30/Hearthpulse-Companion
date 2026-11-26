using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Infrastructure;

/// <summary>
/// A class by the element's own width, where the window's width says too little: <c>i:Tight.Below="400"</c> gives the
/// element the class <c>tight</c> while it is narrower than 400 DIP. A row in one of two cards side by side is as narrow
/// at the WPF minimum window (1040, still the wide layout) as in compact, so its styles drop columns and wrap text by
/// this class, not by the window's (the group rows of My stats, StatRowStyles.axaml).
/// </summary>
public static class Tight
{
    /// <summary>The width under which the element is <c>tight</c>; NaN (the default) never.</summary>
    public static readonly AttachedProperty<double> BelowProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Below", typeof(Tight), double.NaN);

    static Tight() => BelowProperty.Changed.AddClassHandler<Control>(OnBelowChanged);

    public static double GetBelow(Control element) => element.GetValue(BelowProperty);

    public static void SetBelow(Control element, double value) => element.SetValue(BelowProperty, value);

    private static void OnBelowChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        element.SizeChanged -= OnSizeChanged;
        if (!double.IsNaN(GetBelow(element)))
        {
            element.SizeChanged += OnSizeChanged;
        }

        Apply(element, element.Bounds.Width);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control element)
        {
            Apply(element, e.NewSize.Width);
        }
    }

    /// <summary>Not laid out yet (width 0): not tight, so a row is first measured as it is on a wide card.</summary>
    private static void Apply(Control element, double width) => element.Classes.Set("tight", width > 0 && width < GetBelow(element));
}

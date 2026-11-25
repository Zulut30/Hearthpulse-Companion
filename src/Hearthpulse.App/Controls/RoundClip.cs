using System.Windows;
using System.Windows.Media;

namespace Hearthpulse.App.Controls;

/// <summary>
/// Clips an element to a rounded rectangle that follows its size. WPF's ClipToBounds clips to the square
/// layout box and ignores Border.CornerRadius, so card art inside a rounded row would draw square corners.
/// <para><see cref="PillProperty"/> makes the element a capsule whatever its size: the clip and, on a Border, the corner
/// radius follow half the shorter side. WPF's Border clamps an oversized radius (RadiusPill, 999) per axis, so a wide
/// row drew an ellipse instead of a capsule (TASK-2026-11-25-polish S6); Avalonia scales it to a capsule by itself.</para>
/// </summary>
public static class RoundClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundClip), new PropertyMetadata(0.0, OnRadiusChanged));

    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    public static readonly DependencyProperty PillProperty = DependencyProperty.RegisterAttached(
        "Pill", typeof(bool), typeof(RoundClip), new PropertyMetadata(false, OnRadiusChanged));

    public static bool GetPill(DependencyObject element) => (bool)element.GetValue(PillProperty);

    public static void SetPill(DependencyObject element, bool value) => element.SetValue(PillProperty, value);

    /// <summary>The capsule radius of a <paramref name="width"/> by <paramref name="height"/> box: half its shorter side.</summary>
    public static double PillRadius(double width, double height) => Math.Min(width, height) / 2;

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;
        if (GetPill(element) || GetRadius(element) > 0)
        {
            element.SizeChanged += OnSizeChanged;
            Apply(element);
        }
        else
        {
            element.ClearValue(UIElement.ClipProperty);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = GetRadius(element);
        if (GetPill(element))
        {
            radius = PillRadius(width, height);
            if (element is System.Windows.Controls.Border border)
            {
                border.SetCurrentValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(radius));
            }
        }

        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}

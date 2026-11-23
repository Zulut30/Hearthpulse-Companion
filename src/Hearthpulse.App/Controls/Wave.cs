using System.Windows;
using System.Windows.Media;

namespace Hearthpulse.App.Controls;

/// <summary>
/// A gold rule that fills its width: a small diamond at the left end and a hairline that fades out to the right.
/// (The name is kept from the old wavy divider so existing XAML keeps working.)
/// </summary>
public sealed class Wave : FrameworkElement
{
    /// <summary>Edge of the diamond before rotation; the rotated square spans about 7 px.</summary>
    private const double DiamondSide = 5;

    private static readonly double DiamondHalf = DiamondSide / Math.Sqrt(2);

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Wave),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Colour of the rule at its left end (defaults to the theme's GoldTrim).</summary>
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>As tall as the diamond, plus a pixel.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 40 : availableSize.Width, Math.Ceiling(DiamondHalf * 2) + 1);

    protected override void OnRender(DrawingContext dc)
    {
        var stroke = Stroke ?? TryFindResource("GoldTrim") as Brush;
        if (stroke is not null)
        {
            Draw(dc, stroke, TryFindResource("Accent") as Brush ?? stroke, 0, ActualWidth, ActualHeight / 2);
        }
    }

    /// <summary>Diamond in <paramref name="diamond"/> at the left end, then a hairline in <paramref name="stroke"/> fading to transparent.</summary>
    public static void Draw(DrawingContext dc, Brush stroke, Brush diamond, double from, double to, double y)
    {
        var length = to - from;
        if (length < (DiamondHalf * 2) + 8)
        {
            return;
        }

        var cx = from + DiamondHalf + 0.5;
        dc.DrawGeometry(diamond, null, Diamond(cx, y));

        // A crisp 1 px line on the pixel grid, fading to the right.
        var start = cx + DiamondHalf + 3;
        var line = Math.Floor(y) + 0.5;
        if (stroke is SolidColorBrush solid)
        {
            dc.DrawRectangle(LineFill(solid.Color, solid.Opacity), null, new Rect(start, line - 0.5, to - start, 1));
            return;
        }

        var fade = new LinearGradientBrush(Colors.Black, Color.FromArgb(0, 0, 0, 0), new Point(start, 0), new Point(to, 0))
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        fade.Freeze();
        var pen = new Pen(stroke, 1);
        pen.Freeze();
        dc.PushOpacityMask(fade);
        dc.DrawLine(pen, new Point(start, line), new Point(to, line));
        dc.Pop();
    }

    // Ornaments also draw through the static Draw entry point. Keep the last shape and
    // colour on the drawing thread, without retaining controls or theme resources.
    [ThreadStatic]
    private static (double X, double Y, Geometry? Shape) _diamond;

    private static Geometry Diamond(double cx, double y)
    {
        if (_diamond.X == cx && _diamond.Y == y && _diamond.Shape is { } shape)
        {
            return shape;
        }

        var gem = new StreamGeometry();
        using (var ctx = gem.Open())
        {
            ctx.BeginFigure(new Point(cx, y - DiamondHalf), true, true);
            ctx.LineTo(new Point(cx + DiamondHalf, y), false, false);
            ctx.LineTo(new Point(cx, y + DiamondHalf), false, false);
            ctx.LineTo(new Point(cx - DiamondHalf, y), false, false);
        }

        gem.Freeze();
        _diamond = (cx, y, gem);
        return gem;
    }

    [ThreadStatic]
    private static (Color Color, double Opacity, LinearGradientBrush? Brush) _line;

    private static LinearGradientBrush LineFill(Color color, double opacity)
    {
        if (_line.Color == color && _line.Opacity == opacity && _line.Brush is { } cached)
        {
            return cached;
        }

        var clear = color;
        clear.ScA = 0;
        var brush = new LinearGradientBrush(color, clear, new Point(0, 0), new Point(1, 0)) { Opacity = opacity };
        brush.Freeze();
        _line = (color, opacity, brush);
        return brush;
    }
}

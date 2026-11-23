using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Controls;

/// <summary>
/// A gold rule that fills its width: a small diamond at the left end and a hairline that fades out to the right.
/// (The name is kept from the old wavy divider so existing XAML keeps working.)
/// </summary>
public sealed class Wave : Control
{
    /// <summary>Edge of the diamond before rotation; the rotated square spans about 7 px.</summary>
    private const double DiamondSide = 5;

    private static readonly double DiamondHalf = DiamondSide / Math.Sqrt(2);

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Wave, IBrush?>(nameof(Stroke));

    static Wave()
    {
        AffectsRender<Wave>(StrokeProperty);
    }

    /// <summary>Colour of the rule at its left end (defaults to the theme's GoldTrim).</summary>
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>As tall as the diamond, plus a pixel.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 40 : availableSize.Width, Math.Ceiling(DiamondHalf * 2) + 1);

    public override void Render(DrawingContext context)
    {
        Draw(context, Stroke ?? ThemeBrushes.Get("GoldTrim"), ThemeBrushes.Get("Accent"), 0, Bounds.Width, Bounds.Height / 2);
    }

    /// <summary>Diamond in <paramref name="diamond"/> at the left end, then a hairline in <paramref name="stroke"/> fading to transparent.</summary>
    public static void Draw(DrawingContext context, IBrush stroke, IBrush diamond, double from, double to, double y)
    {
        var length = to - from;
        if (length < (DiamondHalf * 2) + 8)
        {
            return;
        }

        var cx = from + DiamondHalf + 0.5;
        context.DrawGeometry(diamond, null, Diamond(cx, y));

        // A crisp 1 px line on the pixel grid, fading to the right.
        var start = cx + DiamondHalf + 3;
        var line = Math.Floor(y) + 0.5;
        if (stroke is ISolidColorBrush solid)
        {
            context.DrawRectangle(LineFill(solid.Color, solid.Opacity), null, new Rect(start, line - 0.5, to - start, 1));
            return;
        }

        var fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            },
        };
        using (context.PushOpacityMask(fade, new Rect(start, line - 1, to - start, 2)))
        {
            context.DrawLine(new Pen(stroke, 1), new Point(start, line), new Point(to, line));
        }
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
            ctx.BeginFigure(new Point(cx, y - DiamondHalf), true);
            ctx.LineTo(new Point(cx + DiamondHalf, y));
            ctx.LineTo(new Point(cx, y + DiamondHalf));
            ctx.LineTo(new Point(cx - DiamondHalf, y));
            ctx.EndFigure(true);
        }

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

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
            Opacity = opacity,
        };
        _line = (color, opacity, brush);
        return brush;
    }
}

using System.Windows.Media;
using Hearthpulse.App.Controls;

namespace Hearthpulse.Presentation.Tests;

public sealed class WaveTests
{
    [Fact]
    public void Solid_rule_tracks_changes_to_the_theme_brush_without_changing_an_earlier_drawing() => WpfThread.Run(() =>
    {
        var stroke = new SolidColorBrush(Colors.Gold) { Opacity = 0.75 };
        var first = DrawRule(stroke);
        stroke.Color = Colors.SlateBlue;
        stroke.Opacity = 0.5;
        var second = DrawRule(stroke);

        Assert.Equal(Colors.Gold, first.GradientStops[0].Color);
        Assert.Equal(0.75, first.Opacity);
        Assert.Equal(Colors.SlateBlue, second.GradientStops[0].Color);
        Assert.Equal(0.5, second.Opacity);
        Assert.Equal(0, second.GradientStops[1].Color.A);
    });

    private static LinearGradientBrush DrawRule(Brush stroke)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            Wave.Draw(context, stroke, Brushes.White, 10, 100, 4);
        }

        var rule = Assert.IsType<GeometryDrawing>(drawing.Children[1]);
        Assert.Equal(100, rule.Bounds.Right);
        Assert.Equal(1, rule.Bounds.Height);
        return Assert.IsType<LinearGradientBrush>(rule.Brush);
    }
}

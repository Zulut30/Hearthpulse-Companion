using Avalonia.Media;
using Hearthpulse.UI.Controls;

namespace Hearthpulse.Tests;

public sealed class WaveTests
{
    [Fact]
    public void Solid_rule_preserves_colour_opacity_and_fades_across_one_pixel_of_height() => LocBindingTests.Headless.Run(() =>
    {
        var stroke = new SolidColorBrush(Colors.SlateBlue) { Opacity = 0.6 };
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            Wave.Draw(context, stroke, Brushes.White, 10, 100, 4);
        }

        var rule = Assert.IsType<GeometryDrawing>(group.Children[1]);
        Assert.Equal(100, rule.Geometry!.Bounds.Right);
        Assert.Equal(1, rule.Geometry.Bounds.Height);
        var fill = Assert.IsType<LinearGradientBrush>(rule.Brush);
        Assert.Equal(0.6, fill.Opacity);
        Assert.Equal(Colors.SlateBlue, fill.GradientStops[0].Color);
        Assert.Equal(0, fill.GradientStops[1].Color.A);
    });
}

using Hearthpulse.App.Controls;

namespace Hearthpulse.Presentation.Tests;

public sealed class ScallopTests
{
    [Theory]
    [InlineData(0.35, 0)]
    [InlineData(0.1, 29)]
    public void Outline_depends_on_depth_and_rotation(double depth, double angle) => WpfThread.Run(() =>
    {
        var original = Scallop.Build(97, 83, 5, 0.1, 0);
        Assert.Same(original, Scallop.Build(97, 83, 5, 0.1, 0));
        Assert.NotEqual(original.Bounds, Scallop.Build(97, 83, 5, depth, angle).Bounds);
    });
}

using Hearthpulse.UI.Controls;

namespace Hearthpulse.Tests;

public sealed class ScallopTests
{
    [Theory]
    [InlineData(0.35, 0)]
    [InlineData(0.1, 29)]
    public void Outline_depends_on_depth_and_rotation(double depth, double angle) => LocBindingTests.Headless.Run(() =>
    {
        var original = Scallop.Build(97, 83, 5, 0.1, 0).Bounds;
        Assert.Equal(original, Scallop.Build(97, 83, 5, 0.1, 0).Bounds);
        Assert.NotEqual(original, Scallop.Build(97, 83, 5, depth, angle).Bounds);
    });
}

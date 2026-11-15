using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Overlay;

/// <summary>
/// Holds a movable panel's name tag and buttons (<c>PART_Chrome</c>) without taking part in the panel's size. With the
/// move hint and the buttons the tag is wider than most panels: as a plain template child it widened the panel when
/// Ctrl armed it, so a panel at its default spot moved with its content, and a drag saved the wider panel's spot.
/// Each child gets the strip's width, or its own when wider, and places itself in it by its alignment, moved by
/// <see cref="Offset"/>: the panel puts the tag under itself or lines it up with its right side at the screen's edges
/// (<see cref="PanelLayout.TagSpot"/>).
/// </summary>
public sealed class PanelTagStrip : Panel
{
    private Point _offset = new(-PanelLayout.TagInset, -PanelLayout.TagHeight);

    /// <summary>Where the tag goes from the panel's top-left corner, in the strip's own units; above it at its left edge by default.</summary>
    public Point Offset
    {
        get => _offset;
        set
        {
            if (_offset != value)
            {
                _offset = value;
                InvalidateArrange();
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(_offset.X, _offset.Y, Math.Max(finalSize.Width, child.DesiredSize.Width), child.DesiredSize.Height));
        }

        return finalSize;
    }
}

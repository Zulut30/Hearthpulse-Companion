using System.Windows;
using System.Windows.Controls;

namespace Hearthpulse.App.Controls;

/// <summary>
/// A page's tab row: the first child (the tabs) on the left, every other child (the period bar, the MMR slices; one
/// shows at a time) on the right, at least <see cref="Gap"/> apart. When the window is too narrow for both, the right
/// ones move to a line of their own under the tabs, still on the right, <see cref="LineGap"/> below: they never touch
/// or run under the tabs (bug hunt d1, the Battlegrounds row at 1040).
/// </summary>
public sealed class TabRowPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(TabRowPanel), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LineGapProperty = DependencyProperty.Register(
        nameof(LineGap), typeof(double), typeof(TabRowPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The least room between the tabs and the right-hand bar on one line.</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>The room above the right-hand bar when it has a line of its own.</summary>
    public double LineGap { get => (double)GetValue(LineGapProperty); set => SetValue(LineGapProperty, value); }

    /// <summary>Whether the last layout put the right-hand bar on a line of its own.</summary>
    public bool Wrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        var left = Size.Empty;
        var right = new Size(0, 0);
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(unbounded);
            if (i == 0)
            {
                left = child.DesiredSize;
            }
            else
            {
                right = new Size(Math.Max(right.Width, child.DesiredSize.Width), Math.Max(right.Height, child.DesiredSize.Height));
            }
        }

        if (left.IsEmpty)
        {
            return new Size(0, 0);
        }

        var shown = right.Width > 0;
        Wrapped = shown && left.Width + Gap + right.Width > availableSize.Width;
        var width = Wrapped ? Math.Max(left.Width, right.Width) : left.Width + (shown ? Gap + right.Width : 0);
        var height = Wrapped ? left.Height + LineGap + right.Height : Math.Max(left.Height, right.Height);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var tabs = InternalChildren[0].DesiredSize;
        InternalChildren[0].Arrange(new Rect(0, 0, Math.Min(tabs.Width, finalSize.Width), tabs.Height));

        // Decided again on the width given, which can differ from the one measured.
        var rightWidth = 0.0;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            rightWidth = Math.Max(rightWidth, InternalChildren[i].DesiredSize.Width);
        }

        Wrapped = rightWidth > 0 && tabs.Width + Gap + rightWidth > finalSize.Width;

        // The others right-aligned: on the tabs' line, or on a line of their own under them.
        var top = Wrapped ? tabs.Height + LineGap : 0;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var size = child.DesiredSize;
            child.Arrange(new Rect(Math.Max(0, finalSize.Width - size.Width), top, Math.Min(size.Width, finalSize.Width), size.Height));
        }

        return finalSize;
    }
}

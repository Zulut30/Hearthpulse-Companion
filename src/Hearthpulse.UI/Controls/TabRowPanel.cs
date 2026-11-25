using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Controls;

/// <summary>
/// A row of tabs with actions: the first child (the tabs) on the left, every other child (buttons, a filter) on the
/// right, at least <see cref="Gap"/> apart. When the view is too narrow for both, the right ones move to a line of their
/// own under the tabs, still on the right, <see cref="LineGap"/> below. The twin of the WPF TabRowPanel; History's
/// library head uses it, so Export and Import stand at the right of the tabs in both UIs (bug hunt d2).
/// </summary>
public sealed class TabRowPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<TabRowPanel, double>(nameof(Gap), 12);

    public static readonly StyledProperty<double> LineGapProperty = AvaloniaProperty.Register<TabRowPanel, double>(nameof(LineGap), 8);

    static TabRowPanel() => AffectsMeasure<TabRowPanel>(GapProperty, LineGapProperty);

    /// <summary>The least room between the tabs and the right-hand children on one line.</summary>
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>The room above the right-hand children when they have a line of their own.</summary>
    public double LineGap { get => GetValue(LineGapProperty); set => SetValue(LineGapProperty, value); }

    /// <summary>Whether the last layout put the right-hand children on a line of their own.</summary>
    public bool Wrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        var unbounded = availableSize.WithWidth(double.PositiveInfinity);
        var left = default(Size);
        var right = default(Size);
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
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

        var shown = right.Width > 0;
        Wrapped = shown && left.Width + Gap + right.Width > availableSize.Width;
        var width = Wrapped ? Math.Max(left.Width, right.Width) : left.Width + (shown ? Gap + right.Width : 0);
        var height = Wrapped ? left.Height + LineGap + right.Height : Math.Max(left.Height, right.Height);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var tabs = Children[0].DesiredSize;
        Children[0].Arrange(new Rect(0, 0, Math.Min(tabs.Width, finalSize.Width), tabs.Height));

        // Decided again on the width given, which can differ from the one measured.
        var rightWidth = Children.Skip(1).Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max();
        Wrapped = rightWidth > 0 && tabs.Width + Gap + rightWidth > finalSize.Width;

        // The others right-aligned: on the tabs' line (centred on it), or on a line of their own under them.
        var line = Wrapped ? 0 : Math.Max(tabs.Height, Children.Skip(1).Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max());
        for (var i = 1; i < Children.Count; i++)
        {
            var child = Children[i];
            var size = child.DesiredSize;
            var top = Wrapped ? tabs.Height + LineGap : (line - size.Height) / 2;
            child.Arrange(new Rect(Math.Max(0, finalSize.Width - size.Width), top, Math.Min(size.Width, finalSize.Width), size.Height));
        }

        return finalSize;
    }
}

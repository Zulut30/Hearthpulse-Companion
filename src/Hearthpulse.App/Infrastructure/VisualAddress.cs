using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// Where an element sits under a root, so that its twin can be found in a tree built from the same XAML (the main window
/// rebuilt for a new theme): each step is the child's index, type and name. A step whose index points at another child (a
/// page built lazily in another order) takes the one child of that type and name instead, or gives up.
/// </summary>
internal sealed record VisualAddress(IReadOnlyList<VisualAddress.Step> Steps)
{
    /// <summary>One step down the visual tree.</summary>
    internal readonly record struct Step(int Index, Type Type, string Name);

    /// <summary>Where <paramref name="target"/> is under <paramref name="root"/>; null when it is not under it (or not in a tree).</summary>
    public static VisualAddress? Of(DependencyObject root, DependencyObject? target)
    {
        var steps = new List<Step>();
        for (var node = target; !ReferenceEquals(node, root); )
        {
            if (node is not Visual || VisualTreeHelper.GetParent(node) is not { } parent)
            {
                return null;
            }

            steps.Add(new Step(IndexOf(parent, node), node.GetType(), NameOf(node)));
            node = parent;
        }

        steps.Reverse();
        return new VisualAddress(steps);
    }

    /// <summary>The element at this address under <paramref name="root"/>, or null when the tree there differs.</summary>
    public DependencyObject? Find(DependencyObject root)
    {
        var node = root;
        foreach (var step in Steps)
        {
            if (Child(node, step) is not { } child)
            {
                return null;
            }

            node = child;
        }

        return node;
    }

    /// <summary>Every scrolled list under <paramref name="root"/> on a shown branch (collapsed pages are skipped), outer first.</summary>
    public static IReadOnlyList<ScrollMark> ScrollMarks(DependencyObject root)
    {
        var marks = new List<ScrollMark>();
        Collect(root, root, marks);
        return marks;
    }

    /// <summary>Scrolls the twins of <paramref name="marks"/> under <paramref name="root"/> as far; the next layout applies it.</summary>
    public static void Scroll(DependencyObject root, IEnumerable<ScrollMark> marks)
    {
        foreach (var mark in marks)
        {
            if (mark.Address.Find(root) is ScrollViewer scroller)
            {
                scroller.ScrollToVerticalOffset(mark.Vertical);
                scroller.ScrollToHorizontalOffset(mark.Horizontal);
            }
        }
    }

    private static void Collect(DependencyObject root, DependencyObject node, List<ScrollMark> marks)
    {
        if (node is UIElement { Visibility: not Visibility.Visible })
        {
            return;
        }

        if (node is ScrollViewer { VerticalOffset: > 0 } or ScrollViewer { HorizontalOffset: > 0 } && Of(root, node) is { } address)
        {
            var scroller = (ScrollViewer)node;
            marks.Add(new ScrollMark(address, scroller.VerticalOffset, scroller.HorizontalOffset));
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Collect(root, VisualTreeHelper.GetChild(node, i), marks);
        }
    }

    private static DependencyObject? Child(DependencyObject parent, Step step)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        if (step.Index < count && Matches(VisualTreeHelper.GetChild(parent, step.Index), step))
        {
            return VisualTreeHelper.GetChild(parent, step.Index);
        }

        DependencyObject? found = null;
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (Matches(child, step))
            {
                if (found is not null)
                {
                    return null;
                }

                found = child;
            }
        }

        return found;
    }

    private static bool Matches(DependencyObject child, Step step) => child.GetType() == step.Type && NameOf(child) == step.Name;

    private static string NameOf(DependencyObject node) => (node as FrameworkElement)?.Name ?? "";

    private static int IndexOf(DependencyObject parent, DependencyObject child)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(VisualTreeHelper.GetChild(parent, i), child))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>A scrolled list: where it is and how far down and across.</summary>
internal sealed record ScrollMark(VisualAddress Address, double Vertical, double Horizontal);

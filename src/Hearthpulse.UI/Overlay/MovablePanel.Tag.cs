using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Hearthpulse.UI.Overlay;

/// <summary>
/// The panel's name tag: where it goes (above the panel, under it at the top of the screen, lined up with the right
/// side at the right edge) and how it follows the panel while it shows.
/// </summary>
public sealed partial class MovablePanel
{
    // The name tag and its buttons, which follow a wide row's placeholder to its end of the row.
    private Control? _chrome;

    // The strip holding the tag, which places it, and the tag's row of buttons, at the strip's top or bottom.
    private PanelTagStrip? _strip;
    private Control? _tagRow;
    private bool _followingTag;
    private bool _tagMirrored;

    /// <summary>
    /// While the tag shows, places it after every layout pass (the panel moved, grew or changed size; the tag changed
    /// width), and stops when it is hidden: the panel's own spot alone does not say where it is on the screen.
    /// </summary>
    private void FollowTag()
    {
        var follow = _chrome is { IsVisible: true };
        if (follow == _followingTag)
        {
            return;
        }

        _followingTag = follow;
        if (follow)
        {
            LayoutUpdated += OnLayoutUpdated;
            PlaceTag();
        }
        else
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => PlaceTag();

    /// <summary>
    /// Puts the tag above the panel, or under it at the top of the screen, and lines it up with the panel's right side
    /// at the right edge (<see cref="PanelLayout.TagSpot"/>). The tag sits outside the panel's scaler: it keeps one
    /// size at every scale, and the panel's bounds are already in surface units.
    /// </summary>
    private void PlaceTag()
    {
        if (_strip is null || _chrome is null || Surface is not { Bounds: { Width: > 0, Height: > 0 } bounds })
        {
            return;
        }

        if (_chrome.HorizontalAlignment != HorizontalAlignment.Left)
        {
            // A wide row's placeholder: the tag goes with it to its end of the row, above as always.
            _strip.Offset = new Point(0, -PanelLayout.TagHeight);
            SetTagRow(below: false, right: false);
            return;
        }

        var origin = TopLeft();
        var (x, y, below, right) = PanelLayout.TagSpot(origin.X, origin.Y, Bounds.Width, Bounds.Height,
            _chrome.DesiredSize.Width, _tagRow?.DesiredSize.Height ?? 18, 1, bounds.Width, bounds.Height);
        var offset = new Point(Math.Round(x, 1), Math.Round(y, 1));
        if (_strip.Offset != offset)
        {
            _strip.Offset = offset;
        }

        SetTagRow(below, right);
    }

    private void SetTagRow(bool below, bool right)
    {
        var alignment = below ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        if (_tagRow is null)
        {
            return;
        }

        if (_tagRow.VerticalAlignment != alignment)
        {
            _tagRow.VerticalAlignment = alignment;
        }

        // Lined up with the panel's right side the parts go in mirrored order: the lock, which shows first, stays in the
        // corner under the pointer while the tag and the buttons grow to its left. Not a right-to-left flow, which would
        // mirror the glyphs too.
        if (right != _tagMirrored && _tagRow is Panel row)
        {
            _tagMirrored = right;
            var parts = row.Children.Cast<Control>().Reverse().ToList();
            row.Children.Clear();
            foreach (var part in parts)
            {
                if (part is Control element)
                {
                    element.Margin = new Thickness(element.Margin.Right, element.Margin.Top, element.Margin.Left, element.Margin.Bottom);
                }

                row.Children.Add(part);
            }
        }
    }

    /// <summary>A placeholder in a wide row sits at one end of it: its tag buttons go with it.</summary>
    private void UpdateChrome()
    {
        if (_chrome is not null)
        {
            _chrome.HorizontalAlignment = IsEditing && !Shown && PlaceholderAlignment != HorizontalAlignment.Stretch ? PlaceholderAlignment : HorizontalAlignment.Left;
        }
    }
}

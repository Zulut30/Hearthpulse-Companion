using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hearthpulse.Core;

namespace Hearthpulse.App.Overlay;

/// <summary>
/// The pointer on the panel: a drag moves it or, from the corner marker, resizes it; the wheel changes its size or
/// opacity; a right-click resets it.
/// </summary>
public sealed partial class MovablePanel
{
    /// <summary>Virtual-key code of either Shift key: held while scrolling over a panel, it fades the panel.</summary>
    private const int ShiftKey = 0x10;

    private Point _dragStart;
    private Point _originStart;
    private bool _dragging;

    // A drag that started on the corner marker resizes from the size the panel had then (scale, on-screen size).
    private bool _resizing;
    private double _startScale;
    private Size _startSize;

    /// <summary>Whether the mouse went down on one of the tag's buttons, which must click rather than start a drag.</summary>
    private bool OnTool(object source)
    {
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the mouse went down on the corner marker, which resizes the panel instead of moving it.</summary>
    private bool OnGrip(object source)
    {
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _grip))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!CanMove || Surface is not { } surface || OnTool(e.OriginalSource))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        CaptureDefaults();
        _dragging = true;
        _resizing = OnGrip(e.OriginalSource);
        _startScale = _scale.ScaleX;
        _startSize = new Size(ActualWidth * _scale.ScaleX, ActualHeight * _scale.ScaleY);
        _dragStart = e.GetPosition(surface);
        _originStart = TopLeft();
        CaptureMouse();
        Diag.Debug(PanelArming.DragStarted(Id, _resizing));
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (!_dragging || Surface is not { } surface)
        {
            base.OnPreviewMouseMove(e);
            return;
        }

        DragTo(e.GetPosition(surface));
        e.Handled = true;
    }

    /// <summary>
    /// One step of the drag, for the mouse at <paramref name="position"/> on the surface: the panel moves, or from the
    /// corner marker it resizes. A no-op when no drag is going.
    /// </summary>
    public void DragTo(Point position)
    {
        if (!_dragging || Surface is not { } surface)
        {
            return;
        }

        if (_resizing)
        {
            Resize(position - _dragStart);
            return;
        }

        var width = ActualWidth * _scale.ScaleX;
        var height = ActualHeight * _scale.ScaleY;
        // Edges, the centre line and the other panels' edges pull the panel in, so panels line up without pixel hunting.
        var neighbours = _neighbours ??= Neighbours(surface);
        var (left, top) = PanelLayout.DragSpot(_originStart.X, _originStart.Y, position.X - _dragStart.X, position.Y - _dragStart.Y, width, height,
            surface.ActualWidth, surface.ActualHeight, neighbours.Across, neighbours.Down);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(left, top, 0, 0);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            return;
        }

        EndDrag();
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>
    /// The panel lost the mouse in the middle of a drag: it hid because the game moved on, or the player switched
    /// windows. The drag ends where the panel is; before, it stayed dragging, so the overlay kept it armed and taking
    /// clicks, and it followed the mouse the next time it showed.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            EndDrag();
        }
    }

    /// <summary>Ends a move or resize and saves where the panel is and how big, like the wheel does.</summary>
    private void EndDrag()
    {
        _dragging = false;
        _resizing = false;
        _neighbours = null;
        _saved = TopLeft();
        PlacementChanged?.Invoke(this, CurrentPlacement());
    }

    /// <summary>The corner marker: the size follows the pointer and the top-left corner stays where it was.</summary>
    private void Resize(Vector moved)
    {
        var next = PanelSnap.CornerScale(_startScale, _startSize.Width, _startSize.Height, moved.X, moved.Y);
        if (Math.Abs(next - _scale.ScaleX) < 0.001)
        {
            return;
        }

        SetScale(next);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(_originStart.X, _originStart.Y, 0, 0);
    }

    // The other shown panels' spans on the surface (left-right, top-bottom), taken once per drag.
    private ((double Start, double End)[] Across, (double Start, double End)[] Down)? _neighbours;

    private ((double Start, double End)[] Across, (double Start, double End)[] Down) Neighbours(FrameworkElement surface)
    {
        var across = new List<(double, double)>();
        var down = new List<(double, double)>();
        foreach (var other in (surface as Panel)?.Children.OfType<MovablePanel>() ?? [])
        {
            if (other != this && PanelLayout.PullsDrags(other.IsVisible, other.ActualWidth, other.Shown, other.IsEditing, other.PlaceholderAlignment == HorizontalAlignment.Stretch))
            {
                var origin = other.TranslatePoint(new Point(0, 0), surface);
                across.Add((origin.X, origin.X + (other.ActualWidth * other._scale.ScaleX)));
                down.Add((origin.Y, origin.Y + (other.ActualHeight * other._scale.ScaleY)));
            }
        }

        return (across.ToArray(), down.ToArray());
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (!CanMove)
        {
            base.OnPreviewMouseWheel(e);
            return;
        }

        // The overlay never has the keyboard focus (the game has), so Shift is read from the keyboard itself.
        if (PoolInput.IsDown(ShiftKey))
        {
            ChangeLook(PanelSnap.WheelOpacity(PanelOpacity, e.Delta > 0), GetCompact(this));
        }
        else
        {
            StepScale(e.Delta > 0);
        }

        e.Handled = true;
    }

    private void StepScale(bool up)
    {
        CaptureDefaults();
        var next = PanelLayout.StepScale(_scale.ScaleX, up);
        var origin = TopLeft();
        SetScale(next);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(origin.X, origin.Y, 0, 0);
        _saved = origin;
        PlacementChanged?.Invoke(this, CurrentPlacement());
    }

    /// <summary>Right-click resets the panel in layout mode, Ctrl+right-click during a game; otherwise it is the game's.</summary>
    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (!CanMove)
        {
            base.OnPreviewMouseRightButtonUp(e);
            return;
        }

        Reset();
        e.Handled = true;
    }
}

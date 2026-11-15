using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Hearthpulse.Core;

namespace Hearthpulse.UI.Overlay;

/// <summary>
/// The pointer on the panel: a drag moves it or, from the corner marker, resizes it; the wheel changes its size or
/// opacity; a right-click resets it.
/// </summary>
public sealed partial class MovablePanel
{
    private Point _dragStart;
    private Point _originStart;
    private bool _dragging;

    // A drag that started on the corner marker resizes from the size the panel had then (scale, on-screen size).
    private bool _resizing;
    private double _startScale;
    private Size _startSize;

    /// <summary>Whether the pointer went down on one of the tag's buttons, which must click rather than start a drag.</summary>
    private bool OnTool(object? source)
    {
        for (var node = source as Visual; node is not null && node != this; node = node.GetVisualParent())
        {
            if (node is Button)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the pointer went down on the corner marker, which resizes the panel instead of moving it.</summary>
    private bool OnGrip(object? source)
    {
        for (var node = source as Visual; node is not null && node != this; node = node.GetVisualParent())
        {
            if (node == _grip)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanMove || Surface is not { } surface || OnTool(e.Source))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Right-click resets the panel (layout mode, or Ctrl held during a game); it is released below.
            e.Handled = true;
            return;
        }

        CaptureDefaults();
        _dragging = true;
        _resizing = OnGrip(e.Source);
        _startScale = _scale;
        _startSize = Bounds.Size;
        _dragStart = e.GetPosition(surface);
        _originStart = TopLeft();
        e.Pointer.Capture(this);
        Diag.Debug(PanelArming.DragStarted(Id, _resizing));
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || Surface is not { } surface)
        {
            return;
        }

        var position = e.GetPosition(surface);
        if (_resizing)
        {
            Resize(position - _dragStart);
            e.Handled = true;
            return;
        }

        // Edges, the centre line and the other panels' edges pull the panel in, so panels line up without pixel hunting.
        var neighbours = _neighbours ??= Neighbours(surface);
        var (left, top) = PanelLayout.DragSpot(_originStart.X, _originStart.Y, position.X - _dragStart.X, position.Y - _dragStart.Y, Bounds.Width, Bounds.Height,
            surface.Bounds.Width, surface.Bounds.Height, neighbours.Across, neighbours.Down);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(left, top, 0, 0);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (CanMove && e.InitialPressMouseButton == MouseButton.Right && !OnTool(e.Source))
        {
            Reset();
            e.Handled = true;
            return;
        }

        if (!_dragging)
        {
            return;
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// The panel lost the pointer in the middle of a drag (the player switched windows, the platform took the capture
    /// back): the drag ends where the panel is, rather than leaving it dragging, armed and taking clicks.
    /// </summary>
    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndMissedDrag();

    /// <summary>
    /// Ends a move or resize whose release the panel never got (the pointer was lost, or the host read every mouse
    /// button up); it is saved where the panel is, as on the release.
    /// </summary>
    public void EndMissedDrag()
    {
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
        if (Math.Abs(next - _scale) < 0.001)
        {
            return;
        }

        _scale = next;
        ApplyScale();
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(_originStart.X, _originStart.Y, 0, 0);
    }

    // The other shown panels' spans on the surface (left-right, top-bottom), taken once per drag.
    private ((double Start, double End)[] Across, (double Start, double End)[] Down)? _neighbours;

    private ((double Start, double End)[] Across, (double Start, double End)[] Down) Neighbours(Control surface)
    {
        var across = new List<(double, double)>();
        var down = new List<(double, double)>();
        foreach (var other in (surface as Panel)?.Children.OfType<MovablePanel>() ?? [])
        {
            if (other != this && PanelLayout.PullsDrags(other.IsVisible, other.Bounds.Width, other.Shown, other.IsEditing, other.PlaceholderAlignment == HorizontalAlignment.Stretch)
                && other.TranslatePoint(default, surface) is { } origin)
            {
                across.Add((origin.X, origin.X + other.Bounds.Width));
                down.Add((origin.Y, origin.Y + other.Bounds.Height));
            }
        }

        return (across.ToArray(), down.ToArray());
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!CanMove)
        {
            return;
        }

        // Shift+wheel may arrive as a sideways scroll.
        var up = (e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X) > 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ChangeLook(PanelSnap.WheelOpacity(PanelOpacity, up), IsCompact);
        }
        else
        {
            StepScale(up);
        }

        e.Handled = true;
    }

    private void StepScale(bool up)
    {
        CaptureDefaults();
        var origin = TopLeft();
        _scale = PanelLayout.StepScale(_scale, up);
        ApplyScale();
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(origin.X, origin.Y, 0, 0);
        _saved = origin;
        PlacementChanged?.Invoke(this, CurrentPlacement());
    }
}

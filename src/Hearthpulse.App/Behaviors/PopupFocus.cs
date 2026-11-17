using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Hearthpulse.App.Behaviors;

/// <summary>
/// The WPF adapter of <see cref="PopupState{TElement}"/> (docs/ui-behaviors.md) for a <c>Popup</c> made in code: where
/// the focus goes as it opens (into its content), which keys close it, and whether the focus returns to the trigger as
/// it closes - after Escape, a close key or code closing it with the focus inside, yes; after a click on another control,
/// no (the click's focus stays). The view keeps one instance per popup it shows and calls it from the popup's events.
/// A popup with <c>StaysOpen="False"</c> closes on an outside press by itself, so <see cref="Dismissal.Dismissible"/>
/// governs only the closes asked for through <see cref="RequestClose"/>.
/// </summary>
public sealed class PopupFocus
{
    private readonly PopupState<UIElement> _state;

    // The close this adapter asked for, used when the popup's Closed arrives.
    private PopupCloseResult<UIElement>? _asked;

    public PopupFocus(Dismissal? dismissal = null) => _state = new PopupState<UIElement>(dismissal);

    public bool IsOpen => _state.IsOpen;

    /// <summary>The reason a key asks the popup to close: Escape; Enter and Space when <paramref name="activateCloses"/>. Null for any other key.</summary>
    public static DismissReason? ReasonFor(Key key, bool activateCloses) => key switch
    {
        Key.Escape => DismissReason.EscapeKey,
        Key.Enter or Key.Space when activateCloses => DismissReason.ClosePress,
        _ => null,
    };

    /// <summary>The popup has opened from <paramref name="trigger"/>: the focus goes to <paramref name="content"/> as the shared rule says.</summary>
    public void Opened(UIElement? trigger, UIElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _asked = null;
        var opener = InputManager.Current.MostRecentInputDevice is KeyboardDevice ? PopupOpener.Keyboard : PopupOpener.Pointer;
        if (_state.Open(trigger, opener) != PopupInitialFocus.None)
        {
            content.Focus();
        }
    }

    /// <summary>Asks to close for <paramref name="reason"/>; true when the popup should close now (set its <c>IsOpen</c> to false after).</summary>
    public bool RequestClose(DismissReason reason, bool focusInside)
    {
        if (!_state.IsOpen)
        {
            return true;
        }

        var result = _state.Close(reason, focusInside);
        if (!result.Closed)
        {
            return false;
        }

        _asked = result;
        return true;
    }

    /// <summary>
    /// The popup has closed: the focus returns to the trigger when the shared rule says so and the trigger is still shown.
    /// A close nobody asked for through <see cref="RequestClose"/> was the popup's own, an outside press.
    /// </summary>
    public void Closed()
    {
        var result = _asked ?? _state.Close(DismissReason.OutsidePress, focusInside: false, pressReachedFocusable: PressReachedFocusable());
        _asked = null;
        if (result.ReturnFocusTo is { IsVisible: true } trigger)
        {
            trigger.Focus();
        }
    }

    /// <summary>Whether the mouse is over something that takes the focus itself when pressed.</summary>
    private static bool PressReachedFocusable()
    {
        for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is UIElement { Focusable: true, IsEnabled: true })
            {
                return true;
            }
        }

        return false;
    }
}

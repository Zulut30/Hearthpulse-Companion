using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hearthpulse.UI.Behaviors;

/// <summary>
/// The Avalonia adapter of <see cref="PopupState{TElement}"/> (docs/ui-behaviors.md) as an attached property of a
/// flyout: <c>b:PopupFocus.ReturnFocus="True"</c> on a <c>Flyout</c> puts the focus into its content as it opens (so a
/// screen reader reads it and Escape closes it) and gives it back to the control it was opened from as it closes, which
/// the flyout does not do by itself. A click away closes the flyout first; the clicked control then takes the focus.
/// </summary>
public static class PopupFocus
{
    /// <summary>Whether the flyout moves the focus in on opening and back to its target on closing.</summary>
    public static readonly AttachedProperty<bool> ReturnFocusProperty =
        AvaloniaProperty.RegisterAttached<FlyoutBase, bool>("ReturnFocus", typeof(PopupFocus));

    /// <summary>The flyout's open state and focus rules.</summary>
    private static readonly AttachedProperty<PopupState<Control>?> StateProperty =
        AvaloniaProperty.RegisterAttached<FlyoutBase, PopupState<Control>?>("State", typeof(PopupFocus));

    /// <summary>Escape was pressed in the open flyout: it closes as a dismissal by Escape.</summary>
    private static readonly AttachedProperty<bool> EscapedProperty =
        AvaloniaProperty.RegisterAttached<FlyoutBase, bool>("Escaped", typeof(PopupFocus));

    /// <summary>The open flyout a content control is shown in (the flyout is not in the content's tree).</summary>
    private static readonly AttachedProperty<FlyoutBase?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, FlyoutBase?>("Owner", typeof(PopupFocus));

    static PopupFocus()
    {
        ReturnFocusProperty.Changed.AddClassHandler<FlyoutBase>(OnReturnFocusChanged);
    }

    public static bool GetReturnFocus(FlyoutBase flyout) => flyout.GetValue(ReturnFocusProperty);

    public static void SetReturnFocus(FlyoutBase flyout, bool value) => flyout.SetValue(ReturnFocusProperty, value);

    private static void OnReturnFocusChanged(FlyoutBase flyout, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && e.OldValue is not true)
        {
            flyout.SetValue(StateProperty, new PopupState<Control>());
            flyout.Opened += OnOpened;
            flyout.Closed += OnClosed;
        }
        else if (e.NewValue is not true && e.OldValue is true)
        {
            flyout.Opened -= OnOpened;
            flyout.Closed -= OnClosed;
            flyout.ClearValue(StateProperty);
        }
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not FlyoutBase flyout || flyout.GetValue(StateProperty) is not { } state)
        {
            return;
        }

        flyout.SetValue(EscapedProperty, false);
        var content = ContentOf(flyout);
        if (content is not null)
        {
            content.SetValue(OwnerProperty, flyout);
            content.AddHandler(InputElement.KeyDownEvent, OnContentKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        if (state.Open(flyout.Target, PopupOpener.Imperative) != PopupInitialFocus.None)
        {
            content?.Focus(NavigationMethod.Tab);
        }
    }

    private static void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not FlyoutBase flyout || flyout.GetValue(StateProperty) is not { } state)
        {
            return;
        }

        if (ContentOf(flyout) is { } content)
        {
            content.RemoveHandler(InputElement.KeyDownEvent, OnContentKey);
            content.ClearValue(OwnerProperty);
        }

        var reason = flyout.GetValue(EscapedProperty) ? DismissReason.EscapeKey : DismissReason.OutsidePress;
        flyout.SetValue(EscapedProperty, false);
        // A click away closes the flyout before Avalonia gives the click's focus to what was clicked, so the focus comes
        // back here first and the click's focus still wins (pressReachedFocusable is left false).
        if (state.Close(reason, focusInside: true).ReturnFocusTo is { IsEffectivelyVisible: true } target)
        {
            target.Focus(NavigationMethod.Tab);
        }
    }

    private static void OnContentKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Control content)
        {
            content.GetValue(OwnerProperty)?.SetValue(EscapedProperty, true);
        }
    }

    private static Control? ContentOf(FlyoutBase flyout) => (flyout as Flyout)?.Content as Control;
}

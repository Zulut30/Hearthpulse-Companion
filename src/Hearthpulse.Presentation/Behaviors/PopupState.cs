// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/popups/popup.ts and
// create-popup.ts (open state, openerBySource, initialFocusPlan, outsidePressFocus, returnFocus). Copyright 2026
// Vercel, Inc. Licensed under the Apache License, Version 2.0; see THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C# as a small state machine over an element type the adapter picks;
// placement, presentation (sheet, dialog), modality, hover delays and exit animations are left to WPF and Avalonia.

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>How a popup was opened, which decides where the focus goes.</summary>
public enum PopupOpener
{
    /// <summary>A mouse or pen press on its trigger.</summary>
    Pointer,

    /// <summary>Enter or Space on its trigger (a screen reader's click counts as one).</summary>
    Keyboard,

    /// <summary>A tap: the popup itself takes the focus, so no on-screen keyboard appears.</summary>
    Touch,

    /// <summary>Hovering the trigger: the focus stays where it is.</summary>
    Hover,

    /// <summary>Open from the start: the focus stays where it is.</summary>
    Initial,

    /// <summary>Code opened it.</summary>
    Imperative,
}

/// <summary>Where the focus goes as a popup opens.</summary>
public enum PopupInitialFocus
{
    /// <summary>It stays where it is.</summary>
    None,

    /// <summary>On the popup itself.</summary>
    Popup,

    /// <summary>On the popup's first focusable content, else the popup itself.</summary>
    Content,
}

/// <summary>What a close request did: whether the popup closed, and the element the focus should return to, if any.</summary>
public readonly record struct PopupCloseResult<TElement>(bool Closed, TElement? ReturnFocusTo)
    where TElement : class;

/// <summary>
/// A popup's open state and its focus rules, the part of UI SDK's <c>createPopup</c> that is not the DOM: opening
/// remembers the trigger and says where the focus goes (into the content, except on hover or from the start); closing
/// asks <see cref="Dismissal"/> and says whether the focus returns to the trigger - after Escape, a close control or a
/// press on nothing focusable it does, after a press on another control or the focus leaving it stays where it went.
/// Each UI's <c>PopupFocus</c> adapter maps its Popup/Flyout events here and moves the focus.
/// </summary>
public sealed class PopupState<TElement>
    where TElement : class
{
    public PopupState(Dismissal? dismissal = null) => Dismissal = dismissal ?? new Dismissal();

    /// <summary>Raised after the popup opened.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised after the popup closed.</summary>
    public event EventHandler<DismissReason>? Closed;

    public Dismissal Dismissal { get; }

    public bool IsOpen { get; private set; }

    /// <summary>The element that opened the open popup; where the focus returns. Null once it closed.</summary>
    public TElement? Trigger { get; private set; }

    /// <summary>How the popup was opened last.</summary>
    public PopupOpener Opener { get; private set; }

    /// <summary>Opens the popup from <paramref name="trigger"/> and returns where the focus goes (UI SDK's initialFocusPlan).</summary>
    public PopupInitialFocus Open(TElement? trigger, PopupOpener opener)
    {
        Trigger = trigger;
        Opener = opener;
        var wasOpen = IsOpen;
        IsOpen = true;
        if (!wasOpen)
        {
            Opened?.Invoke(this, EventArgs.Empty);
        }

        return opener switch
        {
            PopupOpener.Hover or PopupOpener.Initial => PopupInitialFocus.None,
            PopupOpener.Touch => PopupInitialFocus.Popup,
            _ => PopupInitialFocus.Content,
        };
    }

    /// <summary>
    /// Asks the popup to close for <paramref name="reason"/>. <paramref name="focusInside"/>: whether the focus was in the
    /// popup (or lost) when it closed; <paramref name="pressReachedFocusable"/>: for an outside press, whether the press
    /// landed on something that takes the focus itself. Returns whether it closed and where the focus should return.
    /// </summary>
    public PopupCloseResult<TElement> Close(DismissReason reason, bool focusInside = true, bool pressReachedFocusable = false)
    {
        if (!IsOpen || !Dismissal.Allows(reason))
        {
            return new(false, null);
        }

        IsOpen = false;
        var returns = reason switch
        {
            DismissReason.EscapeKey or DismissReason.ClosePress => true,
            // A press keeps the focus it gave to a control; a press on nothing focusable would drop it, so it returns.
            DismissReason.OutsidePress => !pressReachedFocusable,
            // The focus already went somewhere the person chose.
            DismissReason.FocusOut or DismissReason.WindowBlur => false,
            _ => focusInside,
        };
        var target = returns ? Trigger : null;
        // A closed popup keeps no element: a row rebuilt meanwhile must not stay alive through it.
        Trigger = null;
        Closed?.Invoke(this, reason);
        return new(true, target);
    }
}

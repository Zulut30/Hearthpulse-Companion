// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/popups/dismissal.ts
// (createDismissal) and the close reasons of popups/popup.ts. Copyright 2026 Vercel, Inc. Licensed under the Apache
// License, Version 2.0; see THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C#; the refusal rules hook, Android's back gesture and the sheet swipe
// are left out (no such input on the desktop).

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>Why a popup, flyout or inline question is asked to close.</summary>
public enum DismissReason
{
    /// <summary>Escape while it was the top layer.</summary>
    EscapeKey,

    /// <summary>A press outside it.</summary>
    OutsidePress,

    /// <summary>The keyboard focus moved out of it.</summary>
    FocusOut,

    /// <summary>Its window lost activation.</summary>
    WindowBlur,

    /// <summary>A press of its own close control (a Close button, Enter or Space on content that closes).</summary>
    ClosePress,

    /// <summary>Code closed it: its row was rebuilt, its view unloaded, its window closed.</summary>
    Imperative,
}

/// <summary>
/// Whether a layer may be dismissed: Escape, an outside press, focus or the window leaving close it unless
/// <see cref="Dismissible"/> is false (unsaved work); its own close control and code always can. A refused dismissal
/// made on purpose (Escape, an outside press) raises <see cref="Attempted"/>, where the layer can ask "Discard
/// changes?"; focus or the window leaving is refused silently.
/// </summary>
public sealed class Dismissal
{
    /// <summary>Raised when <see cref="Dismissible"/> refused Escape or an outside press.</summary>
    public event EventHandler<DismissReason>? Attempted;

    /// <summary>Whether Escape, an outside press, focus or the window leaving may close the layer. On by default.</summary>
    public bool Dismissible { get; set; } = true;

    /// <summary>Whether a reason is a dismissal (the person or the focus left), as opposed to the layer's own close control or code.</summary>
    public static bool IsDismissal(DismissReason reason) =>
        reason is DismissReason.EscapeKey or DismissReason.OutsidePress or DismissReason.FocusOut or DismissReason.WindowBlur;

    /// <summary>Whether the layer may close for <paramref name="reason"/>; raises <see cref="Attempted"/> for a refused try.</summary>
    public bool Allows(DismissReason reason)
    {
        if (Dismissible || !IsDismissal(reason))
        {
            return true;
        }

        // Focus or the window leaving is not a try to dismiss it.
        if (reason is DismissReason.EscapeKey or DismissReason.OutsidePress)
        {
            Attempted?.Invoke(this, reason);
        }

        return false;
    }
}

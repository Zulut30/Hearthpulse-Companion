// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/focus/
// focus-recovery.ts (createFocusRecovery, LostFocus) and the return chain of popups/popup.ts (firstUsable). Copyright
// 2026 Vercel, Inc. Licensed under the Apache License, Version 2.0; see THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C#: the adapter tells the core what held the focus and how to read its
// collection (IFocusTree); focusin/focusout listeners and microtasks became each UI's own events and dispatcher.

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>What <see cref="FocusRecovery{TElement}"/> needs to know about a UI's elements; each UI's adapter answers it.</summary>
public interface IFocusTree<TElement>
    where TElement : class
{
    /// <summary>Whether the element can take the focus now: shown, enabled, still in a window.</summary>
    bool IsUsable(TElement element);

    /// <summary>The items of a collection that can take the focus, in order.</summary>
    IReadOnlyList<TElement> ItemsOf(object collection);

    /// <summary>What identifies the element's item across a rebuild (a card id), or null.</summary>
    string? KeyOf(TElement element);
}

/// <summary>Which item of the collection takes the focus when neither the element nor its key is there any more.</summary>
public enum RecoveryFallback
{
    /// <summary>The collection's first item.</summary>
    FirstItem,

    /// <summary>The item now at the element's old place (the one after a deleted row), else the last.</summary>
    SameIndex,
}

/// <summary>The part that held the focus when it was remembered: its element, item key, collection and place in it.</summary>
public sealed record FocusOrigin<TElement>(TElement Element, string? Key, object? Collection, int Index)
    where TElement : class;

/// <summary>
/// Keeps a keyboard user's place when the element that held the focus leaves - a row rebuilt while a page covered it, a
/// deleted row, a popup's trigger that went away. <see cref="Remember"/> the element when the focus leaves for a page or
/// popup; <see cref="Resolve"/> gives back the element to focus: the same element while it is usable, else the item
/// with its key in the same collection, else <see cref="RecoveryFallback"/>, else nothing (the caller's own fallback,
/// a page's sidebar button).
/// </summary>
public sealed class FocusRecovery<TElement>(IFocusTree<TElement> tree)
    where TElement : class
{
    private FocusOrigin<TElement>? _origin;

    /// <summary>Whether an origin is remembered and not taken yet.</summary>
    public bool HasOrigin => _origin is not null;

    /// <summary>Remembers <paramref name="element"/>, its key and its place in <paramref name="collection"/> (null: none).</summary>
    public void Remember(TElement element, object? collection)
    {
        ArgumentNullException.ThrowIfNull(element);
        var index = collection is null ? -1 : IndexOf(tree.ItemsOf(collection), element);
        _origin = new(element, tree.KeyOf(element), collection, index);
    }

    /// <summary>Takes the remembered origin (once): the caller resolves it now and, if the layout is late, again later.</summary>
    public FocusOrigin<TElement>? Take()
    {
        var origin = _origin;
        _origin = null;
        return origin;
    }

    /// <summary>The element to focus for <paramref name="origin"/>, or null when nothing of its collection is left.</summary>
    public TElement? Resolve(FocusOrigin<TElement>? origin, RecoveryFallback fallback = RecoveryFallback.FirstItem)
    {
        if (origin is null)
        {
            return null;
        }

        if (tree.IsUsable(origin.Element))
        {
            return origin.Element;
        }

        if (origin.Collection is null)
        {
            return null;
        }

        var items = tree.ItemsOf(origin.Collection);
        if (items.Count == 0)
        {
            return null;
        }

        if (origin.Key is { } key && items.FirstOrDefault(item => string.Equals(tree.KeyOf(item), key, StringComparison.Ordinal)) is { } same)
        {
            return same;
        }

        return fallback == RecoveryFallback.SameIndex && origin.Index >= 0 ? items[Math.Min(origin.Index, items.Count - 1)] : items[0];
    }

    private static int IndexOf(IReadOnlyList<TElement> items, TElement element)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], element))
            {
                return i;
            }
        }

        return -1;
    }
}

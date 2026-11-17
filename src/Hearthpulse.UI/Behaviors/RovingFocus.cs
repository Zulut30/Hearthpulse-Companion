using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace Hearthpulse.UI.Behaviors;

/// <summary>
/// The Avalonia adapter of <see cref="ListNavigation"/> and <see cref="Typeahead"/> (docs/ui-behaviors.md): maps
/// Avalonia keys, moves the keyboard focus to the item the shared rule picks and scrolls it into view, and tells a screen
/// reader the item's place in its list (UI Automation's PositionInSet / SizeOfSet, ARIA's aria-posinset / aria-setsize).
/// The list itself stays one Tab stop through <c>KeyboardNavigation.TabNavigation="Once"</c>.
/// </summary>
public static class RovingFocus
{
    /// <summary>The clock new type-to-find queries time out on; tests set a fake one.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>A new type-to-find query for a list, on <see cref="Clock"/>.</summary>
    public static Typeahead NewTypeahead() => new(Clock);

    /// <summary>The navigation key for an Avalonia key, <see cref="NavKey.None"/> for any other.</summary>
    public static NavKey ToNavKey(Key key) => key switch
    {
        Key.Up => NavKey.Up,
        Key.Down => NavKey.Down,
        Key.Left => NavKey.Left,
        Key.Right => NavKey.Right,
        Key.Home => NavKey.Home,
        Key.End => NavKey.End,
        Key.PageUp => NavKey.PageUp,
        Key.PageDown => NavKey.PageDown,
        _ => NavKey.None,
    };

    /// <summary>Whether an item can take the focus now.</summary>
    public static bool IsUsable(InputElement item) => item is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Focusable: true };

    /// <summary>
    /// Moves the focus from <paramref name="current"/> among <paramref name="items"/> for <paramref name="key"/>; true when
    /// the list took the key (also at either end, where the focus stays), false for a key that is not the list's.
    /// </summary>
    public static bool Move<T>(T current, IReadOnlyList<T> items, Key key, ListNavigationOptions options)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(items);
        var at = IndexOf(items, current);
        var target = ListNavigation.Target(at, ToNavKey(key), items.Count, options, i => IsUsable(items[i]));
        if (target < 0)
        {
            return false;
        }

        if (target != at)
        {
            FocusItem(items[target]);
        }

        return true;
    }

    /// <summary>
    /// Type-to-find: moves the focus to the next item whose <paramref name="label"/> starts with what was typed; true when
    /// one matched. Space is left to the item (it activates a button), so a query cannot continue over a space.
    /// </summary>
    public static bool Search<T>(Typeahead typeahead, string? text, T current, IReadOnlyList<T> items, Func<T, string?> label)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(typeahead);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(label);
        if (text is null or " ")
        {
            return false;
        }

        var labels = items.Select(item => IsUsable(item) ? label(item) ?? string.Empty : string.Empty).ToList();
        var found = typeahead.Search(text, labels, IndexOf(items, current));
        if (found < 0)
        {
            return false;
        }

        if (!ReferenceEquals(items[found], current))
        {
            FocusItem(items[found]);
        }

        return true;
    }

    /// <summary>Tells a screen reader the item's place: "3 of 30".</summary>
    public static void Announce<T>(T item, IReadOnlyList<T> items)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(items);
        var at = IndexOf(items, item);
        if (at >= 0)
        {
            AutomationProperties.SetPositionInSet(item, at + 1);
            AutomationProperties.SetSizeOfSet(item, items.Count);
        }
    }

    /// <summary>The focus on an item, scrolled into view.</summary>
    public static void FocusItem(Control item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Focus(NavigationMethod.Directional);
        item.BringIntoView();
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, T item)
        where T : class
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}

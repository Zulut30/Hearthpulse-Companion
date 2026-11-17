// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/focus/collection.ts
// (stepFor, radioStep, moveIndex) and focus/roving-focus.ts. Copyright 2026 Vercel, Inc. Licensed under the Apache
// License, Version 2.0; see THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C# over item indexes and framework-neutral keys; the DOM order, tab
// stop state and right-to-left detection are the adapters' (Behaviors/RovingFocus.cs in each UI); page keys added.

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>A navigation key as the behaviors see it; each UI's adapter maps its framework's key to one.</summary>
public enum NavKey
{
    None,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
}

/// <summary>Which arrows move between the items of a group: Up and Down, Left and Right, or all four (radios).</summary>
public enum NavOrientation
{
    Vertical,
    Horizontal,
    Both,
}

/// <summary>How a list moves focus between its items.</summary>
public sealed record ListNavigationOptions
{
    /// <summary>Up / Down, a list and the default.</summary>
    public static ListNavigationOptions Vertical { get; } = new();

    /// <summary>Left / Right, a row of buttons or tabs.</summary>
    public static ListNavigationOptions Horizontal { get; } = new() { Orientation = NavOrientation.Horizontal };

    public NavOrientation Orientation { get; init; } = NavOrientation.Vertical;

    /// <summary>Whether a step past either end wraps around. Off by default: the focus stays at the end.</summary>
    public bool Loop { get; init; }

    /// <summary>Whether the items are laid out right to left, so Left and Right swap.</summary>
    public bool RightToLeft { get; init; }

    /// <summary>Whether Home and End go to the first and the last item (on by default, as WAI-ARIA asks of lists).</summary>
    public bool HomeEnd { get; init; } = true;

    /// <summary>How many items Page Up and Page Down move by; 0 leaves those keys alone.</summary>
    public int PageSize { get; init; }
}

/// <summary>
/// Arrow-key movement inside a group of items with one Tab stop (WAI-ARIA "roving tab index"): which item a key moves
/// the focus to. Pure, so the same rule runs in both UIs and in tests; each UI's <c>RovingFocus</c> adapter maps its
/// keys, finds the items and moves the focus. Items that cannot take the focus now (hidden, disabled) are skipped.
/// </summary>
public static class ListNavigation
{
    /// <summary>
    /// The index the focus moves to from <paramref name="current"/> for <paramref name="key"/>, among
    /// <paramref name="count"/> items of which <paramref name="usable"/> (default: every one) can take the focus; -1 when
    /// the key is not this list's to handle (an arrow of the other axis, a page key without a page size). At either end
    /// without <see cref="ListNavigationOptions.Loop"/> the answer is <paramref name="current"/> itself: the key is still
    /// the list's, so the page does not scroll away under it.
    /// </summary>
    public static int Target(int current, NavKey key, int count, ListNavigationOptions options, Func<int, bool>? usable = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (count <= 0 || StepFor(key, options) is not { } step)
        {
            return -1;
        }

        var items = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (usable?.Invoke(i) ?? true)
            {
                items.Add(i);
            }
        }

        if (items.Count == 0)
        {
            return current;
        }

        if (step.Edge != 0)
        {
            return step.Edge < 0 ? items[0] : items[^1];
        }

        // Paging never wraps: Page Down at the end is the end.
        var loop = options.Loop && !step.Page;
        var at = items.IndexOf(current);
        if (at < 0)
        {
            // From an item that cannot take the focus (or none): the nearest usable one in the key's direction.
            var next = step.Delta > 0 ? items.FindIndex(i => i > current) : items.FindLastIndex(i => i < current);
            if (next >= 0)
            {
                return items[next];
            }

            return loop ? (step.Delta > 0 ? items[0] : items[^1]) : (step.Delta > 0 ? items[^1] : items[0]);
        }

        return items[MoveIndex(at, step.Delta, items.Count, loop)];
    }

    /// <summary>A key's step: by <see cref="Delta"/> items, or to the first (<see cref="Edge"/> -1) or the last (+1).</summary>
    internal readonly record struct Step(int Delta, int Edge, bool Page);

    /// <summary>The step a key makes in a list of this shape, or null for a key the list does not handle.</summary>
    internal static Step? StepFor(NavKey key, ListNavigationOptions options)
    {
        switch (key)
        {
            case NavKey.Home when options.HomeEnd:
                return new Step(0, -1, false);
            case NavKey.End when options.HomeEnd:
                return new Step(0, 1, false);
            case NavKey.PageUp when options.PageSize > 0:
                return new Step(-options.PageSize, 0, true);
            case NavKey.PageDown when options.PageSize > 0:
                return new Step(options.PageSize, 0, true);
        }

        var (forward, backward) = options.Orientation switch
        {
            NavOrientation.Vertical => (NavKey.Down, NavKey.Up),
            NavOrientation.Horizontal => options.RightToLeft ? (NavKey.Left, NavKey.Right) : (NavKey.Right, NavKey.Left),
            _ => (NavKey.None, NavKey.None),
        };

        if (options.Orientation == NavOrientation.Both)
        {
            // Like radios: Down and the inline end go forward, Up and the inline start go back.
            var inlineEnd = options.RightToLeft ? NavKey.Left : NavKey.Right;
            var inlineStart = options.RightToLeft ? NavKey.Right : NavKey.Left;
            return key == NavKey.Down || key == inlineEnd ? new Step(1, 0, false)
                : key == NavKey.Up || key == inlineStart ? new Step(-1, 0, false)
                : null;
        }

        return key == forward ? new Step(1, 0, false) : key == backward ? new Step(-1, 0, false) : null;
    }

    /// <summary>The index a step lands on, wrapping or stopping at the ends.</summary>
    internal static int MoveIndex(int index, int step, int count, bool loop)
    {
        var next = index + step;
        return loop ? ((next % count) + count) % count : Math.Clamp(next, 0, count - 1);
    }
}

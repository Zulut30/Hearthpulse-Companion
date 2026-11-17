// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/selection/
// selection-model.ts (createKeySelection, createSelection) and selection/listbox-range.ts. Copyright 2026 Vercel, Inc.
// Licensed under the Apache License, Version 2.0; see THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C# over keys of any type; the primitive's store and change callbacks
// became the Changing (cancellable) and Changed events.

using System.ComponentModel;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>A selection about to change; <see cref="CancelEventArgs.Cancel"/> refuses it, and then no anchor moves.</summary>
public sealed class SelectionChangingEventArgs<TKey>(IReadOnlyList<TKey> next) : CancelEventArgs
{
    /// <summary>The keys that would be selected, in the order they were chosen.</summary>
    public IReadOnlyList<TKey> Next { get; } = next;
}

/// <summary>
/// The selection rules of a list, single or multiple, over item keys: <see cref="Choose"/> selects one item,
/// <see cref="Toggle"/> adds or removes one (Ctrl+click, Space), <see cref="Extend(TKey, bool)"/> selects a Shift range
/// from the anchor, and <see cref="ToggleAll"/> is Ctrl+A. Every choice without Shift moves the anchor; a range made
/// again from the same anchor replaces the range, not what was selected before it, like a file manager. In single mode
/// <see cref="Toggle"/> clears the selected item, and <see cref="Extend(TKey, bool)"/> and <see cref="ToggleAll"/> choose.
/// The list's order is read when a range or "all" needs it, so the model follows a list that changes.
/// </summary>
public sealed class SelectionModel<TKey>
    where TKey : notnull
{
    private readonly Func<IReadOnlyList<TKey>> _order;
    private readonly IEqualityComparer<TKey> _comparer;
    private List<TKey> _selected = [];

    // Where Shift ranges start, and what was selected when it was set (the range adds to that).
    private (TKey Key, List<TKey> Base)? _anchor;

    /// <param name="order">Every key in list order (the visible items' keys), for ranges and "all".</param>
    /// <param name="comparer">How keys compare; the default comparer by default.</param>
    public SelectionModel(Func<IReadOnlyList<TKey>> order, IEqualityComparer<TKey>? comparer = null)
    {
        _order = order ?? throw new ArgumentNullException(nameof(order));
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    /// <summary>A change was asked for; cancel it to keep the selection (and the anchor) as they are.</summary>
    public event EventHandler<SelectionChangingEventArgs<TKey>>? Changing;

    /// <summary>The selection changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether more than one item can be selected. Off by default.</summary>
    public bool Multiple { get; set; }

    /// <summary>Whether a key can be chosen, toggled or added to a range; every key by default. Replacing the selection can still remove it.</summary>
    public Func<TKey, bool>? Selectable { get; init; }

    /// <summary>The selected keys, in the order they were chosen.</summary>
    public IReadOnlyList<TKey> Selected => _selected;

    /// <summary>The key ranges start from; meaningful only while <see cref="HasAnchor"/>.</summary>
    public TKey? Anchor => _anchor is { } anchor ? anchor.Key : default;

    public bool HasAnchor => _anchor is not null;

    public bool IsSelected(TKey key) => _selected.Contains(key, _comparer);

    /// <summary>Selects only <paramref name="key"/> and starts the next range there.</summary>
    public void Choose(TKey key)
    {
        if (!CanSelect(key))
        {
            return;
        }

        if (!Same(_selected, [key]) && !Set([key]))
        {
            return;
        }

        _anchor = (key, [key]);
    }

    /// <summary>Adds or removes <paramref name="key"/> and starts the next range there. In single mode, selects it or clears it.</summary>
    public void Toggle(TKey key)
    {
        if (!CanSelect(key))
        {
            return;
        }

        if (!Multiple && !Same(_selected, [key]))
        {
            Choose(key);
            return;
        }

        var next = IsSelected(key) ? _selected.Where(k => !_comparer.Equals(k, key)).ToList() : [.. _selected, key];
        if (Set(next))
        {
            _anchor = (key, next);
        }
    }

    /// <summary>Shift range from the anchor to <paramref name="to"/>; without an anchor the range starts at <paramref name="to"/>.</summary>
    public void Extend(TKey to, bool additive = true) => Extend(to, to, additive);

    /// <summary>
    /// Selects the range from the anchor to <paramref name="to"/>, keeping what was selected when the anchor was set
    /// (unless <paramref name="additive"/> is false). Without an anchor the range starts at <paramref name="from"/>, the
    /// item the focus was on. In single mode, chooses <paramref name="to"/>.
    /// </summary>
    public void Extend(TKey to, TKey from, bool additive = true)
    {
        if (!Multiple)
        {
            Choose(to);
            return;
        }

        var order = _order();
        if (_anchor is not { } anchor || !order.Contains(anchor.Key, _comparer))
        {
            anchor = (from, [.. _selected]);
            _anchor = anchor;
        }

        var range = Between(order, anchor.Key, to).Where(CanSelect);
        var next = additive ? new List<TKey>(anchor.Base) : [];
        next.AddRange(range.Where(k => !next.Contains(k, _comparer)).ToList());
        if (!Same(next, _selected))
        {
            Set(next);
        }
    }

    /// <summary>Selects every selectable item, or none once they all are (Ctrl+A twice). Only in multiple mode.</summary>
    public void ToggleAll()
    {
        if (!Multiple)
        {
            return;
        }

        var selectable = _order().Where(CanSelect).ToList();
        List<TKey> next;
        if (selectable.Count > 0 && selectable.TrueForAll(IsSelected))
        {
            next = _selected.Where(k => !selectable.Contains(k, _comparer)).ToList();
        }
        else
        {
            next = [.. _selected, .. selectable.Where(k => !IsSelected(k))];
        }

        if (!Same(next, _selected))
        {
            Set(next);
        }
    }

    /// <summary>Starts the next range at <paramref name="key"/> without changing the selection (arrows that move focus but do not select).</summary>
    public void SetAnchor(TKey key) => _anchor = (key, [.. _selected]);

    public void ClearAnchor() => _anchor = null;

    /// <summary>Takes a selection set from outside (a view model's own value): no event, and ranges start over.</summary>
    public void Reset(IEnumerable<TKey> keys)
    {
        _selected = [.. keys];
        _anchor = null;
    }

    private bool CanSelect(TKey key) => Selectable?.Invoke(key) ?? true;

    private bool Same(IReadOnlyList<TKey> a, IReadOnlyList<TKey> b) =>
        a.Count == b.Count && a.Select((key, i) => _comparer.Equals(key, b[i])).All(same => same);

    /// <summary>Asks for <paramref name="next"/>; false when a <see cref="Changing"/> handler refused it.</summary>
    private bool Set(List<TKey> next)
    {
        var changing = new SelectionChangingEventArgs<TKey>(next);
        Changing?.Invoke(this, changing);
        if (changing.Cancel)
        {
            return false;
        }

        _selected = next;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The keys from <paramref name="from"/> to <paramref name="to"/> in list order, both included, either way round.</summary>
    private List<TKey> Between(IReadOnlyList<TKey> order, TKey from, TKey to)
    {
        var start = IndexOf(order, from);
        var end = IndexOf(order, to);
        if (start < 0 || end < 0)
        {
            return [];
        }

        return order.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1).ToList();
    }

    private int IndexOf(IReadOnlyList<TKey> order, TKey key)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (_comparer.Equals(order[i], key))
            {
                return i;
            }
        }

        return -1;
    }
}

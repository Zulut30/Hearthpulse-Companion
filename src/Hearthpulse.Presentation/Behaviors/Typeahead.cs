// Ported from UI SDK (https://github.com/vercel-labs/ui) at af0321f, packages/core/src/foundation/text/typeahead.ts
// (createTypeahead). Copyright 2026 Vercel, Inc. Licensed under the Apache License, Version 2.0; see
// THIRD-PARTY-NOTICES.txt.
// NOTICE: changed by Hearthpulse - rewritten in C#; the timer is a timestamp read from an injected TimeProvider, and
// case and accents fold through the culture's CompareInfo instead of Intl.Collator.

using System.Globalization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Behaviors;
#else
namespace Hearthpulse.UI.Behaviors;
#endif

/// <summary>
/// Type-to-find in a list, as the WAI-ARIA practices describe it for listboxes, menus and trees: typed characters build
/// a query that clears after <see cref="Timeout"/> of quiet, and the next item whose label starts with it is the match.
/// Pressing the same character again steps through the items that start with it, unless a label starts with the
/// repeated characters ("Llama" for "ll"). Case and accents fold the way the culture folds them ("e" finds "Éclair",
/// "е" finds "Ёж"). One instance per list; it keeps no reference to the items.
/// </summary>
public sealed class Typeahead
{
    /// <summary>How long a query lives after its last character, as UI SDK uses.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private const CompareOptions Fold = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly TimeProvider _clock;
    private readonly CompareInfo _compare;
    private string _query = string.Empty;
    private string _lastKey = string.Empty;
    private bool _stepping;
    private long _typedAt;

    /// <param name="clock">Time for the query's timeout (tests pass a fake one); the system clock by default.</param>
    /// <param name="timeout">How long a query lives after its last character; <see cref="DefaultTimeout"/> by default.</param>
    /// <param name="culture">Whose case and accent rules fold the text; the current culture by default.</param>
    public Typeahead(TimeProvider? clock = null, TimeSpan? timeout = null, CultureInfo? culture = null)
    {
        _clock = clock ?? TimeProvider.System;
        Timeout = timeout ?? DefaultTimeout;
        _compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
    }

    public TimeSpan Timeout { get; }

    /// <summary>Whether a query is in progress, so Space continues it rather than activating the item.</summary>
    public bool Active => _query.Length > 0 && !Expired();

    /// <summary>Forgets the query, as a click or a focus move elsewhere should.</summary>
    public void Reset()
    {
        _query = string.Empty;
        _lastKey = string.Empty;
        _stepping = false;
    }

    /// <summary>
    /// Adds <paramref name="typed"/> (one character) to the query and returns the index of the item to focus among
    /// <paramref name="labels"/>, searching from <paramref name="current"/> (-1: none); -1 when nothing matches or the text
    /// is not a single character. An item that cannot be chosen passes an empty label. Space only continues a query.
    /// </summary>
    public int Search(string typed, IReadOnlyList<string> labels, int current)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (_query.Length > 0 && Expired())
        {
            Reset();
        }

        // One printable character (Enter arrives as text "\r" too); Space only continues a query.
        if (typed is not { Length: 1 } || char.IsControl(typed[0]) || (typed == " " && _query.Length == 0))
        {
            return -1;
        }

        _typedAt = _clock.GetTimestamp();
        _stepping = _lastKey.Length > 0 && _compare.Compare(typed, _lastKey, Fold) == 0 && (_stepping || _query.Length == 1);
        _lastKey = typed;
        _query += typed;

        if (_stepping)
        {
            var whole = Scan(labels, _query, Math.Max(current, 0));
            return whole != -1 ? whole : Scan(labels, typed, current + 1);
        }

        return Scan(labels, _query, _query.Length == 1 ? current + 1 : Math.Max(current, 0));
    }

    private bool Expired() => _clock.GetElapsedTime(_typedAt) >= Timeout;

    /// <summary>The first label from <paramref name="start"/> on, wrapping around, that starts with <paramref name="prefix"/>.</summary>
    private int Scan(IReadOnlyList<string> labels, string prefix, int start)
    {
        var count = labels.Count;
        for (var step = 0; step < count; step++)
        {
            var index = (((start + step) % count) + count) % count;
            if (labels[index] is { Length: > 0 } label && _compare.IsPrefix(label.TrimStart(), prefix, Fold))
            {
                return index;
            }
        }

        return -1;
    }
}

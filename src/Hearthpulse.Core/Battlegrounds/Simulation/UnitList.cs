using System.Collections;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// A read-only view of a side's units (<see cref="Side.Minions"/>, <see cref="Side.Extras"/>, <see cref="Side.DeadThisFight"/>).
/// A struct whose <see cref="GetEnumerator"/> returns the list's own struct enumerator, so a <c>foreach</c> in the
/// engine and the effects allocates nothing (through <see cref="IReadOnlyList{T}"/> every loop boxed an enumerator).
/// </summary>
public readonly struct UnitList : IReadOnlyList<Unit>
{
    private readonly List<Unit> _items;

    internal UnitList(List<Unit> items) => _items = items;

    public int Count => _items.Count;

    public Unit this[int index] => _items[index];

    public List<Unit>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<Unit> IEnumerable<Unit>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>
/// A read-only view of a unit's effects (<see cref="Unit.Effects"/>) whose <c>foreach</c> allocates nothing: the struct
/// enumerator walks the array directly.
/// </summary>
public readonly struct EffectList : IReadOnlyList<CombatEffect>
{
    private readonly CombatEffect[] _items;

    internal EffectList(CombatEffect[] items) => _items = items;

    public int Count => _items.Length;

    public CombatEffect this[int index] => _items[index];

    public Enumerator GetEnumerator() => new(_items);

    IEnumerator<CombatEffect> IEnumerable<CombatEffect>.GetEnumerator() => ((IEnumerable<CombatEffect>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>Walks the effects array without allocating.</summary>
    public struct Enumerator
    {
        private readonly CombatEffect[] _items;
        private int _index;

        internal Enumerator(CombatEffect[] items)
        {
            _items = items;
            _index = -1;
        }

        public readonly CombatEffect Current => _items[_index];

        public bool MoveNext() => ++_index < _items.Length;
    }
}

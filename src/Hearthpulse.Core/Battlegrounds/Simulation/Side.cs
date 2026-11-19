using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>One player's side during a simulated fight: board, player-level effects and per-fight counters.</summary>
public sealed partial class Side
{
    public const int MaxMinions = 7;

    private readonly List<Unit> _board = new(MaxMinions);
    private readonly List<Unit> _extras = new();
    private readonly List<Unit> _dead = new(MaxMinions);
    private Dictionary<string, int>? _counters;
    private List<(Func<Unit, bool> Applies, int Attack, int Health)>? _summonBuffs;
    private List<SimMinion>? _hand;
    private Hook _hooks;
    private bool _hooksDirty;

    internal Side(Fight fight, int index, SimSide info) => Init(fight, index, info);

    /// <summary>
    /// Sets every field as a new side has it: the constructor, and a side a thread's <see cref="Fight.Scratch"/> reuses
    /// for its next fight (its three lists are kept and emptied). <c>SimPoolResetTests</c> checks that no field is left out.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Fight), nameof(Info), nameof(Enemy))]
    internal void Init(Fight fight, int index, SimSide info)
    {
        Fight = fight;
        Index = index;
        Info = info;
        Enemy = null!;
        _board.Clear();
        _extras.Clear();
        _dead.Clear();
        _counters = null;
        _summonBuffs = null;
        _hand = null;
        // Empty board and no holders: no hooks, exactly (units entering add theirs).
        _hooks = Hook.None;
        _hooksDirty = false;
        DeathrattlesTriggered = 0;
        ExtraDeathrattles = 0;
        Gap = 0;
        LastAttacker = null;
        HasAppliedAuras = false;
        AuraDirty = true;
        MayHaveDying = false;
        TeammateEntered = false;
    }

    public Fight Fight { get; private set; }

    /// <summary>0 = the local player ("ours"), 1 = the opponent.</summary>
    public int Index { get; private set; }

    public bool IsOpponent => Index == 1;

    /// <summary>What was captured for this player (hero, hero power, tags, the local player's hand).</summary>
    public SimSide Info { get; private set; }

    public Side Enemy { get; internal set; }

    /// <summary>Minions on the board, left to right (dying ones included until the death phase).</summary>
    public UnitList Minions => new(_board);

    public int Count => _board.Count;

    public bool IsFull => _board.Count >= MaxMinions;

    /// <summary>Player-level effect holders (hero power, trinkets, Deity...), never on the board.</summary>
    public UnitList Extras => new(_extras);

    /// <summary>Minions that died this fight, in the order they died.</summary>
    public UnitList DeadThisFight => new(_dead);

    /// <summary>Deathrattle triggers resolved this fight (multipliers included).</summary>
    public int DeathrattlesTriggered { get; internal set; }

    /// <summary>Extra deathrattle triggers from player-level sources (quest rewards, anomaly...), added to minion multipliers.</summary>
    public int ExtraDeathrattles { get; set; }

    /// <summary>
    /// The local player's hand (a copy effects may change); always empty for the opponent, whose hand is hidden.
    /// </summary>
    public List<SimMinion> Hand => _hand ??= IsOpponent ? new List<SimMinion>() : Info.Hand.ToList();

    /// <summary>
    /// Attack order: the next attacker is the first minion at or right of this index (wrapping). It sits just right of the
    /// last attacker, so minions summoned there attack before those further right, and minions summoned to its left wait
    /// for the next round.
    /// </summary>
    internal int Gap { get; set; }

    /// <summary>
    /// The minion this side last chose to attack with. When it dies, its deathrattle summons and reborn copy take its
    /// slot and attack next (they do not wait for the next round like other summons left of the gap).
    /// </summary>
    internal Unit? LastAttacker { get; set; }

    /// <summary>
    /// Every hook of the board and the player-level holders. Read before each dispatch, so the read is a field (inlined);
    /// a unit entering adds its hooks, and only a unit leaving or gaining an effect makes it recount.
    /// </summary>
    internal Hook Hooks => _hooksDirty ? RecountHooks() : _hooks;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private Hook RecountHooks()
    {
        var hooks = Hook.None;
        foreach (var unit in _board) hooks |= unit.Hooks;
        foreach (var unit in _extras) hooks |= unit.Hooks;
        _hooks = hooks;
        _hooksDirty = false;
        return hooks;
    }

    internal bool HasAppliedAuras { get; set; }

    /// <summary>
    /// False only when no minion on the board is dying: set when a unit of this side drops to 0 Health, is destroyed or
    /// enters the board dying, cleared by a look at the board that finds none (<c>Fight.AnyDying</c>). Saves that look
    /// after every attack and summon of a fight in which nothing died.
    /// </summary>
    internal bool MayHaveDying { get; set; }

    /// <summary>Minions entered or left since auras were last recomputed.</summary>
    internal bool AuraDirty { get; set; }

    public int IndexOf(Unit unit)
    {
        // By reference: List.IndexOf goes through EqualityComparer and a virtual Equals per minion.
        var board = _board;
        for (var i = 0; i < board.Count; i++)
        {
            if (ReferenceEquals(board[i], unit))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A per-fight player counter (e.g. "EternalKnightsDead", "BeetleAttack"). Before it is set it reads the captured
    /// player tag of the same name, else 0.
    /// </summary>
    public int Counter(string key) =>
        _counters is not null && _counters.TryGetValue(key, out var value) ? value : Info.Tags.GetValueOrDefault(key);

    public void SetCounter(string key, int value) => (_counters ??= new Dictionary<string, int>(StringComparer.Ordinal))[key] = value;

    public int AddCounter(string key, int delta)
    {
        var value = Counter(key) + delta;
        SetCounter(key, value);
        return value;
    }

    /// <summary>Stats every minion summoned later this fight gets if <paramref name="applies"/> (e.g. "your Beasts have +7/+7").</summary>
    public void AddSummonBuff(Func<Unit, bool> applies, int attack, int health) =>
        (_summonBuffs ??= new()).Add((applies, attack, health));

    /// <summary>Stats every minion of <paramref name="tribe"/> (<see cref="Tribe.All"/>: every minion) summoned later this fight gets.</summary>
    public void AddSummonBuff(Tribe tribe, int attack, int health) => AddSummonBuff(u => u.Is(tribe), attack, health);

    /// <summary>
    /// What the summon buffs registered so far add to a minion like <paramref name="unit"/>: auras a copy of it gets again
    /// when summoned, so a copy taken from its current stats subtracts them first.
    /// </summary>
    internal (int Attack, int Health) SummonBuffsFor(Unit unit)
    {
        var (attack, health) = (0, 0);
        foreach (var (applies, a, h) in _summonBuffs ?? [])
        {
            if (applies(unit))
            {
                (attack, health) = (attack + a, health + h);
            }
        }

        return (attack, health);
    }

    internal void ApplySummonBuffs(Unit unit)
    {
        if (_summonBuffs is null)
        {
            return;
        }

        foreach (var (applies, attack, health) in _summonBuffs)
        {
            if (applies(unit))
            {
                unit.Attack = (int)Math.Clamp((long)unit.Attack + attack, 0, int.MaxValue);
                unit.Health = (int)Math.Clamp((long)unit.Health + health, -1_000_000_000, int.MaxValue);
                unit.MaxHealth = (int)Math.Clamp((long)unit.MaxHealth + health, 0, int.MaxValue);
            }
        }
    }

    internal void AddInitial(Unit unit)
    {
        _board.Add(unit);
        unit.IsOnBoard = true;
        MayHaveDying |= unit.IsDying;
        _hooks |= unit.Hooks;
        AuraDirty = true;
    }

    internal void AddExtra(Unit unit)
    {
        _extras.Add(unit);
        _hooks |= unit.Hooks;
    }

    internal void Insert(int index, Unit unit, bool leftOfGap)
    {
        index = Math.Clamp(index, 0, _board.Count);
        _board.Insert(index, unit);
        unit.IsOnBoard = true;
        MayHaveDying |= unit.IsDying;
        if (index < Gap || (index == Gap && leftOfGap))
        {
            Gap++;
        }

        _hooks |= unit.Hooks;
        AuraDirty = true;
    }

    internal void Remove(Unit unit)
    {
        var index = IndexOf(unit);
        if (index < 0)
        {
            return;
        }

        _board.RemoveAt(index);
        unit.IsOnBoard = false;
        if (index < Gap)
        {
            Gap--;
        }

        _dead.Add(unit);
        _hooksDirty = true;
        AuraDirty = true;
    }

    internal void InvalidateHooks()
    {
        _hooksDirty = true;
        AuraDirty = true;
    }

    /// <summary>The minion that attacks next: the first one with Attack at or right of the gap, wrapping around.</summary>
    internal Unit? NextAttacker()
    {
        var count = _board.Count;
        if (count == 0)
        {
            return null;
        }

        var start = Gap >= count ? 0 : Gap;
        for (var i = 0; i < count; i++)
        {
            var unit = _board[(start + i) % count];
            if (unit.Attack > 0 && !unit.IsDying)
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>Copies board and extra units (so hooks can change the board while being dispatched).</summary>
    internal void CopyObservers(Unit[] buffer)
    {
        _board.CopyTo(buffer);
        _extras.CopyTo(buffer, _board.Count);
    }

    public override string ToString() => string.Join(", ", _board);
}

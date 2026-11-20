using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>Deaths in order, deathrattles and their multipliers, reborn, and summoning into the board.</summary>
public sealed partial class Fight
{
    /// <summary>Runs pending death phases now (the engine also does this after every step).</summary>
    public void ResolveDeaths()
    {
        if (_inDeathPhase)
        {
            // The outer death phase picks these deaths up in its next round.
            return;
        }

        if (!AnyDying(Ours) && !AnyDying(Theirs))
        {
            return;
        }

        _inDeathPhase = true;
        try
        {
            var dead = _dead;
            var round = 0;
            for (; round < MaxDeathRounds; round++)
            {
                dead.Clear();
                CollectDead(Attacking, dead);
                CollectDead(Attacking.Enemy, dead);
                if (dead.Count == 0)
                {
                    break;
                }

                SortByEntry(dead);

                // Remember where each one stood, then take them all off the board at once.
                foreach (var unit in dead)
                {
                    var side = unit.Side;
                    var index = side.IndexOf(unit);
                    unit.DeathIndex = index;
                    // The side's last attacker stands just left of the gap, but what it leaves in its slot attacks next
                    // (rules-evidence 2b: 15 of 15, e.g. 15.11 g1c4 Beetle#2118 attacks at line 29266).
                    unit.DiedLeftOfGap = index < side.Gap && unit != side.LastAttacker;
                    unit.DeathAnchor = null;
                    for (var i = index + 1; i < side.Count; i++)
                    {
                        if (!side.Minions[i].IsDying)
                        {
                            unit.DeathAnchor = side.Minions[i];
                            break;
                        }
                    }
                }

                foreach (var unit in dead)
                {
                    unit.Side.Remove(unit);
                }

                foreach (var unit in dead)
                {
                    if (Tracing) Trace($"{Name(unit)} dies");
                    RunDeathrattles(unit, DeathrattleTriggers(unit.Side));
                }

                foreach (var unit in dead)
                {
                    Raise(unit.Side, Hook.FriendlyDeath, unit);
                    Raise(unit.Enemy, Hook.EnemyDeath, unit);
                }

                // Reborn after Avenge (rules-evidence 4d: 15.11 g4c9, AVENGE at line 997634, the copy at 997669).
                foreach (var unit in dead)
                {
                    if (unit.Reborn)
                    {
                        SummonReborn(unit);
                    }
                }

                // "When you have space" waits look for room once the reborn copies took theirs (simreplay #4/11, 15.11:
                // Bubble Gunner's copy at line 1086990, Diremuck Forager's Murloc only after it died, 1087143).
                var first = EnteredFirst ?? Attacking;
                Raise(first, Hook.DeathPhaseEnd, null);
                Raise(first.Enemy, Hook.DeathPhaseEnd, null);

                RefreshAuras();
            }

            if (round == MaxDeathRounds)
            {
                // Minions still dying after the last round the engine follows: the result is not simulated.
                Truncated = true;
            }
        }
        finally
        {
            _dead.Clear();
            _inDeathPhase = false;
        }
    }

    private static bool AnyDying(Side side)
    {
        if (!side.MayHaveDying)
        {
            return false;
        }

        foreach (var unit in side.Minions)
        {
            if (unit.IsDying)
            {
                return true;
            }
        }

        side.MayHaveDying = false;
        return false;
    }

    private static void CollectDead(Side side, List<Unit> dead)
    {
        if (!side.MayHaveDying)
        {
            return;
        }

        var found = false;
        foreach (var unit in side.Minions)
        {
            if (unit.IsDying)
            {
                dead.Add(unit);
                found = true;
            }
        }

        side.MayHaveDying = found;
    }

    /// <summary>
    /// Puts simultaneous deaths in order of play (stable insertion sort, no allocation): rules-evidence 4a, 83 of 83
    /// phases, e.g. 15.11 g5c12 (PLAYER_ID 8 vs 3): the opponent's N'raqi Sapper (line 1421143) before our Runic Arcanist.
    /// </summary>
    private void SortByEntry(List<Unit> dead)
    {
        for (var i = 1; i < dead.Count; i++)
        {
            var unit = dead[i];
            var key = EntryKey(unit);
            var j = i - 1;
            while (j >= 0 && EntryKey(dead[j]) > key)
            {
                dead[j + 1] = dead[j];
                j--;
            }

            dead[j + 1] = unit;
        }
    }

    private int EntryKey(Unit unit) =>
        unit.Entry >= CapturedSummonEntry ? unit.Entry
        : unit.Side == (EnteredFirst ?? Attacking) ? unit.Entry
        : Side.MaxMinions + unit.Entry;

    /// <summary>How many times a deathrattle of this side triggers: 1 + extra triggers from living minions and the side.</summary>
    public int DeathrattleTriggers(Side side)
    {
        var times = 1 + Math.Max(0, side.ExtraDeathrattles);
        if ((side.Hooks & Hook.ExtraDeathrattles) == 0)
        {
            return times;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                if ((unit.Hooks & Hook.ExtraDeathrattles) == 0 || (!unit.IsAlive && !unit.IsExtra))
                {
                    continue;
                }

                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.ExtraDeathrattles) != 0)
                    {
                        times += Math.Max(0, effect.ExtraDeathrattles(this, unit));
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }

        return times;
    }

    /// <summary>
    /// Triggers a minion's deathrattles without it dying (e.g. "trigger your left-most Deathrattle"); summons go to its right.
    /// </summary>
    /// <param name="withMultiplier">Also apply extra triggers (Titus Rivendare and similar).</param>
    public void TriggerDeathrattles(Unit unit, int times = 1, bool withMultiplier = true) =>
        RunDeathrattles(unit, times * (withMultiplier ? DeathrattleTriggers(unit.Side) : 1));

    private void RunDeathrattles(Unit unit, int times)
    {
        if ((unit.Hooks & Hook.Deathrattle) == 0 || TooDeep())
        {
            return;
        }

        _depth++;
        try
        {
            // Each deathrattle with all its extra triggers before the next one: A, A, B, B (effects-audit E2, 15.11 G4
            // Ravaging Scorpid with Golemancy under Titus: lines 1389939, 1390019, then 1390099, 1390176).
            var effects = unit.Effects;
            foreach (var effect in effects)
            {
                if ((effect.Hooks & Hook.Deathrattle) == 0)
                {
                    continue;
                }

                for (var t = 0; t < times; t++)
                {
                    if (Tracing) Trace($"deathrattle of {Name(unit)}");
                    effect.Deathrattle(this, unit);
                    unit.Side.DeathrattlesTriggered++;
                    Raise(unit.Side, Hook.FriendlyDeathrattle, unit);
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void SummonReborn(Unit dead)
    {
        // A captured minion's copy is made once for all the fights of a simulation (PreparedEffects.RebornCopies).
        var spec = _prepared.RebornCopies is { } copies && copies.TryGetValue(dead.Spec, out var prepared) ? prepared : dead.Spec.RebornCopy();
        foreach (var effect in dead.Effects)
        {
            spec = effect.RebornAs(this, dead, spec);
        }

        var copy = SummonCore(dead.Side, spec, SlotOf(dead), dead.DiedLeftOfGap, rebornFrom: dead);
        if (copy is not null)
        {
            Raise(dead.Side, Hook.Reborn, copy, exclude: copy);
        }
    }

    // ================================================================================================ summons

    /// <summary>Summons at a board index (clamped). Returns null when the side already has 7 minions.</summary>
    public Unit? Summon(Side side, SimMinion minion, int index) => SummonCore(side, minion, index, leftOfGap: false, rebornFrom: null);

    /// <summary>Summons a minion with these printed stats at <paramref name="slot"/> (-1: right end).</summary>
    public Unit? Summon(Side side, string cardId, int attack, int health, Keyword keywords = Keyword.None, int slot = -1,
                        Tribe tribes = Tribe.None, bool golden = false) =>
        Summon(side, SimMinion.Create(cardId, attack, health, tribes, keywords, golden), slot < 0 ? side.Count : slot);

    /// <summary>
    /// Summons where <paramref name="place"/> is: in its old slot when it has died (deathrattles), else to its right.
    /// Returns null when the side already has 7 minions.
    /// </summary>
    public Unit? SummonAt(Unit place, SimMinion minion) =>
        SummonCore(place.Side, minion, SlotOf(place), !place.IsOnBoard && place.DiedLeftOfGap, rebornFrom: null);

    /// <summary>Summons <paramref name="count"/> copies left to right where <paramref name="place"/> is (see <see cref="SummonAt(Unit, SimMinion)"/>).</summary>
    public IReadOnlyList<Unit> SummonAt(Unit place, SimMinion minion, int count)
    {
        var summoned = new List<Unit>(count);
        var anchor = place;
        for (var i = 0; i < count; i++)
        {
            var unit = place.IsOnBoard
                ? SummonCore(place.Side, minion, anchor.IsOnBoard ? anchor.Position + 1 : SlotOf(place), false, null)
                : SummonAt(place, minion);
            if (unit is null)
            {
                break;
            }

            summoned.Add(unit);
            anchor = unit;
        }

        return summoned;
    }

    private static int SlotOf(Unit place)
    {
        if (place.IsOnBoard)
        {
            return place.Position + 1;
        }

        if (place.DeathAnchor is null)
        {
            // Nothing alive stood to its right when it died: append, so every further summon from this slot (and the
            // reborn copy) lands right of the previous one. A fixed index would put each new summon left of the last.
            return place.Side.Count;
        }

        return place.DeathAnchor is { IsOnBoard: true } anchor && anchor.Side == place.Side
            ? anchor.Position
            : Math.Min(place.DeathIndex < 0 ? place.Side.Count : place.DeathIndex, place.Side.Count);
    }

    private Unit? SummonCore(Side side, SimMinion minion, int index, bool leftOfGap, Unit? rebornFrom)
    {
        if (side.IsFull || TooDeep())
        {
            if (Tracing) Trace($"no room to summon {minion.CardId} for {(side.IsOpponent ? "them" : "us")}");
            if (side.IsFull)
            {
                // "Whenever you would summon a minion that doesn't fit in your warband" (Mug of the Sire).
                Raise(side, Hook.SummonDidNotFit, null);
            }

            return null;
        }

        var unit = CreateUnit(minion, side);
        unit.RebornFrom = rebornFrom;
        side.Insert(index, unit, leftOfGap);
        side.ApplySummonBuffs(unit);
        if (Tracing) Trace($"{(rebornFrom is null ? "summoned" : "reborn")} {Name(unit)} at {unit.Position}");
        _chooser.OnSummon(this, unit);

        _depth++;
        try
        {
            if ((unit.Hooks & Hook.Summoned) != 0)
            {
                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.Summoned) != 0)
                    {
                        effect.OnSummoned(this, unit);
                    }
                }
            }

            Raise(side, Hook.WheneverSummon, unit, exclude: unit);
            Raise(side, Hook.AfterSummon, unit, exclude: unit);
            RefreshAuras();
        }
        finally
        {
            _depth--;
        }

        return unit;
    }
}

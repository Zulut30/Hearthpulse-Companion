using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>What effects pick from (alive units, decisions, best, neighbours) and the hooks they are raised on.</summary>
public sealed partial class Fight
{
    /// <summary>Living minions of a side (left to right), optionally of a tribe and without one minion.</summary>
    public List<Unit> Alive(Side side, Tribe tribe = Tribe.All, Unit? except = null)
    {
        var result = new List<Unit>(side.Count);
        foreach (var unit in side.Minions)
        {
            if (unit.IsAlive && unit != except && unit.Is(tribe))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    // ================================================================================================ decisions

    /// <summary>
    /// Asks the fight's <see cref="ICombatChooser"/> to pick one of <paramref name="candidates"/> (at least one) for a
    /// decision of <paramref name="kind"/> made by <paramref name="source"/>; returns its index.
    /// </summary>
    public int Choose(ChoiceKind kind, Unit? source, IReadOnlyList<Unit> candidates) =>
        Pick(new CombatChoice(kind, source, candidates.Count, candidates, null));

    /// <summary>Picks one of the cards <paramref name="cards"/> (a pool or a hand; at least one); returns its index.</summary>
    public int Choose(ChoiceKind kind, Unit? source, IReadOnlyList<SimMinion> cards) =>
        Pick(new CombatChoice(kind, source, cards.Count, null, cards));

    private int Pick(in CombatChoice choice)
    {
        if (choice.Count <= 0)
        {
            throw new ArgumentException("A decision needs at least one candidate.", nameof(choice));
        }

        var index = _chooser.Choose(this, choice);
        return (uint)index < (uint)choice.Count
            ? index
            : throw new InvalidOperationException($"The chooser picked {index} of {choice.Count} for {choice.Kind}.");
    }

    /// <summary>A random one of <paramref name="candidates"/> (null when there are none), picked for <paramref name="source"/>.</summary>
    public Unit? RandomOf(IReadOnlyList<Unit> candidates, Unit? source = null, ChoiceKind kind = ChoiceKind.RandomTarget) =>
        candidates.Count == 0 ? null : candidates[Choose(kind, source, candidates)];

    /// <summary>A random living friendly minion of <paramref name="tribe"/>, by default not <paramref name="self"/>.</summary>
    public Unit? RandomFriendly(Unit self, Tribe tribe = Tribe.All, bool includeSelf = false) =>
        RandomOf(Alive(self.Side, tribe, includeSelf ? null : self), self);

    public Unit? RandomEnemy(Unit self, Tribe tribe = Tribe.All) => RandomOf(Alive(self.Enemy, tribe), self);

    /// <summary>Up to <paramref name="count"/> different random picks (one decision each), returned in board order.</summary>
    public List<Unit> RandomDistinct(IReadOnlyList<Unit> candidates, int count, Unit? source = null)
    {
        var pool = candidates.ToList();
        var picked = new List<Unit>(Math.Min(count, pool.Count));
        while (picked.Count < count && pool.Count > 0)
        {
            var index = Choose(ChoiceKind.RandomTarget, source, pool);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }

        picked.Sort((a, b) => a.Position.CompareTo(b.Position));
        return picked;
    }

    /// <summary>The candidate with the highest <paramref name="key"/>; ties are a <see cref="ChoiceKind.TieBreak"/> decision.</summary>
    public Unit? Best(IReadOnlyList<Unit> candidates, Func<Unit, int> key, Unit? source = null)
    {
        Unit? best = null;
        List<Unit>? ties = null;
        var bestKey = int.MinValue;
        foreach (var unit in candidates)
        {
            var value = key(unit);
            if (best is null || value > bestKey)
            {
                best = unit;
                bestKey = value;
                ties?.Clear();
            }
            else if (value == bestKey)
            {
                (ties ??= new List<Unit>()).Add(unit);
            }
        }

        if (best is null || ties is not { Count: > 0 })
        {
            return best;
        }

        ties.Insert(0, best);
        return ties[Choose(ChoiceKind.TieBreak, source, ties)];
    }

    /// <summary>
    /// Index of the card in <paramref name="cards"/> with the highest <paramref name="key"/> among those
    /// <paramref name="eligible"/> (ties are a <see cref="ChoiceKind.HandTieBreak"/> decision), or -1 when none is.
    /// </summary>
    public int BestCard(IReadOnlyList<SimMinion> cards, Func<SimMinion, bool> eligible, Func<SimMinion, int> key, Unit? source = null)
    {
        int best = -1, bestKey = int.MinValue;
        List<int>? ties = null;
        for (var i = 0; i < cards.Count; i++)
        {
            if (!eligible(cards[i]))
            {
                continue;
            }

            var value = key(cards[i]);
            if (best < 0 || value > bestKey)
            {
                best = i;
                bestKey = value;
                ties?.Clear();
            }
            else if (value == bestKey)
            {
                (ties ??= new List<int>()).Add(i);
            }
        }

        if (best < 0 || ties is not { Count: > 0 })
        {
            return best;
        }

        ties.Insert(0, best);
        var tied = ties.Select(i => cards[i]).ToList();
        return ties[Choose(ChoiceKind.HandTieBreak, source, tied)];
    }

    /// <summary>The minions next to <paramref name="unit"/> on its board (dying ones included), or null at the edges.</summary>
    public (Unit? Left, Unit? Right) Neighbours(Unit unit)
    {
        var index = unit.Position;
        if (index < 0)
        {
            return (null, null);
        }

        var board = unit.Side.Minions;
        return (index > 0 ? board[index - 1] : null, index + 1 < board.Count ? board[index + 1] : null);
    }

    // ================================================================================================ dispatch

    private void Raise(Side side, Hook hook, Unit? a, Unit? b = null, int amount = 0, AttackOutcome outcome = default,
                       string? text = null, Unit? exclude = null)
    {
        if ((side.Hooks & hook) == 0 || TooDeep())
        {
            return;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var observer = observers[i];
                if (observer == exclude || (observer.Hooks & hook) == 0 || (!observer.IsOnBoard && !observer.IsExtra))
                {
                    continue;
                }

                foreach (var effect in observer.Effects)
                {
                    if ((effect.Hooks & hook) == 0)
                    {
                        continue;
                    }

                    switch (hook)
                    {
                        case Hook.FriendlyAttack: effect.OnFriendlyAttack(this, observer, a!, b!); break;
                        case Hook.FriendlyAttacked: effect.OnFriendlyAttacked(this, observer, a!, b!); break;
                        case Hook.AfterFriendlyAttack: effect.AfterFriendlyAttack(this, observer, a!, b!, outcome); break;
                        case Hook.FriendlyDealtDamage: effect.AfterFriendlyDealtDamage(this, observer, a!, b!, amount); break;
                        case Hook.ShieldLost: effect.OnFriendlyShieldLost(this, observer, a!); break;
                        case Hook.FriendlyDeathrattle: effect.OnFriendlyDeathrattle(this, observer, a!); break;
                        case Hook.FriendlyDeath: effect.AfterFriendlyDeath(this, observer, a!); break;
                        case Hook.EnemyDeath: effect.AfterEnemyDeath(this, observer, a!); break;
                        case Hook.DeathPhaseEnd: effect.AfterDeathPhase(this, observer); break;
                        case Hook.WheneverSummon: effect.WheneverFriendlySummoned(this, observer, a!); break;
                        case Hook.AfterSummon: effect.AfterFriendlySummoned(this, observer, a!); break;
                        case Hook.Reborn: effect.AfterFriendlyReborn(this, observer, a!); break;
                        case Hook.SpellCast: effect.AfterFriendlySpellCast(this, observer, text ?? string.Empty, a); break;
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private static string Name(Unit unit) => $"{(unit.Side.IsOpponent ? "their" : "our")} {unit}";
}

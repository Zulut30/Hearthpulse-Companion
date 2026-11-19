using System.Globalization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using CombatEvent = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEvent;
using CombatEventKind = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEventKind;
using CombatTranscript = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatTranscript;
using MinionState = Hearthpulse.Core.Tracking.BattlegroundsTracker.MinionState;

namespace Hearthpulse.Bench;

/// <summary>The board checks of <see cref="TranscriptChooser"/>: the sim's boards against the log's, and its helpers.</summary>
internal sealed partial class TranscriptChooser
{
    // ------------------------------------------------------------------------------------------------ boards

    /// <summary>The sim's boards against the log's board <paramref name="index"/> (before regular attack index + 1, or the last).</summary>
    private void Compare(int index, bool final)
    {
        if (index <= _compared && !final)
        {
            return;
        }

        _compared = index;
        if (index >= _transcript.Boards.Count)
        {
            // The log stopped before this attack: nothing to compare with.
            return;
        }

        var board = _transcript.Boards[index];
        CompareSide(_fight!.Ours, board.Ours, index);
        CompareSide(_fight.Theirs, board.Theirs, index);
    }

    private void CompareSide(Side side, IReadOnlyList<MinionState> log, int attack)
    {
        var sim = side.Minions.Where(u => u.IsAlive).ToList();
        if (Same(sim, log))
        {
            // The boards look the same slot by slot: identical minions (two equal tokens summoned in another order) are
            // the player's same board, so the log's identities are taken over.
            for (var i = 0; i < sim.Count; i++)
            {
                if (_toLog.TryGetValue(sim[i], out var old) && old != log[i].Entity && _toSim.GetValueOrDefault(old) == sim[i])
                {
                    _toSim.Remove(old);
                }

                Map(sim[i], log[i].Entity);
            }

            return;
        }

        // Minions on both boards: stats and keywords.
        foreach (var m in log)
        {
            if (!_toSim.TryGetValue(m.Entity, out var unit) || !sim.Contains(unit))
            {
                continue;
            }

            if (unit.Attack != m.Attack)
            {
                throw Diverge(unit, "attack", Num(m.Attack), Num(unit.Attack), StatCategory(m.Entity, attack, health: false), m.Entity, attack);
            }

            if (unit.Health != m.Health)
            {
                throw Diverge(unit, "health", Num(m.Health), Num(unit.Health), StatCategory(m.Entity, attack, health: true), m.Entity, attack);
            }

            var keywords = Keywords(unit.Keywords);
            if (keywords != Keywords(m.Keywords))
            {
                throw Diverge(unit, "keywords", Keywords(m.Keywords).ToString(), keywords.ToString(), ReplayCategory.Keyword, m.Entity, attack);
            }
        }

        // Minions on one board only.
        foreach (var m in log)
        {
            if (!_toSim.TryGetValue(m.Entity, out var unit))
            {
                throw Diverge(null, "present", "summoned " + m.Card, "not summoned", ReplayCategory.SummonPosition, m.Entity, attack, m.Card);
            }

            if (!sim.Contains(unit))
            {
                throw Diverge(unit, "present", "alive", unit.IsOnBoard ? "on the other board" : "dead", ReplayCategory.DeathOrder, m.Entity, attack);
            }
        }

        foreach (var unit in sim)
        {
            if (!_toLog.TryGetValue(unit, out var id))
            {
                throw Diverge(unit, "present", "not summoned", "summoned " + Name(unit), ReplayCategory.SummonPosition, 0, attack);
            }

            if (!log.Any(m => m.Entity == id))
            {
                throw Diverge(unit, "present", "dead", "alive", ReplayCategory.DeathOrder, id, attack);
            }
        }

        // Same minions: the same order.
        for (var i = 0; i < log.Count; i++)
        {
            if (_toLog[sim[i]] != log[i].Entity)
            {
                throw Diverge(sim[i], "position", string.Join(" ", log.Select(m => m.Card)), string.Join(" ", sim.Select(u => u.CardId)),
                    ReplayCategory.SummonPosition, log[i].Entity, attack);
            }
        }
    }

    /// <summary>Slot by slot the same card, stats and keywords: what the player sees of a board.</summary>
    private bool Same(List<Unit> sim, IReadOnlyList<MinionState> log)
    {
        if (sim.Count != log.Count)
        {
            return false;
        }

        for (var i = 0; i < sim.Count; i++)
        {
            var (u, m) = (sim[i], log[i]);
            if (Normal(m.Card, false).Card != u.CardId || u.Attack != m.Attack || u.Health != m.Health || Keywords(u.Keywords) != Keywords(m.Keywords))
            {
                return false;
            }

            // A minion the sim knows as another of the log's must be an interchangeable one: same card on the same side.
            if (_toSim.TryGetValue(m.Entity, out var other) && other != u && other.IsAlive && other.Side == u.Side && other.CardId != u.CardId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Keywords the log and the sim both describe the same way (Windfury flags compared as attacks per turn).</summary>
    private static Keyword Keywords(Keyword keywords) =>
        keywords & (Keyword.Taunt | Keyword.DivineShield | Keyword.Poisonous | Keyword.Venomous | Keyword.Reborn | Keyword.Stealth
                    | Keyword.Windfury | Keyword.MegaWindfury);

    /// <summary>Why a stat differs, by what the log did to the minion during the attack before.</summary>
    private ReplayCategory StatCategory(int entity, int attack, bool health)
    {
        var from = attack == 0 ? 0 : _regular[Math.Min(attack - 1, _regular.Count - 1)];
        var to = attack < _regular.Count ? _regular[attack] : _events.Count;
        var depth = 0;
        bool inTrigger = false, outside = false, damaged = false;
        for (var i = 0; i < to; i++)
        {
            var e = _events[i];
            if (e.Kind == CombatEventKind.Trigger) depth++;
            if (e.Kind == CombatEventKind.End) depth = Math.Max(0, depth - 1);
            if (i < from || e.Entity != entity)
            {
                continue;
            }

            if (e.Kind == CombatEventKind.Damage) damaged = true;
            if (e.Kind == CombatEventKind.Tag && e.Tag is "ATK" or "HEALTH")
            {
                inTrigger |= depth > 0;
                outside |= depth == 0;
            }
        }

        return inTrigger ? ReplayCategory.Trigger
            : outside ? ReplayCategory.Aura
            : damaged || health ? ReplayCategory.Damage
            : ReplayCategory.Aura;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private void Init(Fight fight)
    {
        if (_fight is not null)
        {
            return;
        }

        _fight = fight;
        foreach (var unit in fight.Ours.Minions.Concat(fight.Theirs.Minions))
        {
            if (unit.EntityId > 0 && !_toSim.ContainsKey(unit.EntityId))
            {
                Map(unit, unit.EntityId);
            }
        }
    }

    private void Map(Unit unit, int entity)
    {
        _toLog[unit] = entity;
        _toSim[entity] = unit;
    }

    /// <summary>A logged card id as the sim names it: the normal card id and whether it is golden.</summary>
    private (string Card, bool Golden) Normal(string card, bool premium)
    {
        if (!_normal.TryGetValue(card, out var normal))
        {
            _normal[card] = normal = CombatRelevance.Normalize(card, _cards);
        }

        return (normal.Card, normal.Golden || premium);
    }

    private string Name(Unit unit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(unit.Side.IsOpponent ? "their" : "our")} {unit.CardId}{(unit.Golden ? "(G)" : string.Empty)}#{_toLog.GetValueOrDefault(unit)} {unit.Attack}/{unit.Health}");

    private string LogName(int entity)
    {
        foreach (var board in _transcript.Boards)
        {
            if (board.Ours.Concat(board.Theirs).FirstOrDefault(m => m.Entity == entity) is { } m)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{m.Card}#{entity}");
            }
        }

        return _events.FirstOrDefault(e => e.Entity == entity && e.Card is not null) is { } summon
            ? string.Create(CultureInfo.InvariantCulture, $"{summon.Card}#{entity}")
            : string.Create(CultureInfo.InvariantCulture, $"#{entity}");
    }

    private string LogCard(int entity) =>
        _transcript.Boards.SelectMany(b => b.Ours.Concat(b.Theirs)).FirstOrDefault(m => m.Entity == entity)?.Card
        ?? _events.FirstOrDefault(e => e.Entity == entity && e.Card is not null)?.Card
        ?? string.Empty;

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private ReplayDivergenceException Diverge(Unit? unit, string field, string expected, string actual, ReplayCategory category,
                                              int entity = -1, int? attack = null, string? card = null)
    {
        var id = entity >= 0 ? entity : unit is not null ? _toLog.GetValueOrDefault(unit) : 0;
        return new ReplayDivergenceException(new Divergence(attack ?? Attacks, id, card ?? unit?.CardId ?? string.Empty, field, expected, actual,
            category, ActiveEffects(unit)));
    }

    /// <summary>The effects around a divergence: the minion's own, then the log's triggered effects in the attack.</summary>
    private IReadOnlyList<string> ActiveEffects(Unit? unit)
    {
        var effects = new List<string>();
        if (unit is not null)
        {
            effects.AddRange(unit.Effects.Select(e => e.GetType().Name));
        }

        var (from, to) = Window();
        for (var i = from; i < to && effects.Count < 12; i++)
        {
            if (_events[i] is { Kind: CombatEventKind.Trigger, Card: { } card })
            {
                var name = "log:" + (_cards.Get(card)?.Name is { Length: > 0 } n ? $"{n} ({card})" : card);
                if (!effects.Contains(name))
                {
                    effects.Add(name);
                }
            }
        }

        return effects;
    }
}

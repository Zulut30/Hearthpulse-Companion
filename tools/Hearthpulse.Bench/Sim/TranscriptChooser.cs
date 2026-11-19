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

/// <summary>
/// Answers every decision of a fight from its transcript and checks the sim against it. Minions are matched by the log's
/// entity id: the captured ones by their own, each summoned one with the log's summon of the same card on the same side
/// in the same order (the n-th such summon in the sim is the n-th in the log; its place is checked with the boards).
/// A decision is answered by what the log did inside the triggered effect of the same source within the attack being
/// replayed (the minion it changed, the card it summoned), else anywhere within that attack; a decision the log cannot
/// answer, or answers with a candidate the sim did not offer, is a divergence.
/// </summary>
internal sealed partial class TranscriptChooser : ICombatChooser
{
    private static readonly CombatEventKind[] Changes = [CombatEventKind.Tag, CombatEventKind.Damage, CombatEventKind.Death, CombatEventKind.Summon];

    private readonly CombatTranscript _transcript;
    private readonly IReadOnlyList<CombatEvent> _events;
    private readonly CardDb _cards;
    private readonly List<int> _regular = new();
    private readonly int[] _end;
    private readonly Dictionary<Unit, int> _toLog = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, Unit> _toSim = new();
    private readonly Dictionary<(bool Opponent, string Card, bool Golden), Queue<int>> _summons = new();
    private readonly HashSet<int> _summoned = new();
    private readonly HashSet<int> _used = new();
    private readonly Dictionary<string, (string Card, bool Golden)> _normal = new(StringComparer.Ordinal);
    private Fight? _fight;
    private int _compared = -1;
    private int _pendingNested = -1;

    public TranscriptChooser(CombatTranscript transcript, CardDb cards)
    {
        _transcript = transcript;
        _events = transcript.Events;
        _cards = cards;
        _end = new int[_events.Count];
        var open = new Stack<int>();
        for (var i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            switch (e.Kind)
            {
                case CombatEventKind.Attack when e.Value == 0:
                    _regular.Add(i);
                    break;
                case CombatEventKind.Trigger:
                    open.Push(i);
                    break;
                case CombatEventKind.End when open.Count > 0:
                    _end[open.Pop()] = i;
                    break;
                case CombatEventKind.Summon when e.Card is { } card:
                    var (normal, golden) = Normal(card, e.Golden);
                    var key = (e.Opponent, normal, golden);
                    if (!_summons.TryGetValue(key, out var queue))
                    {
                        _summons[key] = queue = new Queue<int>();
                    }

                    queue.Enqueue(e.Entity);
                    _summoned.Add(e.Entity);
                    break;
            }
        }

        while (open.Count > 0)
        {
            _end[open.Pop()] = _events.Count;
        }
    }

    /// <summary>Regular attacks the sim has started.</summary>
    public int Attacks { get; private set; }

    /// <summary>The engine's trace of the fight so far (when it runs with tracing).</summary>
    public IReadOnlyList<string> Trace => _fight?.Log ?? [];

    public int Choose(Fight fight, in CombatChoice choice)
    {
        Init(fight);
        return choice.Kind switch
        {
            ChoiceKind.FirstAttacker => FirstAttacker(),
            ChoiceKind.AttackTarget => AttackTarget(fight, choice),
            ChoiceKind.RandomSummon or ChoiceKind.HandTieBreak => FromSummons(choice),
            ChoiceKind.RandomCard => FromHand(choice),
            ChoiceKind.RandomHandCard => FromHandTags(choice),
            ChoiceKind.StatSplit => throw Diverge(choice.Source, "choice:StatSplit", "per-point split (the log shows totals)", "a point", ReplayCategory.Trigger),
            _ => FromAffected(choice),
        };
    }

    public void OnAttack(Fight fight, Unit attacker, Unit target, bool nested)
    {
        Init(fight);
        if (!nested)
        {
            var log = Regular(attacker);
            CheckDefender(log, target, null);
            Attacks++;
            return;
        }

        var index = _pendingNested >= 0 ? _pendingNested : FindNested(attacker);
        _pendingNested = -1;
        if (index < 0)
        {
            throw Diverge(attacker, "attacker", "no attack inside this attack", $"{Name(attacker)} attacks inside it", ReplayCategory.AttackerOrder);
        }

        _used.Add(index);
        CheckDefender(_events[index], target, null);
    }

    public void OnSummon(Fight fight, Unit unit)
    {
        Init(fight);
        var key = (unit.Side.IsOpponent, unit.CardId, unit.Golden);
        if (unit.EntityId > 0 && _summoned.Contains(unit.EntityId) && !_toSim.ContainsKey(unit.EntityId))
        {
            // A card of the hand keeps its entity on the board.
            Map(unit, unit.EntityId);
            return;
        }

        if (_summons.TryGetValue(key, out var queue))
        {
            while (queue.Count > 0)
            {
                var entity = queue.Dequeue();
                if (!_toSim.ContainsKey(entity))
                {
                    Map(unit, entity);
                    return;
                }
            }
        }

        // The log summoned no further such minion: the boards will show it.
    }

    /// <summary>After the fight: the last board against the log's last, and the number of regular attacks.</summary>
    public Divergence? Finish()
    {
        if (_fight is null || !_transcript.Complete)
        {
            return null;
        }

        try
        {
            Compare(_transcript.Boards.Count - 1, final: true);
            if (Attacks != _regular.Count)
            {
                throw new ReplayDivergenceException(new Divergence(Attacks, 0, string.Empty, "attacks",
                    _regular.Count.ToString(CultureInfo.InvariantCulture), Attacks.ToString(CultureInfo.InvariantCulture), ReplayCategory.AttackerOrder, []));
            }
        }
        catch (ReplayDivergenceException ex)
        {
            return ex.Divergence;
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ attacks

    private int FirstAttacker()
    {
        if (_regular.Count == 0 || !_toSim.TryGetValue(_events[_regular[0]].Entity, out var unit))
        {
            throw Diverge(null, "choice:FirstAttacker", "a known first attacker", "a coin flip", ReplayCategory.AttackerOrder);
        }

        return unit.Side.IsOpponent ? 1 : 0;
    }

    private int AttackTarget(Fight fight, in CombatChoice choice)
    {
        var attacker = choice.Source!;
        CombatEvent log;
        if (fight.AttacksInProgress == 0)
        {
            log = Regular(attacker);
        }
        else
        {
            _pendingNested = FindNested(attacker);
            if (_pendingNested < 0)
            {
                throw Diverge(attacker, "attacker", "no attack inside this attack", $"{Name(attacker)} attacks inside it", ReplayCategory.AttackerOrder);
            }

            log = _events[_pendingNested];
        }

        return CheckDefender(log, null, choice.Units!);
    }

    /// <summary>The log's regular attack the sim's next one must be: the boards before it compared, the attacker the same.</summary>
    private CombatEvent Regular(Unit attacker)
    {
        Compare(Attacks, final: false);
        if (Attacks >= _regular.Count)
        {
            // An incomplete transcript (the log stopped) ends the replay where it ends; a complete one says the fight was over.
            throw _transcript.Complete
                ? Diverge(attacker, "attacker", "no further attack (fight over)", Name(attacker), ReplayCategory.AttackerOrder)
                : new ReplayDivergenceException(null);
        }

        var log = _events[_regular[Attacks]];
        if (!_toLog.TryGetValue(attacker, out var id) || id != log.Entity)
        {
            throw Diverge(attacker, "attacker", LogName(log.Entity), Name(attacker), ReplayCategory.AttackerOrder, log.Entity, card: LogCard(log.Entity));
        }

        return log;
    }

    /// <summary>Checks (or, given the candidates, picks) the defender of an attack against the log's.</summary>
    private int CheckDefender(CombatEvent log, Unit? target, IReadOnlyList<Unit>? candidates)
    {
        if (candidates is not null)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (_toLog.TryGetValue(candidates[i], out var id) && id == log.Other)
                {
                    return i;
                }
            }

            throw Diverge(_toSim.GetValueOrDefault(log.Entity), "defender", LogName(log.Other),
                "one of " + string.Join(", ", candidates.Select(Name)), ReplayCategory.Target, log.Entity);
        }

        if (target is not null && (!_toLog.TryGetValue(target, out var targetId) || targetId != log.Other))
        {
            throw Diverge(target, "defender", LogName(log.Other), Name(target), ReplayCategory.Target, log.Other);
        }

        return -1;
    }

    /// <summary>The next attack inside another attack in the current one's window, by this attacker, not matched yet.</summary>
    private int FindNested(Unit attacker)
    {
        if (!_toLog.TryGetValue(attacker, out var id))
        {
            return -1;
        }

        var (from, to) = Window();
        for (var i = from; i < to; i++)
        {
            if (_events[i] is { Kind: CombatEventKind.Attack, Value: > 0 } e && e.Entity == id && !_used.Contains(i))
            {
                return i;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------------------------------------ decisions of effects

    /// <summary>A minion an effect picked: the first candidate the log's effect of the same source changed.</summary>
    private int FromAffected(in CombatChoice choice)
    {
        var units = choice.Units!;
        var ids = units.Select(u => _toLog.GetValueOrDefault(u)).ToArray();
        foreach (var (from, to) in Regions(choice.Source))
        {
            for (var i = from; i < to; i = Next(i))
            {
                var e = _events[i];
                if (!Changes.Contains(e.Kind) || _used.Contains(i))
                {
                    continue;
                }

                var pick = Array.IndexOf(ids, e.Entity);
                if (pick >= 0 && e.Entity != 0)
                {
                    // Everything this effect did to that minion answers this decision, not the next one.
                    for (var j = from; j < to; j = Next(j))
                    {
                        if (_events[j].Entity == e.Entity && Changes.Contains(_events[j].Kind))
                        {
                            _used.Add(j);
                        }
                    }

                    return pick;
                }
            }
        }

        throw Diverge(choice.Source, $"choice:{choice.Kind}", "a minion the log's effect changed", "one of " + string.Join(", ", units.Select(Name)),
            ReplayCategory.Trigger);
    }

    /// <summary>A card an effect summoned (from a pool, or the hand's best): the log's summon of the same source's side.</summary>
    private int FromSummons(in CombatChoice choice)
    {
        var cards = choice.Cards!;
        var opponent = choice.Source?.Side.IsOpponent ?? false;
        foreach (var (from, to) in Regions(choice.Source))
        {
            for (var i = from; i < to; i = Next(i))
            {
                if (_events[i] is not { Kind: CombatEventKind.Summon, Card: { } card } e || e.Opponent != opponent || _used.Contains(i))
                {
                    continue;
                }

                var pick = IndexOfCard(cards, e.Entity, card, e.Golden);
                if (pick >= 0)
                {
                    _used.Add(i);
                    return pick;
                }
            }
        }

        throw Diverge(choice.Source, $"choice:{choice.Kind}", "a summon of one of the candidates", string.Join(", ", cards.Select(c => c.CardId).Distinct().Take(8)),
            ReplayCategory.Trigger);
    }

    /// <summary>A card an effect put into the hand: the local hand only (the opponent's is hidden).</summary>
    private int FromHand(in CombatChoice choice)
    {
        var cards = choice.Cards!;
        if (choice.Source?.Side.IsOpponent != true)
        {
            foreach (var (from, to) in Regions(choice.Source))
            {
                for (var i = from; i < to; i = Next(i))
                {
                    if (_events[i] is { Kind: CombatEventKind.Hand, Card: { } card } e && !_used.Contains(i) && IndexOfCard(cards, 0, card, e.Golden) is >= 0 and var pick)
                    {
                        _used.Add(i);
                        return pick;
                    }
                }
            }
        }

        throw Diverge(choice.Source, $"choice:{choice.Kind}", "a card into the hand", "not in the log (or a hidden hand)", ReplayCategory.Other);
    }

    /// <summary>A card of the hand an effect changed (Murcules): the log's stat change on that card.</summary>
    private int FromHandTags(in CombatChoice choice)
    {
        var cards = choice.Cards!;
        foreach (var (from, to) in Regions(choice.Source))
        {
            for (var i = from; i < to; i = Next(i))
            {
                if (_events[i] is not { Kind: CombatEventKind.Tag } e || _used.Contains(i))
                {
                    continue;
                }

                for (var c = 0; c < cards.Count; c++)
                {
                    if (cards[c].EntityId == e.Entity)
                    {
                        _used.Add(i);
                        return c;
                    }
                }
            }
        }

        throw Diverge(choice.Source, $"choice:{choice.Kind}", "a hand card the log changed", "none", ReplayCategory.Trigger);
    }

    private int IndexOfCard(IReadOnlyList<SimMinion> cards, int entity, string loggedCard, bool premium)
    {
        for (var c = 0; c < cards.Count; c++)
        {
            if (entity > 0 && cards[c].EntityId == entity)
            {
                return c;
            }
        }

        var (normal, golden) = Normal(loggedCard, premium);
        for (var c = 0; c < cards.Count; c++)
        {
            if (cards[c].CardId == normal && cards[c].Golden == golden)
            {
                return c;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where the log's answer to a decision of <paramref name="source"/> is looked for: first each triggered effect of
    /// that source within the current attack (its own events, not those of effects nested in it), then the whole attack.
    /// </summary>
    private IEnumerable<(int From, int To)> Regions(Unit? source)
    {
        var (from, to) = Window();
        if (source is not null)
        {
            for (var i = from; i < to; i++)
            {
                if (_events[i].Kind == CombatEventKind.Trigger && IsSource(_events[i], source))
                {
                    yield return (i + 1, Math.Min(_end[i], to));
                }
            }
        }

        yield return (from, to);
    }

    /// <summary>Skips a nested triggered effect: its events belong to its own source.</summary>
    private int Next(int i) => _events[i].Kind == CombatEventKind.Trigger ? _end[i] + 1 : i + 1;

    private bool IsSource(CombatEvent trigger, Unit source)
    {
        if (_toLog.TryGetValue(source, out var id))
        {
            return trigger.Entity == id || trigger.Other == id;
        }

        return trigger.Card is { } card && Normal(card, false).Card == source.CardId && trigger.Opponent == source.Side.IsOpponent;
    }

    /// <summary>The events of the regular attack being replayed (before the first: up to it).</summary>
    private (int From, int To) Window()
    {
        if (_regular.Count == 0)
        {
            return (0, _events.Count);
        }

        var current = Math.Max(0, Attacks - 1);
        var from = Attacks == 0 ? 0 : _regular[Math.Min(current, _regular.Count - 1)];
        var to = Attacks < _regular.Count ? _regular[Attacks] : _events.Count;
        return (from, Math.Max(from, to));
    }
}

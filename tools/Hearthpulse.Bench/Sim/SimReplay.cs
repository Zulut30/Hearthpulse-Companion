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

/// <summary>What part of the engine a replay's divergence points at.</summary>
internal enum ReplayCategory
{
    /// <summary>The sim's attacker (or number of attacks) is not the log's.</summary>
    AttackerOrder,

    /// <summary>The log's defender was not a target the sim allowed.</summary>
    Target,

    /// <summary>Health differs and the log shows damage (or nothing) on the minion.</summary>
    Damage,

    /// <summary>A keyword differs.</summary>
    Keyword,

    /// <summary>A minion died in one and not in the other.</summary>
    DeathOrder,

    /// <summary>A minion was summoned in one and not in the other, or stands elsewhere.</summary>
    SummonPosition,

    /// <summary>Stats differ and the log changed them outside any triggered effect.</summary>
    Aura,

    /// <summary>Stats differ and a triggered effect of the log changed them, or a decision of an effect is not the log's.</summary>
    Trigger,

    /// <summary>Anything else (an engine error, a hidden value).</summary>
    Other,
}

/// <summary>
/// The first place a replayed fight left the log: after <see cref="Attack"/> regular attacks (0 = before the first),
/// the minion (log entity and card), the field, what the log had and what the sim had, the effects around it and the
/// engine rule it points at.
/// </summary>
internal sealed record Divergence(int Attack, int Entity, string Card, string Field, string Expected, string Actual,
                                  ReplayCategory Category, IReadOnlyList<string> Effects)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"after attack {Attack}: {Category} {Field} of {(Card.Length > 0 ? Card : "-")}#{Entity}: log {Expected}, sim {Actual}{(Effects.Count > 0 ? " | effects: " + string.Join(", ", Effects) : string.Empty)}");
}

/// <summary>One fight of a game with its capture and transcript, as a log or a capture file gives it.</summary>
internal sealed record ReplayableFight(string Key, SavedFight Fight);

/// <summary>A Battlegrounds game whose fights can be replayed.</summary>
internal sealed record ReplayableGame(string Key, DateTime StartedAt, int Build, bool Duos, IReadOnlyList<ReplayableFight> Fights);

/// <summary>
/// A replayed fight: regular attacks in the log and how many the sim made before it stopped, whether the transcript
/// reached the fight's end, and the first divergence (null: the sim followed the log to the end).
/// </summary>
internal sealed record ReplayedFight(string Key, int Turn, string Result, int LogAttacks, int Attacks, bool Complete, string Ending, Divergence? First)
{
    /// <summary>Replayed to the end of a complete transcript without a divergence.</summary>
    public bool Clean => Complete && First is null;
}

/// <summary>
/// Replays real Battlegrounds fights through the engine (Bench <c>simreplay</c>, docs/bg-combat-sim.md): one fight per
/// capture, every decision answered from the fight's transcript (<see cref="TranscriptChooser"/>), the boards compared
/// with the log's before every regular attack and at the end.
/// </summary>
internal static class SimReplay
{
    /// <summary>The Battlegrounds games of one log with their captured fights and transcripts, in order.</summary>
    public static IReadOnlyList<ReplayableGame> Read(IEnumerable<string> lines, string source, DateOnly date, CardDb cards)
    {
        var parser = new PowerParser(date);
        // The tracker's own simulations are not what is replayed: one run each, so reading a log stays fast.
        var tracker = new BattlegroundsTracker { Cards = cards };
        tracker.SimOptions = tracker.SimOptions with { Runs = 1, MinRuns = 0 };
        parser.AddListener(tracker);

        var games = new List<ReplayableGame>();
        GameState? current = null;
        IReadOnlyList<BattlegroundsTracker.CapturedFight> captures = [];
        IReadOnlyList<CombatRecord> combats = [];

        void Close()
        {
            if (current is { IsBattlegrounds: true } game && captures.Count > 0)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{source}#{games.Count + 1}");
                var saved = SavedCaptures.From(game.StartedAt, game.BuildNumber, game.IsDuos, captures, combats);
                games.Add(new ReplayableGame(key, game.StartedAt, game.BuildNumber, game.IsDuos,
                    saved.Fights.Select((f, i) => new ReplayableFight(string.Create(CultureInfo.InvariantCulture, $"{key}/{i + 1}"), f)).ToList()));
            }

            captures = [];
            combats = [];
        }

        void Look()
        {
            if (!ReferenceEquals(parser.Game, current))
            {
                Close();
                current = parser.Game;
            }

            if (current is not { IsBattlegrounds: true })
            {
                return;
            }

            // Copied when they change: the tracker forgets them when the next game starts. A transcript replaces its
            // capture when the fight ends; a combat's damage is filled in after it was added.
            var nowCaptures = tracker.Captures;
            if (nowCaptures.Count != captures.Count || (nowCaptures.Count > 0 && !ReferenceEquals(nowCaptures[^1], captures[^1])))
            {
                captures = nowCaptures.ToList();
            }

            if (tracker.Combats.Count > 0)
            {
                combats = tracker.Combats.ToList();
            }
        }

        foreach (var line in lines)
        {
            parser.Feed(line);
            Look();
        }

        parser.Flush();
        Look();
        Close();
        return games;
    }

    /// <summary>A game of the local capture file (<see cref="CombatCaptureStore"/>) as a game to replay.</summary>
    public static ReplayableGame From(SavedCaptures saved, string source) =>
        new(source, saved.StartedAt, saved.Build, saved.Duos,
            saved.Fights.Select((f, i) => new ReplayableFight(string.Create(CultureInfo.InvariantCulture, $"{source}/{i + 1}"), f)).ToList());

    /// <summary>
    /// Replays one fight: the captured boards fight once, every decision answered from the transcript, the boards
    /// checked against the log's. A fight without a transcript gives null.
    /// </summary>
    public static ReplayedFight? Replay(string key, SavedFight fight, CardDb cards, EffectRegistry? effects = null)
    {
        if (fight.Transcript is not { } transcript)
        {
            return null;
        }

        var chooser = new TranscriptChooser(transcript, cards);
        Divergence? first;
        try
        {
            CombatSimulator.RunSingle(fight.Ours, fight.Theirs, fight.OursFirst, chooser, effects ?? EffectRegistry.Default, trace: false,
                fight.FirstAttackerEntityId);
            first = chooser.Finish();
        }
        catch (ReplayDivergenceException ex)
        {
            first = ex.Divergence;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or IndexOutOfRangeException
                                       or KeyNotFoundException)
        {
            first = new Divergence(chooser.Attacks, 0, string.Empty, "engine", "no error", $"{ex.GetType().Name}: {ex.Message}", ReplayCategory.Other, []);
        }

        return new ReplayedFight(key, fight.Turn, fight.Result, transcript.RegularAttacks, chooser.Attacks, transcript.Complete, transcript.Ending, first);
    }

    /// <summary>A fight's transcript, boards and the engine's trace of its replay, for a developer chasing a divergence.</summary>
    public static IEnumerable<string> Dump(SavedFight fight, CardDb cards, EffectRegistry? effects = null)
    {
        if (fight.Transcript is not { } transcript)
        {
            yield break;
        }

        static string Side(IEnumerable<SimMinion> minions) =>
            string.Join(" | ", minions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.CardId}{(m.Golden ? "(G)" : "")}#{m.EntityId} {m.Attack}/{m.Health} {m.Keywords}")));
        yield return $"capture ours: {Side(fight.Ours.Minions)}";
        yield return $"capture theirs: {Side(fight.Theirs.Minions)}";
        yield return string.Create(CultureInfo.InvariantCulture, $"first: {fight.OursFirst} #{fight.FirstAttackerEntityId}; complete {transcript.Complete} ({transcript.Ending})");
        var boards = transcript.Boards.ToDictionary(b => b.Event, b => b);
        var depth = 0;
        for (var i = 0; i <= transcript.Events.Count; i++)
        {
            foreach (var board in transcript.Boards.Where(b => b.Event == i))
            {
                static string Log(IEnumerable<MinionState> minions) =>
                    string.Join(" | ", minions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Card}#{m.Entity} {m.Attack}/{m.Health} {m.Keywords}")));
                yield return $"  board ours:   {Log(board.Ours)}";
                yield return $"  board theirs: {Log(board.Theirs)}";
            }

            if (i == transcript.Events.Count)
            {
                break;
            }

            var e = transcript.Events[i];
            if (e.Kind == CombatEventKind.End) depth = Math.Max(0, depth - 1);
            yield return string.Create(CultureInfo.InvariantCulture,
                $"{i,5} {new string(' ', depth * 2)}{e.Kind} #{e.Entity}{(e.Other != 0 ? $" -> #{e.Other}" : "")}{(e.Value != 0 ? $" v={e.Value}" : "")}{(e.Card is null ? "" : " " + e.Card)}{(e.Tag is null ? "" : " " + e.Tag)}{(e.Opponent ? " (theirs)" : "")}{(e.Golden ? " (G)" : "")}");
            if (e.Kind == CombatEventKind.Trigger) depth++;
        }

        var chooser = new TranscriptChooser(transcript, cards);
        FightResult? result = null;
        string? error = null;
        try
        {
            result = CombatSimulator.RunSingle(fight.Ours, fight.Theirs, fight.OursFirst, chooser, effects ?? EffectRegistry.Default, trace: true,
                fight.FirstAttackerEntityId);
        }
        catch (ReplayDivergenceException ex)
        {
            error = ex.Divergence?.ToString() ?? "the transcript ends here";
        }

        foreach (var line in chooser.Trace)
        {
            yield return "sim " + line;
        }

        yield return error ?? chooser.Finish()?.ToString() ?? $"no divergence ({result?.Outcome})";
    }

    // -------------------------------------------------------------------------------------------------- report

    /// <summary>The summary and the per-fight list <c>simreplay</c> prints.</summary>
    public static IEnumerable<string> Report(IReadOnlyList<ReplayedFight> fights)
    {
        var complete = fights.Where(f => f.Complete).ToList();
        var clean = complete.Count(f => f.Clean);
        var (low, high) = SimStats.Wilson(clean, complete.Count);
        yield return string.Create(CultureInfo.InvariantCulture,
            $"fights with a transcript: {fights.Count}; complete: {complete.Count}; incomplete (not counted): {fights.Count - complete.Count}");
        yield return string.Create(CultureInfo.InvariantCulture,
            $"replayed to the end without divergence: {clean} / {complete.Count} = {Percent(clean, complete.Count)} (Wilson 95 %: {low * 100:0.0}–{high * 100:0.0} %)");
        var diverged = complete.Where(f => f.First is not null).ToList();
        yield return string.Create(CultureInfo.InvariantCulture, $"first divergences by category ({diverged.Count}):");
        foreach (var group in diverged.GroupBy(f => f.First!.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"  {group.Key,-15} {group.Count(),3}  {Percent(group.Count(), complete.Count)}");
        }

        yield return "per fight (key, turn, result, attacks replayed / in the log, ending, first divergence):";
        foreach (var f in fights)
        {
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {f.Key} t{f.Turn} {f.Result,-4} {f.Attacks}/{f.LogAttacks} {f.Ending,-7} {(f.First is null ? (f.Complete ? "ok" : "incomplete") : f.First.ToString())}");
        }
    }

    private static string Percent(int k, int n) =>
        n == 0 ? "-" : (k * 100.0 / n).ToString("0.0", CultureInfo.InvariantCulture) + " %";
}

/// <summary>Stops a replay at its first divergence, or (null) where an incomplete transcript ends.</summary>
internal sealed class ReplayDivergenceException(Divergence? divergence) : Exception(divergence?.ToString() ?? "the transcript ends here")
{
    public Divergence? Divergence { get; } = divergence;
}

/// <summary>
/// Answers every decision of a fight from its transcript and checks the sim against it. Minions are matched by the log's
/// entity id: the captured ones by their own, each summoned one with the log's summon of the same card on the same side
/// in the same order (the n-th such summon in the sim is the n-th in the log; its place is checked with the boards).
/// A decision is answered by what the log did inside the triggered effect of the same source within the attack being
/// replayed (the minion it changed, the card it summoned), else anywhere within that attack; a decision the log cannot
/// answer, or answers with a candidate the sim did not offer, is a divergence.
/// </summary>
internal sealed class TranscriptChooser : ICombatChooser
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

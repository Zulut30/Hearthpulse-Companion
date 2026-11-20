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
    /// <summary>
    /// The replayed fight's result (Win, Tie, Loss from the player's side) and the hero damage it gives by
    /// <see cref="DamageRules"/>; empty when the replay stopped early. A clean replay whose result and damage are the
    /// log's shows that the rules and inputs give the real outcome once the random decisions are the log's: a missed
    /// forecast of such a fight comes from chance, not from the engine.
    /// </summary>
    public string SimResult { get; init; } = string.Empty;

    /// <summary>Hero damage of the replayed fight (to the loser), -1 when unknown.</summary>
    public int SimDamage { get; init; } = -1;

    /// <summary>Hero damage the log recorded for the fight (to the loser).</summary>
    public int LogDamage { get; init; }

    /// <summary>The random decisions the log made in this fight: kind, number of candidates, index picked.</summary>
    public IReadOnlyList<(ChoiceKind Kind, int Count, int Pick)> Decisions { get; init; } = [];

    /// <summary>The replay gave the log's result and, for a decided fight, its hero damage.</summary>
    public bool OutcomeMatches => SimResult == Result && (Result == "Tie" || SimDamage == LogDamage);

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
        var recorder = new RecordingChooser(chooser);
        Divergence? first;
        FightResult? result = null;
        var ours = SimInputs.PostGame(fight.Ours);
        var theirs = SimInputs.PostGame(fight.Theirs);
        try
        {
            result = CombatSimulator.RunSingle(ours, theirs, fight.OursFirst, recorder, effects ?? EffectRegistry.Default, trace: false,
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

        var replayed = new ReplayedFight(key, fight.Turn, fight.Result, transcript.RegularAttacks, chooser.Attacks, transcript.Complete, transcript.Ending, first)
        {
            LogDamage = fight.Result switch { "Win" => fight.DamageDealt, "Loss" => fight.DamageTaken, _ => 0 },
            Decisions = recorder.Decisions,
        };
        if (result is null)
        {
            return replayed;
        }

        var (simResult, simDamage) = result.Outcome switch
        {
            > 0 => ("Win", DamageRules.Taken(DamageRules.Damage(ours.TavernTier, result.Ours.Select(m => m.TechLevel), fight.DamageCap), theirs.Health,
                DamageRules.HasIceBlock(theirs))),
            < 0 => ("Loss", DamageRules.Taken(DamageRules.Damage(theirs.TavernTier, result.Theirs.Select(m => m.TechLevel), fight.DamageCap), ours.Health,
                DamageRules.HasIceBlock(ours))),
            _ => ("Tie", 0),
        };
        return replayed with { SimResult = simResult, SimDamage = simDamage };
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
        static string Pairs(IEnumerable<KeyValuePair<string, int>> tags) =>
            string.Join(", ", tags.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Key}={t.Value}")));
        yield return $"capture ours: {Side(fight.Ours.Minions)}";
        yield return $"capture theirs: {Side(fight.Theirs.Minions)}";
        foreach (var (name, side) in new[] { ("ours", fight.Ours), ("theirs", fight.Theirs) })
        {
            // The side's player-level inputs: Deity, trinkets, exact and hidden player values, estimates.
            yield return $"inputs {name}: extras [{string.Join(", ", side.Extras)}]; deity tags [{Pairs(side.Tags.Where(t => t.Key.StartsWith("BACON_OLD_GOD", StringComparison.Ordinal)))}]; awakening {side.DeityAwakening?.Progress}/{side.DeityAwakening?.Total}; hidden [{Pairs(side.HiddenTags)}]; estimates [{string.Join(", ", side.Estimates.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Input} {e.Value1}/{e.Value2}")))}]";
        }

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
            result = CombatSimulator.RunSingle(SimInputs.PostGame(fight.Ours), SimInputs.PostGame(fight.Theirs), fight.OursFirst, chooser, effects ?? EffectRegistry.Default, trace: true,
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

        // The result and hero damage of the clean replays against the log's: with the log's decisions the engine gives
        // the real outcome, so what is left of a missed forecast is chance (or a damage rule when only damage differs).
        var outcome = complete.Where(f => f.Clean && f.SimResult.Length > 0).ToList();
        yield return string.Create(CultureInfo.InvariantCulture,
            $"clean replays with the log's result and hero damage: {outcome.Count(f => f.OutcomeMatches)} / {outcome.Count}");
        foreach (var line in DecisionCheck(outcome))
        {
            yield return line;
        }

        yield return "per fight (key, turn, result, attacks replayed / in the log, ending, first divergence; replayed result and damage when not the log's):";
        foreach (var f in fights)
        {
            var differs = f.SimResult.Length > 0 && !f.OutcomeMatches
                ? string.Create(CultureInfo.InvariantCulture, $"; replay {f.SimResult} {f.SimDamage} vs log {f.Result} {f.LogDamage}")
                : string.Empty;
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {f.Key} t{f.Turn} {f.Result,-4} {f.Attacks}/{f.LogAttacks} {f.Ending,-7} {(f.First is null ? (f.Complete ? "ok" : "incomplete") : f.First.ToString())}{differs}");
        }
    }

    /// <summary>
    /// Are the log's random picks uniform over the candidates the engine offers, as the engine draws them? Per kind of
    /// decision with at least two candidates: the number of decisions, the mean relative position of the pick
    /// ((index + 1/2) / candidates; 0.5 when uniform) as a z-score, and Pearson's chi-square over candidate count and
    /// index (Wilson-Hilferty z). |z| above 2 would point at a bias the engine does not model (calibration of the odds).
    /// </summary>
    internal static IEnumerable<string> DecisionCheck(IEnumerable<ReplayedFight> fights)
    {
        var decisions = fights.SelectMany(f => f.Decisions).Where(d => d.Count >= 2).ToList();
        yield return string.Create(CultureInfo.InvariantCulture, $"random decisions of the clean replays (2+ candidates): {decisions.Count}");
        foreach (var kind in decisions.GroupBy(d => d.Kind).OrderBy(g => g.Key))
        {
            double shift = 0, variance = 0, chi = 0;
            var dof = 0;
            foreach (var d in kind)
            {
                shift += ((d.Pick + 0.5) / d.Count) - 0.5;
                variance += ((double)d.Count * d.Count - 1) / (12.0 * d.Count * d.Count);
            }

            foreach (var byCount in kind.GroupBy(d => d.Count))
            {
                var n = byCount.Count();
                var expected = n / (double)byCount.Key;
                for (var i = 0; i < byCount.Key; i++)
                {
                    var seen = byCount.Count(d => d.Pick == i);
                    chi += (seen - expected) * (seen - expected) / expected;
                }

                dof += byCount.Key - 1;
            }

            // Wilson-Hilferty: (chi/dof)^(1/3) is close to normal with mean 1 - 2/(9 dof) and variance 2/(9 dof).
            var chiZ = dof > 0 ? (Math.Cbrt(chi / dof) - (1 - (2.0 / (9 * dof)))) / Math.Sqrt(2.0 / (9 * dof)) : 0;
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {kind.Key,-15} {kind.Count(),4}  position z {(variance > 0 ? shift / Math.Sqrt(variance) : 0),5:0.00}  chi-square {chi:0.0} on {dof} df (z {chiZ:0.00})");
        }
    }

    private static string Percent(int k, int n) =>
        n == 0 ? "-" : (k * 100.0 / n).ToString("0.0", CultureInfo.InvariantCulture) + " %";
}

/// <summary>Passes every decision to the transcript's chooser and keeps what it picked, for <see cref="SimReplay.DecisionCheck"/>.</summary>
internal sealed class RecordingChooser(ICombatChooser inner) : ICombatChooser
{
    private readonly List<(ChoiceKind Kind, int Count, int Pick)> _decisions = [];

    public IReadOnlyList<(ChoiceKind Kind, int Count, int Pick)> Decisions => _decisions;

    public int Choose(Fight fight, in CombatChoice choice)
    {
        var pick = inner.Choose(fight, choice);
        _decisions.Add((choice.Kind, choice.Count, pick));
        return pick;
    }

    public void OnAttack(Fight fight, Unit attacker, Unit target, bool nested) => inner.OnAttack(fight, attacker, target, nested);

    public void OnSummon(Fight fight, Unit unit) => inner.OnSummon(fight, unit);
}

/// <summary>Stops a replay at its first divergence, or (null) where an incomplete transcript ends.</summary>
internal sealed class ReplayDivergenceException(Divergence? divergence) : Exception(divergence?.ToString() ?? "the transcript ends here")
{
    public Divergence? Divergence { get; } = divergence;
}

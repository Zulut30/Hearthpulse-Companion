using System.Globalization;
using System.IO;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Bench;

/// <summary>
/// One recorded combat of a log: what happened, and the boards and options the app handed the simulator at its first
/// attack. <see cref="Capture"/> is null for a combat the app gave no odds (no attack was seen).
/// </summary>
internal sealed record SimFight(
    string Key,
    int Turn,
    string Result,
    int DamageTaken,
    int DamageDealt,
    BattlegroundsTracker.CombatCapture? Capture,
    SimOptions? Options);

/// <summary>A Battlegrounds game of a log: when it was played, the game build, the mode and its combats.</summary>
internal sealed record SimGame(string Key, DateTime StartedAt, int Build, bool Duos, IReadOnlyList<SimFight> Fights);

/// <summary>How many fights the simulator gets for one estimate.</summary>
internal enum SimBudget
{
    /// <summary>The first pass on the log thread (<see cref="BattlegroundsTracker.QuickPass"/>): what the overlay shows at once.</summary>
    Quick,

    /// <summary>The background pass with the tracker's own options: what replaces the first pass and goes to history.</summary>
    App,

    /// <summary>Every run of <see cref="SimSample.FullRuns"/>, no time limit: the model's error without the sampling error.</summary>
    Full,
}

/// <summary>One fight scored under one budget; a line of the per-fight file two runs are compared by.</summary>
/// <param name="Clean">Nothing in the fight was "not simulated".</param>
/// <param name="Hit">The most likely result was the real one (as <see cref="BattlegroundsStats.SimAccuracy"/> counts it).</param>
/// <param name="DamageSeen">The fight was won or lost with damage seen, so the damage forecast can be checked.</param>
/// <param name="DamageHit">That damage fell inside the forecast's 10th to 90th percentile.</param>
internal sealed record SimScore(
    string Key,
    SimBudget Budget,
    string Result,
    int Runs,
    double Win,
    double Tie,
    double Loss,
    int Unsimulated,
    bool Hit,
    bool DamageSeen,
    bool DamageHit,
    double? Brier)
{
    public bool Clean => Unsimulated == 0;
}

/// <summary>Two runs of one budget over the same fights, compared pair by pair.</summary>
internal sealed record SimComparison(SimBudget Budget, PairedShare Clean, PairedShare Hit, PairedShare Damage, PairedMean Brier);

/// <summary>
/// The simulator's measurement sample (docs/bg-combat-sim.md): the Battlegrounds combats of kept Power logs, replayed
/// through <see cref="BattlegroundsTracker"/> as the app does, each then simulated again under a chosen budget. The
/// boards are seeded, so a fight gives the same numbers on every run with the same number of fights.
/// </summary>
internal static class SimSample
{
    /// <summary>Runs of <see cref="SimBudget.Full"/>: <see cref="SimOptions.Runs"/>'s default.</summary>
    public static int FullRuns => SimOptions.Default.Runs;

    private const string Header = "key,budget,result,runs,win,tie,loss,unsimulated,hit,damage_seen,damage_hit,brier";

    /// <summary>
    /// The Battlegrounds games of one log, in order. <paramref name="source"/> names the log in the fights' keys (no
    /// player's name: a session folder and a file).
    /// </summary>
    public static IReadOnlyList<SimGame> Read(IEnumerable<string> lines, string source, DateOnly date, CardDb cards)
    {
        var parser = new PowerParser(date);
        // The tracker's own simulations are not what is measured: one run each, so reading a log stays fast.
        var tracker = new BattlegroundsTracker { Cards = cards };
        tracker.SimOptions = tracker.SimOptions with { Runs = 1, MinRuns = 0 };
        parser.AddListener(tracker);

        var games = new List<SimGame>();
        GameState? current = null;
        IReadOnlyList<CombatRecord> combats = [];
        var captured = new Dictionary<int, (BattlegroundsTracker.CombatCapture Capture, SimOptions Options)>();
        BattlegroundsTracker.CombatCapture? lastCapture = null;
        (BattlegroundsTracker.CombatCapture Capture, SimOptions Options)? pending = null;

        void Close()
        {
            if (current is { IsBattlegrounds: true } game && combats.Count > 0)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{source}#{games.Count + 1}");
                var fights = combats.Select((c, i) =>
                {
                    // A capture counts only with the odds the app saved for that combat.
                    var taken = c.Odds is not null && captured.TryGetValue(i, out var at) ? at : ((BattlegroundsTracker.CombatCapture, SimOptions)?)null;
                    return new SimFight(string.Create(CultureInfo.InvariantCulture, $"{key}/{i + 1}"), c.Turn, c.Result, c.DamageTaken, c.DamageDealt,
                        taken?.Item1, taken?.Item2);
                }).ToList();
                games.Add(new SimGame(key, game.StartedAt, game.BuildNumber, game.IsDuos, fights));
            }

            combats = [];
            captured.Clear();
            pending = null;
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

            if (tracker.LastCapture is { } capture && !ReferenceEquals(capture, lastCapture) && tracker.LastCaptureOptions is { } options)
            {
                lastCapture = capture;
                pending = (capture, options);
            }

            var count = tracker.Combats.Count;
            if (count > combats.Count && pending is { } taken)
            {
                captured[count - 1] = taken;
                pending = null;
            }

            if (count > 0)
            {
                // Copied every time: a combat's damage is filled in after it was added.
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

    /// <summary>The options the app's simulation of <paramref name="captured"/> runs with under <paramref name="budget"/>.</summary>
    public static SimOptions Budgeted(SimOptions captured, SimBudget budget)
    {
        var app = new BattlegroundsTracker().SimOptions;
        return budget switch
        {
            SimBudget.Quick => BattlegroundsTracker.QuickPass(captured),
            SimBudget.App => captured with { Runs = app.Runs, MinRuns = app.MinRuns, TimeBudget = app.TimeBudget },
            _ => captured with { Runs = FullRuns, MinRuns = FullRuns, TimeBudget = null },
        };
    }

    /// <summary>Simulates a captured fight under <paramref name="budget"/> and scores it against what happened; null without a capture.</summary>
    public static SimScore? Score(SimFight fight, SimBudget budget)
    {
        if (fight.Capture is not { } capture || fight.Options is not { } options)
        {
            return null;
        }

        var odds = CombatSimulator.Run(capture.Ours, capture.Theirs, capture.OursFirst, Budgeted(options, budget));
        return Score(fight, budget, odds);
    }

    /// <summary>Scores <paramref name="odds"/> against the fight's real result and damage.</summary>
    public static SimScore Score(SimFight fight, SimBudget budget, CombatOdds odds)
    {
        var likely = new BgCombatPrediction(fight.Turn, odds.Win, odds.Tie, odds.Loss, null, null).Likely;
        var (spread, damage) = fight.Result switch
        {
            "Win" => (odds.DamageDealt, fight.DamageDealt),
            "Loss" => (odds.DamageTaken, fight.DamageTaken),
            _ => (null, 0),
        };
        var covered = spread is not null && new BgDamageRange(spread.Mean, spread.Low, spread.High, spread.Max).Covers(damage);
        return new SimScore(fight.Key, budget, fight.Result, odds.Runs, odds.Win, odds.Tie, odds.Loss, odds.Unsimulated,
            Hit: likely == fight.Result,
            DamageSeen: damage > 0,
            DamageHit: damage > 0 && covered,
            Brier: SimStats.Brier(odds.Win, odds.Tie, odds.Loss, fight.Result));
    }

    /// <summary>The per-fight file: one line per fight and budget, in the order given.</summary>
    public static void Write(TextWriter writer, IEnumerable<SimScore> scores)
    {
        writer.WriteLine(Header);
        foreach (var s in scores)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{s.Key},{s.Budget},{s.Result},{s.Runs},{s.Win:R},{s.Tie:R},{s.Loss:R},{s.Unsimulated},{(s.Hit ? 1 : 0)},{(s.DamageSeen ? 1 : 0)},{(s.DamageHit ? 1 : 0)},{s.Brier?.ToString("R", CultureInfo.InvariantCulture)}"));
        }
    }

    /// <summary>Reads a per-fight file back; a line that is not one of its lines is an error, not a skipped fight.</summary>
    public static IReadOnlyList<SimScore> Parse(IEnumerable<string> lines)
    {
        var scores = new List<SimScore>();
        foreach (var line in lines.Where(l => l.Length > 0 && l != Header))
        {
            var t = line.Split(',');
            if (t.Length != 12)
            {
                throw new FormatException($"Not a line of a per-fight file: {line}");
            }

            static double Number(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            static int Whole(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            scores.Add(new SimScore(t[0], Enum.Parse<SimBudget>(t[1]), t[2], Whole(t[3]), Number(t[4]), Number(t[5]), Number(t[6]), Whole(t[7]),
                t[8] == "1", t[9] == "1", t[10] == "1", t[11].Length > 0 ? Number(t[11]) : null));
        }

        return scores;
    }

    /// <summary>
    /// Compares two runs budget by budget on the fights both hold, pair by pair. A fight only one of them holds is a
    /// different sample and an error: the comparison is of the same fights or it is nothing.
    /// </summary>
    public static IReadOnlyList<SimComparison> Compare(IReadOnlyList<SimScore> before, IReadOnlyList<SimScore> after)
    {
        var result = new List<SimComparison>();
        foreach (var budget in before.Select(s => s.Budget).Distinct().Order())
        {
            var was = before.Where(s => s.Budget == budget).ToList();
            var now = after.Where(s => s.Budget == budget).ToDictionary(s => s.Key, StringComparer.Ordinal);
            if (was.Count != now.Count || was.Any(s => !now.TryGetValue(s.Key, out var other) || other.Result != s.Result))
            {
                throw new InvalidOperationException($"The two runs hold different fights for the {budget} budget.");
            }

            var pairs = was.Select(s => (Before: s, After: now[s.Key])).ToList();
            var damage = pairs.Where(p => p.Before.DamageSeen && p.After.DamageSeen).ToList();
            var scored = pairs.Where(p => p.Before.Brier is not null && p.After.Brier is not null).ToList();
            result.Add(new SimComparison(budget,
                SimStats.Paired(pairs.Select(p => p.Before.Clean).ToList(), pairs.Select(p => p.After.Clean).ToList()),
                SimStats.Paired(pairs.Select(p => p.Before.Hit).ToList(), pairs.Select(p => p.After.Hit).ToList()),
                SimStats.Paired(damage.Select(p => p.Before.DamageHit).ToList(), damage.Select(p => p.After.DamageHit).ToList()),
                SimStats.Mean(scored.Select(p => p.After.Brier!.Value - p.Before.Brier!.Value).ToList())));
        }

        return result;
    }
}

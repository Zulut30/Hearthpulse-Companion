using System.Globalization;
using Hearthpulse.Core.Battlegrounds;

namespace Hearthpulse.Bench;

/// <summary>
/// simcover's list of misses: every fight whose app-budget score got the result wrong or the damage outside the
/// forecast, simulated again under the full budget (seeded, so the same numbers on every run) with the odds, the real
/// damage and the forecast's 10th-90th percentile, and the odds again on the exact post-game inputs, so a miss can be traced without rerunning the fight by hand.
/// </summary>
internal static class SimMisses
{
    /// <summary>One line per missed fight of <paramref name="app"/>, in the order of <paramref name="fights"/>.</summary>
    public static IEnumerable<string> Lines(IReadOnlyList<SimFight> fights, IReadOnlyList<SimScore> app)
    {
        var missed = app.Where(s => !s.Hit || (s.DamageSeen && !s.DamageHit)).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var fight in fights.Where(f => missed.Contains(f.Key)))
        {
            var capture = fight.Capture!;
            var odds = CombatSimulator.Run(capture.Ours, capture.Theirs, capture.OursFirst, SimSample.Budgeted(fight.Options!, SimBudget.Full));
            // The same fight on the exact values the log revealed by the end (what simreplay replays): a miss that
            // goes away here comes from an estimated hidden input, not from a rule or an effect.
            var exact = CombatSimulator.Run(SimInputs.PostGame(capture.Ours), SimInputs.PostGame(capture.Theirs), capture.OursFirst,
                SimSample.Budgeted(fight.Options!, SimBudget.Full) with { PostGame = true });
            var estimated = capture.Ours.Estimates.Count + capture.Theirs.Estimates.Count;
            var score = app.First(s => s.Key == fight.Key);
            var (spread, damage) = fight.Result switch
            {
                "Win" => (odds.DamageDealt, fight.DamageDealt),
                "Loss" => (odds.DamageTaken, fight.DamageTaken),
                _ => (null, 0),
            };
            // How likely the forecast found what happened: the real result, and damage at least as far out as the real one
            // on its side of the mean (a small share is a real surprise; a fair share is chance the interval left out).
            var real = fight.Result switch { "Win" => odds.Win, "Tie" => odds.Tie, "Loss" => odds.Loss, _ => 0 };
            var tail = spread is null || spread.Runs == 0 ? (double?)null
                : (damage >= spread.Mean ? spread.Counts.Skip(damage).Sum() : spread.Counts.Take(damage + 1).Sum()) / (double)spread.Runs;
            var range = spread is null ? "-" : string.Create(CultureInfo.InvariantCulture,
                $"{spread.Low}-{spread.High} (mean {spread.Mean:0.0}, max {spread.Max}, p(as far) {tail:P0})");
            var what = (score.Hit ? string.Empty : "result ") + (score.DamageSeen && !score.DamageHit ? "damage" : string.Empty);
            yield return string.Create(CultureInfo.InvariantCulture,
                $"{fight.Key} t{fight.Turn} {fight.Result,-4} [{what.Trim()}] p(real) {real:P0}; full W/T/L {odds.Win:P0}/{odds.Tie:P0}/{odds.Loss:P0}; exact inputs {exact.Win:P0}/{exact.Tie:P0}/{exact.Loss:P0} ({estimated} estimated); damage {damage} vs {range}; first {capture.OursFirst switch { true => "ours", false => "theirs", null => "coin" }}, boards {capture.Ours.Minions.Count} v {capture.Theirs.Minions.Count}");
        }
    }
}

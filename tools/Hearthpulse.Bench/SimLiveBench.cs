using System.Diagnostics;
using System.Globalization;
using System.IO;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Stats;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>
/// The app's live path for Battlegrounds odds over kept logs (TASK-2026-11-26 AC5): every <c>Power*.log</c> under
/// --logs (any depth) holding a Battlegrounds game is replayed through <see cref="Core.Tracking.GameSession"/> as
/// <see cref="LogReplay"/> feeds it (100 ms batches, OnTick after each, the card database in the language the app would
/// use). After a tick that first showed a fight's odds the replay waits <see cref="BackgroundWait"/>, as a live fight
/// gives the background pass seconds before the next one starts. Printed: per fight, odds on screen, in the session's
/// combat list and in the history row; per game, Power.log lines the session could not read; the Diag errors of the run;
/// the log thread's batch times (the first pass runs inside the batch with the first attack); the accuracy line of the
/// Battlegrounds page over the rows the replay wrote (<see cref="BattlegroundsStats.SimAccuracy"/>).
/// </summary>
internal static class SimLiveBench
{
    /// <summary>The background pass stops after 200 ms (BattlegroundsTracker.DefaultSimOptions); a little more for its last chunk.</summary>
    private static readonly TimeSpan BackgroundWait = TimeSpan.FromMilliseconds(300);

    public static int Run(Options options)
    {
        var logs = Directory.Exists(options.Logs)
            ? Directory.EnumerateFiles(options.Logs, "Power*.log", SearchOption.AllDirectories)
                .Where(f => File.ReadLines(f).Any(l => l.Contains("GameType=GT_BATTLEGROUNDS", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        if (logs.Count == 0)
        {
            Console.WriteLine($"[live] no Power*.log with a Battlegrounds game under {options.Logs}");
            return 0;
        }

        var locale = Environment.GetEnvironmentVariable("HP_BENCH_LOCALE") is { Length: > 0 } asked ? asked : new AppSettings().CardLocale();
        using var http = Http();
        var cards = CardDb.LoadAsync(locale, http).GetAwaiter().GetResult();
        Console.WriteLine($"[live] cards {locale}: {cards.Count} cards; {logs.Count} logs with Battlegrounds");

        var all = new List<(string Log, LiveGame Game)>();
        // Each OnLines call: time, and what it did besides reading lines: put a fight's first odds on screen (the first
        // pass ran inside it) or save a finished game (history row, replay, captures: on the log thread too).
        var batches = new List<(double Ms, string Kind)>();
        var errors = new List<string>();
        var history = new List<GameRecord>();
        foreach (var log in logs)
        {
            var dir = Path.GetDirectoryName(log)!;
            var name = $"{Path.GetFileName(dir)}/{Path.GetFileName(log)}";
            var start = HearthstoneInstall.SessionStart(dir) ?? File.GetCreationTime(log);
            var (session, setClock, db) = LogReplay.NewSession(cards);
            var probe = new LiveOddsProbe();
            var saved = false;
            session.SnapshotChanged += probe.Observe;
            session.GameFinished += outcome =>
            {
                probe.Finished(outcome);
                saved = true;
            };
            // The fight whose first odds were seen last (its index in the view's combat list) and that list's length.
            var (fight, fights) = (-1, 0);
            var logFrom = LogLength();
            LogReplay.Run(session, setClock, dir, log, start,
                onBatch: batch =>
                {
                    saved = false;
                    var t = Stopwatch.GetTimestamp();
                    batch();
                    batches.Add((Stopwatch.GetElapsedTime(t).TotalMilliseconds, saved ? "game saved" : string.Empty));
                },
                onTick: tick =>
                {
                    tick();
                    if (session.Current.Battlegrounds is not { InCombat: true, CombatOdds: { } odds } bg)
                    {
                        return;
                    }

                    // The fight on screen, as LiveOddsProbe tells it; a shorter list is a new game.
                    var index = bg.Combats.Count > 0 && ReferenceEquals(bg.Combats[^1].Odds, odds) ? bg.Combats.Count - 1 : bg.Combats.Count;
                    if (index != fight || bg.Combats.Count < fights)
                    {
                        // The first odds of a fight are on screen: give its background pass the time a live fight gives it.
                        (fight, fights) = (index, bg.Combats.Count);
                        if (batches.Count > 0 && batches[^1].Kind.Length == 0)
                        {
                            batches[^1] = (batches[^1].Ms, "first odds");
                        }

                        Thread.Sleep(BackgroundWait);
                    }
                });

            errors.AddRange(NewErrors(logFrom));
            var rows = new HistoryStore(db).Recent(1000);
            history.AddRange(rows.Where(r => r.IsBattlegrounds));
            foreach (var game in probe.Games(rows))
            {
                all.Add((name, game));
            }

            LogReplay.Delete(db);
        }

        foreach (var (log, game) in all)
        {
            var fights = game.Fights;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[live] {log}#{game.Index} {(game.Duos ? "duos" : "solo")}: {fights.Count} fights; odds on screen {fights.Count(f => f.Shown is not null)}, in the session's last view {fights.Count(f => f.Recorded is not null)}, in history {fights.Count(f => f.Kept is not null)}{(game.HasRow ? string.Empty : " (no history row)")}; unreadable lines {game.ParseErrors}"));
            foreach (var f in fights.Where(f => f.Shown is null || (game.HasRow ? f.Kept is null : f.Recorded is null)))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[live]   t{f.Turn} {f.Result}: on screen {(f.Shown is null ? "no" : "yes")}, last view {(f.Recorded is null ? "no" : "yes")}, history {(f.Kept is null ? "no" : "yes")}"));
            }
        }

        var every = all.SelectMany(g => g.Game.Fights).ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[live] total: {all.Count} games ({all.Count(g => g.Game.Duos)} duos), {every.Count} fights; odds on screen {every.Count(f => f.Shown is not null)}, in the session's last view {every.Count(f => f.Recorded is not null)}, in history {every.Count(f => f.Kept is not null)} of {all.Where(g => g.Game.HasRow).Sum(g => g.Game.Fights.Count)} in rows; unreadable lines {all.Sum(g => g.Game.ParseErrors)}"));
        var refined = every.Where(f => f.Recorded is not null).ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[live] runs behind the kept odds: min {(refined.Count > 0 ? refined.Min(f => f.Recorded!.Runs) : 0)}, avg {(refined.Count > 0 ? refined.Average(f => f.Recorded!.Runs) : 0):N0}, max {(refined.Count > 0 ? refined.Max(f => f.Recorded!.Runs) : 0)}; not simulated in {refined.Count(f => f.Recorded!.Unsimulated > 0)}"));
        var times = batches.Select(b => b.Ms).ToList();
        var captures = batches.Where(b => b.Kind == "first odds").Select(b => b.Ms).ToList();
        Console.WriteLine($"[live] log-thread batches: {batches.Count}, {Spread(times)}; over 40 ms: {times.Count(b => b > 40)}");
        Console.WriteLine($"[live] batches that put a fight's first odds on screen (the first pass inside): {captures.Count}, {Spread(captures)}; over 40 ms: {captures.Count(b => b > 40)}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[live] slowest batches: {string.Join(", ", batches.OrderByDescending(b => b.Ms).Take(6).Select(b => $"{b.Ms:N1} ms{(b.Kind.Length > 0 ? $" ({b.Kind})" : string.Empty)}"))}"));
        var accuracy = BattlegroundsStats.SimAccuracy(history);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[live] accuracy line (rows written): result {accuracy.ResultHits} / {accuracy.Fights} = {accuracy.ResultRate:P1}, damage {accuracy.DamageHits} / {accuracy.DamageFights} = {accuracy.DamageRate:P1}"));
        Console.WriteLine($"[live] Diag errors: {errors.Count}");
        foreach (var group in errors.GroupBy(e => e.Length > 90 ? e[..90] : e).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"[live]   {group.Count(),3} × {group.Key}");
        }

        return 0;
    }

    private static long LogLength() => File.Exists(AppPaths.LogFile) ? new FileInfo(AppPaths.LogFile).Length : 0;

    /// <summary>ERROR lines Diag wrote since <paramref name="from"/> (the scratch data folder's log).</summary>
    private static IEnumerable<string> NewErrors(long from)
    {
        if (!File.Exists(AppPaths.LogFile))
        {
            return [];
        }

        using var stream = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(Math.Min(from, stream.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var at = line.IndexOf(" ERROR ", StringComparison.Ordinal);
            if (at > 0)
            {
                lines.Add(line[(at + 7)..]);
            }
        }

        return lines;
    }
}

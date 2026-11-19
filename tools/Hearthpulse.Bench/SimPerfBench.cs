using System.Diagnostics;
using System.Globalization;
using System.IO;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>One pair of boards of the speed corpus, with the options the app simulates it with.</summary>
internal sealed record PerfBoard(string Name, bool Real, SimSide Ours, SimSide Theirs, bool? OursFirst, SimOptions Options);

/// <summary>
/// The combat simulator's speed (docs/bg-combat-sim.md, "Скорость"): a fixed corpus of boards - every captured fight
/// of the kept logs and capture files, replayed through <see cref="BattlegroundsTracker"/> as <c>simcover</c> does,
/// plus synthetic worst cases (long deathrattle and summon chains, full boards, golden, Reborn, Avenge, cleave,
/// Windfury). Reports fights per second, time and bytes per fight on one thread, then the app's two passes (the
/// quick pass on the log thread and the background pass) as the app runs them: time per pass (p50 / p95 / max), runs
/// done inside the budget and the 95 % half-width of win / tie / loss at that run count.
/// </summary>
internal static class SimPerfBench
{
    /// <summary>Fights per board for the throughput figures (no time limit).</summary>
    private const int ThroughputRuns = 2000;

    public static int Run(Options options)
    {
        using var http = Http();
        var cards = CardDb.LoadAsync("enUS", http).GetAwaiter().GetResult();
        var real = RealBoards(options, cards);
        var synthetic = Synthetic();
        var boards = real.Concat(synthetic).ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[perf] corpus: {real.Count} real boards, {synthetic.Count} synthetic; {Environment.ProcessorCount} logical processors, {RuntimeInformationText()}"));

        // Warm-up: every board's code path is jitted (and tiered up) before anything is timed, as in a running app.
        foreach (var board in boards)
        {
            CombatSimulator.Run(board.Ours, board.Theirs, board.OursFirst, board.Options with { Runs = 300, MinRuns = 300, TimeBudget = null });
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("HP_BENCH_PROFILE"), CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            Profile(boards, seconds);
            return 0;
        }

        for (var round = 1; round <= Math.Max(1, options.Rounds); round++)
        {
            Console.WriteLine($"[perf] round {round}");
            // HP_BENCH_ONLY=passes: the app's two passes and nothing else, for A/B runs taken in turn on a busy PC.
            if (Environment.GetEnvironmentVariable("HP_BENCH_ONLY") == "passes")
            {
                foreach (var budget in new[] { SimBudget.Quick, SimBudget.App })
                {
                    Passes(budget, "app", "real", real, o => o);
                    Passes(budget, "app", "synthetic", synthetic, o => o);
                }

                continue;
            }

            Throughput("real", real, 1);
            Throughput("synthetic", synthetic, 1);
            foreach (var board in synthetic)
            {
                Throughput("  " + board.Name, [board], 1);
            }

            // HP_BENCH_ONLY=throughput: the engine's own cost and nothing else, for A/B runs of a change.
            if (Environment.GetEnvironmentVariable("HP_BENCH_ONLY") == "throughput")
            {
                continue;
            }

            foreach (var threads in new[] { 2, 4, 8, 16 }.Where(t => t <= Environment.ProcessorCount))
            {
                Throughput(string.Create(CultureInfo.InvariantCulture, $"real x{threads}"), real, threads);
                Throughput(string.Create(CultureInfo.InvariantCulture, $"synthetic x{threads}"), synthetic, threads);
            }

            // The passes as the app makes them (BattlegroundsTracker.QuickPass and DefaultSimOptions).
            foreach (var budget in new[] { SimBudget.Quick, SimBudget.App })
            {
                Passes(budget, "app", "real", real, o => o);
                Passes(budget, "app", "synthetic", synthetic, o => o);
            }

            // The same passes on other thread counts: what the choice of Parallelism is made on.
            foreach (var threads in new[] { 1, 2 })
            {
                Passes(SimBudget.Quick, "x" + threads, "real", real, o => o with { Parallelism = threads });
                Passes(SimBudget.Quick, "x" + threads, "synthetic", synthetic, o => o with { Parallelism = threads });
            }

            foreach (var threads in new[] { 1, 2, 4, 8 })
            {
                Passes(SimBudget.App, "x" + threads, "real", real, o => o with { Parallelism = threads });
                Passes(SimBudget.App, "x" + threads, "synthetic", synthetic, o => o with { Parallelism = threads });
            }

            // Without a run cap or a precision stop the time budget alone ends the pass: how many runs fit in it.
            foreach (var budget in new[] { SimBudget.Quick, SimBudget.App })
            {
                Passes(budget, "cap", "real", real, o => o with { Runs = 1_000_000, MinRuns = 0, TargetHalfWidth = 0 });
                Passes(budget, "cap", "synthetic", synthetic, o => o with { Runs = 1_000_000, MinRuns = 0, TargetHalfWidth = 0 });
            }
        }

        return 0;
    }

    /// <summary>
    /// HP_BENCH_PROFILE=n: fights of every board on one thread for n seconds, for a CPU trace to attach to
    /// (<c>dotnet-trace collect -p &lt;pid&gt;</c>), with the runtime's allocation samples (one per ~100 KB) by type.
    /// </summary>
    private static void Profile(IReadOnlyList<PerfBoard> boards, int seconds)
    {
        Console.WriteLine($"[perf] profiling: pid {Environment.ProcessId}, {seconds} s of single-thread fights");
        using var allocations = new AllocationSamples();
        var clock = Stopwatch.StartNew();
        long fights = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            foreach (var board in boards)
            {
                fights += CombatSimulator.Run(board.Ours, board.Theirs, board.OursFirst,
                    board.Options with { Runs = 500, MinRuns = 500, TimeBudget = null, TargetHalfWidth = 0, Parallelism = 1 }).Runs;
            }
        }

        var total = allocations.Bytes.Values.Sum();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[perf] {fights} fights; sampled allocations by type ({total / (double)fights:N0} B/fight sampled):"));
        foreach (var (type, bytes) in allocations.Bytes.OrderByDescending(p => p.Value).Take(20))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[perf]   {bytes * 100.0 / total,5:N1} %  {bytes / (double)fights,7:N0} B/fight  {type}"));
        }
    }

    /// <summary>The runtime's GCAllocationTick samples, summed per allocated type.</summary>
    private sealed class AllocationSamples : System.Diagnostics.Tracing.EventListener
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, long> Bytes { get; } = new(StringComparer.Ordinal);

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                // Keyword GC (0x1) at Verbose carries GCAllocationTick.
                EnableEvents(eventSource, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
            }
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs eventData)
        {
            if (eventData.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || eventData.Payload is null || eventData.PayloadNames is null)
            {
                return;
            }

            var type = eventData.PayloadNames.IndexOf("TypeName") is >= 0 and var t ? eventData.Payload[t] as string ?? "?" : "?";
            var amount = eventData.PayloadNames.IndexOf("AllocationAmount64") is >= 0 and var a ? Convert.ToInt64(eventData.Payload[a], CultureInfo.InvariantCulture) : 0;
            Bytes.AddOrUpdate(type, static (_, add) => add, static (_, sum, add) => sum + add, amount);
        }
    }

    private static string RuntimeInformationText() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, GC {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}";

    /// <summary>
    /// Fights per second, nanoseconds and bytes per fight, every board <see cref="ThroughputRuns"/> times on
    /// <paramref name="threads"/> threads (1: the calling thread, the engine's own cost).
    /// </summary>
    private static void Throughput(string label, IReadOnlyList<PerfBoard> boards, int threads)
    {
        if (boards.Count == 0)
        {
            return;
        }

        GC.Collect();
        var bytes = GC.GetTotalAllocatedBytes(precise: true);
        var gen0 = GC.CollectionCount(0);
        var clock = Stopwatch.StartNew();
        long fights = 0;
        foreach (var board in boards)
        {
            fights += CombatSimulator.Run(board.Ours, board.Theirs, board.OursFirst,
                board.Options with { Runs = ThroughputRuns, MinRuns = ThroughputRuns, TimeBudget = null, TargetHalfWidth = 0, Parallelism = threads }).Runs;
        }

        clock.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - bytes;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[perf]   throughput {label,-22} {fights,7} fights in {clock.Elapsed.TotalMilliseconds,8:N0} ms: {fights / clock.Elapsed.TotalSeconds,9:N0} fights/s, {clock.Elapsed.TotalNanoseconds / fights,7:N0} ns/fight, {allocated / (double)fights,7:N0} B/fight, gen0 {GC.CollectionCount(0) - gen0}"));
    }

    /// <summary>
    /// The app's pass under <paramref name="budget"/> on every board, its options changed by <paramref name="vary"/>:
    /// wall time, process CPU time, runs and sampling error.
    /// </summary>
    private static void Passes(SimBudget budget, string variant, string label, IReadOnlyList<PerfBoard> boards, Func<SimOptions, SimOptions> vary)
    {
        if (boards.Count == 0)
        {
            return;
        }

        var times = new List<double>();
        var runs = new List<int>();
        var widths = new List<double>();
        var clock = new Stopwatch();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        foreach (var board in boards)
        {
            var options = vary(SimSample.Budgeted(board.Options, budget));
            clock.Restart();
            var odds = CombatSimulator.Run(board.Ours, board.Theirs, board.OursFirst, options);
            clock.Stop();
            times.Add(clock.Elapsed.TotalMilliseconds);
            runs.Add(odds.Runs);
            widths.Add(HalfWidth(odds));
        }

        process.Refresh();
        var cpuPerPass = (process.TotalProcessorTime - cpu).TotalMilliseconds / boards.Count;
        var sortedRuns = runs.Order().ToList();
        var sortedWidths = widths.Order().ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[perf]   {budget,-5} {variant,-4} {label,-10} time {Spread(times)}; cpu {cpuPerPass,6:N1} ms/pass; runs avg {runs.Average(),6:N0}, min {sortedRuns[0],5}, p50 {sortedRuns[sortedRuns.Count / 2],5}, max {sortedRuns[^1],6}; half-width max(W,T,L) avg {sortedWidths.Average() * 100,4:N2} pp, p95 {Percentile(sortedWidths, .95) * 100,4:N2} pp, max {sortedWidths[^1] * 100,4:N2} pp"));
    }

    /// <summary>The widest 95 % half-width (Wilson) of win, tie and loss at the odds' run count; 0 without runs.</summary>
    internal static double HalfWidth(CombatOdds odds)
    {
        if (odds.Runs <= 0)
        {
            return 0;
        }

        double Of(double share)
        {
            var (low, high) = SimStats.Wilson((int)Math.Round(share * odds.Runs), odds.Runs);
            return (high - low) / 2;
        }

        return Math.Max(Of(odds.Win), Math.Max(Of(odds.Tie), Of(odds.Loss)));
    }

    /// <summary>Every captured fight of the kept logs (and capture files) with both boards non-empty.</summary>
    private static List<PerfBoard> RealBoards(Options options, CardDb cards)
    {
        var games = new List<SimGame>();
        if (Directory.Exists(options.Logs))
        {
            foreach (var log in Directory.EnumerateFiles(options.Logs, "Power*.log", SearchOption.AllDirectories)
                         .Where(f => File.ReadLines(f).Any(l => l.Contains("GameType=GT_BATTLEGROUNDS", StringComparison.Ordinal)))
                         .Order(StringComparer.Ordinal))
            {
                var source = $"{Path.GetFileName(Path.GetDirectoryName(log))}/{Path.GetFileName(log)}";
                games.AddRange(SimSample.Read(File.ReadLines(log), source, DateOnly.FromDateTime(File.GetLastWriteTime(log)), cards));
            }
        }

        if (options.Captures.Length > 0 && Directory.Exists(options.Captures))
        {
            var store = new CombatCaptureStore(options.Captures);
            foreach (var name in store.Names())
            {
                if (store.Load(name) is { } saved)
                {
                    var game = SimSample.From(saved, $"captures/{name}", cards);
                    // A game whose log is still kept is the same fights twice: it counts once, from the log.
                    if (!games.Any(g => Math.Abs((g.StartedAt - game.StartedAt).TotalSeconds) < 2 && g.Fights.Count == game.Fights.Count))
                    {
                        games.Add(game);
                    }
                }
            }
        }

        return games.SelectMany(g => g.Fights)
            .Where(f => f.Capture is { } c && f.Options is not null && c.Ours.Minions.Count > 0 && c.Theirs.Minions.Count > 0)
            .Select(f => new PerfBoard(f.Key, true, f.Capture!.Ours, f.Capture.Theirs, f.Capture.OursFirst, f.Options!))
            .ToList();
    }

    // ------------------------------------------------------------------------------------------- synthetic worst cases

    private const string Titus = "BG25_354";
    private const string Ghastcoiler = "BGS_008";
    private const string HarmlessBonehead = "BG28_300";
    private const string CordPuller = "BG29_611";
    private const string ScarletSkull = "BG25_022";
    private const string BuzzingVermin = "BG31_803";
    private const string TurquoiseSkitterer = "BG31_809";
    private const string DeathlyStriker = "BG31_835";
    private const string EternalTycoon = "BG34_403";
    private const string RelentlessDeflector = "BG34_405";
    private const string BladeCollector = "BG26_817";
    private const string ObsidianRavager = "BG27_017";
    private const string WildfireElemental = "BGS_126";

    // Package E2 (main 44e706f4): battlecries triggered in combat, Blood Gem bonuses, stat-gain hooks.
    private const string Greenskeeper = "BG34_Giant_041";
    private const string Brann = "BG_LOE_077";
    private const string Kalecgos = "BGS_041";
    private const string SanguineChampion = "BG23_017";
    private const string SanguineRefiner = "BG33_885";
    private const string RotHideGnoll = "BG25_013";
    private const string TideOracleMorgl = "BG27_513";
    private const string Fishbait = "BG36_205";
    private const string Mishmash = "TB_BaconShop_HERO_33_Buddy";
    private const string Joyous = "BG36_110";

    /// <summary>
    /// Boards built to be slow: many deaths, summons and triggers per fight. Card ids are only those with a registered
    /// effect; a missing one is reported, so a renamed effect does not silently make a board cheaper.
    /// </summary>
    private static List<PerfBoard> Synthetic()
    {
        static SimMinion M(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false, int tier = 4,
                           CardMechanic mechanics = CardMechanic.None) =>
            SimMinion.Create(id, attack, health, Tribe.None, keywords, golden, mechanics) with { TechLevel = tier };

        static SimMinion Dr(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false) =>
            M(id, attack, health, keywords, golden, mechanics: CardMechanic.Deathrattle);

        static SimMinion Bc(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false, Tribe tribe = Tribe.None) =>
            M(id, attack, health, keywords, golden, mechanics: CardMechanic.Battlecry | CardMechanic.Deathrattle) with { Tribes = tribe };

        static SimSide S(params SimMinion[] minions) => new(minions) { TavernTier = 6, Health = 30 };

        var options = BattlegroundsTracker.DefaultSimOptions;
        var boards = new List<PerfBoard>
        {
            new("deathrattle chains", false,
                S(M(Titus, 4, 10, golden: true), Dr(HarmlessBonehead, 2, 2, golden: true), Dr(CordPuller, 3, 3, Keyword.DivineShield),
                  Dr(Ghastcoiler, 7, 7), Dr(Ghastcoiler, 7, 7, golden: true), Dr(ScarletSkull, 3, 3, Keyword.Reborn), Dr(BuzzingVermin, 2, 6, Keyword.Taunt)),
                S(M(BladeCollector, 6, 9, Keyword.Windfury), Dr(TurquoiseSkitterer, 4, 4), M(string.Empty, 5, 5, Keyword.Venomous),
                  Dr(HarmlessBonehead, 2, 2), M(ObsidianRavager, 7, 7), Dr(CordPuller, 3, 3), M(string.Empty, 8, 8, Keyword.Taunt | Keyword.DivineShield)),
                null, options),
            new("full keyword boards", false,
                S(M(string.Empty, 12, 20, Keyword.Taunt | Keyword.DivineShield), M(string.Empty, 9, 9, Keyword.Windfury | Keyword.Reborn),
                  M(string.Empty, 3, 30, Keyword.Taunt), M(string.Empty, 1, 1, Keyword.Venomous | Keyword.DivineShield | Keyword.Reborn),
                  M(string.Empty, 15, 15, Keyword.MegaWindfury), M(string.Empty, 6, 18, Keyword.DivineShield), M(string.Empty, 10, 10, Keyword.Reborn)),
                S(M(string.Empty, 8, 25, Keyword.Taunt | Keyword.Reborn), M(string.Empty, 2, 2, Keyword.Poisonous | Keyword.DivineShield),
                  M(string.Empty, 11, 11, Keyword.Windfury), M(string.Empty, 5, 30, Keyword.Taunt | Keyword.DivineShield),
                  M(string.Empty, 14, 7, Keyword.Reborn), M(string.Empty, 4, 20, Keyword.DivineShield), M(string.Empty, 20, 20)),
                true, options),
            new("golden reborn", false,
                S(Dr(HarmlessBonehead, 4, 4, Keyword.Reborn, golden: true), Dr(ScarletSkull, 6, 6, Keyword.Reborn, golden: true),
                  Dr(CordPuller, 6, 6, Keyword.Reborn | Keyword.DivineShield, golden: true), M(Titus, 8, 20, Keyword.Reborn, golden: true),
                  Dr(Ghastcoiler, 14, 14, Keyword.Reborn, golden: true), M(string.Empty, 10, 10, Keyword.Reborn | Keyword.Windfury, golden: true),
                  Dr(BuzzingVermin, 4, 12, Keyword.Reborn | Keyword.Taunt, golden: true)),
                S(Dr(HarmlessBonehead, 4, 4, Keyword.Reborn, golden: true), Dr(TurquoiseSkitterer, 8, 8, Keyword.Reborn, golden: true),
                  M(string.Empty, 18, 18, Keyword.Reborn | Keyword.DivineShield, golden: true), Dr(CordPuller, 6, 6, Keyword.Reborn, golden: true),
                  M(string.Empty, 2, 30, Keyword.Taunt | Keyword.Reborn, golden: true), Dr(Ghastcoiler, 14, 14, Keyword.Reborn, golden: true),
                  M(string.Empty, 12, 12, Keyword.Reborn | Keyword.Venomous, golden: true)),
                null, options),
            new("avenge swarm", false,
                S(M(DeathlyStriker, 5, 8), M(EternalTycoon, 4, 10, golden: true), M(RelentlessDeflector, 3, 12, Keyword.Taunt),
                  Dr(HarmlessBonehead, 1, 1, golden: true), Dr(CordPuller, 2, 2), Dr(HarmlessBonehead, 1, 1), Dr(TurquoiseSkitterer, 2, 2)),
                S(M(DeathlyStriker, 5, 8, golden: true), M(EternalTycoon, 4, 10), M(RelentlessDeflector, 3, 12, golden: true),
                  Dr(HarmlessBonehead, 1, 1), Dr(CordPuller, 2, 2, golden: true), Dr(BuzzingVermin, 1, 4), Dr(Ghastcoiler, 4, 4)),
                true, options),
            new("cleave and windfury", false,
                S(M(BladeCollector, 10, 14, Keyword.Windfury), M(ObsidianRavager, 9, 12, Keyword.Windfury, golden: true),
                  M(WildfireElemental, 12, 10, Keyword.Windfury), M(string.Empty, 4, 4, Keyword.DivineShield),
                  M(string.Empty, 4, 4, Keyword.DivineShield), M(string.Empty, 6, 20, Keyword.Taunt), M(string.Empty, 3, 3, Keyword.Venomous)),
                S(M(string.Empty, 3, 8, Keyword.DivineShield | Keyword.Taunt), M(string.Empty, 3, 8, Keyword.DivineShield),
                  M(string.Empty, 3, 8, Keyword.DivineShield | Keyword.Reborn), Dr(HarmlessBonehead, 2, 2), Dr(CordPuller, 3, 3, Keyword.DivineShield),
                  M(string.Empty, 3, 8, Keyword.DivineShield), Dr(BuzzingVermin, 2, 6, Keyword.Taunt)),
                false, options),
            new("battlecries in combat", false,
                S(M(Greenskeeper, 6, 9, Keyword.Taunt, golden: true, mechanics: CardMechanic.Rally), M(Brann, 4, 8, golden: true),
                  M(Kalecgos, 6, 12, tier: 6) with { Tribes = Tribe.Dragon }, Bc(SanguineChampion, 5, 5, tribe: Tribe.Dragon),
                  M(SanguineRefiner, 4, 6, Keyword.DivineShield, mechanics: CardMechanic.Rally), M(RotHideGnoll, 2, 10, golden: true),
                  Bc(Joyous, 4, 4, Keyword.Reborn, tribe: Tribe.Dragon)),
                S(M(TideOracleMorgl, 5, 7, Keyword.Poisonous, golden: true), Dr(Fishbait, 0, 2, golden: true), M(Mishmash, 8, 8, golden: true),
                  M(Greenskeeper, 6, 9, mechanics: CardMechanic.Rally), Bc(SanguineChampion, 5, 5), Dr(Fishbait, 0, 1),
                  M(RotHideGnoll, 1, 4, Keyword.Taunt)),
                null, options),
            new("long stall", false,
                S(Enumerable.Range(0, 7).Select(i => M(string.Empty, 1 + (i % 2), 40, i % 3 == 0 ? Keyword.DivineShield : Keyword.None)).ToArray()),
                S(Enumerable.Range(0, 7).Select(i => M(string.Empty, 1 + ((i + 1) % 2), 40, i % 3 == 1 ? Keyword.DivineShield : Keyword.None)).ToArray()),
                null, options),
        };

        var missing = boards.SelectMany(b => b.Ours.Minions.Concat(b.Theirs.Minions))
            .Where(m => m.CardId.Length > 0 && EffectRegistry.Default.Get(m.CardId, m.Golden) is null)
            .Select(m => m.CardId).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"[perf] synthetic boards: no effect registered for {string.Join(' ', missing)} (the board runs without it)");
        }

        return boards;
    }
}

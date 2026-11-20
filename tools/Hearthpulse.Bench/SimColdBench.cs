using System.Diagnostics;
using System.Globalization;
using System.IO;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>
/// The combat simulator in a cold process (docs/perf-architecture-audit.md, stage 14): the fights of the capture
/// files in game order, each through the app's quick pass and then its background pass, as the first fights of an
/// app session meet them - nothing jitted yet. Then the same fights again in the same process, warm, for comparison.
/// Run it as a fresh process per measurement: the boards are read from capture files only, which does not simulate
/// (reading logs through the tracker would warm the simulator before the first timed fight).
/// HP_BENCH_WARMUP=1 runs <see cref="CombatWarmup"/> first and waits for it, as the first fight of a game finds it.
/// </summary>
internal static class SimColdBench
{
    public static int Run(Options options)
    {
        var start = Stopwatch.StartNew();
        using var http = Http();
        var cards = CardDb.LoadAsync("enUS", http).GetAwaiter().GetResult();
        var fights = new List<(string Name, BattlegroundsTracker.CombatCapture Capture, SimOptions Options)>();
        if (options.Captures.Length > 0 && Directory.Exists(options.Captures))
        {
            var store = new CombatCaptureStore(options.Captures);
            foreach (var name in store.Names().Order(StringComparer.Ordinal))
            {
                if (store.Load(name) is not { } saved)
                {
                    continue;
                }

                for (var i = 0; i < saved.Fights.Count; i++)
                {
                    var (capture, captured) = saved.Fights[i].Today(cards, Hearthpulse.Core.Battlegrounds.Effects.EffectRegistry.Default);
                    if (capture.Ours.Minions.Count > 0 && capture.Theirs.Minions.Count > 0)
                    {
                        // The app simulates during the game: what the player could see, not the values revealed after it.
                        fights.Add((string.Create(CultureInfo.InvariantCulture, $"{name}/{i + 1}"), capture, captured with { PostGame = false }));
                    }
                }
            }
        }

        // HP_BENCH_FROM=k: the cold process meets fight k first (the app started in the middle of a game).
        if (int.TryParse(Environment.GetEnvironmentVariable("HP_BENCH_FROM"), CultureInfo.InvariantCulture, out var from) && from > 1)
        {
            fights = fights.Skip(from - 1).Concat(fights.Take(from - 1)).ToList();
        }

        if (fights.Count == 0)
        {
            Console.Error.WriteLine("simcold needs --captures <dir> with capture files (.hpc)");
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[cold] {fights.Count} fights; process up {start.Elapsed.TotalMilliseconds:N0} ms; {RuntimeText()}"));

        if (Environment.GetEnvironmentVariable("HP_BENCH_WARMUP") == "1")
        {
            var clock = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            // HP_WARMUP=rounds,slice ms,pause ms: the warm-up's shape, for choosing it.
            if (Environment.GetEnvironmentVariable("HP_WARMUP")?.Split(',') is [var r, var sl, var p])
            {
                CombatWarmup.Rounds = int.Parse(r, CultureInfo.InvariantCulture);
                CombatWarmup.Slice = TimeSpan.FromMilliseconds(int.Parse(sl, CultureInfo.InvariantCulture));
                CombatWarmup.Pause = TimeSpan.FromMilliseconds(int.Parse(p, CultureInfo.InvariantCulture));
            }

            CombatWarmup.Start().GetAwaiter().GetResult();
            process.Refresh();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[cold] warm-up {clock.Elapsed.TotalMilliseconds,6:N0} ms wall, {(process.TotalProcessorTime - cpu).TotalMilliseconds,6:N0} ms cpu"));
        }

        // HP_BENCH_IDLE_MS=n: a pause before the first fight, as the game gives one between its start and the first
        // fight (hero choice, the first shop turn): the runtime's tier-up of what the warm-up ran finishes in it.
        if (int.TryParse(Environment.GetEnvironmentVariable("HP_BENCH_IDLE_MS"), CultureInfo.InvariantCulture, out var idle) && idle > 0)
        {
            Thread.Sleep(idle);
        }

        // HP_BENCH_GAP_MS=n (default 1000): the pause after each fight, standing in for the shop turn between fights
        // (30 s and more in a game); the runtime compiles optimised code in such pauses.
        var gap = int.TryParse(Environment.GetEnvironmentVariable("HP_BENCH_GAP_MS"), CultureInfo.InvariantCulture, out var g) ? g : 1000;
        var cold = Sweep(fights, gap);
        // The reference: the same fights once the runtime has nothing left to optimise (three more games).
        var warm = cold;
        for (var round = 0; round < 3; round++)
        {
            warm = Sweep(fights, gap);
        }

        for (var i = 0; i < fights.Count; i++)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[cold] fight {fights[i].Name[(fights[i].Name.LastIndexOf('/') + 1)..],2} {fights[i].Capture.Ours.Minions.Count}v{fights[i].Capture.Theirs.Minions.Count}  quick {cold[i].QuickMs,6:N1} ms {cold[i].QuickRuns,5} runs | steady {warm[i].QuickMs,6:N1} ms {warm[i].QuickRuns,5} runs  ||  background {cold[i].AppMs,6:N1} ms {cold[i].AppRuns,6} runs | steady {warm[i].AppMs,6:N1} ms {warm[i].AppRuns,6} runs"));
        }

        return 0;
    }

    private static List<(double QuickMs, int QuickRuns, double AppMs, int AppRuns)> Sweep(
        List<(string Name, BattlegroundsTracker.CombatCapture Capture, SimOptions Options)> fights, int gap)
    {
        var result = new List<(double, int, double, int)>();
        var clock = new Stopwatch();
        // HP_BENCH_CAP=1: the quick pass without its run cap, so its 40 ms budget alone ends it: how many runs fit, as
        // on a PC slow enough for the budget to end the capped pass.
        var uncapped = Environment.GetEnvironmentVariable("HP_BENCH_CAP") == "1";
        foreach (var (_, capture, options) in fights)
        {
            clock.Restart();
            var quick = CombatSimulator.Run(capture.Ours, capture.Theirs, capture.OursFirst, uncapped
                ? BattlegroundsTracker.QuickPass(options) with { Runs = 1_000_000, MinRuns = 0, TargetHalfWidth = 0 }
                : BattlegroundsTracker.QuickPass(options));
            var quickMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var app = CombatSimulator.Run(capture.Ours, capture.Theirs, capture.OursFirst, options);
            result.Add((quickMs, quick.Runs, clock.Elapsed.TotalMilliseconds, app.Runs));
            if (gap > 0)
            {
                Thread.Sleep(gap);
            }
        }

        return result;
    }

    private static string RuntimeText() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, tiered env {Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "-"}, PGO env {Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "-"}, R2R env {Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "-"}";
}

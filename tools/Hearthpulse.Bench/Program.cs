using System.Globalization;
using System.IO;

namespace Hearthpulse.Bench;

/// <summary>
/// Measures what docs/perf-architecture-audit.md reports: card loading, the Power.log pipeline, overlay updates,
/// history queries, start-up and memory of the WPF app. Commands print plain text so before/after runs can be diffed.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Hearthpulse.Bench <command> [--logs <dir>] [--rounds <n>] [--games <n>]
                                   [--captures <dir>] [--pool <file>] [--replays <dir>] [--out <file>] [--before <file> --after <file>]
                                   [--cost <n>]

          cards     load the card database: time, allocations, live heap, LOH
          replay    replay kept Power logs through GameSession: parse and publish timings, allocations
          parser    the Power.log parser alone on the kept logs (no network): lines/s, and what the spectator marker check
                    costs per line and the look-behind for a marker when the app joins a log at its last game
          overlay   replay snapshots into the WPF and Avalonia OverlayViewModel: time and bytes per Update, PropertyChanged
          tick      the WPF overlay tick's click-through hit test and hover along cursor paths, game window lookup
          frames    the shown WPF overlay at 1080p and 4K: process and UI-thread CPU idle, at 10 snapshots/s, a small change
                    30/s and when shown again (HP_BENCH_SIZES=WxH,... other sizes)
          hover     replay: game time during which the card under the mouse is read from the game, before and now
          cleanup   run the start-up cache clean-up on HEARTHPULSE_DATA: size of cache/ before and after (deletes files)
          history   history store and My Stats queries on 1 000 / 5 000 (/ --games) synthetic games
          startup   WPF start-up steps up to the first rendered main window and until the log watcher can start
                    (HP_BENCH_SERIAL=1: game and cards looked up after the first frame, the order before stage 6)
          ram       working set step by step (cards, meta, window, every page and tab), image cache bytes, page times
          games     memory after every game of the kept logs replayed into the main window and the shown 4K overlay
                    (--rounds n: the logs n times over; HP_BENCH_ONSCREEN=1: the overlay on the primary screen at 2 %;
                    HP_BENCH_PACE=n: each game lasts its log time / n; HP_BENCH_MAIN=0: no main window;
                    HP_BENCH_DROPART=1: image caches emptied before each count; HP_BENCH_SOFTWARE=1: overlay SoftwareOnly)
          simcover  combat simulator coverage: Battlegrounds fights of the logs (Power*.log at any depth) without "not
                    simulated", result accuracy, damage and Brier under three budgets (first pass, the app's background
                    pass, full), what was not simulated; popular minions without an effect; every fight the app pass
                    missed ([miss]: result or damage, odds again on the exact post-game inputs, real damage vs range)
                    (--out <file>: per-fight scores; --before <file> --after <file>: two such files of the same fights
                    compared pair by pair; --captures <dir>: the fights of the app's capture files (<data>/combat-captures)
                    join those of the logs; --pool <file>: players' fights from the stats pool's owner export, JSONL of
                    server/pool/node/export-combats.mjs, scored the same way (no hand in them); --replays <dir>: fights
                    the .hpr replays hold, Duos fights with both tables)
          simreplay real fights replayed step by step through the combat engine, every decision answered from the fight's
                    transcript: fights replayed to the end without divergence, first divergences by engine rule, per fight
                    (--captures <dir>: the app's capture files join the logs; --pool <file>: players' fights of the stats
                    pool's owner export with their transcripts (no hand in them); --out <file>: the report; --cost <n>:
                    what recording transcripts costs the log thread, n rounds)
          simperf   combat simulator speed: a fixed corpus (the logs' and --captures' fights plus synthetic worst cases):
                    fights/s, ns and bytes per fight, the app's quick and background passes (time p50/p95, runs inside the
                    budget, 95 % half-width of win/tie/loss); --rounds n repeats the timed part
          simcold   combat simulator in a cold process: the --captures fights in game order, quick and background pass,
                    then the same fights warm (one process per measurement; HP_BENCH_WARMUP=1: the warm-up first)
          simlive   the app's live odds path: the Battlegrounds logs (Power*.log at any depth) replayed through GameSession,
                    waiting for each fight's background pass: per fight odds on screen, in the combat list and in history;
                    unreadable lines, Diag errors, log-thread batch times, the Battlegrounds page's accuracy line
                    (HP_BENCH_LOCALE: card language, default the app's)
          simmatrix combat simulator coverage matrix of the current pool (six statuses) -> docs/bg-sim-matrix.md
          pages     first and second opening of every page, Settings tab and stats tab, a deck page and a match page
          theme     warm WPF theme switches Shadow -> Frost -> Tavern -> Shadow (--rounds n)
          (HP_BENCH_REDUCE_MOTION=1 disables WPF motion; HP_BENCH_LIBRARY_ONLY=1 limits pages to library and settings)
          art       decode the cached portraits and deck tiles at once (--games caps the count): time, allocations, GCs
          idle      the overlay window with no Hearthstone window: CPU time over 20 s (run with Hearthstone closed)
          rest      the main window at rest with the overlay and the log watcher: shown, in the tray, let go; CPU,
                    allocations, GCs over 30 s each, then memory (run with Hearthstone closed)
          match     record the kept logs' games, open the newest Battlegrounds match page, every step by hand, then play
                    the replay at x4 for 30 s: time, allocations, CPU
          (HP_BENCH_BG_PANELS=1 with overlay/tick: leaderboard badges and the lobby panel on, the leaderboard arranged)
          watch     LogWatcher + GameSession on the installed game's logs: CPU time over 20 s
          poll      one idle watcher pass and one "is Hearthstone running" (game closed): thread CPU per call
          meta      Battlegrounds meta, Constructed meta and the card pool catalog from the cache: time and allocations
          memory    records a memory fixture of the running game into the folder after it (MemoryBench, tests/Hearthpulse.Tests/Fixtures/Memory)
          all       cards, replay, overlay, history

        HEARTHPULSE_DATA must point at a copy of the data folder; the bench refuses to touch the real one.
        Logs default to D:\Hearthstone\Logs.
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (Environment.GetEnvironmentVariable("HEARTHPULSE_DATA") is not { Length: > 0 } data || !Directory.Exists(data))
        {
            Console.Error.WriteLine("Set HEARTHPULSE_DATA to a copy of the data folder first (it is read and written).");
            return 2;
        }

        // cleanup deletes files and the history store may upgrade its schema: never on the folder the app uses.
        var real = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearthpulse"));
        if (string.Equals(Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar), real, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"HEARTHPULSE_DATA is the app's own data folder ({real}); point it at a copy.");
            return 2;
        }

        var options = Options.Parse(args.Skip(1).ToArray());
        Console.WriteLine($"data={data} logs={options.Logs}");
        switch (args[0])
        {
            case "cards":
                CoreBench.Cards();
                break;
            case "replay":
                CoreBench.Replay(options);
                break;
            case "parser":
                ParserBench.Run(options);
                break;
            case "overlay":
                WpfBench.Overlay(options);
                break;
            case "tick":
                TickBench.Run(options);
                break;
            case "frames":
                FrameBench.Run(options);
                break;
            case "history":
                CoreBench.History(options);
                break;
            case "hover":
                CoreBench.Hover(options);
                break;
            case "watch":
                CoreBench.Watch();
                break;
            case "poll":
                CoreBench.Poll();
                break;
            case "meta":
                CoreBench.Meta();
                break;
            case "memory":
                return MemoryBench.Record(args.Length > 1 ? args[1] : "memory-fixture");
            case "cleanup":
                CoreBench.Cleanup();
                break;
            case "startup":
                WpfBench.Startup();
                break;
            case "ram":
                WpfBench.Ram(options);
                break;
            case "games":
                GamesBench.Run(options);
                break;
            case "simcover":
                return SimCoverBench.Run(options);
            case "simreplay":
                return SimReplayBench.Run(options);
            case "simperf":
                return SimPerfBench.Run(options);
            case "simcold":
                return SimColdBench.Run(options);
            case "simlive":
                return SimLiveBench.Run(options);
            case "simmatrix":
                SimMatrix.Run();
                break;
            case "pages":
                WpfBench.Pages(options);
                break;
            case "theme":
                WpfBench.Theme(options);
                break;
            case "art":
                WpfBench.Art(options);
                break;
            case "idle":
                WpfBench.OverlayIdle();
                break;
            case "rest":
                WindowBench.Rest(options);
                break;
            case "match":
                WindowBench.Match(options);
                break;
            case "all":
                CoreBench.Cards();
                CoreBench.Replay(options);
                WpfBench.Overlay(options);
                CoreBench.History(options);
                break;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }

        return 0;
    }
}

/// <summary>Command-line options shared by the commands.</summary>
internal sealed record Options(string Logs, int Rounds, int Games)
{
    /// <summary>simcover: a folder of .hpr replays to count.</summary>
    public string Replays { get; init; } = string.Empty;

    /// <summary>simcover: where the per-fight scores go.</summary>
    public string Out { get; init; } = string.Empty;

    /// <summary>simcover: the two per-fight files to compare.</summary>
    public string Before { get; init; } = string.Empty;

    public string After { get; init; } = string.Empty;

    /// <summary>simcover: a folder of the app's combat capture files (&lt;data&gt;/combat-captures) to add to the fights.</summary>
    public string Captures { get; init; } = string.Empty;

    /// <summary>simcover, simreplay: the stats pool's export of players' fights (JSONL, server/pool/node/export-combats.mjs).</summary>
    public string Pool { get; init; } = string.Empty;

    /// <summary>simreplay: rounds of measuring what recording transcripts costs; 0 = not measured.</summary>
    public int Cost { get; init; }

    public static Options Parse(string[] args)
    {
        var logs = @"D:\Hearthstone\Logs";
        var rounds = 1;
        var games = 0;
        string replays = string.Empty, output = string.Empty, before = string.Empty, after = string.Empty, captures = string.Empty, pool = string.Empty;
        var cost = 0;
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--logs":
                    logs = args[i + 1];
                    break;
                case "--rounds":
                    rounds = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--games":
                    games = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--replays":
                    replays = args[i + 1];
                    break;
                case "--out":
                    output = args[i + 1];
                    break;
                case "--before":
                    before = args[i + 1];
                    break;
                case "--after":
                    after = args[i + 1];
                    break;
                case "--captures":
                    captures = args[i + 1];
                    break;
                case "--pool":
                    pool = args[i + 1];
                    break;
                case "--cost":
                    cost = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
            }
        }

        return new Options(logs, rounds, games) { Replays = replays, Out = output, Before = before, After = after, Captures = captures, Pool = pool, Cost = cost };
    }
}

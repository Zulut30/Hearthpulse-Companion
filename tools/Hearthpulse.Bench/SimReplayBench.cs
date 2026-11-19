using System.Diagnostics;
using System.Globalization;
using System.IO;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>
/// <c>simreplay</c>: real Battlegrounds fights replayed step by step through the combat engine (docs/bg-combat-sim.md,
/// "Пошаговая сверка"). Every fight of the logs (Power*.log under --logs, any depth) and of the app's capture files
/// (--captures) that has a transcript is fought once with every decision answered from the log, and the boards are
/// compared with the log's before every regular attack and at the end. The report: fights replayed to the end without a
/// divergence (with the Wilson interval), first divergences by engine rule, and every fight's first divergence.
/// <c>--cost n</c> also measures what recording the transcripts costs the log thread: the logs read n times through the
/// tracker with and without it.
/// </summary>
internal static class SimReplayBench
{
    public static int Run(Options options)
    {
        using var http = Http();
        // English: readable card names in the report, and the relevance fallback reads English text (as simcover).
        var cards = CardDb.LoadAsync("enUS", http).GetAwaiter().GetResult();
        var logs = Directory.Exists(options.Logs)
            ? Directory.EnumerateFiles(options.Logs, "Power*.log", SearchOption.AllDirectories)
                .Where(f => File.ReadLines(f).Any(l => l.Contains("GameType=GT_BATTLEGROUNDS", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        var games = new List<ReplayableGame>();
        foreach (var log in logs)
        {
            var source = $"{Path.GetFileName(Path.GetDirectoryName(log))}/{Path.GetFileName(log)}";
            foreach (var game in SimReplay.Read(File.ReadLines(log), source, DateOnly.FromDateTime(File.GetLastWriteTime(log)), cards))
            {
                games.Add(game);
                Print(game);
            }
        }

        if (options.Captures.Length > 0 && Directory.Exists(options.Captures))
        {
            var store = new CombatCaptureStore(options.Captures);
            foreach (var name in store.Names())
            {
                if (store.Load(name) is not { } saved)
                {
                    Console.WriteLine($"[replay] {name}: not read (damaged or of another version)");
                    continue;
                }

                var game = SimReplay.From(saved, $"captures/{name}");
                // A game whose log is still kept is the same fights twice: it counts once, from the log.
                if (games.Any(g => Math.Abs((g.StartedAt - game.StartedAt).TotalSeconds) < 2 && g.Fights.Count == game.Fights.Count))
                {
                    Console.WriteLine($"[replay] {game.Key}: also in the logs, counted once");
                    continue;
                }

                games.Add(game);
                Print(game);
            }
        }

        var fights = games.SelectMany(g => g.Fights).ToList();
        // HP_REPLAY_DUMP=<part of a fight key>: that fight's transcript, boards and the engine's own trace.
        if (Environment.GetEnvironmentVariable("HP_REPLAY_DUMP") is { Length: > 0 } dump)
        {
            foreach (var f in fights.Where(f => f.Key.EndsWith(dump, StringComparison.Ordinal)))
            {
                foreach (var line in SimReplay.Dump(f.Fight, cards))
                {
                    Console.WriteLine($"[dump] {line}");
                }
            }
        }

        var replayed = fights.Select(f => SimReplay.Replay(f.Key, f.Fight, cards)).OfType<ReplayedFight>().ToList();
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"sample: {games.Count} games ({games.Count(g => g.Duos)} duos), {fights.Count} fights with boards, {replayed.Count} with a transcript; builds {string.Join(' ', games.Select(g => g.Build).Distinct().Order())}"),
        };
        lines.AddRange(SimReplay.Report(replayed));
        foreach (var line in lines)
        {
            Console.WriteLine($"[replay] {line}");
        }

        if (options.Out.Length > 0)
        {
            File.WriteAllLines(options.Out, lines);
            Console.WriteLine($"[replay] report -> {options.Out}");
        }

        if (options.Cost > 0)
        {
            Cost(logs, cards, options.Cost);
            FileSize(games);
        }

        return 0;
    }

    private static void Print(ReplayableGame game) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[replay] {game.Key}: {game.StartedAt:yyyy-MM-dd} build {game.Build} {(game.Duos ? "duos" : "solo")}, {game.Fights.Count} fights, {game.Fights.Count(f => f.Fight.Transcript is not null)} with a transcript"));

    /// <summary>
    /// What recording transcripts costs on the log thread: the BG logs fed through a parser and the tracker (its own
    /// simulations cut to one run, as when reading a sample) <paramref name="rounds"/> times with recording off and on,
    /// alternating; thread time and allocations of each pass, and what the transcripts hold.
    /// </summary>
    private static void Cost(IReadOnlyList<string> logs, CardDb cards, int rounds)
    {
        var texts = logs.Select(f => (Lines: File.ReadAllLines(f), Date: DateOnly.FromDateTime(File.GetLastWriteTime(f)))).ToList();
        var off = new List<double>();
        var on = new List<double>();
        long offBytes = 0, onBytes = 0;
        int fights = 0, events = 0, boards = 0;
        for (var round = 0; round < rounds + 1; round++)
        {
            foreach (var record in new[] { false, true })
            {
                GC.Collect();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                var (f, e, b) = Pass(texts, cards, record);
                clock.Stop();
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (round == 0)
                {
                    // The first round warms the code up and is not counted.
                    continue;
                }

                (record ? on : off).Add(clock.Elapsed.TotalMilliseconds);
                if (record)
                {
                    onBytes += bytes;
                    (fights, events, boards) = (f, e, b);
                }
                else
                {
                    offBytes += bytes;
                }
            }
        }

        double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[replay] cost on the log thread ({logs.Count} logs, {rounds} rounds): without transcripts {Median(off):0} ms ({Spread(off)}), {Mb(offBytes / rounds)} allocated; with {Median(on):0} ms ({Spread(on)}), {Mb(onBytes / rounds)}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[replay] transcripts: {fights} fights, {events} events, {boards} boards; per fight {(fights == 0 ? 0 : events / fights)} events, {(fights == 0 ? 0 : (onBytes - offBytes) / rounds / fights / 1024.0):0.0} KB more allocated"));
    }

    /// <summary>What the transcripts add to the local capture file: every game saved with and without them (gzip).</summary>
    private static void FileSize(IReadOnlyList<ReplayableGame> games)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hp-simreplay-size-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CombatCaptureStore(dir) { MaxFiles = int.MaxValue, MaxBytes = long.MaxValue };
            long with = 0, without = 0;
            var saved = 0;
            foreach (var game in games)
            {
                var captures = new SavedCaptures { StartedAt = game.StartedAt, Build = game.Build, Duos = game.Duos, Fights = game.Fights.Select(f => f.Fight).ToList() };
                if (store.Save(game.Key + "|with", captures) is { } a && store.Save(game.Key + "|without", captures with { Fights = captures.Fights.Select(f => f with { Transcript = null }).ToList() }) is { } b)
                {
                    with += new FileInfo(Path.Combine(dir, a)).Length;
                    without += new FileInfo(Path.Combine(dir, b)).Length;
                    saved++;
                }
            }

            if (saved > 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[replay] capture file per game: {with / saved / 1024.0:0.0} KB with transcripts, {without / saved / 1024.0:0.0} KB without ({saved} games)"));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static (int Fights, int Events, int Boards) Pass(List<(string[] Lines, DateOnly Date)> texts, CardDb cards, bool record)
    {
        int fights = 0, events = 0, boards = 0;
        foreach (var (lines, date) in texts)
        {
            var parser = new PowerParser(date);
            var tracker = new BattlegroundsTracker { Cards = cards, RecordsTranscripts = record };
            tracker.SimOptions = tracker.SimOptions with { Runs = 1, MinRuns = 0 };
            parser.AddListener(tracker);
            // Each transcript once, when it is put on its capture (the tracker forgets them when the next game starts).
            BattlegroundsTracker.CombatTranscript? last = null;
            foreach (var line in lines)
            {
                parser.Feed(line);
                if (tracker.Captures.Count > 0 && tracker.Captures[^1].Transcript is { } transcript && !ReferenceEquals(transcript, last))
                {
                    last = transcript;
                    fights++;
                    events += transcript.Events.Count;
                    boards += transcript.Boards.Count;
                }
            }

            parser.Flush();
        }

        return (fights, events, boards);
    }
}

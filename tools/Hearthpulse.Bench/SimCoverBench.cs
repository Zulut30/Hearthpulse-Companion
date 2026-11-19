using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>
/// How much of a real Battlegrounds fight the combat simulator models (plan 1.5). Two reports:
/// <list type="bullet">
/// <item>Real fights: every Battlegrounds game in the logs (Power*.log under --logs, any depth) is replayed through
/// <see cref="BattlegroundsTracker"/> with the real card database, as the app does, and every captured fight is then
/// simulated under each budget (<see cref="SimBudget"/>): the share of fights without "not simulated", the result
/// accuracy and damage hit the Battlegrounds page shows, and the Brier score of the win/tie/loss odds, each with its
/// interval; then what was not simulated (card, side, golden, granted by which enchantment). <c>--out</c> writes
/// the per-fight scores; <c>--before</c> and <c>--after</c> compare two such files of the same fights pair by pair.</item>
/// <item>Replays (<c>--replays</c>): the Battlegrounds fights local .hpr files hold and, for Duos, how many show both
/// tables of the team (<see cref="ReplayFights"/>).</item>
/// <item>Popularity: the community minion list the app caches (cache/api/bg-minions-all.json, share of boards with the
/// minion) against the effect registry: which combat-relevant minions have no effect, by popularity, and the chance
/// that a board (and a fight, two boards) holds none of them.</item>
/// </list>
/// </summary>
internal static class SimCoverBench
{
    public static int Run(Options options)
    {
        if (options.Before.Length > 0 || options.After.Length > 0)
        {
            return Compare(options);
        }

        using var http = Http();
        // English: the relevance fallback reads English card text, and the report names stay readable in any console.
        var cards = CardDb.LoadAsync("enUS", http).GetAwaiter().GetResult();
        RealFights(options, cards);
        if (options.Replays.Length > 0)
        {
            Replays(options.Replays);
        }

        Popularity(cards);
        return 0;
    }

    private static void RealFights(Options options, CardDb cards)
    {
        var logs = Directory.Exists(options.Logs)
            ? Directory.EnumerateFiles(options.Logs, "Power*.log", SearchOption.AllDirectories)
                .Where(f => File.ReadLines(f).Any(l => l.Contains("GameType=GT_BATTLEGROUNDS", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        var games = new List<SimGame>();
        foreach (var log in logs)
        {
            var source = $"{Path.GetFileName(Path.GetDirectoryName(log))}/{Path.GetFileName(log)}";
            foreach (var game in SimSample.Read(File.ReadLines(log), source, DateOnly.FromDateTime(File.GetLastWriteTime(log)), cards))
            {
                games.Add(game);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[sim] {game.Key}: {game.StartedAt:yyyy-MM-dd} build {game.Build} {(game.Duos ? "duos" : "solo")}, {game.Fights.Count} fights, {game.Fights.Count(f => f.Capture is not null)} with boards"));
            }
        }

        // The app's own capture files: the same fights as a log gives, kept after the game deleted the log.
        if (options.Captures.Length > 0 && Directory.Exists(options.Captures))
        {
            var store = new CombatCaptureStore(options.Captures);
            foreach (var name in store.Names())
            {
                if (store.Load(name) is not { } saved)
                {
                    Console.WriteLine($"[sim] {name}: not read (damaged or of another version)");
                    continue;
                }

                var game = SimSample.From(saved, $"captures/{name}", cards);
                // A game whose log is still kept is the same fights twice: it counts once, from the log.
                if (games.Any(g => Math.Abs((g.StartedAt - game.StartedAt).TotalSeconds) < 2 && g.Fights.Count == game.Fights.Count))
                {
                    Console.WriteLine($"[sim] {game.Key}: also in the logs, counted once");
                    continue;
                }

                games.Add(game);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[sim] {game.Key}: {game.StartedAt:yyyy-MM-dd} build {game.Build} {(game.Duos ? "duos" : "solo")}, {game.Fights.Count} fights, {game.Fights.Count(f => f.Capture is not null)} with boards"));
            }
        }

        // Players' fights from the stats pool's owner export. The owner's own games can be in it too: for a held-out
        // measure, run it without --captures and with --logs pointing at an empty folder.
        if (options.Pool.Length > 0 && File.Exists(options.Pool))
        {
            games.AddRange(PoolFights.Read(options.Pool, cards));
        }

        var all = games.SelectMany(g => g.Fights).ToList();
        var fights = all.Where(f => f.Capture is not null).ToList();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[sim] sample: {games.Count} games ({games.Count(g => g.Duos)} duos), {all.Count} fights, {all.Count - fights.Count} without boards (not counted below); builds {string.Join(' ', games.Select(g => g.Build).Distinct().Order())}"));

        // The full budget first: it also warms the code up, as a running app is by the time a fight starts.
        var scores = new Dictionary<SimBudget, List<SimScore>>();
        foreach (var budget in new[] { SimBudget.Full, SimBudget.App, SimBudget.Quick })
        {
            scores[budget] = fights.Select(f => SimSample.Score(f, budget)!).ToList();
        }

        foreach (var budget in Enum.GetValues<SimBudget>())
        {
            Console.WriteLine($"[sim] {budget,-5} {Describe(budget)}");
            Console.WriteLine($"[sim]   {Summary(scores[budget])}");
            Console.WriteLine($"[sim]   {Calibration(scores[budget])}");
        }

        foreach (var line in SimMisses.Lines(fights, scores[SimBudget.App]))
        {
            Console.WriteLine($"[miss] {line}");
        }

        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reason in fights.SelectMany(f => Reasons(f.Capture!, cards)))
        {
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
        }

        foreach (var (code, n) in reasons.GroupBy(r => r.Key[..r.Key.IndexOf(':', StringComparison.Ordinal)]).Select(g => (g.Key, g.Sum(r => r.Value))))
        {
            Console.WriteLine($"[sim]   reason {code}: {n}");
        }

        foreach (var (reason, n) in reasons.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"[sim]   {n,3} × {reason}");
        }

        if (options.Out.Length > 0)
        {
            using var writer = new StreamWriter(options.Out, append: false);
            SimSample.Write(writer, Enum.GetValues<SimBudget>().SelectMany(b => scores[b]));
            Console.WriteLine($"[sim] per-fight scores -> {options.Out}");
        }
    }

    private static string Describe(SimBudget budget)
    {
        var o = SimSample.Budgeted(new SimOptions(), budget);
        var time = o.TimeBudget is { } t ? string.Create(CultureInfo.InvariantCulture, $", stops after {t.TotalMilliseconds:N0} ms (at least {o.MinRuns} fights)") : ", no time limit";
        return budget switch
        {
            SimBudget.Quick => $"first pass on the log thread: up to {o.Runs} fights{time}",
            SimBudget.App => $"background pass of the app: up to {o.Runs} fights{time}",
            _ => $"full budget: {o.Runs} fights{time}",
        };
    }

    private static string Summary(List<SimScore> scores)
    {
        var n = scores.Count;
        var clean = scores.Count(s => s.Clean);
        var hit = scores.Count(s => s.Hit);
        var damage = scores.Count(s => s.DamageSeen);
        var damageHit = scores.Count(s => s.DamageHit);
        var scored = scores.Where(s => s.Brier is not null).Select(s => s.Brier!.Value).ToList();
        var brier = SimStats.Mean(scored);
        return string.Create(CultureInfo.InvariantCulture,
            $"fights {n}; without \"not simulated\" {Share(clean, n)}; result hit {Share(hit, n)}; damage in range {Share(damageHit, damage)}; Brier {brier.Mean:N3} ({brier.Low:N3}..{brier.High:N3}) over {scored.Count}; runs avg {(n > 0 ? scores.Average(s => s.Runs) : 0):N0}, min {(n > 0 ? scores.Min(s => s.Runs) : 0)}");
    }

    /// <summary>
    /// Systematic shift of the odds: per result, the fights the odds expected (the sum of its probability) against the
    /// fights that had it, with z = (seen - expected) / sqrt(sum p(1 - p)). |z| above 2 on one budget would point at a
    /// bias of the engine; the budgets score the same fights, so they are not independent evidence.
    /// </summary>
    internal static string Calibration(IReadOnlyList<SimScore> scores)
    {
        string One(string result, Func<SimScore, double> p)
        {
            var expected = scores.Sum(p);
            var variance = scores.Sum(s => p(s) * (1 - p(s)));
            var seen = scores.Count(s => s.Result == result);
            var z = variance > 0 ? (seen - expected) / Math.Sqrt(variance) : 0;
            return string.Create(CultureInfo.InvariantCulture, $"{result} expected {expected:0.0} seen {seen} (z {z:+0.00;-0.00})");
        }

        return $"calibration: {One("Win", s => s.Win)}; {One("Tie", s => s.Tie)}; {One("Loss", s => s.Loss)}";
    }

    private static string Share(int k, int n)
    {
        var (low, high) = SimStats.Wilson(k, n);
        return n == 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"{k} / {n} = {k / (double)n:P1} (Wilson 95 % {low:P1}..{high:P1})");
    }

    /// <summary>Two per-fight files of the same fights (<c>--before</c>, <c>--after</c>), compared pair by pair.</summary>
    private static int Compare(Options options)
    {
        if (!File.Exists(options.Before) || !File.Exists(options.After))
        {
            Console.Error.WriteLine("simcover --before <file> --after <file>: both per-fight files (written by --out) are needed.");
            return 2;
        }

        foreach (var c in SimSample.Compare(SimSample.Parse(File.ReadLines(options.Before)), SimSample.Parse(File.ReadLines(options.After))))
        {
            Console.WriteLine($"[cmp] {c.Budget}");
            Console.WriteLine($"[cmp]   without \"not simulated\": {Pairs(c.Clean)}");
            Console.WriteLine($"[cmp]   result hit: {Pairs(c.Hit)}");
            Console.WriteLine($"[cmp]   damage in range: {Pairs(c.Damage)}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[cmp]   Brier, after minus before: {c.Brier.Mean:N4} (95 % {c.Brier.Low:N4}..{c.Brier.High:N4}) over {c.Brier.Pairs} fights"));
        }

        return 0;
    }

    private static string Pairs(PairedShare p) => string.Create(CultureInfo.InvariantCulture,
        $"{p.Before} -> {p.After} of {p.Pairs}; gained {p.Gained}, lost {p.Lost}; exact McNemar p = {p.P:G3}");

    /// <summary>
    /// The local replays (.hpr) of a folder: how many Battlegrounds fights they hold, and for Duos how many show both
    /// tables of the team. Their fights are not simulated: a replay lacks part of what a fight is captured with.
    /// </summary>
    private static void Replays(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"[hpr] no {folder}");
            return;
        }

        var inventory = ReplayFights.Inventory(Directory.EnumerateFiles(folder, "*.hpr", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(ReadReplay));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[hpr] {inventory.Games} Battlegrounds replays, {inventory.Fights} fights, {inventory.FightsWithFirstAttack} with a first minion attack; duos: {inventory.DuosGames} games, {inventory.DuosFights} fights, both tables of the team shown in {Share(inventory.DuosBothTables, inventory.DuosFights)}"));
        Console.WriteLine("[hpr] not simulated: a replay does not hold");
        foreach (var missing in ReplayFights.Missing)
        {
            Console.WriteLine($"[hpr]   {missing}");
        }
    }

    private static string ReadReplay(string path)
    {
        try
        {
            using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            using var reader = new StreamReader(gz);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Why a captured fight is "not simulated": one line per unsimulated minion and per player-level card, as
    /// <see cref="CombatSimulator.Explain"/> counts them, led by the reason (missing_effect, missing_input, unknown).
    /// </summary>
    private static IEnumerable<string> Reasons(BattlegroundsTracker.CombatCapture capture, CardDb cards)
    {
        foreach (var (side, isOpponent) in new[] { (capture.Ours, false), (capture.Theirs, true) })
        {
            foreach (var item in CombatSimulator.Explain(side, isOpponent))
            {
                var theirs = isOpponent ? " theirs" : string.Empty;
                if (item.Extra)
                {
                    var known = CombatRelevance.Find(item.CardId) is null ? " (not in table)" : string.Empty;
                    yield return $"{item.Code}: extra {item.CardId} {cards.Get(item.CardId)?.Name}{known}{theirs}";
                    continue;
                }

                var m = side.Minions.First(x => x.HasUnsimulatedEffect && x.CardId == item.CardId && x.Golden == item.Golden);
                var row = CombatRelevance.Find(m.CardId);
                var what = row is null ? "not in table" : $"{row.Relevance} {row.Category}";
                var enchantments = m.Enchantments.Count > 0 ? $" [{string.Join(' ', m.Enchantments)}]" : string.Empty;
                yield return $"{item.Code}: {m.CardId}{(m.Golden ? " golden" : string.Empty)} {cards.Get(m.CardId)?.Name} ({what}){enchantments}{theirs}";
            }
        }
    }

    private static void Popularity(CardDb cards)
    {
        if (Environment.GetEnvironmentVariable("HEARTHPULSE_DATA") is not { Length: > 0 } data)
        {
            Console.WriteLine("[pop] HEARTHPULSE_DATA is not set: no community minion list to read");
            return;
        }

        var path = Path.Combine(data, "cache", "api", "bg-minions-all.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[pop] no {path}");
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var missing = new List<(double Share, string Id, string Why)>();
        var boardClean = 1.0;
        var minions = 0;
        foreach (var row in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = row.GetProperty("card_id").GetString()!;
            var share = row.GetProperty("normal").TryGetProperty("popularity", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() / 100 : 0;
            minions++;
            var card = cards.Get(id);
            if (!CombatRelevance.IsCombatRelevant(id, card, cards.Locale) || EffectRegistry.Default.Implements(id, golden: false))
            {
                continue;
            }

            var table = CombatRelevance.Find(id);
            missing.Add((share, id, $"{card?.Name} ({(table is null ? "not in table" : $"{table.Relevance} {table.Category}")})"));
            boardClean *= 1 - share;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[pop] {minions} minions, {missing.Count} combat-relevant without an effect; a board holds none of them {boardClean:P0}, a fight {boardClean * boardClean:P0}"));
        foreach (var (share, id, why) in missing.OrderByDescending(m => m.Share))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[pop]   {share * 100,5:N2} % {id,-14} {why}"));
        }
    }
}

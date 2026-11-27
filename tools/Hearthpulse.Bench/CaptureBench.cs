using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;

namespace Hearthpulse.Bench;

/// <summary>
/// Source capture on the kept logs: every Power.log replayed through <c>GameSession</c> with the journal on, as the app
/// records games it watches from their start, and each capture decoded by the worker's parser, so a recorder or decoder
/// change can be compared before and after (the columns: Program's usage). Captures stay in memory: nothing is written
/// to a spool or sent, and no card, name or log text is printed (a block type is the client's grammar).
/// </summary>
internal static class CaptureBench
{
    public static int Run(Options options)
    {
        var sessions = LogReplay.Logs(options.Logs);
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine($"No Hearthstone_* session with a Power.log under {options.Logs}.");
            return 2;
        }

        if (options.Cost > 0)
        {
            Cost(sessions, options.Cost);
            return 0;
        }

        var captures = new List<Capture>();
        foreach (var (dir, path, start) in sessions)
        {
            var (session, setClock, db) = LogReplay.NewSession(CardDb.Empty);
            var frames = new List<SourceCaptureFrame>();
            session.SourceCaptureRecorded += frames.Add;
            session.SetSourceCaptureEnabled(true);
            LogReplay.Run(session, setClock, dir, path, start);
            session.StopSourceCapture();
            LogReplay.Delete(db);
            var name = Path.GetFileName(dir);
            foreach (var capture in frames.GroupBy(f => f.CaptureId, StringComparer.Ordinal))
                captures.Add(Decode(name, capture.Select(f => f.Record).ToList()));
        }

        Print(captures);
        Console.WriteLine($"block types the recorder does not know: {Join(UnknownBlocks(sessions))}");
        return 0;
    }

    // BLOCK_START lines of the game state (the lines the recorder sees) whose type the recorder does not know: each is an
    // OTHER block with an unsupported gap in the journal.
    private static SortedDictionary<string, int> UnknownBlocks(IEnumerable<(string Dir, string Path, DateTime Start)> sessions)
    {
        const string Start = "BLOCK_START BlockType=";
        var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, path, _) in sessions)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (!PowerLine.TrySplit(line, out _, out var method, out var range) || line.AsSpan(method) is not "DebugPrintPower") continue;
                var body = line.AsSpan(range).Trim();
                if (!body.StartsWith(Start, StringComparison.Ordinal)) continue;
                var rest = body[Start.Length..];
                var type = rest[..(rest.IndexOf(' ') is >= 0 and var end ? end : rest.Length)].ToString();
                if (!SourceCaptureRecorder.KnownBlockType(type)) unknown[type] = unknown.GetValueOrDefault(type) + 1;
            }
        }

        return unknown;
    }

    /// <summary>
    /// What the journal costs the log thread: every log through <c>GameSession.OnLines</c> with the journal off and on,
    /// alternating, after a warm-up round; time and allocations inside <c>OnLines</c> only (the spool writes on a thread
    /// of its own in the app).
    /// </summary>
    private static void Cost(IReadOnlyList<(string Dir, string Path, DateTime Start)> sessions, int rounds)
    {
        var off = new List<double>();
        var on = new List<double>();
        long offBytes = 0, onBytes = 0, records = 0, lines = 0;
        for (var round = 0; round <= rounds; round++)
        {
            foreach (var journal in new[] { false, true })
            {
                GC.Collect();
                var pass = Pass(sessions, journal);
                lines = pass.Lines;
                if (round == 0) continue; // the warm-up round
                (journal ? on : off).Add(pass.Ms);
                if (journal)
                {
                    onBytes += pass.Bytes;
                    records = pass.Records;
                }
                else
                {
                    offBytes += pass.Bytes;
                }
            }
        }

        static string Rounds(List<double> ms) => string.Join(" / ", ms.Select(m => m.ToString("0", CultureInfo.InvariantCulture)));
        static double Median(List<double> ms) => ms.Order().ElementAt(ms.Count / 2);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[capture] cost on the log thread ({sessions.Count} logs, {lines} lines, {rounds} rounds after a warm-up): journal off {Median(off):0} ms ({Rounds(off)}), {Measure.Mb(offBytes / rounds)} allocated; on {Median(on):0} ms ({Rounds(on)}), {Measure.Mb(onBytes / rounds)}; {records} records"));
    }

    private static (double Ms, long Bytes, long Records, long Lines) Pass(IEnumerable<(string Dir, string Path, DateTime Start)> sessions, bool journal)
    {
        long ticks = 0, bytes = 0, records = 0, lines = 0;
        foreach (var (dir, path, start) in sessions)
        {
            var (session, setClock, db) = LogReplay.NewSession(CardDb.Empty);
            session.SourceCaptureRecorded += _ => records++;
            session.SetSourceCaptureEnabled(journal);
            lines += LogReplay.Run(session, setClock, dir, path, start, onBatch: feed =>
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var at = Stopwatch.GetTimestamp();
                feed();
                ticks += Stopwatch.GetTimestamp() - at;
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            });
            session.StopSourceCapture();
            LogReplay.Delete(db);
        }

        return (Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds, bytes, records, lines);
    }

    private static Capture Decode(string session, List<SourceCaptureRecord> records)
    {
        var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            kinds[record.Kind] = kinds.GetValueOrDefault(record.Kind) + 1;
            if (record.Kind != "gap") continue;
            var data = JsonSerializer.SerializeToElement(record.Data, SourceCaptureRecord.Json);
            var reason = data.GetProperty("reason").GetString() ?? "?";
            gaps[reason] = gaps.GetValueOrDefault(reason) + data.GetProperty("count").GetInt32();
        }

        var start = JsonSerializer.SerializeToElement(records[0].Data, SourceCaptureRecord.Json);
        var end = records[^1].Kind == "end" ? JsonSerializer.SerializeToElement(records[^1].Data, SourceCaptureRecord.Json) : (JsonElement?)null;
        var capture = new Capture(session, start.GetProperty("captureKind").GetString() ?? "?", start.GetProperty("mode").GetString() ?? "?",
            records.Max(r => r.Turn), records.Count, gaps, kinds, end?.GetProperty("status").GetString() ?? "-", end?.GetProperty("reason").GetString() ?? "-");
        var bytes = Encoding.UTF8.GetBytes(string.Concat(records.Select(r => r.ToJson() + "\n")));
        using var stream = new MemoryStream(bytes);
        try
        {
            return capture with { Result = new SourceCaptureParser().ParseAsync(stream).GetAwaiter().GetResult() };
        }
        catch (SourceCaptureException ex)
        {
            return capture with { Failure = ex.Code };
        }
    }

    private static void Print(List<Capture> captures)
    {
        Console.WriteLine("session                          kind  mode   turns records gaps dgaps  unsup end         reason    pool     status   complete outcome  reasons / gaps / unresolved choices");
        foreach (var c in captures)
        {
            var r = c.Result;
            var status = r?.Status ?? "failed:" + c.Failure;
            var reasons = r is null ? "" : string.Join(',', r.Eligibility.Reasons);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Session,-32} {c.Kind,-5} {c.Mode,-6} {c.Turns,5} {c.Records,7} {c.Gaps.Values.Sum(),4} {r?.Gaps ?? 0,5} {r?.Unsupported ?? 0,6} {c.EndStatus,-11} {c.EndReason,-9} {(Refused(c) ? "refuses" : "takes"),-8} {status,-8} {(r?.Summary.CompleteForAggregation ?? false),-8} {(r?.Eligibility.Outcome ?? false),-8} {reasons} / {Join(c.Gaps)} / {Join(Unresolved(r))}"));
        }

        var games = captures.Where(c => c.Kind == "game").ToList();
        var records = captures.Sum(c => c.Records);
        var gapRecords = captures.Sum(c => c.Kinds.GetValueOrDefault("gap"));
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"captures {captures.Count} (games {games.Count}): status {Join(Count(captures, c => c.Result?.Status ?? "failed"))}; end {Join(Count(captures, c => c.EndStatus))}; the pool refuses {captures.Count(Refused)} as sealed"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"records {records}, gap records {gapRecords} ({(records == 0 ? 0 : 100.0 * gapRecords / records):N1} %), per game {(games.Count == 0 ? 0 : (double)games.Sum(c => c.Kinds.GetValueOrDefault("gap")) / games.Count):N0}"));
        Console.WriteLine($"gaps by reason: {Join(Sum(captures.Select(c => c.Gaps)))}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"decoder: unsupported {captures.Sum(c => c.Result?.Unsupported ?? 0)}, gaps {captures.Sum(c => c.Result?.Gaps ?? 0)}, complete for aggregation {captures.Count(c => c.Result?.Summary.CompleteForAggregation == true)}, outcome fit {captures.Count(c => c.Result?.Eligibility.Outcome == true)}"));
        Console.WriteLine($"records by kind: {Join(Sum(captures.Select(c => c.Kinds)))}");
    }

    // The pool's rule for a journal sealed complete (no gap, end complete: the spool's manifest says complete), as
    // validation.mjs finishStream has it: no unsupported observation, no loss the decoder finds (an open block) and no
    // open choice; else 400 incomplete_stream.
    private static bool Refused(Capture c) =>
        c.Result is { } r && c.Gaps.Values.Sum() == 0 && c.EndStatus == "complete" && (r.Unsupported > 0 || r.Gaps > 0 || Unresolved(r).Count > 0);

    // The last record of each choice that is still open when the source ends, by choice type.
    private static SortedDictionary<string, int> Unresolved(SourceCaptureResult? result)
    {
        var last = new Dictionary<int, JsonElement>();
        foreach (var e in result?.Events ?? [])
            if (e.Kind == "choice") last[e.Data.GetProperty("choiceId").GetInt32()] = e.Data;
        var open = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var data in last.Values.Where(d => !d.GetProperty("resolved").GetBoolean()))
        {
            var type = data.TryGetProperty("choiceType", out var t) ? t.GetString() ?? "?" : "choice";
            open[type] = open.GetValueOrDefault(type) + 1;
        }

        return open;
    }

    private static SortedDictionary<string, int> Count(IEnumerable<Capture> captures, Func<Capture, string> key)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in captures) counts[key(c)] = counts.GetValueOrDefault(key(c)) + 1;
        return counts;
    }

    private static SortedDictionary<string, int> Sum(IEnumerable<IReadOnlyDictionary<string, int>> parts)
    {
        var sums = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in parts)
            foreach (var (key, value) in part) sums[key] = sums.GetValueOrDefault(key) + value;
        return sums;
    }

    private static string Join(IReadOnlyDictionary<string, int> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key} {p.Value}")));

    private sealed record Capture(string Session, string Kind, string Mode, int Turns, int Records, SortedDictionary<string, int> Gaps,
        SortedDictionary<string, int> Kinds, string EndStatus, string EndReason)
    {
        public SourceCaptureResult? Result { get; init; }
        public string? Failure { get; init; }
    }
}

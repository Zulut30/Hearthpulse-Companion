using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;

namespace Hearthpulse.Bench;

/// <summary>
/// Source capture on the kept logs: every Power.log replayed through <c>GameSession</c> with the journal on, as the app
/// records games it watches from their start, and each capture decoded by the worker's parser. Prints per capture the
/// mode, turns, records, the recorder's gaps by reason, the decoder's gaps, unsupported count and status, and the
/// choices left unresolved (a reason for partial without a gap), then totals, so a recorder or decoder change can be
/// compared before and after. Captures stay in memory: nothing is written to a spool or sent, and no card, name or
/// log text is printed.
/// </summary>
internal static class CaptureBench
{
    public static int Run(string logs)
    {
        var sessions = LogReplay.Logs(logs);
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine($"No Hearthstone_* session with a Power.log under {logs}.");
            return 2;
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
        return 0;
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
        Console.WriteLine("session                          kind  mode   turns records gaps dgaps  unsup end         reason    status   complete outcome  reasons / gaps / unresolved choices");
        foreach (var c in captures)
        {
            var r = c.Result;
            var status = r?.Status ?? "failed:" + c.Failure;
            var reasons = r is null ? "" : string.Join(',', r.Eligibility.Reasons);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Session,-32} {c.Kind,-5} {c.Mode,-6} {c.Turns,5} {c.Records,7} {c.Gaps.Values.Sum(),4} {r?.Gaps ?? 0,5} {r?.Unsupported ?? 0,6} {c.EndStatus,-11} {c.EndReason,-9} {status,-8} {(r?.Summary.CompleteForAggregation ?? false),-8} {(r?.Eligibility.Outcome ?? false),-8} {reasons} / {Join(c.Gaps)} / {Join(Unresolved(r))}"));
        }

        var games = captures.Where(c => c.Kind == "game").ToList();
        var records = captures.Sum(c => c.Records);
        var gapRecords = captures.Sum(c => c.Kinds.GetValueOrDefault("gap"));
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"captures {captures.Count} (games {games.Count}): status {Join(Count(captures, c => c.Result?.Status ?? "failed"))}; end {Join(Count(captures, c => c.EndStatus))}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"records {records}, gap records {gapRecords} ({(records == 0 ? 0 : 100.0 * gapRecords / records):N1} %), per game {(games.Count == 0 ? 0 : (double)games.Sum(c => c.Kinds.GetValueOrDefault("gap")) / games.Count):N0}"));
        Console.WriteLine($"gaps by reason: {Join(Sum(captures.Select(c => c.Gaps)))}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"decoder: unsupported {captures.Sum(c => c.Result?.Unsupported ?? 0)}, gaps {captures.Sum(c => c.Result?.Gaps ?? 0)}, complete for aggregation {captures.Count(c => c.Result?.Summary.CompleteForAggregation == true)}, outcome fit {captures.Count(c => c.Result?.Eligibility.Outcome == true)}"));
        Console.WriteLine($"records by kind: {Join(Sum(captures.Select(c => c.Kinds)))}");
    }

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

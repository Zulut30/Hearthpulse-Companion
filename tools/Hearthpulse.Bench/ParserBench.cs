using System.Diagnostics;
using System.Globalization;
using System.IO;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Bench;

/// <summary>
/// The Power.log parser alone on the kept logs, lines in memory and no listeners (what <c>replay</c> ends with, without
/// the card database and the meta it downloads first), and the spectator mode check: how many lines reach it (those that
/// are not GameState prints), what it costs per line and against parsing, and the look-behind for a marker when a log is
/// joined at its last game (worst case: from the end, nothing found).
/// </summary>
internal static class ParserBench
{
    public static void Run(Options options)
    {
        var logs = LogReplay.Logs(options.Logs).Select(l => (l.Start, l.Path, Lines: File.ReadAllLines(l.Path))).ToList();
        var total = logs.Sum(l => l.Lines.Length);
        var checkedLines = logs.SelectMany(l => l.Lines).Where(line => !PowerLine.TrySplit(line, out _, out _, out _)).ToArray();
        var hinted = checkedLines.Count(line => line.Contains(PowerParser.SpectatorHint, StringComparison.Ordinal));
        Console.WriteLine($"[parser] {logs.Count} logs, {total:N0} lines; {checkedLines.Length:N0} reach the spectator check, {hinted:N0} hold \"{PowerParser.SpectatorHint}\"");

        for (var round = 0; round < Math.Max(3, options.Rounds); round++)
        {
            var parse = Stopwatch.StartNew();
            foreach (var (start, _, lines) in logs)
            {
                var parser = new PowerParser(DateOnly.FromDateTime(start));
                foreach (var line in lines)
                {
                    parser.Feed(line);
                }
            }

            parse.Stop();
            var check = Stopwatch.StartNew();
            var markers = 0;
            foreach (var line in checkedLines)
            {
                if (PowerParser.SpectatorMarkerOf(line) != SpectatorMarker.None)
                {
                    markers++;
                }
            }

            check.Stop();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[parser r{round}] parse {parse.Elapsed.TotalMilliseconds:N0} ms ({total / parse.Elapsed.TotalSeconds:N0} lines/s); "
                + $"spectator check {check.Elapsed.TotalMilliseconds:N2} ms, {check.Elapsed.TotalMilliseconds * 1e6 / Math.Max(1, checkedLines.Length):N1} ns per checked line, "
                + $"{check.Elapsed.TotalMilliseconds / parse.Elapsed.TotalMilliseconds:P2} of parsing; {markers} markers"));
        }

        foreach (var (_, path, _) in logs)
        {
            var length = new FileInfo(path).Length;
            var watch = Stopwatch.StartNew();
            var marker = LogTail.FindSpectatorMarker(path, length);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[look-behind] {Path.GetFileName(Path.GetDirectoryName(path))}: {Math.Min(length, LogTail.SpectatorLookBehind) / 1048576.0:N1} MB read back in {watch.Elapsed.TotalMilliseconds:N1} ms (warm), marker {(marker is null ? "none" : "found")}"));
        }
    }
}

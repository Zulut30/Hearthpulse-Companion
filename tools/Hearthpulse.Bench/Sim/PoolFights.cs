using System.Globalization;
using System.IO;
using System.Text.Json;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Bench;

/// <summary>
/// Fights of other players from the stats pool's owner export (<c>server/pool/node/export-combats.mjs</c>, one JSON line
/// per fight: <c>{"v":1,"game","day","build","mode","fight"}</c>, docs/source-capture.md): each fight is checked with the
/// app's own <c>bg_combat</c> rules, read back as simulator input and scored like a fight of the local capture file, with
/// "not simulated" decided again by today's card data and effects. The records carry no hand, so an effect that reads
/// the player's hand plays without one. A fight older than the pool keeps it (<see cref="MaxAgeDays"/> from its day) is
/// not read: an export is a working copy, deleted after the measurement (docs/source-capture.md, "Export").
/// </summary>
internal static class PoolFights
{
    /// <summary>The pool's retention: a fight past it is gone from the pool, so a copy of it is not used either.</summary>
    public const int MaxAgeDays = 180;

    public static IReadOnlyList<SimGame> Read(string path, CardDb cards, DateOnly? today = null)
    {
        var oldest = (today ?? DateOnly.FromDateTime(DateTime.UtcNow)).DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber - MaxAgeDays;
        var games = new Dictionary<string, (int Day, int Build, bool Duos, List<SavedFight> Fights)>(StringComparer.Ordinal);
        int lines = 0, skipped = 0, expired = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            lines++;
            if (Fight(line) is not { } read)
            {
                skipped++;
                continue;
            }

            if (read.Day < oldest)
            {
                expired++;
                continue;
            }

            if (!games.TryGetValue(read.Game, out var game))
            {
                game = games[read.Game] = (read.Day, read.Build, read.Duos, []);
            }

            game.Fights.Add(read.Fight);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[sim] pool {Path.GetFileName(path)}: {lines - skipped - expired} fights of {games.Count} games read, {skipped} lines not read, {expired} older than {MaxAgeDays} days left out (delete the export)"));
        return games.Select(g => SimSample.From(
                new SavedCaptures
                {
                    StartedAt = DateTime.UnixEpoch.AddDays(g.Value.Day),
                    Build = g.Value.Build,
                    Duos = g.Value.Duos,
                    Fights = g.Value.Fights,
                },
                $"pool/{g.Key}",
                cards))
            .ToList();
    }

    private static (string Game, int Day, int Build, bool Duos, SavedFight Fight)? Fight(string line)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6
                || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1
                || !root.TryGetProperty("game", out var game) || game.GetString() is not { Length: 16 } key
                || !root.TryGetProperty("day", out var day) || !day.TryGetInt32(out var days) || days is < 0 or > 100_000
                || !root.TryGetProperty("build", out var build) || !root.TryGetProperty("mode", out var mode) || mode.GetString() is not ("bg" or "duos")
                || !root.TryGetProperty("fight", out var data) || SourceCaptureCombat.Read(data) is not { } input)
            {
                return null;
            }

            var fight = new SavedFight
            {
                Turn = input.Turn,
                Ours = input.Capture.Ours,
                Theirs = input.Capture.Theirs,
                OursFirst = input.Capture.OursFirst,
                FirstAttackerEntityId = input.Capture.FirstAttackerEntityId,
                DamageCap = input.DamageCap,
                CountDamage = input.CountDamage,
                Facts = input.Facts,
                Result = input.Result,
                DamageTaken = input.DamageTaken,
                DamageDealt = input.DamageDealt,
                Odds = input.Odds,
            };
            return (key, days, build.ValueKind == JsonValueKind.Number && build.TryGetInt32(out var b) ? b : 0, mode.GetString() == "duos", fight);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

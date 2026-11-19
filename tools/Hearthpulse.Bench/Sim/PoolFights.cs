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
/// app's own <c>bg_combat</c> rules and read back as a saved fight with its inputs and transcript, to be scored like a
/// fight of the local capture file (<c>simcover --pool</c>, "not simulated" decided again by today's card data and
/// effects) or replayed step by step (<c>simreplay --pool</c>). The records carry no hand, so an effect that reads the
/// player's hand plays without one. A fight older than the pool keeps it (<see cref="MaxAgeDays"/> from its day) is not
/// read: an export is a working copy, deleted after the measurement (docs/source-capture.md, "Export").
/// </summary>
internal static class PoolFights
{
    /// <summary>The pool's retention: a fight past it is gone from the pool, so a copy of it is not used either.</summary>
    public const int MaxAgeDays = 180;

    /// <summary>The export's games, to score (<c>simcover</c>).</summary>
    public static IReadOnlyList<SimGame> Read(string path, CardDb cards, DateOnly? today = null) =>
        Games(path, today).Select(g => SimSample.From(g.Saved, g.Key, cards)).ToList();

    /// <summary>The export's games, to replay step by step (<c>simreplay</c>; a fight without a transcript is not replayed).</summary>
    public static IReadOnlyList<ReplayableGame> Replayable(string path, DateOnly? today = null) =>
        Games(path, today).Select(g => SimReplay.From(g.Saved, g.Key)).ToList();

    /// <summary>The export's fights as saved games, keyed <c>pool/&lt;game&gt;</c>, in the order of their first line.</summary>
    public static IReadOnlyList<(string Key, SavedCaptures Saved)> Games(string path, DateOnly? today = null)
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

        var fights = games.Values.Sum(g => g.Fights.Count);
        var transcripts = games.Values.Sum(g => g.Fights.Count(f => f.Transcript is not null));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[sim] pool {Path.GetFileName(path)}: {fights} fights ({transcripts} with a transcript) of {games.Count} games read, {skipped} lines not read, {expired} older than {MaxAgeDays} days left out (delete the export)"));
        return games.Select(g => ($"pool/{g.Key}", new SavedCaptures
            {
                StartedAt = DateTime.UnixEpoch.AddDays(g.Value.Day),
                Build = g.Value.Build,
                Duos = g.Value.Duos,
                Fights = g.Value.Fights,
            }))
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

            // The sides already hold the post-game values (the record is made after the game) and the inputs besides the
            // boards; a saved fight is simulated post-game (SavedFight's options), as a fight of the local capture file.
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
                Transcript = input.Transcript,
            };
            return (key, days, build.ValueKind == JsonValueKind.Number && build.TryGetInt32(out var b) ? b : 0, mode.GetString() == "duos", fight);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Replay;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// One fight of a saved game: exactly what <see cref="CombatSimulator"/> was given at the first attack (both sides, who
/// attacked first, the damage cap, whether damage counts), what the log said about its minions
/// (<see cref="BattlegroundsTracker.MinionFacts"/>), then what happened and what the app forecast.
/// </summary>
internal sealed record SavedFight
{
    public int Turn { get; init; }

    public SimSide Ours { get; init; } = new([]);

    public SimSide Theirs { get; init; } = new([]);

    public bool? OursFirst { get; init; }

    public int FirstAttackerEntityId { get; init; }

    public int DamageCap { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool CountDamage { get; init; } = true;

    public IReadOnlyList<BattlegroundsTracker.MinionFacts> Facts { get; init; } = [];

    /// <summary>Win, Loss or Tie from the player's side, as the combat was recorded.</summary>
    public string Result { get; init; } = string.Empty;

    public int DamageTaken { get; init; }

    public int DamageDealt { get; init; }

    /// <summary>The odds the app had for the fight when the game was saved.</summary>
    public CombatOdds? Odds { get; init; }

    /// <summary>
    /// The fight's course as the log showed it (format 2 on; null in older files, for fights recorded without one, and for
    /// a transcript of a version this build does not know).
    /// </summary>
    public BattlegroundsTracker.CombatTranscript? Transcript { get; init; }

    /// <summary>The fight as it was simulated: the saved "not simulated" flags and the app's own options.</summary>
    public (BattlegroundsTracker.CombatCapture Capture, SimOptions Options) AsSaved() =>
        (new BattlegroundsTracker.CombatCapture(Ours, Theirs, OursFirst, FirstAttackerEntityId), Options(null));

    /// <summary>
    /// The fight for a new simulation: the same boards, with every board minion's "not simulated" flag decided again by
    /// <paramref name="cards"/> and <paramref name="effects"/>, as the tracker would decide it at capture today.
    /// </summary>
    public (BattlegroundsTracker.CombatCapture Capture, SimOptions Options) Today(CardDb cards, EffectRegistry effects)
    {
        var facts = Facts.ToDictionary(f => f.EntityId);
        SimSide Flag(SimSide side, bool isOpponent) => side with
        {
            Minions = side.Minions
                .Select(m => facts.TryGetValue(m.EntityId, out var known)
                    ? m with { HasUnsimulatedEffect = BattlegroundsTracker.IsUnsimulated(known, m.CardId, m.Golden, m.Enchantments, isOpponent, cards, effects) }
                    : m)
                .ToList(),
        };
        return (new BattlegroundsTracker.CombatCapture(Flag(Ours, isOpponent: false), Flag(Theirs, isOpponent: true), OursFirst, FirstAttackerEntityId),
            Options(effects));
    }

    private SimOptions Options(EffectRegistry? effects) => BattlegroundsTracker.DefaultSimOptions with
    {
        Effects = effects,
        FirstAttackerEntityId = FirstAttackerEntityId,
        DamageCap = DamageCap,
        CountDamage = CountDamage,
    };
}

/// <summary>The fights of one Battlegrounds game. No names, account ids or ratings: cards by card id only.</summary>
internal sealed record SavedCaptures
{
    public int Version { get; init; } = CombatCaptureStore.FormatVersion;

    public DateTime StartedAt { get; init; }

    /// <summary>The game client's build.</summary>
    public int Build { get; init; }

    public bool Duos { get; init; }

    public IReadOnlyList<SavedFight> Fights { get; init; } = [];

    /// <summary>
    /// The finished fights of a game: each capture with the combat it became. A fight whose combat never ended (the
    /// game was left during it) has no result and is not kept.
    /// </summary>
    public static SavedCaptures From(DateTime startedAt, int build, bool duos, IReadOnlyList<BattlegroundsTracker.CapturedFight> fights,
                                     IReadOnlyList<CombatRecord> combats) => new()
    {
        StartedAt = startedAt,
        Build = build,
        Duos = duos,
        Fights = fights.Where(f => f.CombatIndex >= 0 && f.CombatIndex < combats.Count).Select(f =>
        {
            var combat = combats[f.CombatIndex];
            return new SavedFight
            {
                Turn = combat.Turn,
                Ours = f.Capture.Ours,
                Theirs = f.Capture.Theirs,
                OursFirst = f.Capture.OursFirst,
                FirstAttackerEntityId = f.Capture.FirstAttackerEntityId,
                DamageCap = f.Options.DamageCap,
                CountDamage = f.Options.CountDamage,
                Facts = f.Facts,
                Result = combat.Result,
                DamageTaken = combat.DamageTaken,
                DamageDealt = combat.DamageDealt,
                Odds = combat.Odds,
                Transcript = f.Transcript,
            };
        }).ToList(),
    };
}

/// <summary>
/// The fights of the player's own Battlegrounds games, kept on this machine so the combat simulator can be measured
/// against them later (<c>Hearthpulse.Bench simcover --captures</c>, docs/bg-combat-sim.md): one gzip JSON file per
/// game in <c>&lt;data&gt;/combat-captures</c>. Nothing reads these files but that measurement: they are not part of a
/// replay, a shared game, a profile upload, a source capture or a crash report, and no code sends them anywhere. They
/// hold what the simulator was given, which is only what the player saw (the opponent's hand never reaches it).
/// The folder is trimmed to <see cref="MaxFiles"/> games and <see cref="MaxBytes"/>, oldest games first.
/// </summary>
internal sealed class CombatCaptureStore(string directory)
{
    // Only the player counters read by simulation effects belong in a stored input. Other numeric player tags may
    // contain ratings or identifiers and do not affect this simulation. Update this list with any new counter input.
    private static readonly HashSet<string> CounterTags = new(StringComparer.Ordinal)
    {
        "BACON_BLOODGEMBUFFATKVALUE", "BACON_BLOODGEMBUFFHEALTHVALUE",
        "TAVERN_SPELL_ATTACK_INCREASE", "TAVERN_SPELL_HEALTH_INCREASE",
        "BACON_OLD_GOD_ATTACK", "BACON_OLD_GOD_HEALTH", "4914", "4915",
    };

    /// <summary>
    /// The version of the file's content: 2 adds each fight's <see cref="SavedFight.Transcript"/>. Files of versions 1 to
    /// this one are read; a newer one is not.
    /// </summary>
    public const int FormatVersion = 2;

    public const string Extension = ".hpc";

    /// <summary>A file larger than this once unpacked is not read (a real game is some hundred kilobytes).</summary>
    public const int MaxUnpackedBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string Directory { get; } = directory;

    /// <summary>Games kept; the oldest go first.</summary>
    public int MaxFiles { get; init; } = 500;

    /// <summary>Bytes the folder may take; the oldest games go first.</summary>
    public long MaxBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>The file name of a game's captures: its start time and a hash of its key, as its replay's.</summary>
    public static string FileNameFor(string gameKey) => Path.ChangeExtension(ReplayStore.FileNameFor(gameKey), Extension);

    /// <summary>
    /// Saves a game's fights (replacing an earlier file of the same game) and trims the folder. Returns the file name,
    /// or null when nothing was saved; a failure is logged and never thrown.
    /// </summary>
    public string? Save(string gameKey, SavedCaptures captures)
    {
        if (captures.Fights.Count == 0)
        {
            return null;
        }

        SimSide Minimize(SimSide side, bool opponent) => side with
        {
            Tags = side.Tags.Where(t => CounterTags.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
            Hand = opponent ? [] : side.Hand,
            ExtraTags = opponent ? SimSide.EmptyExtraTags : side.ExtraTags,
        };
        captures = captures with
        {
            Fights = captures.Fights.Select(f => f with { Ours = Minimize(f.Ours, false), Theirs = Minimize(f.Theirs, true) }).ToList(),
        };

        var name = FileNameFor(gameKey);
        string? temp = null;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, name);
            // Written whole under another name first: a reader never meets half a file.
            temp = $"{path}.{Guid.NewGuid():N}.tmp";
            using (var file = File.Create(temp))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, captures, Json);
            }

            File.Move(temp, path, overwrite: true);
            Prune(keep: name);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            Diag.Error("Saving the game's combat captures failed", ex);
            TryDelete(temp);
            return null;
        }
    }

    /// <summary>The file names kept, oldest game first.</summary>
    public IReadOnlyList<string> Names()
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? System.IO.Directory.EnumerateFiles(Directory, "*" + Extension).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A game's fights, or null when the file is gone, damaged, too large or of another version.</summary>
    public SavedCaptures? Load(string fileName) => Read(Path.Combine(Directory, Path.GetFileName(fileName)));

    /// <summary>Reads a captures file anywhere on disk; null when it cannot be read as one.</summary>
    public static SavedCaptures? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var unpacked = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (unpacked.Length + read > MaxUnpackedBytes)
                {
                    return null;
                }

                unpacked.Write(buffer, 0, read);
            }

            var captures = JsonSerializer.Deserialize<SavedCaptures>(unpacked.GetBuffer().AsSpan(0, (int)unpacked.Length), Json);
            return captures is { Version: >= 1 and <= FormatVersion, Fights: not null } && captures.Fights.All(f =>
                f is { Facts: not null } && ValidSide(f.Ours) && ValidSide(f.Theirs)
                && f.Facts.All(fact => fact is { CardId: not null, Markers: not null } && fact.Markers.All(m => m is not null))
                && f.Facts.Select(fact => fact.EntityId).Distinct().Count() == f.Facts.Count
                && (f.Transcript is null || f.Transcript.Version != BattlegroundsTracker.CombatTranscript.CurrentVersion || ValidTranscript(f.Transcript)))
                ? captures with
                {
                    // A transcript of another version is not understood: the fight stays, without it.
                    Fights = captures.Fights
                        .Select(f => f.Transcript is { Version: not BattlegroundsTracker.CombatTranscript.CurrentVersion } ? f with { Transcript = null } : f)
                        .ToList(),
                }
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException
                                       or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool ValidTranscript(BattlegroundsTracker.CombatTranscript transcript) =>
        transcript is { Events: not null, Boards: not null, Ending: not null }
        && transcript.Events.All(e => e is not null && Enum.IsDefined(e.Kind))
        && transcript.Boards.All(b => b is { Ours: not null, Theirs: not null } && b.Event >= 0 && b.Event <= transcript.Events.Count
                                      && b.Ours.Concat(b.Theirs).All(m => m is { Card: not null }));

    private static bool ValidSide(SimSide? side) =>
        side is { Minions: not null, Hand: not null, Tags: not null, ExtraTags: not null, Extras: not null, HeroCardId: not null, HeroPowerCardId: not null }
        && side.Minions.All(ValidMinion) && side.Hand.All(ValidMinion) && side.Extras.All(e => e is not null)
        && side.ExtraTags.Values.All(tags => tags is not null);

    private static bool ValidMinion(SimMinion? minion) =>
        minion is { CardId: not null, CreatorCardId: not null, Enchantments: not null, Remembered: not null }
        && minion.Enchantments.All(e => e is not null) && minion.Remembered.All(ValidMinion);

    /// <summary>Deletes the oldest games beyond <see cref="MaxFiles"/> and <see cref="MaxBytes"/>; returns how many went.</summary>
    public int Prune(string? keep = null)
    {
        var removed = 0;
        try
        {
            var files = new DirectoryInfo(Directory).EnumerateFiles("*" + Extension).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            var bytes = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (files.Count - removed <= MaxFiles && bytes <= MaxBytes)
                {
                    break;
                }

                if (string.Equals(file.Name, keep, StringComparison.Ordinal))
                {
                    continue;
                }

                bytes -= file.Length;
                file.Delete();
                removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Error("Trimming the combat captures failed", ex);
        }

        return removed;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless: it is not read and not counted.
        }
    }
}

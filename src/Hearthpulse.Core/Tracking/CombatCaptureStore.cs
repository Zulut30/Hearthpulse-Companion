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

    /// <summary>
    /// What <see cref="Ours"/> and <see cref="Theirs"/> keep out of their own JSON (exact inputs and estimates), saved
    /// beside them (format 2 on; null in older files and when there is nothing) and put back on the sides when the file is
    /// read (<see cref="CombatCaptureStore.Read"/>).
    /// </summary>
    public SavedInputs? OursInputs { get; init; }

    public SavedInputs? TheirsInputs { get; init; }

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
        // A saved fight is read after its game: the opponent's values the player could not see are used (HiddenTags).
        PostGame = true,
    };
}

/// <summary>
/// A side's inputs its records leave out of their JSON (<c>[JsonIgnore]</c> in <c>CombatSimulator.Inputs.cs</c> and
/// <c>CombatSimulator.Rules.cs</c>): the player's enchantments, the deaths before the capture, the lobby's tribes, the
/// estimates, the hero's lobby PLAYER_ID and the local player's own secrets, and per minion of the board and hand (by
/// entity id) its enchantment values, the friendly deaths it saw before the capture and whether Start of Combat summoned it. Minions inside
/// these lists (<see cref="DeathsBeforeCapture"/>) keep only what their own JSON holds.
/// </summary>
internal sealed record SavedInputs
{
    public IReadOnlyList<SimEnchantment>? PlayerEnchantments { get; init; }

    public IReadOnlyList<SimMinion>? DeathsBeforeCapture { get; init; }

    public Tribe LobbyTribes { get; init; }

    public IReadOnlyList<SimEstimate>? Estimates { get; init; }

    public IReadOnlyList<SavedMinionInputs>? Minions { get; init; }

    /// <summary>The hero's lobby PLAYER_ID (death order); 0 when unknown.</summary>
    public int PlayerId { get; init; }

    /// <summary>The local player's own secrets (Ice Block); never the opponent's (<see cref="CombatCaptureStore.Save"/>).</summary>
    public IReadOnlyList<string>? Secrets { get; init; }

    /// <summary>The inputs of <paramref name="side"/>, or null when it has none.</summary>
    public static SavedInputs? Of(SimSide side)
    {
        var minions = side.Minions.Concat(side.Hand)
            .Where(m => m.EnchantmentValues.Count > 0 || m.FriendlyDeathsBeforeCapture != 0 || m.SummonedInCombat || m.Crew.Count > 0)
            .Select(m => new SavedMinionInputs(m.EntityId)
            {
                EnchantmentValues = m.EnchantmentValues,
                FriendlyDeathsBeforeCapture = m.FriendlyDeathsBeforeCapture,
                SummonedInCombat = m.SummonedInCombat,
                Crew = m.Crew.Count > 0 ? m.Crew : null,
            })
            .ToList();
        var inputs = new SavedInputs
        {
            PlayerEnchantments = side.PlayerEnchantments.Count > 0 ? side.PlayerEnchantments : null,
            DeathsBeforeCapture = side.DeathsBeforeCapture.Count > 0 ? side.DeathsBeforeCapture : null,
            LobbyTribes = side.LobbyTribes,
            Estimates = side.Estimates.Count > 0 ? side.Estimates : null,
            Minions = minions.Count > 0 ? minions : null,
            PlayerId = side.PlayerId,
            Secrets = side.Secrets.Count > 0 ? side.Secrets : null,
        };
        return inputs is { PlayerEnchantments: null, DeathsBeforeCapture: null, LobbyTribes: Tribe.None, Estimates: null, Minions: null, PlayerId: 0, Secrets: null }
            ? null
            : inputs;
    }

    /// <summary><paramref name="side"/> with these inputs put back.</summary>
    public SimSide ApplyTo(SimSide side)
    {
        var byEntity = (Minions ?? []).GroupBy(m => m.EntityId).ToDictionary(g => g.Key, g => g.First());
        SimMinion Apply(SimMinion m) => m.EntityId != 0 && byEntity.TryGetValue(m.EntityId, out var saved)
            ? m with
            {
                EnchantmentValues = saved.EnchantmentValues ?? [],
                FriendlyDeathsBeforeCapture = saved.FriendlyDeathsBeforeCapture,
                SummonedInCombat = saved.SummonedInCombat,
                Crew = saved.Crew ?? [],
            }
            : m;
        return side with
        {
            Minions = side.Minions.Select(Apply).ToList(),
            Hand = side.Hand.Select(Apply).ToList(),
            PlayerEnchantments = PlayerEnchantments ?? [],
            DeathsBeforeCapture = DeathsBeforeCapture ?? [],
            LobbyTribes = LobbyTribes,
            Estimates = Estimates ?? [],
            PlayerId = PlayerId,
            Secrets = Secrets ?? [],
        };
    }
}

/// <summary>A minion's inputs of <see cref="SavedInputs"/>, by its entity id.</summary>
internal sealed record SavedMinionInputs(int EntityId)
{
    public IReadOnlyList<SimEnchantment>? EnchantmentValues { get; init; }

    public int FriendlyDeathsBeforeCapture { get; init; }

    /// <summary>Summoned during Start of Combat (<see cref="SimMinion.SummonedInCombat"/>).</summary>
    public bool SummonedInCombat { get; init; }

    /// <summary>A Timewarped Nellie's Ship's crew (<see cref="SimMinion.Crew"/>); null when it has none.</summary>
    public IReadOnlyList<SimMinion>? Crew { get; init; }
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
                OursInputs = SavedInputs.Of(f.Capture.Ours),
                TheirsInputs = SavedInputs.Of(f.Capture.Theirs),
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
    /// The version of the file's content: 2 adds each fight's <see cref="SavedFight.Transcript"/> and the sides' inputs
    /// their records keep out of JSON (<see cref="SavedFight.OursInputs"/>). Files of versions 1 to this one are read; a
    /// newer one is not.
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
        // The player's enchantments are the local player's only (whether the client shows the opponent's is not established);
        // so are secrets, which the opponent's side never carries.
        static SavedInputs? MinimizeInputs(SavedInputs? inputs, bool opponent) =>
            opponent && inputs is not null ? inputs with { PlayerEnchantments = null, Secrets = null } : inputs;
        captures = captures with
        {
            Fights = captures.Fights.Select(f => f with
            {
                Ours = Minimize(f.Ours, false),
                Theirs = Minimize(f.Theirs, true),
                OursInputs = MinimizeInputs(f.OursInputs, opponent: false),
                TheirsInputs = MinimizeInputs(f.TheirsInputs, opponent: true),
            }).ToList(),
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
                && (f.Transcript is null || f.Transcript.Version != BattlegroundsTracker.CombatTranscript.CurrentVersion || ValidTranscript(f.Transcript))
                && ValidInputs(f.OursInputs) && ValidInputs(f.TheirsInputs))
                ? captures with
                {
                    // A transcript of another version is not understood: the fight stays, without it. The inputs saved
                    // beside the sides go back on them; a side of an older file keeps its defaults.
                    Fights = captures.Fights.Select(f => f with
                    {
                        Transcript = f.Transcript is { Version: not BattlegroundsTracker.CombatTranscript.CurrentVersion } ? null : f.Transcript,
                        Ours = Restore(f.Ours, f.OursInputs),
                        Theirs = Restore(f.Theirs, f.TheirsInputs),
                    }).ToList(),
                }
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException
                                       or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SimSide Restore(SimSide side, SavedInputs? inputs)
    {
        // A side written without the post-game values (older files, or a JSON null) has none; likewise the minions a hero
        // power or trinket removed at Start of Combat (E3; written in the side's own JSON).
        side = side.HiddenTags is null ? side with { HiddenTags = SimSide.EmptyTags } : side;
        side = side.RemovedByExtras is null ? side with { RemovedByExtras = SimSide.EmptyRemovedByExtras } : side;
        return inputs?.ApplyTo(side) ?? side;
    }

    private static bool ValidInputs(SavedInputs? inputs) =>
        inputs is null
        || ((inputs.PlayerEnchantments ?? []).All(e => e is { CardId: not null })
            && (inputs.DeathsBeforeCapture ?? []).All(ValidMinion)
            && (inputs.Estimates ?? []).All(e => e is { Input: not null, Basis: not null, Tags: not null } && e.Tags.All(t => t is not null))
            && (inputs.Minions ?? []).All(m => m is not null && (m.EnchantmentValues ?? []).All(e => e is { CardId: not null })
                                               && (m.Crew ?? []).All(ValidMinion))
            && (inputs.Secrets ?? []).All(s => s is not null));

    private static bool ValidTranscript(BattlegroundsTracker.CombatTranscript transcript) =>
        transcript is { Events: not null, Boards: not null, Ending: not null }
        && transcript.Events.All(e => e is not null && Enum.IsDefined(e.Kind))
        && transcript.Boards.All(b => b is { Ours: not null, Theirs: not null } && b.Event >= 0 && b.Event <= transcript.Events.Count
                                      && b.Ours.Concat(b.Theirs).All(m => m is { Card: not null }));

    private static bool ValidSide(SimSide? side) =>
        side is { Minions: not null, Hand: not null, Tags: not null, ExtraTags: not null, Extras: not null, HeroCardId: not null, HeroPowerCardId: not null }
        && side.Minions.All(ValidMinion) && side.Hand.All(ValidMinion) && side.Extras.All(e => e is not null)
        && side.ExtraTags.Values.All(tags => tags is not null)
        && (side.RemovedByExtras is null || side.RemovedByExtras.All(r => r.Key is not null && r.Value is not null && r.Value.All(ValidMinion)));

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

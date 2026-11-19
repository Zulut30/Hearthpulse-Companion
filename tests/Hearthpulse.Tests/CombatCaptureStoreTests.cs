using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hearthpulse.Bench;
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Tracking;
using Microsoft.Data.Sqlite;
using static Hearthpulse.Tests.BattlegroundsReplayTests;

namespace Hearthpulse.Tests;

/// <summary>
/// The local file of a game's fights (<see cref="CombatCaptureStore"/>, docs/bg-combat-sim.md): what the simulator was
/// given comes back the same, the folder stays within its limits, a damaged file or a failed save hurts nothing, and no
/// code that sends anything knows the file.
/// </summary>
public sealed class CombatCaptureStoreTests : IDisposable
{
    private readonly string _dir = TestDataFolder.Scratch("hp-captures");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    /// <summary>A combat whose minion carries an enchantment, a script value and a granted deathrattle.</summary>
    private static string[] EnchantedFight() =>
    [
        .. Setup,
        Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4),
        Tag(51, "ZONE", "REMOVEDFROMGAME"),
        .. CombatSetup(),
        .. Entity(130, "BG28_503e", Me, "PLAY", "ENCHANTMENT", "ATTACHED=101"),
        Tag(101, "TAG_SCRIPT_DATA_NUM_1", 7),
        Tag(101, "DEATHRATTLE", 1),
        .. BattlegroundsReplayTests.Fight(win: true),
    ];

    private static SavedCaptures Captured(out BattlegroundsTracker tracker)
    {
        var (game, _, played) = Play(EnchantedFight());
        tracker = played;
        return SavedCaptures.From(game.StartedAt, game.BuildNumber, game.IsDuos, tracker.Captures, tracker.Combats);
    }

    private static SimOptions Fixed(SimOptions options) => options with { Runs = 300, MinRuns = 300, TimeBudget = null };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_damage_mode_survives_a_capture_file_roundtrip(bool countDamage)
    {
        var saved = Captured(out _);
        saved = saved with { Duos = !countDamage, Fights = saved.Fights.Select(f => f with { CountDamage = countDamage }).ToList() };
        var store = new CombatCaptureStore(_dir);

        var name = store.Save("20261023123000|damage-mode", saved);
        var read = store.Load(name!);

        Assert.NotNull(read);
        Assert.Equal(!countDamage, read.Duos);
        var fight = Assert.Single(read.Fights);
        Assert.Equal(countDamage, fight.CountDamage);
        Assert.Equal(countDamage, fight.AsSaved().Options.CountDamage);
        Assert.Equal(countDamage, fight.Today(CardDb.Empty, EffectRegistry.Default).Options.CountDamage);
    }

    [Fact]
    public void A_saved_fight_reads_back_as_the_simulator_got_it_and_gives_the_same_odds()
    {
        var saved = Captured(out var tracker);
        var store = new CombatCaptureStore(_dir);

        var name = store.Save("20261023123000|game", saved);
        var read = store.Load(name!);

        Assert.NotNull(read);
        var original = Assert.Single(tracker.Captures);
        var fight = Assert.Single(read!.Fights);
        // What a replay lacks is here: the enchantment, the script value, the granted deathrattle.
        var ours = Assert.Single(fight.Ours.Minions);
        Assert.Equal((4, 6, 7), (ours.Attack, ours.Health, ours.ScriptData1));
        Assert.Equal(["BG28_503e"], ours.Enchantments);
        Assert.True((ours.Mechanics & CardMechanic.Deathrattle) != 0);
        Assert.Contains(fight.Facts, f => f.EntityId == ours.EntityId && f.CardId == "BG26_801" && f.Markers.Contains("DEATHRATTLE"));
        // The fight's result and the app's forecast came along.
        Assert.Equal(("Win", tracker.Combats[0].DamageDealt, tracker.Combats[0].Turn), (fight.Result, fight.DamageDealt, fight.Turn));
        Assert.Equal(tracker.Combats[0].Odds!.Win, fight.Odds!.Win);

        // The same seed, the same fights: every number of the odds is the same.
        var (capture, options) = fight.AsSaved();
        var before = CombatSimulator.Run(original.Capture.Ours, original.Capture.Theirs, original.Capture.OursFirst, Fixed(original.Options));
        var after = CombatSimulator.Run(capture.Ours, capture.Theirs, capture.OursFirst, Fixed(options));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(300, after.Runs);
        // Non-simulation player tags are deliberately dropped from the local file; every other input survives.
        var minimized = original.Capture with
        {
            Ours = original.Capture.Ours with { Tags = capture.Ours.Tags },
            Theirs = original.Capture.Theirs with { Tags = capture.Theirs.Tags },
        };
        Assert.Equal(JsonSerializer.Serialize(minimized), JsonSerializer.Serialize(capture));
        Assert.Equal((original.Options.DamageCap, original.Options.CountDamage, original.Options.FirstAttackerEntityId),
            (options.DamageCap, options.CountDamage, options.FirstAttackerEntityId));
    }

    [Fact]
    public void Only_simulation_counters_and_the_local_hand_are_kept_in_the_file()
    {
        string[] counters = ["BACON_BLOODGEMBUFFATKVALUE", "BACON_BLOODGEMBUFFHEALTHVALUE",
            "TAVERN_SPELL_ATTACK_INCREASE", "TAVERN_SPELL_HEALTH_INCREASE", "BACON_OLD_GOD_ATTACK", "BACON_OLD_GOD_HEALTH", "4914", "4915"];
        var tags = counters.Select((tag, i) => (tag, value: i + 1)).ToDictionary(t => t.tag, t => t.value);
        tags["RATING"] = 12345;
        tags["ACCOUNT_ID"] = 67890;
        tags["UNKNOWN_PLAYER_TAG"] = 999;
        var hand = new[] { SimMinion.Create("LOCAL_CARD", 2, 3) };
        var side = new SimSide([]) { Tags = tags, Hand = hand, ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>> { ["CARD"] = new Dictionary<string, int> { ["TAG_SCRIPT_DATA_NUM_1"] = 7 } } };
        var saved = new SavedCaptures { Fights = [new SavedFight { Ours = side, Theirs = side }] };
        var store = new CombatCaptureStore(_dir);

        var name = store.Save("20261023123000|counter-fields", saved);
        var fight = Assert.Single(store.Load(name!)!.Fights);

        Assert.Equal(counters.Order(StringComparer.Ordinal), fight.Ours.Tags.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(counters.Order(StringComparer.Ordinal), fight.Theirs.Tags.Keys.Order(StringComparer.Ordinal));
        Assert.All(counters, tag => Assert.Equal(tags[tag], fight.Ours.Tags[tag]));
        Assert.Equal("LOCAL_CARD", Assert.Single(fight.Ours.Hand).CardId);
        Assert.Empty(fight.Theirs.Hand);
        Assert.Empty(fight.Theirs.ExtraTags);
        Assert.Equal(7, fight.Ours.ExtraTags["CARD"]["TAG_SCRIPT_DATA_NUM_1"]);
    }

    [Fact]
    public void A_saved_fight_is_flagged_again_by_the_effects_of_the_day_it_is_measured()
    {
        var saved = Captured(out var tracker);
        var fight = Assert.Single(saved.Fights);
        // At capture the granted deathrattle had no effect behind it: "not simulated".
        Assert.True(Assert.Single(tracker.Captures).Capture.Ours.Minions[0].HasUnsimulatedEffect);

        // The same registry flags the same; one that knows the enchantment does not.
        Assert.True(fight.Today(CardDb.Empty, EffectRegistry.Default).Capture.Ours.Minions[0].HasUnsimulatedEffect);
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""[{"id":"BG26_801","name":"Minion","type":"MINION","attack":4,"health":6,"mechanics":[]}]"""));
        var cards = CardDb.Parse("enUS", json);
        Assert.True(fight.Today(cards, EffectRegistry.Default).Capture.Ours.Minions[0].HasUnsimulatedEffect);
        var knowing = EffectRegistry.FromTypes([typeof(GrantedDeathrattle)]);
        Assert.False(fight.Today(cards, knowing).Capture.Ours.Minions[0].HasUnsimulatedEffect);
        // The measurement reads it as a game of its sample.
        var game = SimSample.From(saved, "captures/x.hpc", cards, knowing);
        Assert.Equal(("captures/x.hpc/1", "Win", false), (game.Fights[0].Key, game.Fights[0].Result, game.Duos));
        Assert.Equal(0, SimSample.Score(game.Fights[0], SimBudget.Full)!.Unsimulated);
    }

    [CardEffect("BG28_503e")]
    private sealed class GrantedDeathrattle : CombatEffect
    {
        public override void Deathrattle(Hearthpulse.Core.Battlegrounds.Fight fight, Unit self)
        {
        }
    }

    [Fact]
    public void The_oldest_games_go_when_the_folder_holds_too_many()
    {
        var saved = Captured(out _);
        var store = new CombatCaptureStore(_dir) { MaxFiles = 3 };

        foreach (var hour in new[] { 11, 10, 13, 12, 14 })
        {
            store.Save($"202610231{hour - 10}0000|game", saved);
        }

        // By the game's start, not by when the file was written: 10 went before 11, though it was saved after it.
        Assert.Equal(["20261023120000", "20261023130000", "20261023140000"], store.Names().Select(n => n[..14]));
        Assert.All(store.Names(), n => Assert.EndsWith(CombatCaptureStore.Extension, n, StringComparison.Ordinal));
    }

    [Fact]
    public void The_oldest_games_go_when_the_folder_is_too_large_but_never_the_game_just_saved()
    {
        var saved = Captured(out _);
        var one = new CombatCaptureStore(_dir);
        one.Save("20261023100000|game", saved);
        var size = new FileInfo(Path.Combine(_dir, one.Names()[0])).Length;
        var store = new CombatCaptureStore(_dir) { MaxBytes = size * 2 + size / 2 };

        store.Save("20261023110000|game", saved);
        store.Save("20261023120000|game", saved);
        Assert.Equal(2, store.Names().Count);

        // A limit below one game: the game just saved stays, even an older one.
        var tight = new CombatCaptureStore(_dir) { MaxBytes = 1 };
        tight.Save("20261023090000|game", saved);
        Assert.Equal(["20261023090000"], tight.Names().Select(n => n[..14]));
    }

    [Fact]
    public void Saving_the_same_game_again_replaces_its_file_and_leaves_no_temp_file()
    {
        var saved = Captured(out _);
        var store = new CombatCaptureStore(_dir);

        store.Save("20261023100000|game", saved);
        store.Save("20261023100000|game", saved with { Build = 7 });

        Assert.Equal(7, store.Load(Assert.Single(store.Names()))!.Build);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Null(store.Save("20261023110000|game", saved with { Fights = [] }));
    }

    [Fact]
    public void A_fights_transcript_survives_the_capture_file()
    {
        var saved = Captured(out var tracker);
        var store = new CombatCaptureStore(_dir);

        var fight = Assert.Single(store.Load(store.Save("20261023123000|transcript", saved)!)!.Fights);

        var original = Assert.Single(tracker.Captures).Transcript;
        Assert.NotNull(original);
        Assert.NotNull(fight.Transcript);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(fight.Transcript));
        Assert.True(fight.Transcript.Complete);
        Assert.Equal(original.RegularAttacks + 1, fight.Transcript.Boards.Count);
    }

    [Fact]
    public void A_file_of_the_first_format_still_loads_without_transcripts()
    {
        var saved = Captured(out _);
        var old = saved with { Version = 1, Fights = saved.Fights.Select(f => f with { Transcript = null }).ToList() };
        var store = new CombatCaptureStore(_dir);
        var name = store.Save("20261023123000|old", old)!;
        // What a build of format 1 wrote: no transcript property at all.
        var json = ReadJson(Path.Combine(_dir, name));
        Assert.DoesNotContain("transcript", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", json, StringComparison.Ordinal);

        var read = store.Load(name);

        Assert.NotNull(read);
        Assert.Equal(1, read.Version);
        Assert.Null(Assert.Single(read.Fights).Transcript);
    }

    [Fact]
    public void A_transcript_of_an_unknown_version_is_dropped_and_its_fight_kept()
    {
        var saved = Captured(out _);
        saved = saved with { Fights = saved.Fights.Select(f => f with { Transcript = f.Transcript! with { Version = 99 } }).ToList() };
        var store = new CombatCaptureStore(_dir);

        var read = store.Load(store.Save("20261023123000|future", saved)!);

        Assert.NotNull(read);
        var fight = Assert.Single(read.Fights);
        Assert.Null(fight.Transcript);
        Assert.Single(fight.Ours.Minions);
    }

    /// <summary>A side carrying every input its records keep out of their JSON (packages I and R).</summary>
    private static SimSide WithInputs(SimSide side, bool opponent) => side with
    {
        Minions = side.Minions.Select((m, i) => m with
        {
            EnchantmentValues = [new SimEnchantment("BG_ENCH_" + i) { ScriptData1 = 3 + i, Attack = 1, Health = 2, CreatorEntityId = 77 }],
            FriendlyDeathsBeforeCapture = 1 + i,
            SummonedInCombat = i == 0,
        }).ToList(),
        PlayerId = opponent ? 6 : 1,
        Secrets = opponent ? [] : [DamageRules.IceBlock],
        PlayerEnchantments = opponent ? [] : [new SimEnchantment("BG_PLAYER_ENCH") { ScriptData1 = 2, CreatorEntityId = 101 }],
        DeathsBeforeCapture = [SimMinion.Create("BG_DIED_EARLY", 2, 3) with { EntityId = 999 }],
        LobbyTribes = Tribe.Beast | Tribe.Murloc,
        Estimates = opponent ? [new SimEstimate("BloodGem", 2, 2, "turn 9") { Tags = ["BACON_BLOODGEMBUFFATKVALUE"] }] : [],
        HiddenTags = opponent ? new Dictionary<string, int> { ["BACON_BLOODGEMBUFFATKVALUE"] = 4 } : SimSide.EmptyTags,
    };

    [Fact]
    public void The_inputs_the_records_keep_out_of_json_survive_the_capture_file()
    {
        var saved = Captured(out _);
        var fight = Assert.Single(saved.Fights);
        var ours = WithInputs(fight.Ours, opponent: false);
        var theirs = WithInputs(fight.Theirs, opponent: true);
        saved = saved with
        {
            Fights = [fight with { Ours = ours, Theirs = theirs, OursInputs = SavedInputs.Of(ours), TheirsInputs = SavedInputs.Of(theirs) }],
        };
        var store = new CombatCaptureStore(_dir);

        var read = Assert.Single(store.Load(store.Save("20261023123000|inputs", saved)!)!.Fights);

        foreach (var (before, after) in new[] { (ours, read.Ours), (theirs, read.Theirs) })
        {
            Assert.Equal(before.Minions.Select(m => (m.EntityId, m.FriendlyDeathsBeforeCapture, m.SummonedInCombat)),
                         after.Minions.Select(m => (m.EntityId, m.FriendlyDeathsBeforeCapture, m.SummonedInCombat)));
            Assert.Equal(before.PlayerId, after.PlayerId);
            Assert.Equal(before.Minions.SelectMany(m => m.EnchantmentValues), after.Minions.SelectMany(m => m.EnchantmentValues));
            Assert.Equal(before.LobbyTribes, after.LobbyTribes);
            Assert.Equal(before.DeathsBeforeCapture.Select(m => (m.CardId, m.EntityId, m.Attack, m.Health)), after.DeathsBeforeCapture.Select(m => (m.CardId, m.EntityId, m.Attack, m.Health)));
            Assert.Equal(JsonSerializer.Serialize(before.Estimates), JsonSerializer.Serialize(after.Estimates));
            Assert.Equal(before.HiddenTags.OrderBy(t => t.Key), after.HiddenTags.OrderBy(t => t.Key));
        }

        Assert.Equal(ours.PlayerEnchantments, read.Ours.PlayerEnchantments);
        Assert.Equal([DamageRules.IceBlock], read.Ours.Secrets);
        // A saved fight is read after its game: its options use the opponent's post-game values.
        Assert.True(read.AsSaved().Options.PostGame);
        Assert.True(read.Today(CardDb.Empty, EffectRegistry.Default).Options.PostGame);
    }

    [Fact]
    public void The_opponents_secrets_never_reach_the_file()
    {
        // The opponent's secrets are hidden: the tracker never reads them, and the file drops any that a side carried.
        var saved = Captured(out _);
        var fight = Assert.Single(saved.Fights);
        var theirs = fight.Theirs with { Secrets = ["BG_THEIR_SECRET"] };
        saved = saved with { Fights = [fight with { Theirs = theirs, TheirsInputs = SavedInputs.Of(theirs) }] };
        var store = new CombatCaptureStore(_dir);

        var name = store.Save("20261023123000|their-secrets", saved)!;

        Assert.DoesNotContain("BG_THEIR_SECRET", ReadJson(Path.Combine(_dir, name)), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(store.Load(name)!.Fights).Theirs.Secrets);
    }

    [Fact]
    public void The_opponents_player_enchantments_never_reach_the_file()
    {
        var saved = Captured(out _);
        var fight = Assert.Single(saved.Fights);
        var theirs = fight.Theirs with { PlayerEnchantments = [new SimEnchantment("BG_THEIR_PLAYER_ENCH")] };
        saved = saved with { Fights = [fight with { Theirs = theirs, TheirsInputs = SavedInputs.Of(theirs) }] };
        var store = new CombatCaptureStore(_dir);

        var name = store.Save("20261023123000|their-enchantments", saved)!;

        Assert.DoesNotContain("BG_THEIR_PLAYER_ENCH", ReadJson(Path.Combine(_dir, name)), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(store.Load(name)!.Fights).Theirs.PlayerEnchantments);
    }

    [Fact]
    public void A_file_without_inputs_or_post_game_values_reads_with_their_defaults()
    {
        var saved = Captured(out _);
        var store = new CombatCaptureStore(_dir);
        var name = store.Save("20261023123000|plain", saved with { Version = 1, Fights = saved.Fights.Select(f => f with { OursInputs = null, TheirsInputs = null, Transcript = null }).ToList() })!;
        // What an older build (or a hand-edited file) writes: no inputs, and the opponent's post-game values as null.
        var json = ReadJson(Path.Combine(_dir, name)).Replace("\"theirs\":{", "\"theirs\":{\"hiddenTags\":null,", StringComparison.Ordinal);
        Assert.Contains("\"hiddenTags\":null", json, StringComparison.Ordinal);
        File.Delete(Path.Combine(_dir, name));
        using (var gzip = new GZipStream(File.Create(Path.Combine(_dir, name)), CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        var fight = Assert.Single(store.Load(name)!.Fights);

        Assert.Empty(fight.Theirs.HiddenTags);
        Assert.Empty(fight.Ours.PlayerEnchantments);
        Assert.Empty(fight.Theirs.Estimates);
        Assert.Equal(Tribe.None, fight.Ours.LobbyTribes);
        Assert.All(fight.Ours.Minions, m => Assert.Empty(m.EnchantmentValues));
    }

    private static string ReadJson(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_damaged_file_reads_as_nothing()
    {
        var saved = Captured(out _);
        var store = new CombatCaptureStore(_dir);
        var good = Path.Combine(_dir, store.Save("20261023100000|game", saved)!);
        static void Gzip(string path, string text)
        {
            using var gzip = new GZipStream(File.Create(path), CompressionLevel.Fastest);
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        File.WriteAllText(Path.Combine(_dir, "a.hpc"), "not gzip at all");
        File.WriteAllBytes(Path.Combine(_dir, "b.hpc"), File.ReadAllBytes(good)[..40]);
        Gzip(Path.Combine(_dir, "c.hpc"), "{ \"fights\": [ { \"ours\": 5 } ] }");
        Gzip(Path.Combine(_dir, "d.hpc"), "{ \"version\": 99, \"fights\": [] }");
        Gzip(Path.Combine(_dir, "e.hpc"), "[1, 2");
        Gzip(Path.Combine(_dir, "f.hpc"), "{ \"version\": 1, \"fights\": [ { \"ours\": null } ] }");
        File.WriteAllBytes(Path.Combine(_dir, "g.hpc"), []);
        Gzip(Path.Combine(_dir, "h.hpc"), "{\"version\":1,\"fights\":[{\"ours\":{\"minions\":[null]},\"theirs\":{\"minions\":[]}}]}");
        Gzip(Path.Combine(_dir, "i.hpc"), "{\"version\":1,\"fights\":[{\"ours\":{\"minions\":[]},\"theirs\":{\"minions\":[]},\"facts\":null}]}");
        Gzip(Path.Combine(_dir, "j.hpc"), "{\"version\":1,\"fights\":[{\"ours\":{\"minions\":[],\"tags\":null},\"theirs\":{\"minions\":[]}}]}");

        Assert.All(new[] { "a.hpc", "b.hpc", "c.hpc", "d.hpc", "e.hpc", "f.hpc", "g.hpc", "h.hpc", "i.hpc", "j.hpc", "missing.hpc" }, name => Assert.Null(store.Load(name)));
        Assert.NotNull(store.Load(Path.GetFileName(good)));
        // A name with a path in it stays inside the folder.
        Assert.Null(store.Load(Path.Combine("..", "..", "settings.json")));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_fails_the_save_and_nothing_else()
    {
        // A file stands where the folder should be.
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "x");
        var store = new CombatCaptureStore(blocked);

        Assert.Null(store.Save("20261023100000|game", Captured(out _)));
        Assert.Empty(store.Names());
        Assert.Equal(0, store.Prune());
    }

    // ------------------------------------------------------------------------------------------ the live session

    private (GameSession Session, HistoryStore History, HearthstoneMemory Memory) Session(string captures)
    {
        Directory.CreateDirectory(_dir);
        var memory = new HearthstoneMemory();
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        var now = new DateTime(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);
        var session = new GameSession(history, memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Replays = new ReplayStore(Path.Combine(_dir, "replays")),
            CombatCaptures = new CombatCaptureStore(captures),
        };
        session.OnSessionStarted("Hearthstone_2026_10_23_12_00_00", new DateTime(2026, 10, 23, 12, 0, 0), attachedMidSession: false);
        // The lobby names the opponent of each combat: without it the tracker records no fight.
        string[] game = [.. Setup, Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4), .. Game5th().Skip(Setup.Length)];
        session.OnLines("Power.log", game.Select(b => "D 12:30:00.0000000 GameState." + b).ToList(), catchingUp: false);
        now = now.AddSeconds(5);
        session.OnTick();
        return (session, history, memory);
    }

    [Fact]
    public async Task A_finished_game_leaves_its_fights_in_the_capture_folder_and_nowhere_else()
    {
        var captures = Path.Combine(_dir, "combat-captures");
        var (session, history, memory) = Session(captures);
        using (memory)
        {
            await session.CombatCapturesSaved.WaitAsync(TimeSpan.FromSeconds(30));
            var record = Assert.Single(history.Recent());
            var store = new CombatCaptureStore(captures);
            var name = Assert.Single(store.Names());
            var saved = store.Load(name)!;

            // One fight per combat of the history row, with the row's results.
            Assert.Equal(CombatCaptureStore.FileNameFor(record.Key), name);
            Assert.Equal(2, saved.Fights.Count);
            Assert.Equal(["Win", "Loss"], saved.Fights.Select(f => f.Result));
            Assert.All(saved.Fights, f => Assert.NotNull(f.Odds));
            Assert.False(saved.Duos);

            // No name, BattleTag or account id of the log is in the file (the fixture's player is Me#1234).
            using var gzip = new GZipStream(File.OpenRead(Path.Combine(captures, name)), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            var text = await reader.ReadToEndAsync();
            Assert.DoesNotContain("Me#1234", text, StringComparison.Ordinal);
            Assert.DoesNotContain("#1234", text, StringComparison.Ordinal);
            Assert.DoesNotContain("144115198130930503", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GameAccountId", text, StringComparison.OrdinalIgnoreCase);

            // The replay of the game is as it was: the capture went into its own folder, not into the replay.
            Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "replays"), "*" + CombatCaptureStore.Extension));
            Assert.DoesNotContain("TAG_SCRIPT_DATA", session.Replays.Load(record.Replay) ?? string.Empty, StringComparison.Ordinal);
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task A_capture_folder_that_cannot_be_written_does_not_cost_the_game_its_history_row()
    {
        Directory.CreateDirectory(_dir);
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "x");
        var (session, history, memory) = Session(blocked);
        using (memory)
        {
            await session.CombatCapturesSaved.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(session.CombatCapturesSaved.IsCompletedSuccessfully);
            Assert.Equal(5, Assert.Single(history.Recent()).Placement);
            SqliteConnection.ClearAllPools();
        }
    }

    // ------------------------------------------------------------------------------------- nothing sends the file

    /// <summary>The words that reach the capture file from code: its store, its records, its folder.</summary>
    private static readonly string[] CaptureWords = ["CombatCaptureStore", "SavedCaptures", "SavedFight", "CombatCaptures", "combat-captures", "CapturedFight"];

    /// <summary>
    /// The only source files that may know the capture file: the store, who fills it, and its path. And the journal's
    /// allowlisted copy of the same fights in memory (<c>bg_combat</c>, docs/source-capture.md; owner, 2026-11-19), which
    /// reads the tracker's fights, never the file, and holds no network type (checked below).
    /// </summary>
    private static readonly string[] Allowed =
    [
        "AppPaths.cs", "CombatCaptureStore.cs", "GameSession.Captures.cs", "GameSession.Recording.cs",
        "BattlegroundsTracker.cs", "BattlegroundsTracker.Combat.cs", "BattlegroundsTracker.Entities.cs", "BattlegroundsTracker.Types.cs",
        "SourceCaptureCombat.cs",
        "BattlegroundsTracker.Transcript.cs",
    ];

    [Fact]
    public void No_source_file_but_the_store_and_the_tracker_knows_the_capture_file()
    {
        var knowing = RepoFiles.Sources(".cs", ".xaml", ".axaml")
            .Where(f => CaptureWords.Any(w => File.ReadAllText(f).Contains(w, StringComparison.Ordinal)))
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToList();

        // A new name here is a new reader of the file: uploads, shares, crash reports and the pool must stay off this list.
        Assert.Equal(Allowed.Order(StringComparer.Ordinal), knowing);
    }

    [Fact]
    public void No_source_file_that_sends_anything_can_reach_the_capture_file()
    {
        // Every file that posts or puts over HTTP, or builds an upload, a share or a crash report.
        var senders = RepoFiles.Sources(".cs")
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("PostAsync", StringComparison.Ordinal) || text.Contains("PutAsync", StringComparison.Ordinal)
                    || text.Contains("HttpMethod.Post", StringComparison.Ordinal) || text.Contains("HttpMethod.Put", StringComparison.Ordinal)
                    || text.Contains("SendAsync", StringComparison.Ordinal) || text.Contains("MultipartFormDataContent", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(senders.Count >= 5, $"{senders.Count} sending files found");
        Assert.All(senders, f => Assert.DoesNotContain(Path.GetFileName(f), Allowed));
        // And the capture's own code holds no network type at all.
        foreach (var name in new[] { "CombatCaptureStore.cs", "GameSession.Captures.cs", "SourceCaptureCombat.cs" })
        {
            var text = File.ReadAllText(RepoFiles.Sources(".cs").Single(f => Path.GetFileName(f) == name));
            Assert.DoesNotContain("Http", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Net", text, StringComparison.Ordinal);
        }

        // The journal's copy knows the fights in memory only: not the store, its records, its folder or its path.
        var journal = File.ReadAllText(RepoFiles.Sources(".cs").Single(f => Path.GetFileName(f) == "SourceCaptureCombat.cs"));
        Assert.All(new[] { "CombatCaptureStore", "SavedCaptures", "SavedFight", "CombatCaptures", "combat-captures", "AppPaths" },
            word => Assert.DoesNotContain(word, journal, StringComparison.Ordinal));
    }

    [Fact]
    public void The_capture_folder_is_none_of_the_folders_that_are_read_for_sending()
    {
        var captures = Path.GetFullPath(AppPaths.CombatCaptures);

        Assert.Equal(Path.GetFullPath(AppPaths.Root), Path.GetDirectoryName(captures));
        foreach (var sent in new[] { AppPaths.Replays, AppPaths.SavedReplays, AppPaths.ImportedReplays, AppPaths.ShareTemp, AppPaths.Cache })
        {
            Assert.False(captures.StartsWith(Path.GetFullPath(sent), StringComparison.OrdinalIgnoreCase), sent);
            Assert.False(Path.GetFullPath(sent).StartsWith(captures, StringComparison.OrdinalIgnoreCase), sent);
        }
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthpulse.Bench;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Tests.Sim;
using static Hearthpulse.Tests.SourceCaptureCombatTests;
using CombatEvent = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEvent;
using CombatEventKind = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEventKind;
using CombatTranscript = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatTranscript;
using MinionState = Hearthpulse.Core.Tracking.BattlegroundsTracker.MinionState;

namespace Hearthpulse.Tests;

/// <summary>
/// The transcript of a <c>bg_combat</c> record (TASK-2026-11-19 U2, docs/source-capture.md "Battlegrounds fights"): only
/// the steps of the boards leave, never a hand card, its stats or its effects; the limits; the same rules in the decoder
/// as in the pool (the shared fixture); and a real fight that goes app → pool export → Bench replays as it did locally.
/// </summary>
public sealed class SourceCaptureCombatTranscriptTests : IDisposable
{
    private readonly string _dir = TestDataFolder.Scratch("hp-source-transcript");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private static CombatEvent E(CombatEventKind kind, int entity, int other = 0, int value = 0, string? card = null, string? tag = null, bool opponent = false) =>
        new(kind, entity) { Other = other, Value = value, Card = card, Tag = tag, Opponent = opponent };

    private static MinionState S(int entity, string card, int attack, int health) => new(entity, card, attack, health, Keyword.None);

    /// <summary>
    /// A transcript of <see cref="SourceCaptureCombatTests.Fight"/> (log ids) with what must stay home among the steps: a
    /// card into the hand, a hand minion's stats, a hand card's triggered effect, a minion no board holds.
    /// </summary>
    internal static CombatTranscript Transcript() => new()
    {
        Events =
        [
            E(CombatEventKind.Attack, 9050, other: 5003),
            E(CombatEventKind.Tag, 5003, tag: "DIVINE_SHIELD"),
            E(CombatEventKind.Damage, 9050, value: 3),
            E(CombatEventKind.Trigger, 8000, other: 5003, card: "BG28_503e"),
            E(CombatEventKind.End, 0),
            E(CombatEventKind.Hand, 6100, card: "HAND_DRAWN_CARD"),
            E(CombatEventKind.Tag, 6000, value: 5, tag: "ATK"),
            E(CombatEventKind.Trigger, 6000, card: "LOCAL_HAND_CARD"),
            E(CombatEventKind.Tag, 4100, value: 6, tag: "ATK"),
            E(CombatEventKind.End, 0),
            E(CombatEventKind.Attack, 4100, other: 9050),
            E(CombatEventKind.Damage, 9050, value: 4),
            E(CombatEventKind.Death, 9050),
            E(CombatEventKind.Trigger, 9050, card: "BGS_119", opponent: true),
            E(CombatEventKind.Summon, 9200, value: 1, card: "BGS_119", opponent: true),
            E(CombatEventKind.End, 0),
            E(CombatEventKind.Trigger, 300, card: "TB_BaconShop_HP_001"),
            E(CombatEventKind.Tag, 4100, value: 7, tag: "HEALTH"),
            E(CombatEventKind.End, 0),
            E(CombatEventKind.Tag, 7777, value: 1, tag: "ATK"),
        ],
        Boards =
        [
            new(0, [S(5003, "BG_CFM_315", 3, 2), S(4100, "BG26_800_G", 4, 6)], [S(9050, "BGS_119", 5, 5)]),
            new(10, [S(5003, "BG_CFM_315", 3, 2), S(4100, "BG26_800_G", 6, 6)], [S(9050, "BGS_119", 5, 2)]),
            new(20, [S(5003, "BG_CFM_315", 3, 2), S(4100, "BG26_800_G", 6, 7)], [S(9200, "BGS_119", 1, 1)]),
        ],
        Complete = true,
        Ending = "hero",
    };

    private static JsonElement Steps(SourceCombat fight) => JsonDocument.Parse(Wire(fight)).RootElement.GetProperty("transcript").Clone();

    [Fact]
    public void Only_the_steps_of_the_boards_leave_never_a_hand_card_its_stats_or_its_effects()
    {
        var fight = Project(Fight(), transcript: Transcript());
        var transcript = Steps(fight);
        var text = transcript.GetRawText();

        // 300 (hero power), 4000, 4100, 5001, 5003, 8000 (enchantment), 9050, 9200 -> 1..8; the hand cards 6000/6100 and 7777
        // (no board) get no reference.
        Assert.Equal(
            """[["attack",7,5,0],["tag",5,"DIVINE_SHIELD",0],["damage",7,3],["trigger",6,5,"BG28_503e",false],["end"],["tag",3,"ATK",6],["attack",3,7,0],["damage",7,4],["death",7],["trigger",7,0,"BGS_119",true],["summon",8,"BGS_119",1,true,false],["end"],["trigger",1,0,"TB_BaconShop_HP_001",false],["tag",3,"HEALTH",7],["end"]]""",
            transcript.GetProperty("events").GetRawText());
        // Boards stand where they stood among the steps kept: before the second attack, at the end.
        Assert.Equal([0, 6, 15], transcript.GetProperty("boards").EnumerateArray().Select(b => b[0].GetInt32()));
        Assert.Equal("""[8,"BGS_119",1,1,0]""", transcript.GetProperty("boards")[2][2][0].GetRawText());
        Assert.DoesNotContain("HAND", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hand", text, StringComparison.Ordinal);
        Assert.Equal((1, true, "hero"), (transcript.GetProperty("version").GetInt32(), transcript.GetProperty("complete").GetBoolean(),
            transcript.GetProperty("ending").GetString()));
    }

    [Fact]
    public void A_transcript_outside_the_limits_or_not_understood_is_left_out_and_the_fight_still_goes()
    {
        var steps = Transcript();
        var tooMany = steps with { Events = [.. Enumerable.Repeat(E(CombatEventKind.Damage, 5003, value: 1), SourceCaptureCombat.MaxTranscriptSteps + 1)], Boards = [] };
        var tooLong = steps with
        {
            Events = [.. Enumerable.Range(0, 200).Select(i => E(CombatEventKind.Summon, 20_000 + i, value: 1, card: "BG_" + new string('X', 77)))],
            Boards = [],
        };
        var noBoard = steps with { Events = [E(CombatEventKind.Attack, 6000, other: 5003)] };
        var stranger = steps with { Boards = [new(0, [S(6000, "LOCAL_HAND_CARD", 1, 1)], [])] };
        var version = steps with { Version = 2 };
        foreach (var transcript in new[] { tooMany, tooLong, noBoard, stranger, version })
        {
            var fight = Project(Fight(), transcript: transcript);
            Assert.Null(fight.Transcript);
            Assert.Equal(Wire(Project(Fight())), Wire(fight));
        }

        // Exactly at the step limit it goes.
        var full = steps with { Events = [.. Enumerable.Repeat(E(CombatEventKind.Damage, 5003, value: 1), SourceCaptureCombat.MaxTranscriptSteps)], Boards = [] };
        Assert.Equal(SourceCaptureCombat.MaxTranscriptSteps, Steps(Project(Fight(), transcript: full)).GetProperty("events").GetArrayLength());
    }

    [Fact]
    public void The_decoder_refuses_a_hand_step_an_entity_no_board_holds_or_anything_outside_the_allowlist()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root, "server", "pool", "test", "bg-combat.fixture.json")))!;
        var bad = new Action<JsonNode>[]
        {
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("hand", 1, "BG_CFM_315")),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("tag", 99, "ATK", 1)),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("tag", 3, "COST", 1)),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("damage", 2, 1)),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("trigger", 99, 0, "LOCAL_HAND_CARD", false)),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("trigger", 99, 0, "TB_BaconShop_HP_001", true)),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("end")),
            d => d["transcript"]!["events"]!.AsArray().Add(new JsonArray("attack", 3, 4, 0, "extra")),
            d => d["transcript"]!["events"]![0]![2] = 99,
            d => d["transcript"]!["boards"]![2]![0] = 1,
            d => d["transcript"]!["boards"]![0]![1]!.AsArray().Add(new JsonArray(99, null, 1, 1, 0)),
            d => d["transcript"]!["boards"]![0]![2]!.AsArray().Add(new JsonArray(1, null, 1, 1, 0)),
            d => d["transcript"]!["version"] = 2,
            d => d["transcript"]!["ending"] = "other",
            d => d["transcript"]!["hand"] = new JsonArray(),
            d => d["theirs"]!["playerEnchantments"]!.AsArray().Add(JsonNode.Parse(d["ours"]!["playerEnchantments"]![0]!.ToJsonString())),
            d => d["ours"]!["playerEnchantments"]![0]!["name"] = "x",
            d => d["ours"]!["minions"]![0]!["enchantmentValues"]![0]!["creatorEntity"] = SourceCaptureCombat.MaxRef + 1,
            d => d["ours"]!["lobbyTribes"] = 4096,
            // The order the sides entered play, never a lobby number, an account id or the player's secrets.
            d => d["oursEnteredFirst"] = 1,
            d => d["playerId"] = 3,
            d => d["ours"]!["playerId"] = 3,
            d => d["theirs"]!["gameAccountId"] = JsonNode.Parse("{\"hi\":144115198130930503,\"lo\":12345678}"),
            d => d["theirs"]!["hi"] = 1,
            d => d["ours"]!["lo"] = 12345678,
            d => d["ours"]!["secrets"] = new JsonArray("LOCAL_SECRET_CARD"),
            d => d["ours"]!["minions"]![0]!["friendlyDeaths"] = -1,
            d => d["ours"]!["deaths"]![0]!["remembered"] = JsonNode.Parse(d["ours"]!["deaths"]!.ToJsonString()),
            d => d["ours"]!["cardTags"]!["BG30_MagicItem_301"]!["COST"] = 1,
            d => d["ours"]!["deaths"] = new JsonArray([.. Enumerable.Range(0, SourceCaptureCombat.MaxDeaths + 1).Select(_ => JsonNode.Parse(d["ours"]!["deaths"]![0]!.ToJsonString()))]),
        };
        using var good = JsonDocument.Parse(fixture.ToJsonString());
        SourceCaptureCombat.Validate(good.RootElement);
        foreach (var change in bad)
        {
            var copy = JsonNode.Parse(fixture.ToJsonString())!;
            change(copy);
            using var json = JsonDocument.Parse(copy.ToJsonString());
            Assert.Throws<SourceCaptureException>(() => SourceCaptureCombat.Validate(json.RootElement));
        }
    }

    // ------------------------------------------------------------------------- a real fight through the pool

    private static readonly Lazy<CardDb> FixtureCards = new(() =>
    {
        using var gz = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BattlegroundsGame", "cards.json.gz")),
            CompressionMode.Decompress);
        return CardDb.Parse("ruRU", gz);
    });

    [Fact]
    public void A_real_fight_goes_through_the_pool_export_and_replays_as_it_did_at_home()
    {
        Directory.CreateDirectory(_dir);
        var cards = FixtureCards.Value;
        var home = Assert.Single(SimReplayKit.Fights(BattlegroundsFixtureTests.Lines(), new DateOnly(2026, 10, 23), cards)).Fight;
        var local = SimReplay.Replay("home", home, cards)!;
        Assert.True(local.Clean);

        // What the app would send of it, as the pool's owner export writes it.
        var (capture, options) = home.AsSaved();
        var record = SourceCaptureCombat.Project(0, capture, options, home.Facts,
            new CombatRecord(home.Turn, 0, string.Empty, home.Result, home.DamageTaken, home.DamageDealt, home.Odds), home.Transcript)!;
        Assert.NotNull(record.Transcript);
        var file = Path.Combine(_dir, "fights.jsonl");
        File.WriteAllLines(file, [$$"""{"v":1,"game":"0123456789abcdef","day":20776,"build":1,"mode":"bg","fight":{{Wire(record)}}}"""]);

        var today = DateOnly.FromDateTime(DateTime.UnixEpoch).AddDays(20776);
        var pool = Assert.Single(Assert.Single(PoolFights.Replayable(file, today)).Fights);
        var replayed = SimReplay.Replay(pool.Key, pool.Fight, cards)!;

        Assert.Equal("pool/0123456789abcdef/1", pool.Key);
        Assert.True(replayed.Clean, replayed.First?.ToString());
        Assert.Equal((local.LogAttacks, local.Attacks, local.Ending, local.Result), (replayed.LogAttacks, replayed.Attacks, replayed.Ending, replayed.Result));
        // The same fight scores in simcover too.
        Assert.NotNull(SimSample.Score(Assert.Single(Assert.Single(PoolFights.Read(file, cards, today)).Fights), SimBudget.Quick));
    }
}

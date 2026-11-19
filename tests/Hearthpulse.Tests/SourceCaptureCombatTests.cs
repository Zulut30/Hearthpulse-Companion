using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hearthpulse.Bench;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;
using Hearthpulse.Core.Tracking;
using Microsoft.Data.Sqlite;
using static Hearthpulse.Tests.BattlegroundsReplayTests;
using Facts = System.Collections.Generic.IReadOnlyList<Hearthpulse.Core.Tracking.BattlegroundsTracker.MinionFacts>;

namespace Hearthpulse.Tests;

/// <summary>
/// Source v3 <c>bg_combat</c> (TASK-2026-11-19 U, docs/source-capture.md "Battlegrounds fights"): what of a fight leaves the
/// machine, that the pool's rules are the app's (the shared fixture), that it is written only at the end of a game, and
/// that the decoder reads it. Synthetic fights and the synthetic game of <see cref="BattlegroundsReplayTests"/> only.
/// </summary>
public sealed class SourceCaptureCombatTests : IDisposable
{
    private readonly string _dir = TestDataFolder.Scratch("hp-source-combat");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private static SimMinion M(int entity, string card, int attack, int health, Keyword keywords = Keyword.None) =>
        SimMinion.Create(card, attack, health, Tribe.Beast, keywords) with { EntityId = entity, TechLevel = 1 };

    /// <summary>A fight with everything that must not leave: both hands, a rating and an id among the player tags, card values that are no input, the opponent's card values.</summary>
    private static (BattlegroundsTracker.CombatCapture Capture, SimOptions Options, Facts Facts, CombatRecord Combat) Fight(int minions = 2, string result = "Win", double win = 0.61234)
    {
        SimMinion[] board =
        [
            M(5003, "BG_CFM_315", 3, 2, Keyword.Taunt | Keyword.DivineShield) with { Enchantments = ["BG28_503e"], ScriptData1 = 7, Remembered = [M(5001, "BG_CFM_315", 2, 1)] },
            M(4100, "BG26_800", 4, 6, Keyword.Windfury) with { Golden = true, CreatorCardId = "TB_BaconShop_HP_001", Mechanics = CardMechanic.Deathrattle },
            .. Enumerable.Range(0, Math.Max(0, minions - 2)).Select(i => M(7000 + i, "BG_CFM_315", 1, 1)),
        ];
        var ours = new SimSide(board)
        {
            HeroCardId = "TB_BaconShop_HERO_01",
            HeroPowerCardId = "TB_BaconShop_HP_001",
            Extras = ["TB_BaconShop_HP_001", "BG30_MagicItem_301"],
            Hand = [M(6000, "LOCAL_HAND_CARD", 1, 1)],
            Tags = new Dictionary<string, int>(StringComparer.Ordinal) { ["BACON_BLOODGEMBUFFATKVALUE"] = 2, ["PLAYER_ID"] = 3, ["BACON_RATING"] = 7000, ["4914"] = 1 },
            ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
            {
                ["BG30_MagicItem_301"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["TAG_SCRIPT_DATA_NUM_1"] = 2, ["COST"] = 3 },
            },
            TavernTier = 2,
            Health = 30,
            Turn = 3,
        };
        var theirs = new SimSide([M(9050, "BGS_119", 5, 5, Keyword.Stealth | Keyword.Reborn)])
        {
            HeroCardId = "TB_BaconShop_HERO_02",
            Hand = [M(9100, "OPPONENT_HAND_CARD", 9, 9)],
            Tags = new Dictionary<string, int>(StringComparer.Ordinal) { ["BACON_BLOODGEMBUFFHEALTHVALUE"] = 3, ["GAME_ACCOUNT_ID"] = 123456 },
            ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
            {
                ["OPPONENT_TRINKET"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["TAG_SCRIPT_DATA_NUM_1"] = 4 },
            },
            TavernTier = 3,
            Health = 25,
            Turn = 3,
        };
        var capture = new BattlegroundsTracker.CombatCapture(ours, theirs, true, 9050);
        var options = BattlegroundsTracker.DefaultSimOptions with { FirstAttackerEntityId = 9050, DamageCap = 15 };
        Facts facts = [new(4100, "BG26_800_G", ["DEATHRATTLE"]), new(5003, "BG_CFM_315", []), new(9050, "BGS_119", ["AVENGE"])];
        var combat = new CombatRecord(3, 4, "TB_BaconShop_HERO_02", result, 0, 5, new CombatOdds(win, 0.1, 1 - win - 0.1, 1) { Runs = 4000, LethalDealt = 0 });
        return (capture, options, facts, combat);
    }

    private static SourceCombat Project((BattlegroundsTracker.CombatCapture Capture, SimOptions Options, Facts Facts, CombatRecord Combat) f, int index = 2) =>
        SourceCaptureCombat.Project(index, f.Capture, f.Options, f.Facts, f.Combat)!;

    private static string Wire(SourceCombat fight) => JsonSerializer.Serialize(fight, SourceCaptureRecord.Json);

    private static readonly string Fixture = File.ReadAllText(Path.Combine(RepoFiles.Root, "server", "pool", "test", "bg-combat.fixture.json"));

    /// <summary>The field names of a record at every depth; the values of tag maps are data, not fields.</summary>
    private static SortedSet<string> Shape(JsonElement value, string at = "")
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (value.ValueKind == JsonValueKind.Object && !at.EndsWith(".tags", StringComparison.Ordinal) && !at.EndsWith(".cardTags", StringComparison.Ordinal))
        {
            foreach (var p in value.EnumerateObject())
            {
                paths.Add($"{at}.{p.Name}");
                paths.UnionWith(Shape(p.Value, $"{at}.{p.Name}"));
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                paths.UnionWith(Shape(item, at + "[]"));
            }
        }

        return paths;
    }

    [Fact]
    public void What_the_app_writes_is_what_the_pool_accepts()
    {
        using var fixture = JsonDocument.Parse(Fixture);
        SourceCaptureCombat.Validate(fixture.RootElement);
        Assert.NotNull(SourceCaptureCombat.Read(fixture.RootElement));

        using var written = JsonDocument.Parse(Wire(Project(Fight())));
        // The pool's tests read the fixture with the pool's rules: the same fields at every depth as the app writes.
        Assert.Equal(Shape(fixture.RootElement), Shape(written.RootElement));
        SourceCaptureCombat.Validate(written.RootElement);
    }

    [Fact]
    public void Only_the_allowlist_leaves_never_a_hand_a_rating_an_id_or_the_opponent_s_card_values()
    {
        var text = Wire(Project(Fight()));
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;

        foreach (var absent in new[] { "LOCAL_HAND_CARD", "OPPONENT_HAND_CARD", "hand", "PLAYER_ID", "BACON_RATING", "GAME_ACCOUNT_ID", "COST", "OPPONENT_TRINKET", "123456", "7000" })
        {
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
        }

        Assert.Equal("""{"4914":1,"BACON_BLOODGEMBUFFATKVALUE":2}""", root.GetProperty("ours").GetProperty("tags").GetRawText());
        Assert.Equal("""{"BG30_MagicItem_301":{"TAG_SCRIPT_DATA_NUM_1":2}}""", root.GetProperty("ours").GetProperty("cardTags").GetRawText());
        Assert.Equal("""{"BACON_BLOODGEMBUFFHEALTHVALUE":3}""", root.GetProperty("theirs").GetProperty("tags").GetRawText());
        Assert.Equal("{}", root.GetProperty("theirs").GetProperty("cardTags").GetRawText());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("transcript").ValueKind);
        Assert.Equal("W", root.GetProperty("result").GetString());
        Assert.Equal(0.6123, root.GetProperty("odds").GetProperty("win").GetDouble());
    }

    [Fact]
    public void Power_log_entity_ids_become_fight_references_in_the_same_order()
    {
        var fight = Project(Fight());

        // 4100, 5001, 5003, 9050 -> 1, 2, 3, 4: the simulator only compares them, and no id of the game remains.
        Assert.Equal([3, 1], fight.Ours.Minions.Select(m => m.Entity));
        Assert.Equal(2, Assert.Single(fight.Ours.Minions[0].Remembered).Entity);
        Assert.Equal(4, Assert.Single(fight.Theirs.Minions).Entity);
        Assert.Equal(4, fight.FirstAttacker);
        Assert.Equal([1, 3, 4], fight.Facts.Select(f => f.Entity));
        Assert.Equal(["DEATHRATTLE"], fight.Facts[0].Markers);
    }

    [Fact]
    public void A_record_reads_back_as_the_same_simulator_input_and_gives_the_same_odds()
    {
        var original = Fight();
        var wire = Wire(Project(original));
        using var json = JsonDocument.Parse(wire);
        var input = SourceCaptureCombat.Read(json.RootElement)!;

        Assert.Empty(input.Capture.Ours.Hand);
        Assert.Empty(input.Capture.Theirs.Hand);
        Assert.Equal(("Win", 5, 3, 15, true), (input.Result, input.DamageDealt, input.Turn, input.DamageCap, input.CountDamage));
        var first = input.Capture.Ours.Minions[0];
        Assert.True(first is { Taunt: true, DivineShield: true, Stealth: false, ScriptData1: 7 });
        Assert.Equal(["BG28_503e"], first.Enchantments);
        Assert.Equal(2, input.Capture.Ours.Minions[1].AttacksPerTurn);
        Assert.True(input.Capture.Theirs.Minions[0] is { Stealth: true, Reborn: true });
        Assert.Equal(4000, input.Odds!.Runs);

        // Projected again it is the same record: the reading loses nothing the record holds.
        var options = BattlegroundsTracker.DefaultSimOptions with { FirstAttackerEntityId = input.Capture.FirstAttackerEntityId, DamageCap = input.DamageCap, CountDamage = input.CountDamage };
        var again = SourceCaptureCombat.Project(2, input.Capture, options, input.Facts,
            new CombatRecord(input.Turn, 0, string.Empty, input.Result, input.DamageTaken, input.DamageDealt, input.Odds));
        Assert.Equal(wire, Wire(again!));

        // The simulator gives the same odds for the record as for the fight without what the record leaves out.
        static SimSide Kept(SimSide side, bool opponent) => side with
        {
            Hand = [],
            Tags = side.Tags.Where(t => SourceCaptureCombat.PlayerTags.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value),
            ExtraTags = opponent ? SimSide.EmptyExtraTags : side.ExtraTags.ToDictionary(t => t.Key,
                t => (IReadOnlyDictionary<string, int>)t.Value.Where(v => SourceCaptureCombat.CardTags.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value)),
        };
        var fixedRuns = original.Options with { Runs = 300, MinRuns = 300, TimeBudget = null };
        var before = CombatSimulator.Run(Kept(original.Capture.Ours, false), Kept(original.Capture.Theirs, true), original.Capture.OursFirst, fixedRuns);
        var after = CombatSimulator.Run(input.Capture.Ours, input.Capture.Theirs, input.Capture.OursFirst, options with { Runs = 300, MinRuns = 300, TimeBudget = null });
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    }

    [Fact]
    public void A_fight_outside_the_rules_is_left_out_whole_and_the_others_still_go()
    {
        var eight = Fight(minions: 8);
        Assert.Null(SourceCaptureCombat.Project(0, eight.Capture, eight.Options, eight.Facts, eight.Combat));
        var nan = Fight(win: double.NaN);
        Assert.Null(SourceCaptureCombat.Project(0, nan.Capture, nan.Options, nan.Facts, nan.Combat));
        var unknown = Fight(result: string.Empty);
        Assert.Null(SourceCaptureCombat.Project(0, unknown.Capture, unknown.Options, unknown.Facts, unknown.Combat));
        var ok = Fight();
        Assert.Null(SourceCaptureCombat.Project(SourceCaptureCombat.MaxFights, ok.Capture, ok.Options, ok.Facts, ok.Combat));
        var enchanted = ok.Capture with
        {
            Ours = ok.Capture.Ours with { Minions = [ok.Capture.Ours.Minions[0] with { Enchantments = Enumerable.Repeat("BG28_503e", SourceCaptureCombat.MaxEnchantments + 1).ToArray() }] },
        };
        Assert.Null(SourceCaptureCombat.Project(0, enchanted, ok.Options, [], ok.Combat));
        Assert.Null(SourceCaptureCombat.Project(0, ok.Capture, ok.Options, [new(4100, string.Empty, [])], ok.Combat));
        Assert.Null(SourceCaptureCombat.Project(0, ok.Capture, ok.Options, [new(4100, "BG26_800", ["TAUNT"])], ok.Combat));

        // Combat 0 twice, combat 1 too big, combat 2 fine, combat 5 never ended: 0 and 2 go.
        BattlegroundsTracker.CapturedFight Captured(int index, (BattlegroundsTracker.CombatCapture Capture, SimOptions Options, Facts Facts, CombatRecord Combat) f) =>
            new(index, f.Capture, f.Options, f.Facts);
        var combats = new[] { ok.Combat, ok.Combat, ok.Combat with { Result = "Loss", DamageTaken = 7, DamageDealt = 0 } };
        var written = SourceCaptureCombat.FromGame([Captured(0, ok), Captured(0, ok), Captured(1, eight), Captured(2, ok), Captured(5, ok)], combats);
        Assert.Equal([0, 2], written.Select(f => f.Fight));
        Assert.Equal(["W", "L"], written.Select(f => f.Result));
    }

    [Fact]
    public void The_wire_lists_are_the_ones_the_simulator_reads()
    {
        // A new tribe, keyword or mechanic bit, marker, card value or player counter needs this record, the decoder and
        // the pool in the same change; until then a fight that has it would be left out.
        Assert.All(Enum.GetValues<Tribe>(), t => Assert.Equal(0, (int)t & ~SourceCaptureCombat.TribeMask));
        Assert.All(Enum.GetValues<Keyword>(), k => Assert.Equal(0, (int)k & ~SourceCaptureCombat.BaseKeywordMask));
        Assert.All(Enum.GetValues<CardMechanic>(), m => Assert.Equal(0, (int)m & ~SourceCaptureCombat.MechanicMask));
        Assert.Equal(CombatRelevance.EffectMarkers(_ => true), SourceCaptureCombat.Markers);
        Assert.Equal(CombatEffect.CapturedTags.Order(StringComparer.Ordinal), SourceCaptureCombat.CardTags.Order(StringComparer.Ordinal));
        var stored = typeof(BattlegroundsTracker).Assembly.GetType("Hearthpulse.Core.Tracking.CombatCaptureStore")!
            .GetField("CounterTags", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) as IEnumerable<string>;
        Assert.Equal(stored!.Order(StringComparer.Ordinal), SourceCaptureCombat.PlayerTags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_recorder_writes_fights_only_when_a_battlegrounds_game_ends_just_before_its_end()
    {
        foreach (var (gameType, expected) in new[] { ("GT_BATTLEGROUNDS", 1), ("GT_BATTLEGROUNDS_DUO", 1), ("GT_RANKED", 0) })
        {
            var recorder = new SourceCaptureRecorder();
            var frames = new List<SourceCaptureFrame>();
            var asked = 0;
            recorder.Recorded += frames.Add;
            recorder.Combats = () => { asked++; return [Project(Fight(), index: 0)]; };
            var game = SourceCaptureRecorderTests.Game(gameType);
            recorder.SetEnabled(true);
            recorder.OnGameStarted(game);
            recorder.OnEntityCreated(game, SourceCaptureRecorderTests.Entity(game, 49, "LOCAL_SEED", "HAND", 1, "MINION"));
            Assert.Equal(0, asked);
            Assert.DoesNotContain(frames, f => f.Record.Kind == "bg_combat");

            recorder.OnGameEnded(game);

            Assert.Equal(expected, asked);
            Assert.Equal(expected, frames.Count(f => f.Record.Kind == "bg_combat"));
            Assert.Equal("end", frames[^1].Record.Kind);
            if (expected > 0)
            {
                var combat = frames[^2];
                Assert.Equal(("bg_combat", "none", 0, 0, (string?)null), (combat.Record.Kind, combat.Record.Side, combat.Record.Entity, combat.Record.Target, combat.Record.Card));
            }

            Assert.All(frames, f => Assert.Equal(SourceCaptureRecord.CurrentVersion, f.Record.V));
        }
    }

    [Fact]
    public async Task A_finished_game_journals_its_fights_once_and_the_decoder_reads_them()
    {
        Directory.CreateDirectory(_dir);
        var frames = new List<SourceCaptureFrame>();
        using var memory = new HearthstoneMemory();
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        var now = new DateTime(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);
        var session = new GameSession(history, memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Replays = new ReplayStore(Path.Combine(_dir, "replays")),
            CombatCaptures = null,
        };
        session.SourceCaptureRecorded += frames.Add;
        session.SetSourceCaptureEnabled(true);
        session.OnSessionStarted("Hearthstone_2026_10_23_12_00_00", new DateTime(2026, 10, 23, 12, 0, 0), attachedMidSession: false);
        // The lobby names the opponent of each combat: without it the tracker records no fight.
        string[] game = [.. Setup, Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4), .. Game5th().Skip(Setup.Length)];
        session.OnLines("Power.log", game.Select(b => "D 12:30:00.0000000 GameState." + b).ToList(), catchingUp: false);
        now = now.AddSeconds(5);
        session.OnTick();
        SqliteConnection.ClearAllPools();

        var capture = frames.GroupBy(f => f.CaptureId).Single(g => g.Any(f => f.Record.Kind == "bg_combat")).ToArray();
        var fights = capture.Where(f => f.Record.Kind == "bg_combat").ToArray();
        Assert.Equal(2, fights.Length);
        Assert.Equal(["bg_combat", "bg_combat", "end"], capture[^3..].Select(f => f.Record.Kind));
        var results = fights.Select(f => JsonSerializer.SerializeToElement(f.Record.Data, SourceCaptureRecord.Json)).ToArray();
        Assert.Equal(["W", "L"], results.Select(r => r.GetProperty("result").GetString()));
        Assert.Equal([0, 1], results.Select(r => r.GetProperty("fight").GetInt32()));
        Assert.All(results, r => Assert.NotEqual(JsonValueKind.Null, r.GetProperty("odds").ValueKind));
        var source = string.Join('\n', capture.Select(f => f.Record.ToJson())) + "\n";
        Assert.DoesNotContain("Me#1234", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#1234", source, StringComparison.Ordinal);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        var result = await new SourceCaptureParser().ParseAsync(stream);
        Assert.Equal(3, result.SourceVersion);
        var events = result.Events.Where(e => e.Kind == "bg_combat").ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, e => Assert.Equal("{}", e.Data.GetRawText()));
        Assert.Equal(capture.Length, result.Records);
    }

    [Fact]
    public async Task The_decoder_reads_v3_and_refuses_a_fight_in_v2_outside_battlegrounds_or_out_of_order()
    {
        var fight = Wire(Project(Fight(), index: 0));
        string Line(int v, int seq, string kind, string data, int turn = 0) =>
            $$"""{"v":{{v}},"seq":{{seq}},"kind":"{{kind}}","turn":{{turn}},"side":"none","entity":0,"card":null,"target":0,"data":{{data}}}""";
        string Start(int v, string mode) => Line(v, 0, "start", $$"""{"captureKind":"game","mode":"{{mode}}","day":20776,"build":1,"format":null,"arena":null}""");
        string Gap(int v) => Line(v, 1, "gap", """{"reason":"combat_view","count":1}""");
        string End(int v, int seq) => Line(v, seq, "end", """{"status":"incomplete","result":"L","placement":4,"reason":"normal"}""", turn: 9);
        string Combat(int v, int seq, int index) => Line(v, seq, "bg_combat", fight.Replace("\"fight\":0", $"\"fight\":{index}", StringComparison.Ordinal), turn: 9);
        async Task<SourceCaptureResult> Parse(params string[] lines)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
            return await new SourceCaptureParser().ParseAsync(stream);
        }

        var ok = await Parse(Start(3, "bg"), Gap(3), Combat(3, 2, 0), Combat(3, 3, 4), End(3, 4));
        Assert.Equal((3, 0, 1), (ok.SourceVersion, ok.Unsupported, ok.Gaps));
        Assert.True(ok.Eligibility.Outcome);
        Assert.Equal(4, ok.Facts.Outcome!.Placement);

        // A constructed v3 game is a v2 game.
        var ranked = await Parse(Line(3, 0, "start", """{"captureKind":"game","mode":"ranked","day":20776,"build":1,"format":"standard","arena":null}"""),
            Line(3, 1, "end", """{"status":"complete","result":"W","placement":null,"reason":"normal"}"""));
        Assert.Equal(("parsed", 3), (ranked.Status, ranked.SourceVersion));

        foreach (var lines in new[]
        {
            new[] { Start(2, "bg"), Gap(2), Combat(2, 2, 0), End(2, 3) },
            [Line(3, 0, "start", """{"captureKind":"game","mode":"ranked","day":20776,"build":1,"format":"standard","arena":null}"""), Combat(3, 1, 0)],
            [Start(3, "bg"), Gap(3), Combat(3, 2, 3), Combat(3, 3, 3), End(3, 4)],
            [Start(3, "bg"), Gap(3), Combat(3, 2, 3), Combat(3, 3, 1), End(3, 4)],
            [Start(3, "bg"), Gap(3), Combat(3, 2, 0).Replace("\"transcript\":null", "\"transcript\":{\"v\":1,\"steps\":[]}", StringComparison.Ordinal), End(3, 3)],
            [Start(3, "bg"), Gap(3), Combat(3, 2, 0).Replace("\"side\":\"none\"", "\"side\":\"local\"", StringComparison.Ordinal), End(3, 3)],
        })
        {
            var error = await Assert.ThrowsAsync<SourceCaptureException>(() => Parse(lines));
            Assert.Equal("invalid_source", error.Code);
        }
    }

    [Fact]
    public void The_bench_reads_the_pool_export_as_fights_to_score()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "fights.jsonl");
        var fight = Wire(Project(Fight(), index: 0));
        string Line(string game, string mode, string data, int day = 20776) =>
            $$"""{"v":1,"game":"{{game}}","day":{{day}},"build":4321,"mode":"{{mode}}","fight":{{data}}}""";
        File.WriteAllLines(file,
        [
            Line("0123456789abcdef", "bg", fight),
            Line("0123456789abcdef", "bg", fight.Replace("\"fight\":0", "\"fight\":1", StringComparison.Ordinal)),
            Line("fedcba9876543210", "duos", fight),
            // Past the pool's 180 days (from 20776 = 2026-11-19): a stale export gives nothing of it.
            Line("1111111111111111", "bg", fight, day: 20776 - PoolFights.MaxAgeDays - 1),
            Line("0123456789abcdef", "bg", fight.Replace("\"fight\":0", "\"fight\":2", StringComparison.Ordinal), day: 20776 - PoolFights.MaxAgeDays),
            // A line the app's rules refuse, a line that is no export line, an empty line: not read.
            Line("0123456789abcdef", "bg", fight.Replace("\"transcript\":null", "\"transcript\":1", StringComparison.Ordinal)),
            """{"v":1,"game":"0123456789abcdef","day":20776,"build":4321,"mode":"ranked","fight":{}}""",
            "not json",
            string.Empty,
        ]);

        var today = DateOnly.FromDateTime(DateTime.UnixEpoch).AddDays(20776);
        var games = PoolFights.Read(file, CardDb.Empty, today);

        Assert.Equal([3, 1], games.Select(g => g.Fights.Count));
        Assert.Empty(PoolFights.Read(file, CardDb.Empty, today.AddDays(PoolFights.MaxAgeDays + 1)));
        Assert.Equal([false, true], games.Select(g => g.Duos));
        Assert.Equal([4321, 4321], games.Select(g => g.Build));
        Assert.Equal(DateTime.UnixEpoch.AddDays(20776), games[0].StartedAt);
        var first = games[0].Fights[0];
        Assert.Equal(("Win", 5, 3), (first.Result, first.DamageDealt, first.Turn));
        Assert.Empty(first.Capture!.Ours.Hand);
        Assert.NotNull(SimSample.Score(first, SimBudget.Quick));
    }

    // --------------------------------------------------------------------------------- a pool without source v3

    /// <summary>A pool from before source v3: every v3 line is an unknown record (400 invalid_record), v2 is taken.</summary>
    private sealed class OldPool(List<(string Route, int Version, string Source)> seen, string refusal = "invalid_record") : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            if (request.RequestUri!.AbsolutePath.EndsWith("chunks", StringComparison.Ordinal))
            {
                using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(root.GetProperty("data").GetString()!)), CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                var source = await reader.ReadToEndAsync(cancellationToken);
                var version = source.Contains("\"v\":3", StringComparison.Ordinal) ? 3 : 2;
                seen.Add(("chunks", version, source));
                return version == 3
                    ? new(HttpStatusCode.BadRequest) { Content = new StringContent($$"""{"ok":false,"error":"{{refusal}}"}""") }
                    : Ok(new { ok = true, stored = true, duplicate = false, index = root.GetProperty("index").GetInt32(), sha256 = root.GetProperty("sha256").GetString() });
            }

            seen.Add(("complete", root.GetProperty("sourceVersion").GetInt32(), string.Empty));
            return Ok(new { ok = true, status = "stored", captureId = root.GetProperty("captureId").GetString() });
        }

        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }

    private static void SpoolBattlegroundsGame(SourceCaptureQueue queue, char id)
    {
        var capture = new string(id, 32);
        SourceCaptureFrame Frame(int seq, string kind, object data, int turn = 0) =>
            new(capture, "game", "bg", 20776, new SourceCaptureRecord(seq, kind, turn, "none", 0, null, 0, data, 3));
        Assert.True(queue.TryAppend(Frame(0, "start", new { captureKind = "game", mode = "bg", day = 20776, build = (int?)4321, format = (string?)null, arena = (string?)null })));
        Assert.True(queue.TryAppend(Frame(1, "gap", new { reason = "combat_view", count = 1 })));
        Assert.True(queue.TryAppend(Frame(2, "tag", new { tag = "TURN", value = 9 }, turn: 9)));
        Assert.True(queue.TryAppend(Frame(3, "bg_combat", Project(Fight(), index: 0), turn: 9)));
        Assert.True(queue.TryAppend(Frame(4, "bg_combat", Project(Fight(), index: 1), turn: 9)));
        Assert.True(queue.TryAppend(Frame(5, "end", new { status = "incomplete", result = "L", placement = (int?)4, reason = "normal" }, turn: 9)));
    }

    [Fact]
    public async Task A_pool_without_source_v3_gets_the_journal_as_v2_without_fights_until_the_app_restarts()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, int Version, string Source)>();
        using var http = new HttpClient(new OldPool(seen));
        await using (var queue = await scratch.Open())
        {
            SpoolBattlegroundsGame(queue, 'a');
            SpoolBattlegroundsGame(queue, 'b');
            await queue.FlushAsync();
            await queue.SendPendingAsync(new(http));

            // One refused v3 attempt, then both journals as v2: nothing waits behind the first one.
            Assert.Single(seen, x => x.Version == 3);
            Assert.Equal(2, seen.Count(x => x is { Route: "complete", Version: 2 }));
            Assert.Equal(0, queue.State.PendingCaptures);
            foreach (var (_, _, source) in seen.Where(x => x is { Route: "chunks", Version: 2 }))
            {
                Assert.DoesNotContain("bg_combat", source, StringComparison.Ordinal);
                var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(4, lines.Length);
                Assert.All(lines.Select((line, i) => (line, i)), x => Assert.StartsWith($"{{\"v\":2,\"seq\":{x.i},", x.line, StringComparison.Ordinal));
                // It is a valid v2 source for the decoder, with the same game in it.
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
                var result = await new SourceCaptureParser().ParseAsync(stream);
                Assert.Equal((2, 4), (result.SourceVersion, result.Facts.Outcome?.Placement ?? 0));
            }
        }

        // After a restart the pool is asked with v3 again: it may have been updated meanwhile.
        await using (var restarted = await scratch.Open())
        {
            SpoolBattlegroundsGame(restarted, 'c');
            await restarted.FlushAsync();
            await restarted.SendPendingAsync(new(http));
            Assert.Equal(2, seen.Count(x => x.Version == 3));
            Assert.Equal(0, restarted.State.PendingCaptures);
        }
    }

    [Fact]
    public async Task Another_refusal_of_a_v3_journal_keeps_it_waiting_as_before()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, int Version, string Source)>();
        using var http = new HttpClient(new OldPool(seen, refusal: "invalid_sequence"));
        await using var queue = await scratch.Open();
        SpoolBattlegroundsGame(queue, 'a');
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        Assert.Equal([3], seen.Select(x => x.Version));
        Assert.Equal(1, queue.State.PendingCaptures);
        Assert.Equal("rejected", queue.State.Status);
        Assert.Contains("bg_combat", await File.ReadAllTextAsync(Directory.GetFiles(scratch.Path, "*.jsonl", SearchOption.AllDirectories).Single()), StringComparison.Ordinal);
    }
}

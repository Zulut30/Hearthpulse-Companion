using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.BattlegroundsReplayTests;

namespace Hearthpulse.Tests;

/// <summary>
/// Source v4 (TASK-2026-11-26-pro-collection P1): a gap is a real loss. A whole game recorded through the live path
/// (GameSession: every Power.log line to the recorder and the parser) decodes as parsed whatever known log grammar the
/// format leaves out, while an unknown line or an undecodable value still leaves it partial; the decoder's v4 rules; the
/// spool's fallback to v3 and v2 for a pool that does not take v4 yet. Synthetic lines in the order real logs have them.
/// </summary>
public sealed class SourceCaptureV4Tests : IDisposable
{
    private const string Spell = "[entityName=X id=99 zone=HAND zonePos=1 cardId=CORE_CS2_004 player=1]";
    private const string OurHero = "[entityName=X id=20 zone=PLAY zonePos=0 cardId=HERO_09 player=1]";
    private const string TheirHero = "[entityName=X id=21 zone=PLAY zonePos=0 cardId=HERO_08 player=2]";
    private const string Effect = "EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0";
    private readonly string _dir = TestDataFolder.Scratch("hp-source-v4");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    // Regression: before v4 each of these left every real game partial (12 244 gaps in the 10 kept games of 24-25.11, 46 %
    // of the records); a mulligan keeping every card left its choice open.
    [Theory]
    [InlineData("none")]
    [InlineData("options")]
    [InlineData("power_list")]
    [InlineData("sent")]
    [InlineData("meta_data")]
    [InlineData("sub_spell")]
    [InlineData("shuffle")]
    [InlineData("dormant")]
    [InlineData("step")]
    [InlineData("attack")]
    [InlineData("trade")]
    [InlineData("location")]
    [InlineData("kept_hand")]
    public async Task A_whole_ranked_game_with_log_lines_the_format_leaves_out_is_parsed(string kind)
    {
        var capture = RecordGame(kind == "kept_hand" ? Ranked(KeptHand(), []) : Ranked([], LeftOut(kind)));

        Assert.DoesNotContain(capture, f => f.Record.Kind == "gap");
        Assert.Equal("complete", Data(capture[^1]).GetProperty("status").GetString());
        var result = await Decode(capture);
        Assert.Equal(("parsed", 0, 0), (result.Status, result.Gaps, result.Unsupported));
        Assert.True(result.Summary.CompleteForAggregation);
        Assert.Equal(new SourceCaptureOutcome("W", null), result.Facts.Outcome);
        Assert.Equal(SourceCaptureRecord.CurrentVersion, result.SourceVersion);
        switch (kind)
        {
            case "step":
                Assert.DoesNotContain(capture, f => f.Record.Kind == "tag" && Data(f).GetProperty("tag").GetString() == "STEP");
                break;
            case "attack":
                Assert.Contains(capture, f => f.Record.Kind == "block_start" && Data(f).GetProperty("block").GetString() == "ATTACK");
                Assert.DoesNotContain(capture, f => f.Record.Kind == "action");
                break;
            case "trade":
                Assert.Contains(capture, f => f.Record.Kind == "block_start" && Data(f).GetProperty("block").GetString() == "OTHER");
                Assert.Contains(capture, f => f.Record.Kind == "tag" && Data(f).GetProperty("tag").GetString() == "RESOURCES_USED");
                break;
            case "location":
                Assert.Contains(capture, f => f.Record.Kind == "block_start" && f.Record.Card == "LOCATION_CARD");
                Assert.DoesNotContain(capture, f => f.Record.Kind == "action");
                break;
            case "kept_hand":
                var choice = Assert.Single(result.Events, e => e.Kind == "choice" && e.Data.GetProperty("resolved").GetBoolean());
                Assert.Equal("mulligan", choice.Data.GetProperty("choiceType").GetString());
                Assert.Empty(choice.Data.GetProperty("picked").EnumerateArray());
                break;
        }
    }

    [Fact]
    public async Task An_unknown_log_line_or_an_undecodable_value_is_still_a_gap()
    {
        foreach (var line in new[]
        {
            "DebugPrintPower() - FUTURE_STRUCTURE Entity=GameEntity",
            "DebugPrintPower() -     META_DATA_FUTURE Info=1",
            $"DebugPrintPower() - TAG_CHANGE Entity={Spell} tag=COST value=ONE ",
        })
        {
            var capture = RecordGame(Ranked([], [line]));
            var gap = Assert.Single(capture, f => f.Record.Kind == "gap");
            Assert.Equal("unsupported", Data(gap).GetProperty("reason").GetString());
            Assert.Equal("incomplete", Data(capture[^1]).GetProperty("status").GetString());
            var result = await Decode(capture);
            Assert.Equal(("partial", 1, 1), (result.Status, result.Gaps, result.Unsupported));
            Assert.False(result.Summary.CompleteForAggregation);
            // The outcome survives a lost observation (ADR 0013).
            Assert.True(result.Eligibility.Outcome);
            Assert.DoesNotContain("FUTURE", string.Join('\n', capture.Select(f => f.Record.ToJson())), StringComparison.Ordinal);
        }
    }

    // Regression: a Battlegrounds game was partial by its combat_view gap alone, and by its phases and attacks.
    [Fact]
    public async Task A_whole_battlegrounds_game_is_parsed_and_fit_for_its_place_and_hero_offer()
    {
        var turn = Array.IndexOf(Setup, Game("TURN", 1));
        string[] lines =
        [
            .. Setup.Take(turn),
            .. Entity(70, "TB_BaconShop_HERO_01", Me, "HAND", "HERO"),
            .. Entity(71, "TB_BaconShop_HERO_02", Me, "HAND", "HERO"),
            .. Offer("MULLIGAN", null, 70, 71),
            "DebugPrintOptions() - id=1",
            "DebugPrintOptions() -   option 0 type=CHOOSE mainEntity= error=NONE errorParam=",
            "SendChoices() - id=1 ChoiceType=MULLIGAN",
            "SendChoices() -   m_chosenEntities[0]=[entityName=X id=71 zone=HAND zonePos=2 cardId=TB_BaconShop_HERO_02 player=1]",
            .. Chosen(71),
            .. Setup.Skip(turn),
            "DebugPrintPowerList() - Count=1",
            "SendOption() - selectedOption=1 selectedSubOption=-1 selectedTarget=51 selectedPosition=0",
            "DebugPrintPower() -     META_DATA - Meta=SLUSH_TIME Data=1000 InfoCount=1",
            "DebugPrintPower() -                 Info[0] = [entityName=X id=10 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_50 player=1]",
            // The lobby names the opponent of each combat, so the tracker records the fights for the journal.
            Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4),
            .. Game5th().Skip(Setup.Length),
        ];
        var capture = RecordGame(lines);

        Assert.DoesNotContain(capture, f => f.Record.Kind == "gap");
        Assert.Equal(2, capture.Count(f => f.Record.Kind == "bg_combat"));
        Assert.Equal("complete", Data(capture[^1]).GetProperty("status").GetString());
        var result = await Decode(capture);
        Assert.Equal(("parsed", 0, 0), (result.Status, result.Gaps, result.Unsupported));
        Assert.True(result.Summary.CompleteForAggregation);
        Assert.True(result.Eligibility.Outcome);
        Assert.True(result.Eligibility.Offers);
        Assert.Empty(result.Eligibility.Reasons);
        Assert.Equal(new SourceCaptureOutcome("L", 5), result.Facts.Outcome);
        var offer = Assert.Single(result.Facts.Offers);
        Assert.Equal(["TB_BaconShop_HERO_01", "TB_BaconShop_HERO_02"], offer.Options);
        Assert.Equal(["TB_BaconShop_HERO_02"], offer.Picked);
    }

    [Fact]
    public async Task The_decoder_counts_no_structural_gap_in_v4_and_keeps_the_v2_v3_rules()
    {
        var clean = await Parse(Start(4, "bg"), Hero(4, 1), End(4, 2, placement: 3));
        Assert.Equal(("parsed", 0, 0, 4), (clean.Status, clean.Gaps, clean.Unsupported, clean.SourceVersion));
        Assert.True(clean.Summary.CompleteForAggregation);
        Assert.True(clean.Eligibility.Offers);
        Assert.Equal(3, clean.Facts.Outcome!.Placement);
        Assert.Equal("source-v2.1", clean.ParserVersion);

        // An OTHER block keeps its contents and is no loss in v4; before v4 it is an unsupported observation.
        var other = await Parse(Start(4, "ranked"), Line(4, 1, "block_start", """{"block":"OTHER"}"""), Line(4, 2, "block_end", """{"block":"OTHER"}"""), End(4, 3, result: "W"));
        Assert.Equal(("parsed", 0, 0), (other.Status, other.Gaps, other.Unsupported));
        var older = await Parse(Start(3, "ranked"), Line(3, 1, "block_start", """{"block":"OTHER"}"""), Line(3, 2, "block_end", """{"block":"OTHER"}"""), End(3, 3, result: "W"));
        Assert.Equal(("partial", 1, 1), (older.Status, older.Gaps, older.Unsupported));
        // An unknown action and a real gap stay what they were.
        var unknown = await Parse(Start(4, "ranked"), Line(4, 1, "action", """{"action":"unknown","cost":null}""", side: "local"), End(4, 2, result: "W"));
        Assert.Equal(("partial", 1), (unknown.Status, unknown.Unsupported));
        var lost = await Parse(Start(4, "ranked"), Line(4, 1, "gap", """{"reason":"limit","count":1}"""), End(4, 2, result: "W", status: "incomplete"));
        Assert.Equal(("partial", 1), (lost.Status, lost.Gaps));
        // A v3 BG game still names its combat_view gap once and stays partial.
        var v3 = await Parse(Start(3, "bg"), Line(3, 1, "gap", """{"reason":"combat_view","count":1}"""), End(3, 2, placement: 3, status: "incomplete"));
        Assert.Equal(("partial", 1), (v3.Status, v3.Gaps));
        Assert.True(v3.Eligibility.Outcome);

        foreach (var source in new[]
        {
            new[] { Start(4, "bg"), Line(4, 1, "gap", """{"reason":"combat_view","count":1}"""), End(4, 2, placement: 3) },
            [Start(3, "bg"), End(3, 1, placement: 3)],
            [Start(4, "ranked"), End(3, 1, result: "W")],
        })
        {
            var error = await Assert.ThrowsAsync<SourceCaptureException>(() => Parse(source));
            Assert.Equal("invalid_source", error.Code);
        }

        var future = await Assert.ThrowsAsync<SourceCaptureException>(() => Parse(Start(5, "ranked"), End(5, 1, result: "W")));
        Assert.Equal("unsupported_version", future.Code);
    }

    // A pool that knows v3 (the production pool of 2026-11-26) gets a v4 journal as v3, fights kept.
    [Fact]
    public async Task A_pool_without_v4_gets_the_journal_as_v3_with_its_fights_and_the_structural_gap_back()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, int Version, string Source)>();
        using var http = new HttpClient(new Pool(3, seen));
        await using (var queue = await scratch.Open())
        {
            Spool(queue, 'a', "bg");
            Spool(queue, 'b', "ranked");
            Spool(queue, 'c', "ranked", other: true);
            await queue.FlushAsync();
            await queue.SendPendingAsync(new(http));

            // One refused v4 chunk, then every journal as v3: nothing waits behind the first one.
            Assert.Single(seen, x => x.Version == 4);
            Assert.Equal(3, seen.Count(x => x is { Route: "complete", Version: 3 }));
            Assert.Equal(0, queue.State.PendingCaptures);
        }

        var sent = seen.Where(x => x is { Route: "chunks", Version: 3 }).Select(x => x.Source).ToArray();
        Assert.Equal(3, sent.Length);
        var bg = await ParseText(sent.Single(s => s.Contains("\"mode\":\"bg\"", StringComparison.Ordinal)));
        Assert.Equal((3, "partial", 2), (bg.SourceVersion, bg.Status, bg.Events.Count(e => e.Kind == "bg_combat")));
        Assert.Equal(("gap", "combat_view"), (bg.Events[1].Kind, bg.Events[1].Data.GetProperty("reason").GetString()));
        Assert.Equal(4, bg.Facts.Outcome!.Placement);
        Assert.Equal(Enumerable.Range(0, bg.Records), bg.Events.Select(e => e.Seq));
        var ranked = await ParseText(sent.Single(s => s.Contains("\"mode\":\"ranked\"", StringComparison.Ordinal) && !s.Contains("OTHER", StringComparison.Ordinal)));
        Assert.Equal((3, "parsed"), (ranked.SourceVersion, ranked.Status));
        var other = await ParseText(sent.Single(s => s.Contains("OTHER", StringComparison.Ordinal)));
        Assert.Equal((3, "partial", 1), (other.SourceVersion, other.Status, other.Unsupported));
        // The manifests say what the v3 journals now hold: complete only for the Ranked game without a loss.
        Assert.Equal(["a:incomplete", "b:complete", "c:incomplete"], seen.Where(x => x.Route == "complete").Select(x => x.Source).Order(StringComparer.Ordinal));

        // After a restart the pool is asked with v4 again: it may have been updated meanwhile.
        await using var restarted = await scratch.Open();
        Spool(restarted, 'd', "bg");
        await restarted.FlushAsync();
        await restarted.SendPendingAsync(new(http));
        Assert.Equal(2, seen.Count(x => x.Version == 4));
        Assert.Equal(0, restarted.State.PendingCaptures);
    }

    [Fact]
    public async Task A_pool_that_knows_only_v2_gets_v4_journals_as_v2_without_fights_and_one_that_knows_v4_as_written()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, int Version, string Source)>();
        using var http = new HttpClient(new Pool(2, seen));
        await using (var queue = await scratch.Open())
        {
            Spool(queue, 'a', "bg");
            Spool(queue, 'b', "bg");
            await queue.FlushAsync();
            await queue.SendPendingAsync(new(http));
            Assert.Equal([4, 3, 2, 2], seen.Where(x => x.Route == "chunks").Select(x => x.Version));
            Assert.Equal(0, queue.State.PendingCaptures);
        }

        var v2 = await ParseText(seen.First(x => x is { Route: "chunks", Version: 2 }).Source);
        Assert.Equal((2, 0, "combat_view"), (v2.SourceVersion, v2.Events.Count(e => e.Kind == "bg_combat"), v2.Events[1].Data.GetProperty("reason").GetString()));
        Assert.Equal(4, v2.Facts.Outcome!.Placement);

        using var current = new SourceScratch();
        var taken = new List<(string Route, int Version, string Source)>();
        using var pool = new HttpClient(new Pool(4, taken));
        await using var updated = await current.Open();
        Spool(updated, 'e', "bg");
        await updated.FlushAsync();
        await updated.SendPendingAsync(new(pool));
        Assert.Equal([("chunks", 4), ("complete", 4)], taken.Select(x => (x.Route, x.Version)));
        Assert.Equal("e:complete", taken[1].Source);
        var v4 = await ParseText(taken[0].Source);
        Assert.Equal(("parsed", 2), (v4.Status, v4.Events.Count(e => e.Kind == "bg_combat")));
    }

    /// <summary>A pool that takes sources up to <paramref name="known"/>; a newer line is an unknown record (400 invalid_record).</summary>
    private sealed class Pool(int known, List<(string Route, int Version, string Source)> seen) : HttpMessageHandler
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
                using var first = JsonDocument.Parse(source[..source.IndexOf('\n', StringComparison.Ordinal)]);
                var version = first.RootElement.GetProperty("v").GetInt32();
                seen.Add(("chunks", version, source));
                return version > known
                    ? new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"ok":false,"error":"invalid_record"}""") }
                    : Ok(new { ok = true, stored = true, duplicate = false, index = root.GetProperty("index").GetInt32(), sha256 = root.GetProperty("sha256").GetString() });
            }

            // The manifest: its version, and the journal (first letter of its id) with the status it claims.
            var id = root.GetProperty("captureId").GetString()!;
            seen.Add(("complete", root.GetProperty("sourceVersion").GetInt32(), $"{id[0]}:{root.GetProperty("captureStatus").GetString()}"));
            return Ok(new { ok = true, status = "stored", captureId = root.GetProperty("captureId").GetString() });
        }

        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }

    /// <summary>A whole game as the v4 recorder writes it: no gap, a Battlegrounds game with two fights, place 4.</summary>
    private static void Spool(SourceCaptureQueue queue, char id, string mode, bool other = false)
    {
        var capture = new string(id, 32);
        var seq = 0;
        void Add(string kind, object data, int turn = 9) =>
            Assert.True(queue.TryAppend(new(capture, "game", mode, 20776, new SourceCaptureRecord(seq++, kind, turn, "none", 0, null, 0, data))));
        Add("start", new { captureKind = "game", mode, day = 20776, build = (int?)4321, format = mode == "bg" ? null : "standard", arena = (string?)null }, turn: 0);
        Add("tag", new { tag = "TURN", value = 9 });
        if (other)
        {
            Add("block_start", new { block = "OTHER" });
            Add("block_end", new { block = "OTHER" });
        }

        if (mode == "bg")
        {
            Add("bg_combat", SourceCaptureCombatTests.Project(SourceCaptureCombatTests.Fight(), index: 0));
            Add("bg_combat", SourceCaptureCombatTests.Project(SourceCaptureCombatTests.Fight(), index: 1));
        }

        Add("end", new { status = "complete", result = mode == "bg" ? "L" : "W", placement = mode == "bg" ? (int?)4 : null, reason = "normal" });
    }

    // A Ranked game from its start (the capture begins at turn 0) to the local player's win, in the order of a real log.
    private static string[] Ranked(string[] mulligan, string[] turn) =>
    [
        .. ReplayRecorderTests.Setup[..^3],
        .. mulligan,
        .. ReplayRecorderTests.Setup[^3..],
        .. turn,
        "DebugPrintPower() - TAG_CHANGE Entity=velox#21276 tag=PLAYSTATE value=WON ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_WRAPUP ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
    ];

    // The local mulligan keeping every card: the chosen header names no card.
    private static string[] KeptHand() =>
    [
        "DebugPrintEntityChoices() - id=1 Player=velox#21276 TaskList= ChoiceType=MULLIGAN CountMin=0 CountMax=3",
        "DebugPrintEntityChoices() -   Source=GameEntity",
        $"DebugPrintEntityChoices() -   Entities[0]={Spell}",
        "SendChoices() - id=1 ChoiceType=MULLIGAN",
        "DebugPrintEntitiesChosen() - id=1 Player=velox#21276 EntitiesCount=0",
    ];

    // Known Power.log grammar the format leaves out (docs/source-capture.md, "Source v4"), as the client writes it.
    private static string[] LeftOut(string kind) => kind switch
    {
        "options" =>
        [
            "DebugPrintOptions() - id=2",
            "DebugPrintOptions() -   option 0 type=END_TURN mainEntity= error=INVALID errorParam=",
            $"DebugPrintOptions() -   option 1 type=POWER mainEntity={Spell} error=NONE errorParam=",
            $"DebugPrintOptions() -     target 0 entity={TheirHero} error=NONE errorParam=",
            $"DebugPrintOptions() -     subOption 0 entity={Spell} error=NONE errorParam=",
        ],
        "power_list" => ["DebugPrintPowerList() - Count=1"],
        "sent" =>
        [
            "SendOption() - selectedOption=1 selectedSubOption=-1 selectedTarget=21 selectedPosition=0",
            "OnEntityChoices() - id=3 playerId=1 queued",
            "SendChoices() - id=3 ChoiceType=GENERAL",
            $"SendChoices() -   m_chosenEntities[0]={Spell}",
        ],
        "meta_data" => ["DebugPrintPower() -     META_DATA - Meta=DAMAGE Data=2 InfoCount=1", $"DebugPrintPower() -                 Info[0] = {TheirHero}"],
        "sub_spell" =>
        [
            "DebugPrintPower() -     SUB_SPELL_START - SpellPrefabGUID=ReusableFX:0 Source=99 TargetCount=1",
            $"DebugPrintPower() -                     Source = {Spell}",
            $"DebugPrintPower() -                     Targets[0] = {TheirHero}",
            "DebugPrintPower() -     SUB_SPELL_END",
        ],
        "shuffle" => ["DebugPrintPower() - SHUFFLE_DECK PlayerID=1"],
        "dormant" => [$"DebugPrintPower() - CACHED_TAG_FOR_DORMANT_CHANGE Entity={Spell} tag=ATK value=3"],
        "step" => ["DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_READY ", "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_ACTION "],
        // The proposed attacker and defender inside the ATTACK block, as the client logs them.
        "attack" =>
        [
            $"DebugPrintPower() - BLOCK_START BlockType=ATTACK Entity={OurHero} {Effect} Target={TheirHero} SubOption=-1 ",
            "DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=PROPOSED_ATTACKER value=20 ",
            "DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=PROPOSED_DEFENDER value=21 ",
            $"DebugPrintPower() -     TAG_CHANGE Entity={TheirHero} tag=DAMAGE value=2 ",
            "DebugPrintPower() - BLOCK_END",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=PROPOSED_ATTACKER value=0 ",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=PROPOSED_DEFENDER value=0 ",
        ],
        // A trade: a block type the format does not name.
        "trade" =>
        [
            $"DebugPrintPower() - BLOCK_START BlockType=DECK_ACTION Entity={Spell} {Effect} Target=0 SubOption=-1 ",
            "DebugPrintPower() -     TAG_CHANGE Entity=velox#21276 tag=RESOURCES_USED value=1 ",
            "DebugPrintPower() - BLOCK_END",
        ],
        // A location used from play: its PLAY block is the observation, no action names it.
        "location" =>
        [
            .. ReplayRecorderTests.Card(60, "LOCATION_CARD", "PLAY", 1, "LOCATION", "tag=ZONE_POSITION value=1"),
            $"DebugPrintPower() - BLOCK_START BlockType=PLAY Entity=[entityName=X id=60 zone=PLAY zonePos=1 cardId=LOCATION_CARD player=1] {Effect} Target=0 SubOption=-1 ",
            "DebugPrintPower() - BLOCK_END",
        ],
        _ => [],
    };

    /// <summary>The live path: GameSession with source capture on, every line as the log watcher hands it over.</summary>
    private List<SourceCaptureFrame> RecordGame(IEnumerable<string> bodies)
    {
        Directory.CreateDirectory(_dir);
        using var memory = new HearthstoneMemory();
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Replays = new ReplayStore(Path.Combine(_dir, "replays")),
            CombatCaptures = null,
        };
        var frames = new List<SourceCaptureFrame>();
        session.SourceCaptureRecorded += frames.Add;
        session.SetSourceCaptureEnabled(true);
        session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", new DateTime(2026, 11, 26, 12, 0, 0), attachedMidSession: false);
        session.OnLines("Power.log", bodies.Select(b => "D 12:30:00.0000000 GameState." + b).ToList(), catchingUp: false);
        now = now.AddSeconds(5);
        session.OnTick();
        return frames.GroupBy(f => f.CaptureId).Single().ToList();
    }

    private static JsonElement Data(SourceCaptureFrame frame) => SourceCaptureRecorderTests.Data(frame);

    private static Task<SourceCaptureResult> Decode(IEnumerable<SourceCaptureFrame> frames) =>
        ParseText(string.Join('\n', frames.Select(f => f.Record.ToJson())) + "\n");

    private static Task<SourceCaptureResult> Parse(params string[] lines) => ParseText(string.Join('\n', lines) + "\n");

    private static async Task<SourceCaptureResult> ParseText(string source)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return await new SourceCaptureParser().ParseAsync(stream);
    }

    private static string Start(int v, string mode) => Line(v, 0, "start",
        JsonSerializer.Serialize(new { captureKind = "game", mode, day = 20766, build = 123, format = mode == "ranked" ? "standard" : null, arena = (string?)null }));

    private static string End(int v, int seq, string? result = null, int? placement = null, string status = "complete") =>
        Line(v, seq, "end", JsonSerializer.Serialize(new { status, result, placement, reason = "normal" }));

    private static string Hero(int v, int seq) => Line(v, seq, "choice", JsonSerializer.Serialize(new
    {
        choiceId = 1, sourceCard = (string?)null, options = new[] { "HERO_A", "HERO_B" }, picked = new[] { "HERO_B" }, resolved = true, choiceType = "mulligan",
    }), side: "local");

    private static string Line(int v, int seq, string kind, string data, string side = "none") =>
        JsonSerializer.Serialize(new { v, seq, kind, turn = 0, side, entity = 0, card = (string?)null, target = 0, data = JsonSerializer.Deserialize<JsonElement>(data) });
}

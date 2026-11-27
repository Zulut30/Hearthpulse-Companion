using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.SourceCapture.Processing;
using Hearthpulse.Core.Tracking;
using Microsoft.Extensions.Time.Testing;
using static Hearthpulse.Tests.BattlegroundsReplayTests;

namespace Hearthpulse.Tests;

/// <summary>
/// Source v4 (TASK-2026-11-26-pro-collection P1): a gap is a real loss. A whole game recorded through the live path
/// (GameSession: every Power.log line to the recorder and the parser) decodes as parsed whatever known log grammar the
/// format leaves out, while an unknown line or an undecodable value still leaves it partial, and so does a pick that
/// never became visible; a mulligan's pick is the cards kept; the decoder's v4 rules; the spool's fallback to v3 and v2
/// for a pool that does not take v4 yet or no longer does, and a journal the pool refuses for good dropped instead of
/// holding the queue.
/// Synthetic lines in the order real logs have them.
/// </summary>
public sealed class SourceCaptureV4Tests : IDisposable
{
    private const string Spell = "[entityName=X id=99 zone=HAND zonePos=1 cardId=CORE_CS2_004 player=1]";
    private const string OurHero = "[entityName=X id=20 zone=PLAY zonePos=0 cardId=HERO_09 player=1]";
    private const string TheirHero = "[entityName=X id=21 zone=PLAY zonePos=0 cardId=HERO_08 player=2]";
    private const string Effect = "EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0";
    private readonly string _dir = TestDataFolder.Scratch("hp-source-v4");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    // Regression: before v4 each of these left every real game partial (12 245 gaps in the 10 kept games of 24-25.11, 46 %
    // of the records); a mulligan that replaced every card left its choice open.
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
    [InlineData("replaced_hand")]
    public async Task A_whole_ranked_game_with_log_lines_the_format_leaves_out_is_parsed(string kind)
    {
        var capture = RecordGame(kind == "replaced_hand" ? Ranked(Mulligan(), []) : Ranked([], LeftOut(kind)));

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
            case "replaced_hand":
                // No card kept: the empty pick is the whole hand replaced.
                var choice = Assert.Single(result.Events, e => e.Kind == "choice" && e.Data.GetProperty("resolved").GetBoolean());
                Assert.Equal("mulligan", choice.Data.GetProperty("choiceType").GetString());
                Assert.Empty(choice.Data.GetProperty("picked").EnumerateArray());
                break;
        }
    }

    // QA Q5: every Power.log block type without a kind of its own is an OTHER block and no loss, not only the trade's
    // DECK_ACTION (the only one in the kept logs). The recorder reads the type alone, so one body serves them all.
    [Theory]
    [InlineData("DECK_ACTION")]
    [InlineData("FATIGUE")]
    [InlineData("RITUAL")]
    [InlineData("JOUST")]
    [InlineData("REVEAL_CARD")]
    [InlineData("GAME_RESET")]
    public async Task A_block_type_without_a_kind_of_its_own_is_an_other_block_and_no_loss(string type)
    {
        var capture = RecordGame(Ranked([],
        [
            $"DebugPrintPower() - BLOCK_START BlockType={type} Entity={Spell} {Effect} Target=0 SubOption=-1 ",
            "DebugPrintPower() -     TAG_CHANGE Entity=velox#21276 tag=RESOURCES_USED value=1 ",
            "DebugPrintPower() - BLOCK_END",
        ]));

        Assert.Contains(capture, f => f.Record.Kind == "block_start" && Data(f).GetProperty("block").GetString() == "OTHER");
        Assert.DoesNotContain(capture, f => f.Record.Kind == "gap");
        var result = await Decode(capture);
        Assert.Equal(("parsed", 0, 0), (result.Status, result.Gaps, result.Unsupported));
    }

    [Theory]
    [InlineData("structure")]
    [InlineData("meta_data")]
    [InlineData("value")]
    [InlineData("debug_print")] // QA Q2: a DebugPrint method the recorder does not know
    [InlineData("game_state")] // QA Q2: another GameState method it does not know
    [InlineData("block_type")] // review m1: a block type outside both lists; its contents stay as OTHER, its type is a loss
    public async Task An_unknown_log_line_or_an_undecodable_value_is_still_a_gap(string kind)
    {
        string[] lines = kind switch
        {
            "structure" => ["DebugPrintPower() - FUTURE_STRUCTURE Entity=GameEntity"],
            "meta_data" => ["DebugPrintPower() -     META_DATA_FUTURE Info=1"],
            "value" => [$"DebugPrintPower() - TAG_CHANGE Entity={Spell} tag=COST value=ONE "],
            "debug_print" => ["DebugPrintFutureThing() - id=7"],
            "game_state" => ["SendFutureThing() - id=7"],
            _ => [$"DebugPrintPower() - BLOCK_START BlockType=FUTURE_TYPE Entity={Spell} {Effect} Target=0 SubOption=-1 ", "DebugPrintPower() - BLOCK_END"],
        };
        var capture = RecordGame(Ranked([], lines));

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

    // Regression (review B1, QA Q1): a game that ended with the local choice open was sealed complete without a gap, which
    // the pool refuses (400 incomplete_stream). The pick never became visible: one source_missing gap, so the journal
    // ends incomplete and the pool takes it (capture-validation.test.mjs).
    [Theory]
    [InlineData("mulligan")] // the opponent conceded during the mulligan
    [InlineData("discover")] // ... during a Discover, its block already closed
    [InlineData("header")] // QA Q3: the chosen header came (EntitiesCount=1), its card line never did
    public async Task A_game_that_ends_with_the_local_choice_open_has_one_gap_and_ends_incomplete(string open)
    {
        var capture = RecordGame(open == "mulligan"
            ? [.. ReplayRecorderTests.Setup[..^3], .. MulliganOffer(), .. GameOver]
            : Ranked([], OpenDiscover(header: open == "header")));

        var gap = Assert.Single(capture, f => f.Record.Kind == "gap");
        Assert.Equal("source_missing", Data(gap).GetProperty("reason").GetString());
        Assert.False(Data(capture.Last(f => f.Record.Kind == "choice")).GetProperty("resolved").GetBoolean());
        Assert.Equal("incomplete", Data(capture[^1]).GetProperty("status").GetString());
        var result = await Decode(capture);
        Assert.Equal(("partial", 1, 0), (result.Status, result.Gaps, result.Unsupported));
        // A lost pick is not a lost end of the game: the result still counts (ADR 0013).
        Assert.True(result.Eligibility.Outcome);
        Assert.Equal(new SourceCaptureOutcome("W", null), result.Facts.Outcome);
        Assert.Equal(("incomplete", 1), await Sealed(capture));
    }

    // Regression (review B1): in Battlegrounds the turn timer picks a Discover the player left (no chosen line), and an
    // offer the next one replaced was never picked; either sealed the journal complete with an open choice.
    [Theory]
    [InlineData("timer")]
    [InlineData("replaced")]
    public async Task A_battlegrounds_pick_that_never_showed_is_one_gap_and_the_place_still_counts(string kind)
    {
        string[] offer = kind == "timer"
            ?
            [
                .. Entity(80, "TB_BaconShop_Triples_01", Me, "HAND", "SPELL"),
                .. Entity(81, "BG30_121", Me, "SETASIDE", "MINION"),
                .. Entity(82, "BG32_340", Me, "SETASIDE", "MINION"),
                BattlegroundsReplayTests.Start("PLAY", 80),
                Tag(80, "ZONE", "PLAY"),
                .. Offer("GENERAL", 80, 81, 82),
                BattlegroundsReplayTests.End,
            ]
            :
            [
                .. Entity(81, "BG30_121", Me, "SETASIDE", "MINION"),
                .. Entity(82, "BG32_340", Me, "SETASIDE", "MINION"),
                .. Entity(83, "BG21_001", Me, "SETASIDE", "MINION"),
                .. Entity(84, "BGS_004", Me, "SETASIDE", "MINION"),
                .. Offer("GENERAL", null, 81, 82),
                .. Offer("GENERAL", null, 83, 84),
                .. Chosen(84),
            ];
        var capture = RecordGame(BattlegroundsGame(offer));

        var gap = Assert.Single(capture, f => f.Record.Kind == "gap");
        Assert.Equal("source_missing", Data(gap).GetProperty("reason").GetString());
        Assert.Equal("incomplete", Data(capture[^1]).GetProperty("status").GetString());
        var result = await Decode(capture);
        Assert.Equal(("partial", 1, 0), (result.Status, result.Gaps, result.Unsupported));
        Assert.True(result.Eligibility.Outcome);
        Assert.True(result.Eligibility.Offers);
        Assert.Equal(new SourceCaptureOutcome("L", 5), result.Facts.Outcome);
        Assert.Equal(("incomplete", 1), await Sealed(capture));
    }

    // QA Q8: the client closed during a Discover and its next run writes a new log folder. The rotation seals that capture
    // with the choice still open; the next game starts without it, and its own mulligan is no loss.
    [Fact]
    public async Task A_choice_left_open_by_a_rotation_is_no_gap_in_the_next_game()
    {
        var captures = RecordSessions([.. ReplayRecorderTests.Setup, .. OpenDiscover(header: false)], Ranked(Mulligan(98), []));

        Assert.Equal(2, captures.Count);
        var left = captures[0];
        Assert.False(Data(left.Last(f => f.Record.Kind == "choice")).GetProperty("resolved").GetBoolean());
        Assert.Equal(["rotation"], left.Where(f => f.Record.Kind == "gap").Select(f => Data(f).GetProperty("reason").GetString()));
        Assert.DoesNotContain(captures[1], f => f.Record.Kind == "gap");
        var result = await Decode(captures[1]);
        Assert.Equal(("parsed", 0, 0), (result.Status, result.Gaps, result.Unsupported));
    }

    // QA Q3: the chosen header names how many cards follow, so it resolves nothing by itself; the card line does, once.
    [Fact]
    public void A_pick_resolves_its_choice_with_its_card_and_not_with_the_header_before_it()
    {
        string[] pick =
        [
            .. OpenDiscover(header: true).SkipLast(1),
            "DebugPrintEntitiesChosen() -   Entities[0]=[entityName=X id=98 zone=SETASIDE zonePos=0 cardId=CORE_CS2_189 player=1]",
            "DebugPrintPower() - BLOCK_END",
        ];
        var capture = RecordGame(Ranked([], pick));

        var records = capture.Where(f => f.Record.Kind == "choice").Select(Data).ToArray();
        Assert.Equal([false, false, true], records.Select(r => r.GetProperty("resolved").GetBoolean()));
        Assert.Equal(["CORE_CS2_189"], records[^1].GetProperty("picked").EnumerateArray().Select(c => c.GetString()));
        Assert.DoesNotContain(capture, f => f.Record.Kind == "gap");
    }

    // Review M1: the chosen entities of a mulligan are the cards kept. The client lists them and the others go back to the
    // deck (checked on the 10 real mulligans of 24-25.11), so mulligan.keep reads picked as kept, and an empty pick is a
    // mulligan that replaced every card.
    [Fact]
    public void A_mulligans_pick_is_the_cards_kept_in_hand()
    {
        var parser = new PowerParser(new DateOnly(2026, 11, 26));
        var recorder = new SourceCaptureRecorder();
        var frames = new List<SourceCaptureFrame>();
        recorder.Recorded += frames.Add;
        recorder.SetEnabled(true);
        parser.AddListener(recorder);
        ReplayRecorderTests.Feed(parser, Ranked(Mulligan(98), []));
        parser.Flush();

        var choice = Data(frames.Last(f => f.Record.Kind == "choice"));
        Assert.True(choice.GetProperty("resolved").GetBoolean());
        Assert.Equal("mulligan", choice.GetProperty("choiceType").GetString());
        Assert.Equal(["CORE_CS2_189"], choice.GetProperty("picked").EnumerateArray().Select(c => c.GetString()));
        var game = parser.Game!;
        Assert.Equal(("HAND", "DECK", "DECK"), (game.Entities[98].Zone, game.Entities[97].Zone, game.Entities[99].Zone));
    }

    // Regression: a Battlegrounds game was partial by its combat_view gap alone, and by its phases and attacks.
    [Fact]
    public async Task A_whole_battlegrounds_game_is_parsed_and_fit_for_its_place_and_hero_offer()
    {
        var capture = RecordGame(BattlegroundsGame());

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
            Spool(queue, 'd', "duos");
            await queue.FlushAsync();
            await queue.SendPendingAsync(new(http));

            // One refused v4 chunk, then every journal as v3: nothing waits behind the first one.
            Assert.Single(seen, x => x.Version == 4);
            Assert.Equal(4, seen.Count(x => x is { Route: "complete", Version: 3 }));
            Assert.Equal(0, queue.State.PendingCaptures);
        }

        var sent = seen.Where(x => x is { Route: "chunks", Version: 3 }).Select(x => x.Source).ToArray();
        Assert.Equal(4, sent.Length);
        // QA Q4: a Duos game gets its combat_view gap back too, or a v3 pool refuses it (invalid_combat_view).
        foreach (var mode in new[] { "bg", "duos" })
        {
            var bg = await ParseText(sent.Single(s => s.Contains($"\"mode\":\"{mode}\"", StringComparison.Ordinal)));
            Assert.Equal((3, "partial", 2), (bg.SourceVersion, bg.Status, bg.Events.Count(e => e.Kind == "bg_combat")));
            Assert.Equal(("gap", "combat_view"), (bg.Events[1].Kind, bg.Events[1].Data.GetProperty("reason").GetString()));
            Assert.Equal(4, bg.Facts.Outcome!.Placement);
            Assert.Equal(Enumerable.Range(0, bg.Records), bg.Events.Select(e => e.Seq));
        }

        var ranked = await ParseText(sent.Single(s => s.Contains("\"mode\":\"ranked\"", StringComparison.Ordinal) && !s.Contains("OTHER", StringComparison.Ordinal)));
        Assert.Equal((3, "parsed"), (ranked.SourceVersion, ranked.Status));
        var other = await ParseText(sent.Single(s => s.Contains("OTHER", StringComparison.Ordinal)));
        Assert.Equal((3, "partial", 1), (other.SourceVersion, other.Status, other.Unsupported));
        // The manifests say what the v3 journals now hold: complete only for the Ranked game without a loss.
        Assert.Equal(["a:incomplete", "b:complete", "c:incomplete", "d:incomplete"], seen.Where(x => x.Route == "complete").Select(x => x.Source).Order(StringComparer.Ordinal));

        // After a restart the pool is asked with v4 again: it may have been updated meanwhile.
        await using var restarted = await scratch.Open();
        Spool(restarted, 'e', "bg");
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

    // Review B1, the queue's side: a 400 whose code says the pool will never take the journal as it is (here what it
    // answered the open choice) was retried every 30 s ahead of the others, so every journal behind it waited for 14
    // days. Such a journal is dropped with a log line and the pass goes on.
    [Fact]
    public async Task A_journal_the_pool_refuses_for_good_is_dropped_and_the_next_ones_go_on()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, char Id, int Version)>();
        using var http = new HttpClient(new Answering(seen, (route, id, _) => route == "complete" && id != 'd' ? (HttpStatusCode.BadRequest, "incomplete_stream") : null));
        await using var queue = await scratch.Open();
        foreach (var id in "abcd") Spool(queue, id, "ranked");
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        // One pass offers every journal once, in whatever order: the refused ones are gone, the last one is sent.
        Assert.Equal("abcd", string.Concat(seen.Where(x => x.Route == "complete").Select(x => x.Id).Order()));
        Assert.Equal((0, 0L), (queue.State.PendingCaptures, queue.State.Dropped));
        Assert.Empty(Directory.GetDirectories(scratch.Path));
        Assert.Contains("Source capture: the pool refused a ranked journal (incomplete_stream), dropped; source v4, sent as v4", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    // A temporary answer still ends the pass and keeps every journal: the pool is busy or away, it refuses nothing.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)0)] // no network
    public async Task A_temporary_answer_keeps_every_journal_waiting(HttpStatusCode status)
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, char Id, int Version)>();
        using var http = new HttpClient(new Answering(seen, (_, _, _) => (status, null)));
        await using var queue = await scratch.Open();
        Spool(queue, 'a', "ranked");
        Spool(queue, 'b', "ranked");
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        Assert.Single(seen);
        Assert.Equal((2, 0L, "offline"), (queue.State.PendingCaptures, queue.State.Dropped, queue.State.Status));
    }

    // Review m6: a pool rolled back after it took v4 refuses v4 lines again (400 invalid_record). That refusal counted as
    // the journal's own, so every v4 journal was dropped until the app restarted. Each one goes as v3 instead, and the
    // session stays on v4: the next journal is asked in v4 first, the pool may be updated again.
    [Fact]
    public async Task A_pool_rolled_back_after_it_took_v4_gets_each_journal_as_v3_and_the_session_stays_on_v4()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, char Id, int Version)>();
        // Journal a goes before the rollback; from then on a v4 line is an unknown record.
        using var http = new HttpClient(new Answering(seen, (route, id, version) => route == "chunks" && id != 'a' && version == 4 ? (HttpStatusCode.BadRequest, "invalid_record") : null));
        await using var queue = await scratch.Open();
        Spool(queue, 'a', "bg");
        await queue.FlushAsync();
        await queue.SendPendingAsync(new(http));
        Spool(queue, 'b', "bg");
        Spool(queue, 'c', "ranked");
        await queue.FlushAsync();
        await queue.SendPendingAsync(new(http));
        Spool(queue, 'd', "bg");
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        Assert.Equal([("chunks", 4), ("complete", 4)], seen.Where(x => x.Id == 'a').Select(x => (x.Route, x.Version)));
        foreach (var id in "bcd")
            Assert.Equal([("chunks", 4), ("chunks", 3), ("complete", 3)], seen.Where(x => x.Id == id).Select(x => (x.Route, x.Version)));
        Assert.Equal((0, 0L), (queue.State.PendingCaptures, queue.State.Dropped));
    }

    // Review m5: a journal refused in v4, v3 and v2 alike is the journal's fault: it is dropped and the session stays on
    // v4. It used to stay waiting and take the session down to v2 without fights until a restart, with a log line saying
    // the pool did not take v4.
    [Theory]
    [InlineData(false)]
    [InlineData(true)] // review m6: after the pool took v4 the journal is tried older all the same
    public async Task A_journal_refused_in_every_version_is_dropped_and_the_next_one_goes_as_v4(bool tookV4)
    {
        using var scratch = new SourceScratch();
        var time = new FakeTimeProvider();
        var seen = new List<(string Route, char Id, int Version)>();
        using var http = new HttpClient(new Answering(seen, (route, id, _) => route == "chunks" && id == 'a' ? (HttpStatusCode.BadRequest, "invalid_record") : null));
        await using var queue = await scratch.Open(time: time);
        if (tookV4)
        {
            Spool(queue, 'f', "bg");
            await queue.FlushAsync();
            await queue.SendPendingAsync(new(http));
        }

        Spool(queue, 'a', "bg");
        await queue.FlushAsync();
        await queue.SendPendingAsync(new(http));

        Assert.Equal([4, 3, 2], seen.Where(x => x.Id == 'a').Select(x => x.Version));
        Assert.Equal((0, 0L), (queue.State.PendingCaptures, queue.State.Dropped));

        time.Advance(TimeSpan.FromMinutes(1)); // past the retry wait a refusal used to set
        Spool(queue, 'b', "bg");
        await queue.FlushAsync();
        await queue.SendPendingAsync(new(http));
        Assert.Equal([("chunks", 4), ("complete", 4)], seen.Where(x => x.Id == 'b').Select(x => (x.Route, x.Version)));
    }

    // QA Q6: a journal spooled in v3 before the update goes as v3 as it was written. The pool took it without a refusal,
    // so the session stays on v4: the next Battlegrounds game goes without the combat_view gap of v3.
    [Fact]
    public async Task A_journal_spooled_in_v3_before_an_update_leaves_the_next_ones_in_v4()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, int Version, string Source)>();
        using var http = new HttpClient(new Pool(4, seen));
        await using var queue = await scratch.Open();
        Spool(queue, 'a', "ranked", version: 3);
        await queue.FlushAsync();
        await queue.SendPendingAsync(new(http));
        Spool(queue, 'b', "bg");
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        Assert.Equal([("chunks", 3), ("complete", 3), ("chunks", 4), ("complete", 4)], seen.Select(x => (x.Route, x.Version)));
        Assert.Equal("b:complete", seen[^1].Source);
    }

    // Review m8: the pool deletes an upload that got no manifest within its unfinished hours and answers 410
    // deleted_capture to every later request of it. Such a journal waited 30 s ahead of the others again and again until
    // its 14 days were up; it is dropped with a log line and its code, and the pass goes on.
    [Theory]
    [InlineData("chunks")] // a later chunk of a journal whose first ones the pool took
    [InlineData("complete")]
    public async Task A_journal_whose_unfinished_upload_the_pool_deleted_is_dropped_and_the_next_ones_go_on(string route)
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, char Id, int Version)>();
        using var http = new HttpClient(new Answering(seen, (to, id, _) => to == route && id != 'd' ? (HttpStatusCode.Gone, "deleted_capture") : null));
        await using var queue = await scratch.Open();
        foreach (var id in "abcd") Spool(queue, id, "ranked");
        await queue.FlushAsync();

        await queue.SendPendingAsync(new(http));

        Assert.Equal("abcd", string.Concat(seen.Where(x => x.Route == route).Select(x => x.Id).Order()));
        Assert.Contains(seen, x => x is { Route: "complete", Id: 'd' });
        Assert.Equal((0, 0L), (queue.State.PendingCaptures, queue.State.Dropped));
        Assert.Empty(Directory.GetDirectories(scratch.Path));
        Assert.Contains("Source capture: the pool refused a ranked journal (deleted_capture), dropped; source v4, sent as v4", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    // Settings → Data, "Some journals could not be saved: {0}", counts the journals lost on this PC across restarts. A
    // journal the pool refused is not one of them: its log line is its record.
    [Fact]
    public async Task A_journal_the_pool_refused_is_not_counted_with_the_ones_that_could_not_be_saved()
    {
        using var scratch = new SourceScratch();
        var seen = new List<(string Route, char Id, int Version)>();
        using var http = new HttpClient(new Answering(seen, (route, _, _) => route == "complete" ? (HttpStatusCode.BadRequest, "incomplete_stream") : null));
        await using (var queue = await scratch.Open())
        {
            Spool(queue, 'a', "ranked");
            // Lost on this PC: a journal whose first line is not its start.
            Assert.True(queue.TryAppend(new(new string('b', 32), "game", "ranked", 20776, new SourceCaptureRecord(1, "tag", 9, "none", 0, null, 0, new { tag = "TURN", value = 9 }))));
            await queue.FlushAsync();

            await queue.SendPendingAsync(new(http));

            Assert.Single(seen, x => x is { Route: "complete", Id: 'a' });
            Assert.Equal((0, 1L), (queue.State.PendingCaptures, queue.State.Dropped));
        }

        await using var reopened = await scratch.Open();
        Assert.Equal(1L, reopened.State.Dropped);
    }

    /// <summary>
    /// A pool that answers each request with what <paramref name="refuse"/> gives for its route, journal (the first
    /// letter of its id) and source version: a status and error code (status 0: no network), or null to take it.
    /// </summary>
    private sealed class Answering(List<(string Route, char Id, int Version)> seen, Func<string, char, int, (HttpStatusCode Status, string? Code)?> refuse) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            var chunk = request.RequestUri!.AbsolutePath.EndsWith("chunks", StringComparison.Ordinal);
            var version = root.TryGetProperty("sourceVersion", out var manifest) ? manifest.GetInt32() : 0;
            if (chunk)
            {
                using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(root.GetProperty("data").GetString()!)), CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                using var first = JsonDocument.Parse(await reader.ReadLineAsync(cancellationToken) ?? "{}");
                version = first.RootElement.GetProperty("v").GetInt32();
            }

            var route = chunk ? "chunks" : "complete";
            var id = root.GetProperty("captureId").GetString()![0];
            seen.Add((route, id, version));
            switch (refuse(route, id, version))
            {
                case { Status: 0 }:
                    throw new HttpRequestException("offline");
                case { } answer:
                    return new(answer.Status) { Content = new StringContent(answer.Code is null ? """{"ok":false}""" : $$"""{"ok":false,"error":"{{answer.Code}}"}""") };
            }

            object receipt = chunk
                ? new { ok = true, stored = true, duplicate = false, index = root.GetProperty("index").GetInt32(), sha256 = root.GetProperty("sha256").GetString() }
                : new { ok = true, status = "stored", captureId = root.GetProperty("captureId").GetString() };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(receipt)) };
        }
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

    /// <summary>
    /// A whole game as the v4 recorder writes it: no gap, a Battlegrounds or Duos game with two fights, place 4.
    /// <paramref name="version"/>: the wire version of a Ranked journal spooled before an update.
    /// </summary>
    private static void Spool(SourceCaptureQueue queue, char id, string mode, bool other = false, int version = SourceCaptureRecord.CurrentVersion)
    {
        var capture = new string(id, 32);
        var seq = 0;
        var battlegrounds = mode is "bg" or "duos";
        void Add(string kind, object data, int turn = 9) =>
            Assert.True(queue.TryAppend(new(capture, "game", mode, 20776, new SourceCaptureRecord(seq++, kind, turn, "none", 0, null, 0, data, version))));
        Add("start", new { captureKind = "game", mode, day = 20776, build = (int?)4321, format = battlegrounds ? null : "standard", arena = (string?)null }, turn: 0);
        Add("tag", new { tag = "TURN", value = 9 });
        if (other)
        {
            Add("block_start", new { block = "OTHER" });
            Add("block_end", new { block = "OTHER" });
        }

        if (battlegrounds)
        {
            Add("bg_combat", SourceCaptureCombatTests.Project(SourceCaptureCombatTests.Fight(), index: 0));
            Add("bg_combat", SourceCaptureCombatTests.Project(SourceCaptureCombatTests.Fight(), index: 1));
        }

        Add("end", new { status = "complete", result = battlegrounds ? "L" : "W", placement = battlegrounds ? (int?)4 : null, reason = "normal" });
    }

    /// <summary>The manifest the spool seals for these frames: what the pool is told about the journal.</summary>
    private static async Task<(string Status, int Gaps)> Sealed(IEnumerable<SourceCaptureFrame> frames)
    {
        using var scratch = new SourceScratch();
        await using (var queue = await scratch.Open())
        {
            foreach (var frame in frames) Assert.True(queue.TryAppend(frame));
            await queue.FlushAsync();
        }

        using var manifest = scratch.Manifest();
        return (manifest.RootElement.GetProperty("captureStatus").GetString()!, manifest.RootElement.GetProperty("gaps").GetInt32());
    }

    // A Ranked game from its start (the capture begins at turn 0) to the local player's win, in the order of a real log.
    private static string[] Ranked(string[] mulligan, string[] turn) =>
    [
        .. ReplayRecorderTests.Setup[..^3],
        .. mulligan,
        .. ReplayRecorderTests.Setup[^3..],
        .. turn,
        .. GameOver,
    ];

    private static readonly string[] GameOver =
    [
        "DebugPrintPower() - TAG_CHANGE Entity=velox#21276 tag=PLAYSTATE value=WON ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_WRAPUP ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER ",
        "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
    ];

    // The local opening hand of three cards and the mulligan offered for it.
    private static string[] MulliganOffer() =>
    [
        .. ReplayRecorderTests.Card(97, "CORE_EX1_011", "HAND", 1, "MINION", "tag=ZONE_POSITION value=1"),
        .. ReplayRecorderTests.Card(98, "CORE_CS2_189", "HAND", 1, "MINION", "tag=ZONE_POSITION value=2"),
        "DebugPrintEntityChoices() - id=1 Player=velox#21276 TaskList=7 ChoiceType=MULLIGAN CountMin=0 CountMax=3",
        "DebugPrintEntityChoices() -   Source=GameEntity",
        .. Opening.Select((id, i) => $"DebugPrintEntityChoices() -   Entities[{i}]={InHand(id)}"),
    ];

    /// <summary>
    /// The local mulligan in the order of a real log: the offer, what the client sends, the chosen entities (the cards
    /// <paramref name="kept"/>), then the others back to the deck and a new card drawn. None kept: the whole hand replaced.
    /// </summary>
    private static string[] Mulligan(params int[] kept) =>
    [
        .. MulliganOffer(),
        "SendChoices() - id=1 ChoiceType=MULLIGAN",
        .. kept.Select((id, i) => $"SendChoices() -   m_chosenEntities[{i}]={InHand(id)}"),
        $"DebugPrintEntitiesChosen() - id=1 Player=velox#21276 EntitiesCount={kept.Length}",
        .. kept.Select((id, i) => $"DebugPrintEntitiesChosen() -   Entities[{i}]={InHand(id)}"),
        "DebugPrintPower() - TAG_CHANGE Entity=velox#21276 tag=MULLIGAN_STATE value=DEALING ",
        $"DebugPrintPower() - BLOCK_START BlockType=TRIGGER Entity=velox#21276 {Effect} Target=0 SubOption=-1 TriggerKeyword=TAG_NOT_SET",
        .. Opening.Except(kept).SelectMany(id => new[]
        {
            $"DebugPrintPower() -     HIDE_ENTITY - Entity={InHand(id)} tag=ZONE value=DECK",
            $"DebugPrintPower() -     TAG_CHANGE Entity={InHand(id)} tag=ZONE value=DECK ",
        }),
        "DebugPrintPower() -     SHOW_ENTITY - Updating Entity=[entityName=UNKNOWN ENTITY [cardType=INVALID] id=40 zone=DECK zonePos=0 cardId= player=1] CardID=CORE_CS2_120",
        "DebugPrintPower() -         tag=CARDTYPE value=MINION",
        "DebugPrintPower() -         tag=ZONE value=HAND",
        "DebugPrintPower() - BLOCK_END",
    ];

    private static readonly int[] Opening = [97, 98, 99];

    private static string InHand(int id) => id switch
    {
        97 => "[entityName=X id=97 zone=HAND zonePos=1 cardId=CORE_EX1_011 player=1]",
        98 => "[entityName=X id=98 zone=HAND zonePos=2 cardId=CORE_CS2_189 player=1]",
        _ => Spell,
    };

    // A Discover of the local player from a card played from the hand, its block closed, the pick not seen yet (header:
    // the chosen header with its count came, the card line did not).
    private static string[] OpenDiscover(bool header) =>
    [
        $"DebugPrintPower() - BLOCK_START BlockType=PLAY Entity={Spell} {Effect} Target=0 SubOption=-1 ",
        .. ReplayRecorderTests.Card(97, "CORE_EX1_011", "SETASIDE", 1, "MINION"),
        .. ReplayRecorderTests.Card(98, "CORE_CS2_189", "SETASIDE", 1, "MINION"),
        "DebugPrintEntityChoices() - id=2 Player=velox#21276 TaskList=30 ChoiceType=GENERAL CountMin=1 CountMax=1",
        $"DebugPrintEntityChoices() -   Source={Spell}",
        "DebugPrintEntityChoices() -   Entities[0]=[entityName=X id=97 zone=SETASIDE zonePos=0 cardId=CORE_EX1_011 player=1]",
        "DebugPrintEntityChoices() -   Entities[1]=[entityName=X id=98 zone=SETASIDE zonePos=0 cardId=CORE_CS2_189 player=1]",
        .. header
            ?
            [
                "SendChoices() - id=2 ChoiceType=GENERAL",
                "SendChoices() -   m_chosenEntities[0]=[entityName=X id=98 zone=SETASIDE zonePos=0 cardId=CORE_CS2_189 player=1]",
                "DebugPrintEntitiesChosen() - id=2 Player=velox#21276 EntitiesCount=1",
            ]
            : Array.Empty<string>(),
        "DebugPrintPower() - BLOCK_END",
    ];

    // A whole Battlegrounds game: the hero pick, turn 1 with known grammar the format leaves out and then <paramref
    // name="turn"/>, two fights, out in 5th place.
    private static string[] BattlegroundsGame(params string[] turn)
    {
        var first = Array.IndexOf(Setup, Game("TURN", 1));
        return
        [
            .. Setup.Take(first),
            .. Entity(70, "TB_BaconShop_HERO_01", Me, "HAND", "HERO"),
            .. Entity(71, "TB_BaconShop_HERO_02", Me, "HAND", "HERO"),
            .. Offer("MULLIGAN", null, 70, 71),
            "DebugPrintOptions() - id=1",
            "DebugPrintOptions() -   option 0 type=CHOOSE mainEntity= error=NONE errorParam=",
            "SendChoices() - id=1 ChoiceType=MULLIGAN",
            "SendChoices() -   m_chosenEntities[0]=[entityName=X id=71 zone=HAND zonePos=2 cardId=TB_BaconShop_HERO_02 player=1]",
            .. Chosen(71),
            .. Setup.Skip(first),
            "DebugPrintPowerList() - Count=1",
            "SendOption() - selectedOption=1 selectedSubOption=-1 selectedTarget=51 selectedPosition=0",
            "DebugPrintPower() -     META_DATA - Meta=SLUSH_TIME Data=1000 InfoCount=1",
            "DebugPrintPower() -                 Info[0] = [entityName=X id=10 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_50 player=1]",
            .. turn,
            // The lobby names the opponent of each combat, so the tracker records the fights for the journal.
            Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4),
            .. Game5th().Skip(Setup.Length),
        ];
    }

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
    private List<SourceCaptureFrame> RecordGame(IEnumerable<string> bodies) => RecordSessions(bodies).Single();

    /// <summary>
    /// The live path for one run of the client per entry of <paramref name="sessions"/> (its Power.log in a log folder of
    /// its own, an hour after the one before): the frames of each capture, in the order the captures began.
    /// </summary>
    private List<List<SourceCaptureFrame>> RecordSessions(params IEnumerable<string>[] sessions)
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
        for (var i = 0; i < sessions.Length; i++)
        {
            var hour = 12 + i;
            session.OnSessionStarted($"Hearthstone_2026_11_26_{hour}_00_00", new DateTime(2026, 11, 26, hour, 0, 0), attachedMidSession: false);
            session.OnLines("Power.log", sessions[i].Select(b => $"D {hour}:30:00.0000000 GameState." + b).ToList(), catchingUp: false);
        }

        now = now.AddSeconds(5);
        session.OnTick();
        return frames.GroupBy(f => f.CaptureId).Select(g => g.ToList()).ToList();
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

using System.Net;
using System.Text;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests;

/// <summary>
/// Opening-hand pairs from the stats pool (plan 2.4.2, metric <c>mulligan.pair_winrate</c>) over a fake pool: what is
/// read and what is dropped, the six-hour copy on disk and its withdrawal (no older copy while the pool is down, a
/// failing disk, a clock set back, an answer held in flight), an endpoint that is not plain https, and the pairs
/// reaching the guide of a deck on their own. The answers are synthetic, in the shape
/// <c>server/pool/src/events.js</c> writes; the clock is the test's.
/// </summary>
public sealed class MulliganPairsTests : IDisposable
{
    private const string Pool = "https://pool.test/stats";
    private const string Warlock = "7X8Zqlb1wBPwkkaQVGT8ee";
    private const string Rogue = "vqugUxEMufzPN9UNvcADrg";

    private static readonly DateTimeOffset Noon = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _cache = TestDataFolder.Scratch("mullpairs");
    private readonly FakeTimeProvider _time = new(Noon);

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Fact]
    public async Task A_published_pair_is_read_with_its_four_cells_from_the_pools_route()
    {
        var pool = new FakePool(Published());

        var pairs = await Pairs(pool).LoadAsync("standard", "mage");

        var pair = Assert.Single(pairs);
        Assert.Equal(("CARD_A", "CARD_B", 7, 412), (pair.CardA, pair.CardB, pair.Installations, pair.Games));
        Assert.Equal(new PairCell(412, 240), pair.Both);
        Assert.Equal(new PairCell(200, 100), pair.OnlyA);
        Assert.Equal(new PairCell(100, 45), pair.OnlyB);
        Assert.Null(pair.Neither);
        Assert.Equal(["GET https://pool.test/stats/v1/events/mulligan-pairs?fmt=standard&cls=MAGE"], pool.Requests);
    }

    [Fact]
    public async Task Rows_below_the_thresholds_or_malformed_are_dropped_whatever_the_pool_sent()
    {
        var pool = new FakePool(Answer(
            Row("CARD_A", "CARD_B", 5, "[200,120]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_C", 5, "[199,120]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_D", 4, "[900,500]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_A", 9, "[900,500]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "bad id!", 9, "[900,500]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_E", 9, "[900,901]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_F", 9, "\"many\"", "[40,20]", "[40,20]", "[40,20]"),
            // Values of the wrong kind where a number belongs: each is one dropped row or one hidden cell, never an exception.
            Row("CARD_A", "CARD_H", 9, "[\"900\",\"500\"]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_I", 9, "[900.5,500]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_J", 9, "[true,null]", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_K", 9, "{\"games\":900}", "[40,20]", "[40,20]", "[40,20]"),
            Row("CARD_A", "CARD_L", 9, "[900,500]", "[40,20]", "[40,20]", "[40,20]").Replace("\"installs\":9", "\"installs\":\"9\"", StringComparison.Ordinal),
            Row("CARD_A", "CARD_M", 9, "[900,500]", "[40,20]", "[40,20]", "[40,20]").Replace("\"a\":\"CARD_A\"", "\"a\":17", StringComparison.Ordinal),
            "\"a row\"", "17", "null", "[1,2]",
            Row("CARD_A", "CARD_G", 9, "[300,150]", "[40,41]", "[\"40\",20]", "[40]")));

        var pairs = await Pairs(pool).LoadAsync("standard", "MAGE");

        Assert.Equal(["CARD_B", "CARD_G"], pairs.Select(p => p.CardB));
        // A cell that cannot be true is hidden, not repaired.
        Assert.Equal((null, null, null), (pairs[1].OnlyA, pairs[1].OnlyB, pairs[1].Neither));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("17")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"v":"1","metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","pairs":[]}""")]
    [InlineData("""{"v":1.5,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","pairs":[]}""")]
    [InlineData("""{"v":1,"metric":7,"rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","pairs":[]}""")]
    [InlineData("""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-2","fmt":"standard","cls":"MAGE","pairs":[]}""")]
    [InlineData("""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"PRIEST","pairs":[]}""")]
    [InlineData("""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","pairs":{}}""")]
    [InlineData("""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","pairs":"none"}""")]
    [InlineData("""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE"}""")]
    public async Task An_answer_that_is_not_this_metrics_is_no_answer_and_nothing_throws(string body)
    {
        Assert.Null(MulliganPairs.Parse(body, "standard", "MAGE"));
        Assert.Empty(await Pairs(new FakePool(body)).LoadAsync("standard", "MAGE"));
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://pool.test/stats")]
    [InlineData("pool.test/stats")]
    [InlineData("https://user:secret@pool.test/stats")]
    public async Task Without_a_plain_https_pool_the_list_is_empty_and_nothing_is_asked(string endpoint)
    {
        var pool = new FakePool(Published());
        using var http = new HttpClient(pool);
        var pairs = new MulliganPairs(http, () => endpoint, _cache);

        Assert.Empty(await pairs.LoadAsync("standard", "MAGE"));
        Assert.Empty(pool.Requests);
        Assert.Equal(string.Empty, pairs.Address);
    }

    [Theory]
    [InlineData("classic", "MAGE")]
    [InlineData("standard&cls=PRIEST", "MAGE")]
    [InlineData("standard", "MAGE&fmt=wild")]
    [InlineData("standard", "")]
    public async Task A_format_or_class_that_is_not_a_key_never_reaches_the_pool(string format, string playerClass)
    {
        var pool = new FakePool(Answer());

        Assert.Empty(await Pairs(pool).LoadAsync(format, playerClass));
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task The_copy_answers_for_six_hours_and_there_is_no_older_copy_while_the_pool_is_down()
    {
        var pool = new FakePool(Published());
        var pairs = Pairs(pool);

        Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        _time.Advance(MulliganPairs.MaxAge);
        Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        Assert.Single(pool.Requests);
        Assert.Single(Files());
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));

        // A minute later the copy is too old. The pool is down: no pairs, and the copy is gone for good, so no state of
        // the clock or the network brings it back.
        _time.Advance(TimeSpan.FromMinutes(1));
        pool.Status = null;
        Assert.Empty(await pairs.LoadAsync("standard", "MAGE"));
        Assert.Equal(2, pool.Requests.Count);
        Assert.Empty(Files());
        Assert.Empty(await Pairs(pool, Noon).LoadAsync("standard", "MAGE"));

        // An error status or an answer that is not this metric's is no answer either, and nothing of it is kept.
        foreach (var (status, body) in new[] { (HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.NotFound, "{\"error\":\"not found\"}"), (HttpStatusCode.OK, new string(' ', 2 * 1024 * 1024)) })
        {
            (pool.Status, pool.Body) = (status, body);
            Assert.Empty(await pairs.LoadAsync("standard", "MAGE"));
        }

        Assert.Empty(Files());

        // Another class or another pool has a file of its own.
        (pool.Status, pool.Body) = (HttpStatusCode.OK, Published());
        Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        pool.Status = null;
        Assert.Empty(await pairs.LoadAsync("standard", "PRIEST"));
        using var http = new HttpClient(pool);
        Assert.Empty(await new MulliganPairs(http, () => "https://other.test", _cache) { Time = _time }.LoadAsync("standard", "MAGE"));
    }

    [Fact]
    public async Task A_withdrawn_pair_is_gone_after_the_next_answer_and_stays_gone_offline_after_a_restart_and_with_the_clock_set_back()
    {
        var pool = new FakePool(Published());
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));

        // The player had their games deleted: the pool's next answer has no pair.
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Answer();
        Assert.Empty(await Pairs(pool).LoadAsync("standard", "MAGE"));

        pool.Status = null;
        Assert.Empty(await Pairs(pool).LoadAsync("standard", "MAGE"));
        // The copy of that answer is young: it is the one that speaks, and the pool is not even asked.
        Assert.Equal(2, pool.Requests.Count);
        // A restart, with the clock back at the time the pair was still published: the copy on disk is the withdrawal.
        Assert.Empty(await Pairs(pool, Noon).LoadAsync("standard", "MAGE"));
        Assert.Empty(await Pairs(pool, Noon + TimeSpan.FromHours(1)).LoadAsync("standard", "MAGE"));
    }

    [Fact]
    public async Task A_disk_that_refuses_the_withdrawal_leaves_no_older_pairs_behind()
    {
        var pool = new FakePool(Published());
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));

        // The copy is past its age, the pool answers "no pairs" and the write of that answer fails.
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Answer();
        using var http = new HttpClient(pool);
        var broken = new MulliganPairs(http, () => Pool, _cache) { Time = _time, WriteFile = (_, _) => false };
        Assert.Empty(await broken.LoadAsync("standard", "MAGE"));
        Assert.Empty(Files());

        // Offline now, after a restart, and with the clock back where the old copy would have been young.
        pool.Status = null;
        Assert.Empty(await broken.LoadAsync("standard", "MAGE"));
        Assert.Empty(await Pairs(pool).LoadAsync("standard", "MAGE"));
        Assert.Empty(await Pairs(pool, Noon + TimeSpan.FromHours(1)).LoadAsync("standard", "MAGE"));
    }

    [Fact]
    public async Task A_copy_that_can_be_neither_replaced_nor_deleted_is_never_read_again()
    {
        var pool = new FakePool(Published());
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Answer();
        using var http = new HttpClient(pool);
        var broken = new MulliganPairs(http, () => Pool, _cache) { Time = _time, WriteFile = (_, _) => false, DeleteFile = _ => false };

        Assert.Empty(await broken.LoadAsync("standard", "MAGE"));
        Assert.Single(Files());

        // The old pairs are still on disk and, by a clock set back, young again: nobody in this process reads them.
        pool.Status = null;
        Assert.Empty(await broken.LoadAsync("standard", "MAGE"));
        Assert.Empty(await Pairs(pool, Noon + TimeSpan.FromHours(1)).LoadAsync("standard", "MAGE"));

        // The next answer that does reach the disk is read as usual.
        (pool.Status, pool.Body) = (HttpStatusCode.OK, Published());
        var asked = pool.Requests.Count;
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));
        Assert.Equal(asked + 1, pool.Requests.Count);
    }

    [Fact]
    public async Task A_copy_dated_in_the_future_is_not_young()
    {
        var pool = new FakePool(Published());
        Assert.Single(await Pairs(pool).LoadAsync("standard", "MAGE"));

        // The clock went back an hour: the copy's age cannot be told, so it is not used and is deleted.
        pool.Status = null;
        Assert.Empty(await Pairs(pool, Noon - TimeSpan.FromHours(1)).LoadAsync("standard", "MAGE"));
        Assert.Empty(Files());
        Assert.Empty(await Pairs(pool).LoadAsync("standard", "MAGE"));
    }

    [Fact]
    public async Task An_answer_held_in_flight_keeps_every_later_lookup_behind_it()
    {
        var pool = new FakePool(Published()) { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var first = Pairs(pool).LoadAsync("standard", "MAGE");
        await pool.Arrived.Task.WaitAsync(TimeSpan.FromMinutes(1));

        // Meanwhile the pool changes its answer, and another reader of the same file asks: it waits its turn instead of
        // racing, so the two answers cannot land on disk out of order.
        pool.Body = Answer();
        var second = Pairs(pool).LoadAsync("standard", "MAGE");
        Assert.False(second.IsCompleted);
        Assert.Single(pool.Requests);

        pool.Hold.SetResult();
        Assert.Single(await first);
        // It reads what the first one just fetched (seconds old), and asks the pool for nothing.
        Assert.Single(await second);
        Assert.Single(pool.Requests);

        // A caller that gives up while waiting its turn gets nothing and no exception.
        pool.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        var held = Pairs(pool).LoadAsync("standard", "MAGE");
        using var cancel = new CancellationTokenSource();
        var waiting = Pairs(pool).LoadAsync("standard", "MAGE", cancel.Token);
        await cancel.CancelAsync();
        Assert.Empty(await waiting);
        pool.Hold.SetResult();
        Assert.Empty(await held);
    }

    [Fact]
    public async Task The_external_guide_is_out_before_the_pool_answers_and_the_pairs_join_it_later()
    {
        var pool = new FakePool(GuidePairs(), "WARLOCK") { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var api = new FixtureApi();
        var (source, changed) = Source(api, pool);
        var deck = new DeckDefinition("Deck", 0, string.Empty, "FT_STANDARD", [], api.DeckList(Warlock));

        Assert.Null(source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty));
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        await pool.Arrived.Task.WaitAsync(TimeSpan.FromMinutes(1));

        // The pool has not answered: the guide of the deck is there all the same, without pairs.
        var guide = source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty)!;
        Assert.Equal(MulliganScope.Deck, guide.Scope);
        Assert.Empty(guide.Pairs);
        Assert.False(source.PairsWork.IsCompleted);

        pool.Hold.SetResult();
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        guide = source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty)!;

        // The third row is below the pool's threshold and never read.
        Assert.Equal(["PAIR_B", "PAIR_C"], guide.Pairs.Select(p => p.CardB));
        Assert.Equal(["GET https://pool.test/stats/v1/events/mulligan-pairs?fmt=standard&cls=WARLOCK"], pool.Requests);
        var hand = new[] { new OfferedCard(1, "PAIR_A", "A", 1), new OfferedCard(2, "PAIR_B", "B", 2), new OfferedCard(3, "PAIR_C", "C", 3) };
        var view = MulliganGuide.Compute(guide, hand, null, null, null, guide.Name)!;
        // +5 points over 4,000 games a cell (56 - 50 - 50 + 49): its nominal interval is clear of zero. The second
        // pair's +4 rests on 60 games a cell and is not listed.
        var line = Assert.Single(view.Synergies);
        Assert.Equal((1, 2, 4000), (line.EntityA, line.EntityB, line.Games));
        Assert.Equal(0.05, line.Interaction, 9);
        Assert.True(line.Margin < 0.05);
        Assert.True(view.HasPairData);
    }

    [Fact]
    public async Task Pairs_that_arrive_for_a_deck_no_longer_played_are_discarded()
    {
        var pool = new FakePool(GuidePairs(), "WARLOCK") { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), HoldOnlyFirst = true };
        var api = new FixtureApi();
        var (source, changed) = Source(api, pool);
        var warlock = new DeckDefinition("Warlock", 0, string.Empty, "FT_STANDARD", [], api.DeckList(Warlock));
        var rogue = new DeckDefinition("Rogue", 0, string.Empty, "FT_STANDARD", [], api.DeckList(Rogue));

        Assert.Null(source.For("WARLOCK", warlock, "FT_STANDARD", CardDb.Empty));
        await pool.Arrived.Task.WaitAsync(TimeSpan.FromMinutes(1));

        // The player is in another game with another deck before the pool answered for the first one.
        GuideArchetype? guide;
        while ((guide = source.For("ROGUE", rogue, "FT_STANDARD", CardDb.Empty)) is null)
        {
            Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        }

        pool.Hold.SetResult();
        await source.PairsWork.WaitAsync(TimeSpan.FromMinutes(1));

        guide = source.For("ROGUE", rogue, "FT_STANDARD", CardDb.Empty)!;
        Assert.Equal("ROGUE", guide.Class);
        Assert.Empty(guide.Pairs);

        // The late answer is still the Warlock's: back on that deck its pairs are there.
        while (source.For("WARLOCK", warlock, "FT_STANDARD", CardDb.Empty) is not { Pairs.Count: 2 })
        {
            Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        }

        Assert.Equal(1, pool.Requests.Count(r => r.EndsWith("cls=WARLOCK", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Pairs_in_memory_are_asked_again_in_time_dropped_when_withdrawn_and_dropped_at_once_with_the_pools_address()
    {
        var pool = new FakePool(GuidePairs(), "WARLOCK");
        var api = new FixtureApi();
        var address = Pool;
        var (source, changed) = Source(api, pool, () => address);
        var deck = new DeckDefinition("Deck", 0, string.Empty, "FT_STANDARD", [], api.DeckList(Warlock));
        GuideArchetype? Guide() => source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty);
        async Task Until(Func<GuideArchetype?, bool> shown)
        {
            while (!shown(Guide()))
            {
                Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
            }
        }

        await Until(g => g is { Pairs.Count: 2 });
        Assert.Single(pool.Requests);

        // The pool withdrew the pairs. Inside the refresh time nothing is looked up at all.
        pool.Body = ForClass(Answer(), "WARLOCK");
        _time.Advance(MulliganGuideSource.PairsRefresh - TimeSpan.FromSeconds(1));
        Assert.Equal(2, Guide()!.Pairs.Count);
        await source.PairsWork;
        Assert.Single(pool.Requests);

        // Past it the pairs are looked up again; the copy on disk is still young, so the pool is not asked.
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Guide()!.Pairs.Count);
        await source.PairsWork;
        Assert.Equal(2, Guide()!.Pairs.Count);
        Assert.Single(pool.Requests);

        // Once the copy is past its six hours the pool is asked, answers "no pairs", and the guide loses them.
        _time.Advance(MulliganPairs.MaxAge);
        await Until(g => g is { Pairs.Count: 0 });
        Assert.Equal(2, pool.Requests.Count);

        // Published again, then the pool goes down: after the copy's six hours the pairs are dropped, not kept.
        pool.Body = ForClass(GuidePairs(), "WARLOCK");
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        await Until(g => g is { Pairs.Count: 2 });
        pool.Status = null;
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        await Until(g => g is { Pairs.Count: 0 });

        // Another pool in the settings: the pairs of the old one are gone in the same call, before anything is asked.
        pool.Status = HttpStatusCode.OK;
        _time.Advance(MulliganPairs.MaxAge + TimeSpan.FromMinutes(1));
        await Until(g => g is { Pairs.Count: 2 });
        address = "https://other.test/stats";
        Assert.Empty(Guide()!.Pairs);
        await Until(g => g is { Pairs.Count: 2 });
        Assert.StartsWith("GET https://other.test/stats/v1/events/mulligan-pairs", pool.Requests[^1], StringComparison.Ordinal);

        // No pool at all: dropped at once, and nothing is asked.
        var asked = pool.Requests.Count;
        address = string.Empty;
        Assert.Empty(Guide()!.Pairs);
        await source.PairsWork;
        Assert.Equal(asked, pool.Requests.Count);
    }

    [Fact]
    public async Task A_backwards_wall_clock_drops_live_guide_pairs_before_the_refresh_deadline()
    {
        var pool = new FakePool(GuidePairs(), "WARLOCK");
        var api = new FixtureApi();
        var clock = new OffsetClock(_time);
        var (source, changed) = Source(api, pool, time: clock);
        var deck = new DeckDefinition("Deck", 0, string.Empty, "FT_STANDARD", [], api.DeckList(Warlock));
        GuideArchetype? Guide() => source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty);
        while (Guide() is not { Pairs.Count: 2 })
        {
            Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        }

        // This is the same live source, not a new disk reader. The UTC clock alone moves back a day;
        // its monotonic timestamp has not advanced through even one five-minute refresh interval.
        clock.Offset = -TimeSpan.FromDays(1);
        pool.Body = ForClass(Answer(), "WARLOCK");
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Hold = hold;
        try
        {
            Assert.Empty(Guide()!.Pairs);
        }
        finally
        {
            hold.TrySetResult();
        }

        await source.PairsWork.WaitAsync(TimeSpan.FromMinutes(1));
        Assert.Empty(Guide()!.Pairs);
        Assert.Equal(2, pool.Requests.Count);
    }

    private sealed class OffsetClock(TimeProvider clock) : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + Offset;

        public override long GetTimestamp() => clock.GetTimestamp();

        public override long TimestampFrequency => clock.TimestampFrequency;
    }

    private (MulliganGuideSource Source, SemaphoreSlim Changed) Source(FixtureApi api, FakePool pool, Func<string>? address = null, TimeProvider? time = null)
    {
        var source = new MulliganGuideSource(new HearthpulseData(new HearthpulseApi(new HttpClient(api), "https://api.test/"), Path.Combine(_cache, "api")),
            new MulliganPairs(new HttpClient(pool), address ?? (() => Pool), Path.Combine(_cache, "pool")) { Time = time ?? _time }) { Time = time ?? _time };
        var changed = new SemaphoreSlim(0);
        source.Changed += () => changed.Release();
        return (source, changed);
    }

    /// <summary>A reader of the pool with the test's clock, or with a clock of its own (a restart at another time).</summary>
    private MulliganPairs Pairs(FakePool pool, DateTimeOffset? now = null) =>
        new(new HttpClient(pool), () => Pool, _cache) { Time = now is { } at ? new FakeTimeProvider(at) : _time };

    private string[] Files() => Directory.Exists(_cache) ? Directory.GetFiles(_cache, "*.json") : [];

    private static string Row(string a, string b, int installs, string both, string onlyA, string onlyB, string none) =>
        $$"""{"a":"{{a}}","b":"{{b}}","installs":{{installs}},"both":{{both}},"onlyA":{{onlyA}},"onlyB":{{onlyB}},"none":{{none}}}""";

    private static string Answer(params string[] rows) =>
        $$"""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"MAGE","window":30,"updated":1794700000000,"minGames":200,"minInstalls":5,"pairs":[{{string.Join(',', rows)}}]}""";

    private static string ForClass(string body, string cls) => body.Replace("\"cls\":\"MAGE\"", $"\"cls\":\"{cls}\"", StringComparison.Ordinal);

    private static string Published() => Answer(Row("CARD_A", "CARD_B", 7, "[412,240]", "[200,100]", "[100,45]", "null"));

    private static string GuidePairs() => Answer(
        Row("PAIR_A", "PAIR_B", 9, "[4000,2240]", "[4000,2000]", "[4000,2000]", "[4000,1960]"),
        Row("PAIR_A", "PAIR_C", 9, "[200,112]", "[60,30]", "[60,30]", "[60,29]"),
        Row("PAIR_B", "PAIR_C", 9, "[150,150]", "[60,0]", "[60,0]", "[60,60]"));

    /// <summary>
    /// Answers every request with the status and body it had when the request arrived; a null status throws like a
    /// machine without network. With <see cref="Hold"/> the answer waits until the test lets it go.
    /// </summary>
    private sealed class FakePool : HttpMessageHandler
    {
        private int _arrived;

        public FakePool(string body) => Body = body;

        /// <summary>The same rows answered for another class.</summary>
        public FakePool(string body, string cls) => Body = ForClass(body, cls);

        public HttpStatusCode? Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Only the first request waits for <see cref="Hold"/>.</summary>
        public bool HoldOnlyFirst { get; init; }

        /// <summary>Completes when the first request has arrived.</summary>
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = (Status, Body);
            int order;
            lock (Requests)
            {
                Requests.Add($"{request.Method} {request.RequestUri}");
                order = ++_arrived;
            }

            Arrived.TrySetResult();
            if (Hold is { } hold && (!HoldOnlyFirst || order == 1))
            {
                await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return status is { } code
                ? new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : throw new HttpRequestException("No route to host");
        }
    }

    /// <summary>The data API of the per-deck mulligan fixtures: the list of Standard decks and two decks' all-games slices.</summary>
    private sealed class FixtureApi : HttpMessageHandler
    {
        private static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mulligan", name));

        public Dictionary<int, int> DeckList(string deckId)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Text($"hsreplay-deck-mulligan-{deckId}-legend-all-any.json"));
            return HearthpulseData.ParseDeckMulligan(doc.RootElement.GetProperty("data"), default, deckId)!.DeckList.ToDictionary(c => c.Key, c => c.Value);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var body = path.Contains("v1/hsreplay/mulligan/decks?format=standard", StringComparison.Ordinal) ? Text("hsreplay-mulligan-decks-standard.json") : null;
            foreach (var deck in new[] { Warlock, Rogue })
            {
                if (path.Contains($"v1/hsreplay/mulligan?deck={deck}&format=standard&rank=legend&initiative=all&opponent_class=any", StringComparison.Ordinal))
                {
                    body = Text($"hsreplay-deck-mulligan-{deck}-legend-all-any.json");
                }
            }

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"Not Found\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

using System.Net;
using System.Text;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// Opening-hand pairs from the stats pool (plan 2.4.2, metric <c>mulligan.pair_winrate</c>) over a fake pool: what is
/// read and what is dropped, the disk cache and its old copy offline, an endpoint that is not plain https, and the pair
/// reaching the guide of a deck. The answers are synthetic, in the shape <c>server/pool/src/events.js</c> writes.
/// </summary>
public sealed class MulliganPairsTests : IDisposable
{
    private const string Pool = "https://pool.test/stats";

    private readonly string _cache = TestDataFolder.Scratch("mullpairs");

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Fact]
    public async Task A_published_pair_is_read_with_its_four_cells_from_the_pools_route()
    {
        var pool = new FakePool(Answer(Row("CARD_A", "CARD_B", 7, "[412,240]", "[200,100]", "[100,45]", "null")));

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
            Row("CARD_A", "CARD_G", 9, "[300,150]", "[40,41]", "[0,0]", "[40]")));

        var pairs = await Pairs(pool).LoadAsync("standard", "MAGE");

        Assert.Equal(["CARD_B", "CARD_G"], pairs.Select(p => p.CardB));
        // A cell that cannot be true is hidden, not repaired.
        Assert.Equal((null, null, null), (pairs[1].OnlyA, pairs[1].OnlyB, pairs[1].Neither));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://pool.test/stats")]
    [InlineData("pool.test/stats")]
    [InlineData("https://user:secret@pool.test/stats")]
    public async Task Without_a_plain_https_pool_the_list_is_empty_and_nothing_is_asked(string endpoint)
    {
        var pool = new FakePool(Answer(Row("CARD_A", "CARD_B", 7, "[412,240]", "[200,100]", "[100,45]", "[50,20]")));
        using var http = new HttpClient(pool);

        Assert.Empty(await new MulliganPairs(http, () => endpoint, _cache).LoadAsync("standard", "MAGE"));
        Assert.Empty(pool.Requests);
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
    public async Task A_fresh_answer_is_served_from_disk_and_an_old_one_stands_in_while_the_pool_is_down()
    {
        var pool = new FakePool(Answer(Row("CARD_A", "CARD_B", 7, "[412,240]", "[200,100]", "[100,45]", "[50,20]")));
        var pairs = Pairs(pool);

        Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        Assert.Single(pool.Requests);
        var file = Assert.Single(Directory.GetFiles(_cache));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));

        // Past six hours the pool is asked again; it is down, so the old copy answers.
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - MulliganPairs.MaxAge - TimeSpan.FromMinutes(1));
        pool.Status = null;
        Assert.Equal("CARD_B", Assert.Single(await pairs.LoadAsync("standard", "MAGE")).CardB);
        Assert.Equal(2, pool.Requests.Count);

        // An error status or an answer that is not this metric's keeps the old copy too, and never replaces it.
        foreach (var (status, body) in new[] { (HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.OK, "not json"), (HttpStatusCode.OK, Answer().Replace("mull-pairs-1", "mull-pairs-2", StringComparison.Ordinal)) })
        {
            (pool.Status, pool.Body) = (status, body);
            Assert.Single(await pairs.LoadAsync("standard", "MAGE"));
        }

        Assert.Contains("CARD_B", await File.ReadAllTextAsync(file), StringComparison.Ordinal);

        // A copy older than a week describes a window long gone: nothing is shown from it.
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - MulliganPairs.MaxStale - TimeSpan.FromMinutes(1));
        pool.Status = null;
        Assert.Empty(await pairs.LoadAsync("standard", "MAGE"));

        // Another class or another pool has a file of its own.
        Assert.Empty(await pairs.LoadAsync("standard", "PRIEST"));
        using var http = new HttpClient(pool);
        Assert.Empty(await new MulliganPairs(http, () => "https://other.test", _cache).LoadAsync("standard", "MAGE"));
    }

    [Fact]
    public async Task Without_a_pool_answer_or_a_copy_the_list_is_empty_and_nothing_throws()
    {
        foreach (var (status, body) in new (HttpStatusCode?, string)[]
                 {
                     (null, string.Empty), (HttpStatusCode.NotFound, "{\"error\":\"not found\"}"), (HttpStatusCode.OK, "[]"),
                     (HttpStatusCode.OK, Answer().Replace("\"cls\":\"MAGE\"", "\"cls\":\"PRIEST\"", StringComparison.Ordinal)),
                     (HttpStatusCode.OK, new string(' ', 2 * 1024 * 1024)),
                 })
        {
            Assert.Empty(await Pairs(new FakePool(body) { Status = status }).LoadAsync("standard", "MAGE"));
        }

        Assert.Empty(Directory.Exists(_cache) ? Directory.GetFiles(_cache) : []);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.Empty(await Pairs(new FakePool(Answer()) { Hold = true }).LoadAsync("standard", "MAGE", cancelled.Token));
    }

    [Fact]
    public async Task The_guide_of_a_deck_carries_the_pools_pairs_and_shows_the_one_whose_difference_is_real()
    {
        // A homebrew the data API knows nothing about still needs a guide to show a pair in: the per-deck guide of the
        // DeckMulliganLoadTests fixtures (C'Thun Warlock) stands in for "any guide the source found".
        var api = new FixtureApi();
        var list = api.DeckList();
        var pool = new FakePool(Answer(
            // +5 points over 4,000 games a cell (56 - 50 - 50 + 49): told from zero. The second pair's +5 rests on 60
            // games a cell and is not; the third is below the pool's threshold and never read.
            Row("PAIR_A", "PAIR_B", 9, "[4000,2240]", "[4000,2000]", "[4000,2000]", "[4000,1960]"),
            Row("PAIR_A", "PAIR_C", 9, "[200,112]", "[60,30]", "[60,30]", "[60,29]"),
            Row("PAIR_B", "PAIR_C", 9, "[150,150]", "[60,0]", "[60,0]", "[60,60]")), "WARLOCK");
        using var poolHttp = new HttpClient(pool);
        using var apiHttp = new HttpClient(api);
        var source = new MulliganGuideSource(new HearthpulseData(new HearthpulseApi(apiHttp, "https://api.test/"), Path.Combine(_cache, "api")),
            new MulliganPairs(poolHttp, () => Pool, Path.Combine(_cache, "pool")));
        var changed = new SemaphoreSlim(0);
        source.Changed += () => changed.Release();
        var deck = new DeckDefinition("Deck", 0, string.Empty, "FT_STANDARD", [], list);

        Assert.Null(source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty));
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        var guide = source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty)!;

        Assert.Equal(MulliganScope.Deck, guide.Scope);
        Assert.Equal(["PAIR_B", "PAIR_C"], guide.Pairs.Select(p => p.CardB));
        Assert.Equal(["GET https://pool.test/stats/v1/events/mulligan-pairs?fmt=standard&cls=WARLOCK"], pool.Requests);
        var hand = new[] { new OfferedCard(1, "PAIR_A", "A", 1), new OfferedCard(2, "PAIR_B", "B", 2), new OfferedCard(3, "PAIR_C", "C", 3) };
        var view = MulliganGuide.Compute(guide, hand, null, null, null, guide.Name)!;
        var synergy = Assert.Single(view.Synergies);
        Assert.Equal((1, 2, 4000), (synergy.EntityA, synergy.EntityB, synergy.Games));
        Assert.Equal(0.05, synergy.Interaction, 9);
        Assert.True(synergy.Margin < 0.05);
        Assert.True(view.HasPairData);
    }

    private MulliganPairs Pairs(FakePool pool) => new(new HttpClient(pool), () => Pool, _cache);

    private static string Row(string a, string b, int installs, string both, string onlyA, string onlyB, string none) =>
        $$"""{"a":"{{a}}","b":"{{b}}","installs":{{installs}},"both":{{both}},"onlyA":{{onlyA}},"onlyB":{{onlyB}},"none":{{none}}}""";

    private static string Answer(params string[] rows) => Answer("MAGE", rows);

    private static string Answer(string cls, string[] rows) =>
        $$"""{"v":1,"metric":"mulligan.pair_winrate","rules":"mull-pairs-1","fmt":"standard","cls":"{{cls}}","window":30,"updated":1794700000000,"minGames":200,"minInstalls":5,"pairs":[{{string.Join(',', rows)}}]}""";

    /// <summary>Answers every request with one status and body; a null status throws like a machine without network.</summary>
    private sealed class FakePool : HttpMessageHandler
    {
        public FakePool(string body) => Body = body;

        /// <summary>The same rows answered for another class.</summary>
        public FakePool(string body, string cls) => Body = body.Replace("\"cls\":\"MAGE\"", $"\"cls\":\"{cls}\"", StringComparison.Ordinal);

        public HttpStatusCode? Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; }

        /// <summary>The request waits until its caller gives up.</summary>
        public bool Hold { get; init; }

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            if (Hold)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            return Status is { } code
                ? new HttpResponseMessage(code) { Content = new StringContent(Body, Encoding.UTF8, "application/json") }
                : throw new HttpRequestException("No route to host");
        }
    }

    /// <summary>The data API of the per-deck mulligan fixtures: the list of Standard decks and the Warlock deck's all-games slice.</summary>
    private sealed class FixtureApi : HttpMessageHandler
    {
        private const string Warlock = "7X8Zqlb1wBPwkkaQVGT8ee";

        private static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mulligan", name));

        public Dictionary<int, int> DeckList()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Text($"hsreplay-deck-mulligan-{Warlock}-legend-all-any.json"));
            return HearthpulseData.ParseDeckMulligan(doc.RootElement.GetProperty("data"), default, Warlock)!.DeckList.ToDictionary(c => c.Key, c => c.Value);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var body = path.Contains("v1/hsreplay/mulligan/decks?format=standard", StringComparison.Ordinal) ? Text("hsreplay-mulligan-decks-standard.json")
                : path.Contains($"v1/hsreplay/mulligan?deck={Warlock}&format=standard&rank=legend&initiative=all&opponent_class=any", StringComparison.Ordinal)
                    ? Text($"hsreplay-deck-mulligan-{Warlock}-legend-all-any.json")
                    : null;
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"Not Found\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

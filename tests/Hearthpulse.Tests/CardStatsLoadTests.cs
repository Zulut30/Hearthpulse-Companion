using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Api;

namespace Hearthpulse.Tests;

/// <summary>
/// The card page's loader (<see cref="HearthpulseData.LoadCardStatsAsync"/>) over a fake server answering with cut-down
/// real answers (Fixtures/Cards, made as its README says): numbers, the trimmed cache, a missing source remembered,
/// the old copy offline, and cancellation that neither throws nor leaves a file. The cache goes to a scratch folder.
/// </summary>
public sealed class CardStatsLoadTests : IDisposable
{
    private readonly string _cache = TestDataFolder.Scratch("cardstats");

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Theory]
    [InlineData("NaN%")]
    [InlineData("Infinity%")]
    [InlineData("-Infinity%")]
    public async Task Non_finite_percentages_are_missing_and_do_not_break_the_cache(string invalid)
    {
        var answer = Fixture("cards-standard.json").Replace("22.36%", invalid, StringComparison.Ordinal);
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", answer);
        var slice = await Data(server).LoadCardStatsAsync("standard");
        Assert.NotNull(slice);
        Assert.Null(slice.Find("BE_014", 0)!.DeckShare);
        Assert.True(File.Exists(Path.Combine(_cache, "card-stats-standard.json")));
    }

    [Fact]
    public void Repeated_rows_do_not_make_a_complete_card_dataset()
    {
        using var doc = JsonDocument.Parse("""{"structured":{"cards":[{"id":"A"},{"id":"A"},{"id":"A"}]}}""");
        Assert.False(HearthpulseData.UsableCardStats(doc.RootElement, 2));
    }

    [Fact]
    public async Task Standard_numbers_come_as_fractions_with_the_slice_and_date_the_answer_names()
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));

        var slice = await Data(server).LoadCardStatsAsync("standard");

        Assert.NotNull(slice);
        Assert.Equal("standard", slice.Format);
        // The source's id says "legend"; the answer says Gold, and the answer is what the page names.
        Assert.Equal("GOLD", slice.RankRange);
        Assert.Equal("LAST_14_DAYS", slice.TimeRange);
        Assert.Equal(new DateTime(2026, 11, 16, 6, 4, 12, DateTimeKind.Utc), slice.FetchedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(9, slice.Cards.Count);

        // "22.36%", "55.13%", 1.99, 143721, "53.61%", "54.07%", "50.20%", "52.59%", 6.41 in the fixture.
        var spawn = slice.Find("BE_014", 0)!;
        Assert.Equal(132347, spawn.DbfId);
        Assert.Equal(0.2236, spawn.DeckShare!.Value, 6);
        Assert.Equal(0.5513, spawn.DeckWinRate!.Value, 6);
        Assert.Equal(1.99, spawn.AverageCopies!.Value, 6);
        Assert.Equal(143721, spawn.TimesPlayed);
        Assert.Equal(0.5361, spawn.PlayedWinRate!.Value, 6);
        Assert.Equal(0.5407, spawn.DrawnWinRate!.Value, 6);
        Assert.Equal(0.5020, spawn.KeepRate!.Value, 6);
        Assert.Equal(0.5259, spawn.OpeningHandWinRate!.Value, 6);
        Assert.Equal(6.41, spawn.AverageTurnPlayed!.Value, 6);

        // Another printing's id finds the card through its dbf id; an unknown card is not there.
        Assert.Same(spawn, slice.Find("CORE_BE_014", 132347));
        Assert.Null(slice.Find("NOPE_001", 1));
        Assert.Null(slice.Find("NOPE_001", 0));
    }

    [Fact]
    public async Task A_number_the_source_left_out_stays_missing()
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));

        var slice = await Data(server).LoadCardStatsAsync("standard");

        // Played 0 times: no "when played" win rate and no turn, though the deck numbers are there.
        var unplayed = slice!.Find("CATA_820", 0)!;
        Assert.Equal(0, unplayed.TimesPlayed);
        Assert.Null(unplayed.PlayedWinRate);
        Assert.Null(unplayed.AverageTurnPlayed);
        Assert.Equal(0.4943, unplayed.DeckWinRate!.Value, 6);

        // No keep_percentage and no opening_hand_winrate in the row.
        var noMulligan = slice.Find("TIME_020", 0)!;
        Assert.Null(noMulligan.KeepRate);
        Assert.Null(noMulligan.OpeningHandWinRate);
        Assert.Equal(0.6721, noMulligan.PlayedWinRate!.Value, 6);
    }

    [Fact]
    public async Task The_cache_keeps_a_trimmed_copy_and_serves_the_same_numbers_without_asking_again()
    {
        var fixture = Fixture("cards-standard.json");
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", fixture);
        var data = Data(server);

        var first = await data.LoadCardStatsAsync("standard");
        var second = await data.LoadCardStatsAsync("standard");

        Assert.Single(server.Requests);
        var file = Path.Combine(_cache, "card-stats-standard.json");
        var cached = await File.ReadAllTextAsync(file);
        // Names, the second copy of the list and the collector's details are not kept.
        Assert.True(cached.Length < fixture.Length / 4, $"cache {cached.Length} of {fixture.Length}");
        Assert.DoesNotContain("name", cached, StringComparison.Ordinal);
        Assert.DoesNotContain("hsreplay_extracted", cached, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", cached, StringComparison.Ordinal);
        using (var doc = JsonDocument.Parse(cached))
        {
            Assert.Equal("2026-11-16T06:04:12.257082+00:00", doc.RootElement.GetProperty("meta").GetProperty("fetched_at").GetString());
        }

        Assert.Equal(first!.FetchedAt, second!.FetchedAt);
        Assert.Equal(first.RankRange, second.RankRange);
        Assert.Equal(first.TimeRange, second.TimeRange);
        Assert.Equal(first.Cards.Keys.Order(StringComparer.Ordinal), second.Cards.Keys.Order(StringComparer.Ordinal));
        Assert.All(first.Cards, pair => Assert.Equal(pair.Value, second.Cards[pair.Key]));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
    }

    [Fact]
    public async Task Wild_reads_its_own_list()
    {
        var server = new Server().Route("datasets/hsreplay_cards_wild_legend_14d", Fixture("cards-wild.json"));

        var slice = await Data(server).LoadCardStatsAsync("wild");

        Assert.Equal("datasets/hsreplay_cards_wild_legend_14d", Assert.Single(server.Requests));
        Assert.Equal("LEGEND", slice!.RankRange);
        Assert.Equal(new DateTime(2026, 11, 16, 8, 36, 7, DateTimeKind.Utc), slice.FetchedAt!.Value, TimeSpan.FromSeconds(1));
        // "15.10%" and 12272 in the fixture.
        var prep = slice.Find("CORE_EX1_145", 0)!;
        Assert.Equal(0.1510, prep.DeckShare!.Value, 6);
        Assert.Equal(12272, prep.TimesPlayed);
        Assert.True(File.Exists(Path.Combine(_cache, "card-stats-wild.json")));
    }

    [Fact]
    public async Task A_format_without_a_list_asks_nothing()
    {
        var server = new Server();

        Assert.Null(await Data(server).LoadCardStatsAsync("twist"));
        Assert.Null(await Data(server).LoadCardStatsAsync("../standard"));

        Assert.Empty(server.Requests);
        Assert.False(Directory.Exists(_cache) && Directory.GetFiles(_cache).Length > 0);
    }

    [Fact]
    public async Task A_missing_source_is_remembered_instead_of_asked_on_every_open()
    {
        // No route: the server answers 404 {"detail":"Unknown source"}.
        var server = new Server();
        var data = Data(server);

        Assert.Null(await data.LoadCardStatsAsync("standard"));
        Assert.Null(await data.LoadCardStatsAsync("standard"));

        Assert.Single(server.Requests);
        Assert.Equal("{\"data\":null}", await File.ReadAllTextAsync(Path.Combine(_cache, "card-stats-standard.json")));
    }

    [Fact]
    public async Task Offline_serves_the_old_copy_with_its_own_date()
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        var data = Data(server);
        var fresh = await data.LoadCardStatsAsync("standard");

        // The copy is past its six hours and the server cannot be reached.
        var file = Path.Combine(_cache, "card-stats-standard.json");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - HearthpulseData.MaxAge - TimeSpan.FromHours(30));
        server.Offline = true;
        var stale = await data.LoadCardStatsAsync("standard");

        Assert.Equal(2, server.Requests.Count);
        Assert.NotNull(stale);
        Assert.Equal(fresh!.FetchedAt, stale.FetchedAt);
        Assert.Equal(fresh.Find("BE_014", 0), stale.Find("BE_014", 0));
    }

    [Fact]
    public async Task Offline_without_a_copy_is_nothing_and_leaves_no_file()
    {
        var server = new Server { Offline = true };

        Assert.Null(await Data(server).LoadCardStatsAsync("standard"));

        Assert.False(File.Exists(Path.Combine(_cache, "card-stats-standard.json")));
    }

    [Fact]
    public async Task A_server_that_does_not_answer_in_time_ends_the_load_with_nothing_or_with_the_old_copy()
    {
        // The Wild list is 4 MB: on a slow line the 20-second limit can pass before it is in.
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var slow = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }
            .Route("datasets/hsreplay_cards_wild_legend_14d", Fixture("cards-wild.json"));
        var data = new HearthpulseData(new HearthpulseApi(new HttpClient(slow), "https://api.test/") { Time = time }, _cache) { MinCardStatRows = 5 };

        var load = data.LoadCardStatsAsync("wild");
        await slow.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(load.IsCompleted);
        time.Advance(HearthpulseApi.Timeout);

        // The load ends by itself: nothing to show, nothing remembered, so the next try asks again.
        Assert.Null(await load.WaitAsync(TimeSpan.FromSeconds(30)));
        var file = Path.Combine(_cache, "card-stats-wild.json");
        Assert.False(File.Exists(file));

        // With a copy from an earlier day the same timeout serves that copy, under its own date.
        var fresh = await Data(new Server().Route("datasets/hsreplay_cards_wild_legend_14d", Fixture("cards-wild.json"))).LoadCardStatsAsync("wild");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow - HearthpulseData.MaxAge - TimeSpan.FromHours(30));
        var slowAgain = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }
            .Route("datasets/hsreplay_cards_wild_legend_14d", Fixture("cards-wild.json"));
        var again = new HearthpulseData(new HearthpulseApi(new HttpClient(slowAgain), "https://api.test/") { Time = time }, _cache) { MinCardStatRows = 5 }.LoadCardStatsAsync("wild");
        await slowAgain.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        time.Advance(HearthpulseApi.Timeout);
        var stale = await again.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(stale);
        Assert.Equal(fresh!.FetchedAt, stale.FetchedAt);
        Assert.Equal(fresh.Find("CORE_EX1_145", 0), stale.Find("CORE_EX1_145", 0));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
        slow.Hold!.TrySetResult();
        slowAgain.Hold!.TrySetResult();
    }

    public static TheoryData<string, string> UnusableAnswers() => new()
    {
        { "no rows", """{"fetched_at": "2026-11-17T06:04:00+00:00", "data": {"structured": {"type": "card_stats", "blocked": false, "cards": []}}}""" },
        { "blocked", Fixture("cards-standard.json").Replace("\"blocked\": false", "\"blocked\": true", StringComparison.Ordinal) },
        { "another type", Fixture("cards-standard.json").Replace("\"type\": \"card_stats\"", "\"type\": \"deck_stats\"", StringComparison.Ordinal) },
        { "failed validation", Fixture("cards-standard.json").Replace("\"ok\": true", "\"ok\": false", StringComparison.Ordinal) },
        { "another shape", """{"source_id": "x", "fetched_at": "not a date", "data": {"structured": {"cards": [7, {"dbfId": 3}, {"id": "A", "deck_popularity": "abc"}]}}}""" },
        { "no list at all", """{"fetched_at": "2026-11-17T06:04:00+00:00", "data": {"structured": {"type": "card_stats"}}}""" },
        { "not an object", """{"fetched_at": "2026-11-17T06:04:00+00:00", "data": [1, 2, 3]}""" },
    };

    [Theory]
    [MemberData(nameof(UnusableAnswers))]
    public async Task A_200_answer_that_is_not_a_whole_list_is_a_failed_load_and_the_good_copy_stays(string what, string body)
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        var data = Data(server);
        var good = await data.LoadCardStatsAsync("standard");
        var file = Path.Combine(_cache, "card-stats-standard.json");
        var kept = await File.ReadAllBytesAsync(file);
        var aged = DateTime.UtcNow - HearthpulseData.MaxAge - TimeSpan.FromHours(30);
        File.SetLastWriteTimeUtc(file, aged);

        // The copy is past its six hours and the server answers 200 with something that is not a card list.
        server.Route("datasets/hsreplay_cards_legend_included_popularity", body);
        var after = await data.LoadCardStatsAsync("standard");

        // The old copy is served under its own date: nothing reads as "this card is hardly played".
        Assert.True(after is not null, what);
        Assert.Equal(good!.FetchedAt, after.FetchedAt);
        Assert.Equal(good.Cards.Count, after.Cards.Count);
        Assert.Equal(good.Find("BE_014", 0), after.Find("BE_014", 0));
        // The file is the same bytes and keeps its age, so the next open asks the server again.
        Assert.Equal(kept, await File.ReadAllBytesAsync(file));
        Assert.Equal(aged, File.GetLastWriteTimeUtc(file), TimeSpan.FromSeconds(2));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
        await data.LoadCardStatsAsync("standard");
        Assert.Equal(3, server.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(UnusableAnswers))]
    public async Task Without_a_copy_such_an_answer_is_nothing_and_writes_no_file(string what, string body)
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", body);
        var data = Data(server);

        Assert.True(await data.LoadCardStatsAsync("standard") is null, what);
        Assert.False(File.Exists(Path.Combine(_cache, "card-stats-standard.json")));

        // Not remembered as "nothing" (a 404 is): the page's "Try again" reaches the server.
        Assert.Null(await data.LoadCardStatsAsync("standard"));
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_list_of_a_few_cards_is_a_cut_short_collection_not_a_list()
    {
        // The fixture's nine rows against the real floor (300; a real list holds 1 300 and more).
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        var strict = new HearthpulseData(new HearthpulseApi(new HttpClient(server), "https://api.test/"), _cache);

        Assert.Equal(HearthpulseData.MinCardRows, strict.MinCardStatRows);
        Assert.Null(await strict.LoadCardStatsAsync("standard"));
        Assert.False(File.Exists(Path.Combine(_cache, "card-stats-standard.json")));

        // The same rows pass a floor they reach, and one row fewer than the floor does not.
        using var whole = JsonDocument.Parse(Fixture("cards-standard.json"));
        Assert.True(HearthpulseData.UsableCardStats(whole.RootElement.GetProperty("data"), 9));
        Assert.False(HearthpulseData.UsableCardStats(whole.RootElement.GetProperty("data"), 10));
    }

    [Fact]
    public async Task Callers_that_ask_together_share_one_request_and_one_answer()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }
            .Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        var data = Data(server);

        // Five opens in a row while the first request is still on its way.
        var loads = Enumerable.Range(0, 5).Select(_ => data.LoadCardStatsAsync("standard")).ToList();
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(loads, l => Assert.False(l.IsCompleted));
        server.Hold!.TrySetResult();
        var slices = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(server.Requests);
        Assert.All(slices, s => Assert.Same(slices[0], s));
        Assert.Equal(9, slices[0]!.Cards.Count);

        // The next caller reads the cache; another format is another request.
        Assert.NotNull(await data.LoadCardStatsAsync("standard"));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task A_caller_that_stops_waiting_gets_nothing_and_does_not_end_the_shared_load()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }
            .Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        var data = Data(server);
        using var cancel = new CancellationTokenSource();

        var left = data.LoadCardStatsAsync("standard", cancel.Token);
        var stays = data.LoadCardStatsAsync("standard");
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();

        // The page that left gets nothing and no exception; the request it started is still the one in flight.
        Assert.Null(await left.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(stays.IsCompleted);
        Assert.False(Directory.Exists(_cache) && Directory.GetFiles(_cache).Length > 0);

        // An already cancelled token joins the same load and leaves at once: still one request.
        Assert.Null(await data.LoadCardStatsAsync("standard", cancel.Token));
        server.Hold!.TrySetResult();
        var slice = await stays.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(slice);
        Assert.Single(server.Requests);
        Assert.True(File.Exists(Path.Combine(_cache, "card-stats-standard.json")));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
    }

    /// <summary>The fixtures hold a handful of rows: the floor of a whole list is lowered to five for them.</summary>
    private HearthpulseData Data(Server server) => new(new HearthpulseApi(new HttpClient(server), "https://api.test/"), _cache) { MinCardStatRows = 5 };

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cards", name));

    /// <summary>Answers a path with its routed body, 404 "Unknown source" otherwise; can be offline or hold its answers.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public bool Offline { get; set; }

        /// <summary>While set and not completed, every answer waits for it (or for the request's cancellation).</summary>
        public TaskCompletionSource? Hold { get; init; }

        /// <summary>Completes once a held request is waiting.</summary>
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Server Route(string path, string body)
        {
            _routes[path] = body;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            lock (Requests)
            {
                Requests.Add(path);
            }

            if (Offline)
            {
                throw new HttpRequestException("No such host is known.");
            }

            if (Hold is { } hold)
            {
                Waiting.TrySetResult();
                await hold.Task.WaitAsync(cancellationToken);
            }

            return _routes.TryGetValue(path, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"Unknown source\"}", Encoding.UTF8, "application/json") };
        }
    }
}

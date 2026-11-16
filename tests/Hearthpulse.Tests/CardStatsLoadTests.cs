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
    public async Task An_answer_of_another_shape_is_an_empty_list_not_an_exception()
    {
        var server = new Server().Route("datasets/hsreplay_cards_legend_included_popularity",
            """{"source_id": "x", "fetched_at": "not a date", "data": {"structured": {"cards": [7, {"dbfId": 3}, {"id": "A", "deck_popularity": "abc", "times_played": -4}]}}}""");

        var slice = await Data(server).LoadCardStatsAsync("standard");

        Assert.NotNull(slice);
        Assert.Null(slice.FetchedAt);
        Assert.Null(slice.RankRange);
        var only = Assert.Single(slice.Cards).Value;
        Assert.Equal("A", only.CardId);
        Assert.Null(only.DeckShare);
        Assert.Null(only.TimesPlayed);
    }

    [Fact]
    public async Task A_cancelled_load_gives_nothing_without_throwing_and_writes_no_file()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) }
            .Route("datasets/hsreplay_cards_legend_included_popularity", Fixture("cards-standard.json"));
        using var cancel = new CancellationTokenSource();

        var load = Data(server).LoadCardStatsAsync("standard", cancel.Token);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();
        var slice = await load.WaitAsync(TimeSpan.FromSeconds(30));
        server.Hold!.TrySetResult();

        Assert.Null(slice);
        Assert.False(Directory.Exists(_cache) && Directory.GetFiles(_cache).Length > 0);

        // An already cancelled token: the same, and the server is not asked.
        Assert.Null(await Data(server).LoadCardStatsAsync("standard", cancel.Token));
    }

    private HearthpulseData Data(Server server) => new(new HearthpulseApi(new HttpClient(server), "https://api.test/"), _cache);

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

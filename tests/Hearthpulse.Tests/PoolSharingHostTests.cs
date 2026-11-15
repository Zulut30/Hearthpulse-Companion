using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// <see cref="PoolSharingHost"/>, what both apps send to the stats pool: on by default once the notice was shown,
/// nothing after the player turned it off, never before a live host started it, and the earlier games once. No
/// network: a handler that records the requests.
/// </summary>
public sealed class PoolSharingHostTests : IDisposable
{
    private const string Endpoint = "https://pool.test";
    private readonly string _dir = TestDataFolder.Scratch("hp-pool-host");
    private readonly HistoryStore _history;
    private readonly Recorder _pool = new();
    private readonly HttpClient _http;

    public PoolSharingHostTests()
    {
        Directory.CreateDirectory(_dir);
        _history = new HistoryStore(Path.Combine(_dir, "history.db"));
        _http = new HttpClient(_pool);
    }

    public void Dispose()
    {
        _http.Dispose();
        TestDataFolder.TryDelete(_dir);
    }

    /// <summary>The notice was shown; <paramref name="consent"/> null is a player who never touched the switch.</summary>
    private static AppSettings Noticed(bool? consent = null, string endpoint = Endpoint) =>
        new() { PoolEndpoint = endpoint, PoolConsent = consent, PoolNoticeVersion = PoolClient.NoticeVersion };

    private PoolSharingHost Host(AppSettings settings, CardDb? cards = null, bool start = true)
    {
        var host = new PoolSharingHost(settings, _http, _history, () => cards ?? CardDb.Empty) { Delay = _ => Task.CompletedTask };
        if (start)
        {
            host.Start();
        }

        return host;
    }

    private static GameOutcome Battlegrounds(int? ratingAfter = null) =>
        new("Battlegrounds", "Win", 1, 7000, ratingAfter, "BG_HERO_1", ["BG_HERO_1", "BG_HERO_2"])
        {
            Key = "20261023120000|Player#1234|GT_BATTLEGROUNDS",
            Details = new BgGameDetails(14, 6, 2, "BG_ANOMALY", ["BEAST", "DEMON"],
                [new BgMinion("BEAST_A", "A", 40, 30, true, 4, [])], ["TRK_1"], ["TRK_1"], [new TierStep(3, 2)],
                [new CombatRecord(2, 5, "BG_HERO_OTHER", "Win", 0, 3)])
            {
                Choices = [new BgChoice(3, "discover", "TB_BaconShop_Triples_01", ["A", "B", "C"], ["B"])],
                Plays = [new BgPlay(1, "BEAST_A")],
            },
        };

    private static GameOutcome Constructed() =>
        new("Ranked", "Win", 0, null, null, "HERO_08")
        {
            Key = "20261026120000|Player#1234|GT_RANKED",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            ConstructedPicks = [new ConstructedPick(4, "CORE_YOP_001", ["CARD_A", "CARD_B", "CARD_C"], ["CARD_B"])],
        };

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task After_the_notice_a_battlegrounds_game_is_one_post_of_the_game_with_its_picks_and_plays(bool? consent)
    {
        var settings = Noticed(consent);
        var saves = 0;
        var host = Host(settings);
        host.SettingsChanged += () => saves++;

        await host.ShareAsync(Battlegrounds());

        var request = Assert.Single(_pool.Requests);
        Assert.Equal((HttpMethod.Post, Endpoint + "/v1/games"), (request.Method, request.Url));
        using var json = JsonDocument.Parse(request.Body);
        var game = Assert.Single(json.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal("BG_HERO_1", game.GetProperty("picked").GetString());
        Assert.Equal(7000, game.GetProperty("bracket").GetInt32());
        Assert.Equal(1, game.GetProperty("board").GetArrayLength());
        Assert.Equal(1, game.GetProperty("choices").GetArrayLength());
        Assert.Equal(1, game.GetProperty("plays").GetArrayLength());
        // Nothing that names the player or another player's hero.
        Assert.DoesNotContain("Player#1234", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("BG_HERO_OTHER", request.Body, StringComparison.Ordinal);
        // The secret behind the game's id is new in the settings: the host is told to save them.
        Assert.Equal(64, settings.PoolSecret?.Length);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task After_the_notice_a_constructed_game_with_picks_is_one_post_of_its_picks()
    {
        var host = Host(Noticed());

        await host.ShareAsync(Constructed());

        var request = Assert.Single(_pool.Requests);
        Assert.Equal((HttpMethod.Post, Endpoint + "/v1/constructed"), (request.Method, request.Url));
        using var json = JsonDocument.Parse(request.Body);
        var game = Assert.Single(json.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal(["id", "day", "format", "won", "src", "picks"], game.EnumerateObject().Select(p => p.Name));
        Assert.Equal("""[[4,"CORE_YOP_001",["CARD_A","CARD_B","CARD_C"],["CARD_B"]]]""", game.GetProperty("picks").GetRawText());
    }

    [Fact]
    public async Task After_the_player_turned_sharing_off_nothing_is_sent_and_no_secret_is_made()
    {
        var settings = Noticed(consent: false);
        var saves = 0;
        var host = Host(settings, Cards());
        host.SettingsChanged += () => saves++;
        Assert.True(_history.Add(Earlier(0)));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.Null(settings.PoolSecret);
        Assert.False(settings.PoolHistoryShared);
        Assert.Equal(0, saves);
    }

    /// <summary>Not shown at all (0) or an answer to the question older versions asked (4): the notice comes first.</summary>
    [Theory]
    [InlineData(null, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 4)]
    public async Task Before_the_current_notice_was_shown_nothing_is_sent(bool? consent, int seen)
    {
        var settings = new AppSettings { PoolEndpoint = Endpoint, PoolConsent = consent, PoolNoticeVersion = seen };
        var host = Host(settings, Cards());
        Assert.True(_history.Add(Earlier(0)));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.Null(settings.PoolSecret);
        Assert.False(settings.PoolHistoryShared);

        // The notice answered with "Got it": the same host shares from then on.
        settings.PoolNoticeVersion = PoolClient.NoticeVersion;
        await host.ShareAsync(Battlegrounds());
        await host.ShareHistoryAsync();

        Assert.Equal(2, _pool.Requests.Count);
        Assert.True(settings.PoolHistoryShared);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://pool.test")]
    [InlineData("pool.test")]
    public async Task Without_an_https_pool_nothing_is_sent(string endpoint)
    {
        var settings = Noticed(endpoint: endpoint);
        var host = Host(settings, Cards());
        Assert.True(_history.Add(Earlier(0)));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task The_second_call_with_the_new_rating_does_not_send_the_game_again()
    {
        var host = Host(Noticed());

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Battlegrounds(ratingAfter: 7050));

        Assert.Single(_pool.Requests);
    }

    /// <summary>An off-screen render builds the services and never starts the host.</summary>
    [Fact]
    public async Task Before_a_live_host_starts_it_nothing_is_sent()
    {
        var settings = Noticed();
        var host = Host(settings, Cards(), start: false);
        Assert.True(_history.Add(Earlier(0)));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        host.OnGameFinished(Battlegrounds());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.Null(settings.PoolSecret);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task Earlier_games_of_30_days_go_once_in_batches_of_20_with_a_pause_between()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        Add25(now);
        Assert.True(_history.Add(Earlier(100, now.AddDays(-45))));
        Assert.True(_history.Add(Earlier(101, now.AddDays(-1)) with { Mode = "Ranked" }));
        var settings = Noticed();
        var pauses = new List<TimeSpan>();
        var saves = 0;
        var host = Host(settings, Cards());
        host.Now = () => now;
        host.Delay = pause =>
        {
            pauses.Add(pause);
            return Task.CompletedTask;
        };
        host.SettingsChanged += () => saves++;

        await host.ShareHistoryAsync();
        await host.ShareHistoryAsync();

        Assert.Equal([20, 5], _pool.Requests.Select(Games));
        Assert.All(_pool.Requests, r => Assert.Equal(Endpoint + "/v1/games", r.Url));
        Assert.Contains("\"src\":\"history\"", _pool.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal([PoolClient.HistoryBatchDelay, PoolClient.HistoryBatchDelay], pauses);
        Assert.True(settings.PoolHistoryShared);
        Assert.Equal(1, saves);
    }

    /// <summary>Review L-B1: the switch is asked again before every batch, not only before the first.</summary>
    [Fact]
    public async Task Sharing_turned_off_during_the_pause_stops_the_remaining_batches_and_turning_it_on_sends_them_all()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        Add25(now);
        var settings = Noticed();
        var host = Host(settings, Cards());
        host.Now = () => now;
        host.Delay = _ =>
        {
            // The player turns the switch off while the first batch's pause runs.
            settings.PoolConsent = false;
            return Task.CompletedTask;
        };

        await host.ShareHistoryAsync();

        Assert.Equal([20], _pool.Requests.Select(Games));
        Assert.False(settings.PoolHistoryShared);

        // Still off: a later call sends nothing.
        host.Delay = _ => Task.CompletedTask;
        await host.ShareHistoryAsync();
        Assert.Single(_pool.Requests);

        // Back on: the earlier games go again from the start (ids are stable, the pool stores each once).
        settings.PoolConsent = true;
        await host.ShareHistoryAsync();

        Assert.Equal([20, 20, 5], _pool.Requests.Select(Games));
        Assert.True(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task Sharing_turned_off_during_the_last_pause_does_not_mark_the_earlier_games_as_shared()
    {
        Assert.True(_history.Add(Earlier(0)));
        var settings = Noticed();
        var host = Host(settings, Cards());
        host.Delay = _ =>
        {
            settings.PoolConsent = false;
            return Task.CompletedTask;
        };

        await host.ShareHistoryAsync();

        Assert.Single(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Earlier_games_the_pool_did_not_take_are_tried_again_next_time(bool offline)
    {
        Assert.True(_history.Add(Earlier(0)));
        var settings = Noticed();
        var host = Host(settings, Cards());
        _pool.Offline = offline;
        _pool.Status = HttpStatusCode.TooManyRequests;

        await host.ShareHistoryAsync();

        Assert.Single(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);

        _pool.Offline = false;
        _pool.Status = HttpStatusCode.OK;
        await host.ShareHistoryAsync();

        Assert.Equal(2, _pool.Requests.Count);
        Assert.True(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task Earlier_games_wait_for_the_cards()
    {
        Assert.True(_history.Add(Earlier(0)));
        var settings = Noticed();

        await Host(settings).ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public Task The_apps_services_hand_every_finished_game_of_the_session_to_the_host() =>
        AppServicesTests.With(Noticed(), (services, _) =>
        {
            var handlers = (services.Session.GetType()
                .GetField(nameof(GameSession.GameFinished), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(services.Session) as Delegate)!.GetInvocationList();
            Assert.Contains(handlers, h => h.Target == services.PoolSharing && h.Method.Name == nameof(PoolSharingHost.OnGameFinished));
            return Task.CompletedTask;
        });

    private void Add25(DateTime now)
    {
        for (var i = 0; i < 25; i++)
        {
            Assert.True(_history.Add(Earlier(i, now.AddDays(-1))));
        }
    }

    private static int Games((HttpMethod Method, string Url, string Body) request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("games").GetArrayLength();
    }

    private static GameRecord Earlier(int n, DateTime? day = null)
    {
        var start = (day ?? DateTime.Now.AddDays(-1)).AddMinutes(n);
        return new GameRecord
        {
            Key = $"{start:yyyyMMddHHmmss}-{n}|Player#1234|GT_BATTLEGROUNDS",
            StartedAt = start,
            EndedAt = start.AddMinutes(20),
            Mode = "Battlegrounds",
            HeroCardId = "BG_HERO_1",
            Placement = 3,
            RatingBefore = 6789,
        };
    }

    private static CardDb Cards() => CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [{"id": "BG_HERO_1", "dbfId": 1, "name": "Hero", "type": "HERO"}]
        """)));

    /// <summary>Answers every request with <see cref="Status"/>, or fails it as a lost network does, and keeps what was asked.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Url, string Body)> _requests = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool Offline { get; set; }

        public IReadOnlyList<(HttpMethod Method, string Url, string Body)> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests)
            {
                _requests.Add((request.Method, request.RequestUri!.ToString(), body));
            }

            return Offline ? throw new HttpRequestException("No network") : new HttpResponseMessage(Status);
        }
    }
}

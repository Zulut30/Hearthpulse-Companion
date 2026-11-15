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
/// <see cref="PoolSharingHost"/>, what both apps send to the stats pool: only with the player's answer and only the
/// list they answered for, never before a live host started it, and the earlier games once. No network: a handler
/// that records the requests.
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

    private static AppSettings Agreed(int version = PoolClient.ConsentVersion, string endpoint = Endpoint) =>
        new() { PoolEndpoint = endpoint, PoolConsent = true, PoolConsentVersion = version };

    private PoolSharingHost Host(AppSettings settings, CardDb? cards = null, bool start = true)
    {
        var host = new PoolSharingHost(settings, _http, _history, () => cards ?? CardDb.Empty);
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

    [Fact]
    public async Task With_the_newest_answer_a_battlegrounds_game_is_one_post_of_the_game_with_its_picks_and_plays()
    {
        var settings = Agreed();
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
    public async Task With_the_newest_answer_a_constructed_game_with_picks_is_one_post_of_its_picks()
    {
        var host = Host(Agreed());

        await host.ShareAsync(Constructed());

        var request = Assert.Single(_pool.Requests);
        Assert.Equal((HttpMethod.Post, Endpoint + "/v1/constructed"), (request.Method, request.Url));
        using var json = JsonDocument.Parse(request.Body);
        var game = Assert.Single(json.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal(["id", "day", "format", "won", "src", "picks"], game.EnumerateObject().Select(p => p.Name));
        Assert.Equal("""[[4,"CORE_YOP_001",["CARD_A","CARD_B","CARD_C"],["CARD_B"]]]""", game.GetProperty("picks").GetRawText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Without_the_players_yes_nothing_is_sent_and_no_secret_is_made(bool? answer)
    {
        var settings = new AppSettings { PoolEndpoint = Endpoint, PoolConsent = answer, PoolConsentVersion = PoolClient.ConsentVersion };
        var saves = 0;
        var host = Host(settings);
        host.SettingsChanged += () => saves++;

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.Null(settings.PoolSecret);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task An_answer_to_the_second_list_sends_the_game_without_picks_and_plays_and_no_constructed_picks()
    {
        var settings = Agreed(version: 2);
        var host = Host(settings);

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        var request = Assert.Single(_pool.Requests);
        Assert.Equal(Endpoint + "/v1/games", request.Url);
        using var json = JsonDocument.Parse(request.Body);
        var game = json.RootElement.GetProperty("games")[0];
        Assert.Equal(0, game.GetProperty("choices").GetArrayLength());
        Assert.Equal(0, game.GetProperty("plays").GetArrayLength());
        // What the second list did name still goes.
        Assert.Equal(1, game.GetProperty("board").GetArrayLength());
        Assert.DoesNotContain("TB_BaconShop_Triples_01", request.Body, StringComparison.Ordinal);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task An_answer_to_the_first_list_sends_the_result_alone()
    {
        var host = Host(Agreed(version: 1));

        await host.ShareAsync(Battlegrounds());

        using var json = JsonDocument.Parse(Assert.Single(_pool.Requests).Body);
        var game = json.RootElement.GetProperty("games")[0];
        Assert.False(game.TryGetProperty("board", out _));
        Assert.False(game.TryGetProperty("choices", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://pool.test")]
    [InlineData("pool.test")]
    public async Task Without_an_https_pool_nothing_is_sent(string endpoint)
    {
        var settings = Agreed(endpoint: endpoint);
        var host = Host(settings, Cards());
        _history.Add(Earlier(0));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task The_second_call_with_the_new_rating_does_not_send_the_game_again()
    {
        var host = Host(Agreed());

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Battlegrounds(ratingAfter: 7050));

        Assert.Single(_pool.Requests);
    }

    [Fact]
    public async Task Before_a_live_host_starts_it_nothing_is_sent()
    {
        var settings = Agreed();
        var host = Host(settings, Cards(), start: false);
        _history.Add(Earlier(0));

        await host.ShareAsync(Battlegrounds());
        await host.ShareAsync(Constructed());
        host.OnGameFinished(Battlegrounds());
        await host.ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task Earlier_games_of_30_days_go_once_in_batches_of_20_with_a_pause_between()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        for (var i = 0; i < 25; i++)
        {
            Assert.True(_history.Add(Earlier(i, now.AddDays(-1))));
        }

        Assert.True(_history.Add(Earlier(100, now.AddDays(-45))));
        Assert.True(_history.Add(Earlier(101, now.AddDays(-1)) with { Mode = "Ranked" }));
        var settings = Agreed();
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

        Assert.Equal(2, _pool.Requests.Count);
        Assert.All(_pool.Requests, r => Assert.Equal(Endpoint + "/v1/games", r.Url));
        Assert.Equal([20, 5], _pool.Requests.Select(r =>
        {
            using var json = JsonDocument.Parse(r.Body);
            return json.RootElement.GetProperty("games").GetArrayLength();
        }));
        Assert.Contains("\"src\":\"history\"", _pool.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal([PoolClient.HistoryBatchDelay, PoolClient.HistoryBatchDelay], pauses);
        Assert.True(settings.PoolHistoryShared);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Earlier_games_the_pool_refused_are_tried_again_next_time()
    {
        Assert.True(_history.Add(Earlier(0, DateTime.Now.AddDays(-1))));
        var settings = Agreed();
        var host = Host(settings, Cards());
        host.Delay = _ => Task.CompletedTask;
        _pool.Status = HttpStatusCode.TooManyRequests;

        await host.ShareHistoryAsync();

        Assert.Single(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);

        _pool.Status = HttpStatusCode.OK;
        await host.ShareHistoryAsync();

        Assert.Equal(2, _pool.Requests.Count);
        Assert.True(settings.PoolHistoryShared);
    }

    [Fact]
    public async Task Earlier_games_wait_for_the_cards()
    {
        Assert.True(_history.Add(Earlier(0, DateTime.Now.AddDays(-1))));
        var settings = Agreed();

        await Host(settings).ShareHistoryAsync();

        Assert.Empty(_pool.Requests);
        Assert.False(settings.PoolHistoryShared);
    }

    [Fact]
    public Task The_apps_services_hand_every_finished_game_of_the_session_to_the_host() =>
        AppServicesTests.With(Agreed(), (services, _) =>
        {
            var handlers = (services.Session.GetType()
                .GetField(nameof(GameSession.GameFinished), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(services.Session) as Delegate)!.GetInvocationList();
            Assert.Contains(handlers, h => h.Target == services.PoolSharing && h.Method.Name == nameof(PoolSharingHost.OnGameFinished));
            return Task.CompletedTask;
        });

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

    /// <summary>Answers every request with <see cref="Status"/> and keeps what was asked.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Url, string Body)> _requests = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

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

            return new HttpResponseMessage(Status);
        }
    }
}

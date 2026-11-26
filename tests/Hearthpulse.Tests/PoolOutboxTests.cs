using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// <see cref="PoolOutbox"/>, the queue of shared games (docs/game-stats.md, "Shared games: queue and sending"): what the
/// pool does not take now waits on disk for the pool it was queued for and goes when that pool takes it, a refusal of
/// sharing empties every queue without asking any pool anything, and a queued batch is the body a direct send has. No
/// network: a handler that records the requests; a clock the test moves.
/// </summary>
public sealed class PoolOutboxTests : IDisposable
{
    private const string Endpoint = "https://pool.test";
    private const string Other = "https://other.test/stats";
    private const string Secret = "5555555555555555555555555555555555555555555555555555555555555555";

    private readonly string _root = TestDataFolder.Scratch("hp-pool-outbox");
    private readonly string _historyPath = TestDataFolder.Scratch("hp-pool-outbox-history") + ".db";
    private readonly FakePool _pool = new();
    private readonly HttpClient _http;
    private readonly List<IDisposable> _owned = [];
    private DateTime _now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    public PoolOutboxTests() =>
        // A short limit, so a pool that never answers is a timeout within the test.
        _http = new HttpClient(_pool) { Timeout = TimeSpan.FromSeconds(1) };

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        _http.Dispose();
        TestDataFolder.TryDelete(_root);
        TestDataFolder.TryDelete(_historyPath);
    }

    [Fact]
    public async Task A_503_keeps_the_game_and_nothing_goes_again_before_its_Retry_After()
    {
        var outbox = Outbox(Settings());
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(1)));
        _pool.Status = HttpStatusCode.ServiceUnavailable;
        // The pool's answer once its budget of the day is spent.
        _pool.RetryAfter = 3600;

        Assert.False(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(1, outbox.Pending);
        Assert.Equal(_now.AddHours(1), outbox.NextTry);
        Assert.Contains("Pool outbox: /v1/games not now (503), 1 game(s) kept, next try in 3600 s", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // A pass before then (another game ended, the switch moved, the 15 minutes) asks the pool nothing.
        _pool.Status = HttpStatusCode.OK;
        _pool.RetryAfter = null;
        Assert.False(await outbox.PassAsync(CancellationToken.None));
        _now = _now.AddMinutes(59);
        Assert.False(await outbox.PassAsync(CancellationToken.None));
        Assert.Single(_pool.Requests);

        _now = _now.AddMinutes(1);
        Assert.True(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(0, outbox.Pending);
        Assert.Null(outbox.NextTry);
        Assert.Equal(2, _pool.Requests.Count);
        Assert.Equal(_pool.Requests[0].Body, _pool.Requests[1].Body);
    }

    [Theory]
    [InlineData("429")]
    [InlineData("timeout")]
    [InlineData("offline")]
    public async Task A_429_a_timeout_or_no_network_keeps_the_game_and_retries_after_30_seconds_doubling(string failure)
    {
        var outbox = Outbox(Settings());
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(1)));
        _pool.Status = HttpStatusCode.TooManyRequests;
        _pool.Failure = failure;

        Assert.False(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(1, outbox.Pending);
        Assert.Equal(_now.AddSeconds(30), outbox.NextTry);

        _now = _now.AddSeconds(30);
        Assert.False(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(_now.AddSeconds(60), outbox.NextTry);
        Assert.Equal(2, _pool.Requests.Count);
        Assert.Equal(1, outbox.Pending);
    }

    [Fact]
    public void The_wait_doubles_up_to_30_minutes_and_a_Retry_After_is_kept_up_to_6_hours()
    {
        Assert.Equal(
            [30, 60, 120, 240, 480, 960, 1800, 1800],
            Enumerable.Range(1, 8).Select(n => (int)PoolOutbox.Backoff(n, null).TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(30), PoolOutbox.Backoff(1, TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromHours(1), PoolOutbox.Backoff(1, TimeSpan.FromHours(1)));
        Assert.Equal(PoolOutbox.MaxRetryAfter, PoolOutbox.Backoff(1, TimeSpan.FromDays(3)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_refused_batch_is_dropped_for_good(HttpStatusCode status)
    {
        var outbox = Outbox(Settings());
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(1)));
        _pool.Status = status;

        Assert.True(await outbox.PassAsync(CancellationToken.None));
        Assert.True(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(0, outbox.Pending);
        Assert.Null(outbox.NextTry);
        Assert.Single(_pool.Requests);
    }

    /// <summary>
    /// The row is written when the game ends: sent after a restart on a later day, it still names the day the game was
    /// played (a row built at sending time would name the day of sending).
    /// </summary>
    [Fact]
    public async Task A_queued_game_survives_a_restart_and_keeps_the_day_it_ended_on()
    {
        var yesterday = PoolGame.Today - 1;
        var before = Outbox(Settings());
        var ticket = before.Ticket()!.Value;
        Assert.True(before.Add(ticket, Game(1) with { Day = yesterday }));
        Assert.True(before.Add(ticket, Picks(2) with { Day = yesterday }));
        before.Dispose();

        var after = Outbox(Settings());
        Assert.Equal(2, after.Pending);
        Assert.True(await after.PassAsync(CancellationToken.None));

        Assert.Equal([Endpoint + "/v1/games", Endpoint + "/v1/constructed"], _pool.Requests.Select(r => r.Url));
        Assert.All(_pool.Requests, r => Assert.Equal(yesterday, OnlyGame(r).GetProperty("day").GetInt32()));
        Assert.Equal(0, after.Pending);
    }

    [Fact]
    public async Task Turning_sharing_off_empties_the_queues_of_every_address_and_asks_no_pool_anything()
    {
        var settings = Settings();
        var host = Host(settings);
        var outbox = host.Outbox;
        outbox.Open();
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(1)));
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Picks(2)));
        settings.PoolEndpoint = Other;
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(3)));

        settings.PoolConsent = false;
        host.SharingChanged();

        Assert.Empty(Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories));
        // No deletion request is kept for later either: these routes have none.
        Assert.DoesNotContain(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), f => Path.GetFileName(f) is "forget" or "sent");
        Assert.Contains("Pool outbox: sharing is off, dropped 3 waiting game(s)", TestDataFolder.ReadLog(), StringComparison.Ordinal);
        Assert.Null(outbox.Ticket());

        // Passes while it is off send nothing, and on again nothing of what was dropped comes back.
        Assert.True(await outbox.PassAsync(CancellationToken.None));
        settings.PoolConsent = null;
        host.SharingChanged();
        Assert.True(await outbox.PassAsync(CancellationToken.None));
        settings.PoolEndpoint = Endpoint;
        Assert.True(await outbox.PassAsync(CancellationToken.None));
        Assert.Empty(_pool.Requests);
    }

    /// <summary>A refusal saved before a restart, with games still in the queue: the first pass drops them, unsent.</summary>
    [Fact]
    public async Task A_pass_while_sharing_is_off_empties_what_waits()
    {
        var settings = Settings();
        var outbox = Outbox(settings);
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(1)));
        settings.PoolEndpoint = Other;
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Picks(2)));
        settings.PoolEndpoint = Endpoint;

        settings.PoolConsent = false;
        Assert.True(await outbox.PassAsync(CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories));
        Assert.Empty(_pool.Requests);
    }

    [Fact]
    public async Task A_queue_goes_only_to_the_pool_its_games_ended_under()
    {
        var settings = Settings();
        var outbox = Outbox(settings);
        // Game 1 ends under the first pool; the address changes while its row is built.
        var first = outbox.Ticket()!.Value;
        settings.PoolEndpoint = Other;
        Assert.True(outbox.Add(first, Game(1)));
        Assert.True(outbox.Add(outbox.Ticket()!.Value, Game(2)));

        Assert.True(await outbox.PassAsync(CancellationToken.None));

        var sent = Assert.Single(_pool.Requests);
        Assert.Equal(Other + "/v1/games", sent.Url);
        Assert.Equal(Game(2).GameId, OnlyGame(sent).GetProperty("id").GetString());
        Assert.Equal(0, outbox.Pending);

        // Back on the first pool: its own game goes there, and only there.
        settings.PoolEndpoint = Endpoint;
        Assert.Equal(1, outbox.Pending);
        Assert.True(await outbox.PassAsync(CancellationToken.None));

        Assert.Equal(Endpoint + "/v1/games", _pool.Requests[1].Url);
        Assert.Equal(Game(1).GameId, OnlyGame(_pool.Requests[1]).GetProperty("id").GetString());
        Assert.Equal(2, _pool.Requests.Count);
    }

    [Fact]
    public void A_game_built_across_a_refusal_is_not_queued_even_when_sharing_is_back_on()
    {
        var settings = Settings();
        var host = Host(settings);
        // The game ends; while its rows are built the player turns sharing off and on again.
        var ticket = host.Outbox.Ticket()!.Value;
        settings.PoolConsent = false;
        host.SharingChanged();
        settings.PoolConsent = true;
        host.SharingChanged();

        host.Queue(Battlegrounds(1), ticket);

        Assert.Equal(0, host.Outbox.Pending);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories));
        var log = TestDataFolder.ReadLog();
        Assert.Contains("Pool outbox (games): a record built across a withdrawal was not queued", log, StringComparison.Ordinal);
        // A refusal before anything was queued (no queue folder yet) writes no failure line.
        Assert.DoesNotContain("Pool outbox (games): earlier queue could not be deleted", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Pool outbox (constructed): earlier queue could not be deleted", log, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live host starts its log watcher first: a game the catch-up finds finished at start-up ends before the host
    /// starts. It waits in the queue, unsent, and goes once the host starts.
    /// </summary>
    [Fact]
    public async Task A_game_that_ended_before_Start_is_sent_once_the_host_starts()
    {
        var host = Host(Settings());
        host.OnGameFinished(Battlegrounds(1));
        Assert.True(SpinWait.SpinUntil(() => host.Outbox.Pending == 1, TimeSpan.FromSeconds(30)), "the game was queued");
        Assert.True(await host.Outbox.PassAsync(CancellationToken.None));
        Assert.Empty(_pool.Requests);

        host.Start();

        Assert.True(SpinWait.SpinUntil(() => _pool.Requests.Count == 1 && host.Outbox.Pending == 0, TimeSpan.FromSeconds(30)), "the game was sent");
        Assert.Equal(Endpoint + "/v1/games", _pool.Requests[0].Url);
    }

    [Fact]
    public async Task A_queued_batch_is_the_body_a_direct_send_of_the_same_games_has()
    {
        var game = Game(1);
        var picks = Picks(2);
        Assert.True(await PoolClient.SubmitAsync(_http, Endpoint, [game]));
        Assert.True(await PoolClient.SubmitConstructedAsync(_http, Endpoint, [picks]));

        var outbox = Outbox(Settings());
        var ticket = outbox.Ticket()!.Value;
        Assert.True(outbox.Add(ticket, game));
        Assert.True(outbox.Add(ticket, picks));
        Assert.True(await outbox.PassAsync(CancellationToken.None));

        var requests = _pool.Requests;
        Assert.Equal(4, requests.Count);
        Assert.Equal(requests[0], requests[2]);
        Assert.Equal(requests[1], requests[3]);
        // The details went along: the board, a win chance with its decimals, the picks and plays.
        Assert.Contains("\"board\":[{\"c\":\"BEAST_A\"", requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("0.613", requests[0].Body, StringComparison.Ordinal);
    }

    private static AppSettings Settings() => new() { PoolEndpoint = Endpoint, PoolSecret = Secret };

    /// <summary>An outbox of the test's own, open for sending, with no background loop: the test runs the passes.</summary>
    private PoolOutbox Outbox(AppSettings settings)
    {
        var outbox = new PoolOutbox(settings, _http, _root) { UtcNow = () => _now, Delay = TimeSpan.Zero };
        outbox.Open();
        _owned.Add(outbox);
        return outbox;
    }

    /// <summary>A host as the apps build it, not started.</summary>
    private PoolSharingHost Host(AppSettings settings)
    {
        var host = new PoolSharingHost(settings, _http, new HistoryStore(_historyPath), () => CardDb.Empty, _root);
        host.Outbox.Delay = TimeSpan.Zero;
        _owned.Add(host);
        return host;
    }

    private static GameOutcome Battlegrounds(int n) =>
        new("Battlegrounds", "Win", 2, 7000, null, "BG_HERO_1", ["BG_HERO_1", "BG_HERO_2"])
        {
            Key = $"20261126120000-{n}|Player#1234|GT_BATTLEGROUNDS",
            Details = new BgGameDetails(14, 6, 2, "BG_ANOMALY", ["BEAST", "DEMON"],
                [new BgMinion("BEAST_A", "A", 40, 30, true, 4, [])], ["TRK_1"], ["TRK_1"], [new TierStep(3, 2)],
                [new CombatRecord(2, 5, "BG_HERO_OTHER", "Win", 0, 3, new CombatOdds(0.61349, 0.1, 0.28651, 0))])
            {
                Choices = [new BgChoice(3, "discover", "TB_BaconShop_Triples_01", ["A", "B", "C"], ["B"])],
                Plays = [new BgPlay(1, "BEAST_A")],
            },
        };

    private static PoolGame Game(int n) => PoolGame.From(Battlegrounds(n), id => id, details: true, secret: Secret)!;

    private static ConstructedPoolGame Picks(int n) => ConstructedPoolGame.From(
        new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08")
        {
            Key = $"20261126130000-{n}|Player#1234|GT_RANKED",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            ConstructedPicks = [new ConstructedPick(4, "CORE_YOP_001", ["CARD_A", "CARD_B", "CARD_C"], ["CARD_B"])],
        },
        Secret)!;

    /// <summary>The one game of a request's body.</summary>
    private static JsonElement OnlyGame((string Url, string Body) request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return Assert.Single(json.RootElement.GetProperty("games").EnumerateArray()).Clone();
    }

    /// <summary>
    /// Answers every request with <see cref="Status"/> (and <see cref="RetryAfter"/> seconds when set), or fails it as no
    /// network does, or never answers (a timeout of the client), and keeps what was asked.
    /// </summary>
    private sealed class FakePool : HttpMessageHandler
    {
        private readonly List<(string Url, string Body)> _requests = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int? RetryAfter { get; set; }

        /// <summary>"offline" or "timeout"; anything else answers.</summary>
        public string? Failure { get; set; }

        public IReadOnlyList<(string Url, string Body)> Requests
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
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests)
            {
                _requests.Add((request.RequestUri!.ToString(), body));
            }

            switch (Failure)
            {
                case "offline":
                    throw new HttpRequestException("No network");
                case "timeout":
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    break;
            }

            var response = new HttpResponseMessage(Status) { Content = new StringContent("{}") };
            if (RetryAfter is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }
}

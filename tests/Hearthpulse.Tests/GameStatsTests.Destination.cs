using System.Net;
using System.Text.Json.Nodes;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// A game record queue belongs to one pool address (docs/game-stats.md, "Queue destination"): nothing queued for one
/// pool is ever sent to another, through an address change, a removed override, a held answer or a restart.
/// </summary>
public sealed partial class GameStatsTests
{
    private const string PoolA = "https://pool-a.test/stats";
    private const string PoolB = "https://pool-b.test";
    private static readonly string DefaultHost = new Uri(PoolClient.DefaultEndpoint).Host;

    private static AppSettings Sharing(string? endpoint) =>
        new() { PoolEndpoint = endpoint, GameStatsNoticeShown = true, PoolSecret = new string('5', 64) };

    private GameStatsUploader Uploader(AppSettings settings, HttpMessageHandler pool, GameStatsQueues queues) =>
        new(settings, new HttpClient(pool), new HistoryStore(Path.Combine(_folder, "h.db")), () => null, () => null, null, queues) { Delay = TimeSpan.Zero };

    [Theory]
    [InlineData("https://Pool-A.test/stats/", PoolA)]
    [InlineData("https://pool-a.test:443/stats", PoolA)]
    [InlineData("https://pool-a.test:8443/", "https://pool-a.test:8443")]
    [InlineData("http://pool-a.test", null)]
    [InlineData("https://user:key@pool-a.test", null)]
    [InlineData("https://pool-a.test/stats?token=1", null)]
    [InlineData("off", null)]
    [InlineData("", null)]
    public void A_destination_is_the_https_origin_and_path_in_one_spelling(string endpoint, string? expected) =>
        Assert.Equal(expected, PoolDestination.Normalize(endpoint));

    [Fact]
    public async Task Records_queued_for_one_pool_are_not_sent_to_another_and_go_when_their_pool_returns()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        for (var i = 0; i < 7; i++)
        {
            queues.For(PoolA)!.Add(Record(i));
        }

        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        var states = new List<GameStatsState>();
        uploader.StateChanged += states.Add;

        settings.PoolEndpoint = PoolB;
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Equal(7, queues.For(PoolA)!.Count);
        Assert.Equal(new GameStatsState(0, 7, 0), uploader.State);
        Assert.Equal(new GameStatsState(0, 7, 0), Assert.Single(states));
        Assert.Contains("Game stats: queue for another pool kept, 7 record(s), 0 deletion request(s)", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Equal(("pool-a.test", "/stats/v1/events"), (Assert.Single(pool.Requests).Host, pool.Requests[0].Path));
        Assert.Equal(7, JsonNode.Parse(pool.Requests[0].Body)!["games"]!.AsArray().Count);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
    }

    [Fact]
    public async Task Removing_the_override_sends_nothing_of_the_custom_queue_to_the_default_pool_not_even_its_forget()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        settings.GameStatsOff = true;
        uploader.Withdraw();
        settings.GameStatsOff = false;
        queues.For(PoolA)!.Add(Record('a'));
        Assert.True(queues.For(PoolA)!.ForgetPending);

        settings.PoolEndpoint = null;
        Assert.Equal(PoolClient.DefaultEndpoint, PoolDestination.Of(settings));
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 1, 1), uploader.State);
        Assert.True(queues.For(PoolA)!.ForgetPending);

        // A game of the default pool goes there alone; the custom pool's queue still waits.
        queues.For(PoolClient.DefaultEndpoint)!.Add(Record('d'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        var sent = Assert.Single(pool.Requests);
        Assert.Equal(DefaultHost, sent.Host);
        Assert.Equal(new string('d', 32), (string?)JsonNode.Parse(sent.Body)!["games"]![0]!["id"]);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(["/stats/v1/events/forget", "/stats/v1/events"], pool.Requests.Skip(1).Select(r => r.Path));
        Assert.All(pool.Requests.Skip(1), r => Assert.Equal("pool-a.test", r.Host));
        Assert.False(queues.For(PoolA)!.ForgetPending);
    }

    [Fact]
    public async Task An_answer_held_while_the_address_changes_acknowledges_only_the_queue_it_was_sent_from()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        for (var i = 0; i < 26; i++)
        {
            queues.For(PoolA)!.Add(Record(i));
        }

        // The same game waits for pool B as well: an answer from A must not take it out of B's queue.
        queues.For(PoolB)!.Add(Record(0));
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        var held = pool.HoldNext();
        var pass = uploader.PassAsync(CancellationToken.None);
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        settings.PoolEndpoint = PoolB;
        held.SetResult(HttpStatusCode.OK);
        Assert.True(await pass);

        Assert.Equal("pool-a.test", Assert.Single(pool.Requests).Host);
        Assert.Equal(6, queues.For(PoolA)!.Count);
        Assert.Equal(1, queues.For(PoolB)!.Count);

        // And the other way round: B's answer, held while the address goes back to A, leaves A's queue alone.
        held = pool.HoldNext();
        pass = uploader.PassAsync(CancellationToken.None);
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        settings.PoolEndpoint = PoolA;
        held.SetResult(HttpStatusCode.OK);
        Assert.True(await pass);

        Assert.Equal(["pool-a.test", "pool-b.test"], pool.Requests.Select(r => r.Host));
        Assert.Equal(0, queues.For(PoolB)!.Count);
        Assert.Equal(6, queues.For(PoolA)!.Count);
    }

    [Fact]
    public async Task A_restart_keeps_every_queue_with_its_own_pool()
    {
        var first = new GameStatsQueues(_folder);
        first.For(PoolA)!.Add(Record('a'));
        first.For(PoolA)!.Add(Record('b'));
        first.For(PoolB)!.Clear(forget: true);

        // New objects over the same folder, as after a restart, with the settings naming neither pool.
        var settings = Sharing("https://pool-c.test");
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 2, 1), uploader.State);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-a.test", "/stats/v1/events"), (Assert.Single(pool.Requests).Host, pool.Requests[0].Path));

        settings.PoolEndpoint = PoolB;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-b.test", "/v1/events/forget"), (pool.Requests[1].Host, pool.Requests[1].Path));
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
    }

    [Fact]
    public async Task A_queue_of_the_earlier_version_goes_to_the_pool_the_settings_name_by_themselves()
    {
        LegacyQueue(records: 3, forget: true);
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Equal(["/stats/v1/events/forget", "/stats/v1/events"], pool.Requests.Select(r => r.Path));
        Assert.All(pool.Requests, r => Assert.Equal("pool-a.test", r.Host));
        Assert.Equal(3, JsonNode.Parse(pool.Requests[1].Body)!["games"]!.AsArray().Count);
        Assert.Empty(Directory.GetFiles(_folder, "*.json"));
        Assert.False(File.Exists(Path.Combine(_folder, "forget")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("off")]
    public async Task A_queue_of_the_earlier_version_without_an_override_is_set_aside_and_never_reaches_any_pool(string? endpoint)
    {
        LegacyQueue(records: 3, forget: true);
        var settings = Sharing(endpoint);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 3, 0), uploader.State);
        Assert.Empty(Directory.GetFiles(_folder, "*.json"));
        Assert.False(File.Exists(Path.Combine(_folder, "forget")));
        Assert.Contains("Game stats: earlier queue has no known pool, set aside, 3 record(s) not sent, 1 deletion request(s) dropped", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // Naming a pool later does not adopt them, and neither does a restart.
        settings.PoolEndpoint = PoolA;
        using var restarted = Uploader(settings, pool, new GameStatsQueues(_folder));
        Assert.True(await restarted.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 3, 0), restarted.State);

        // Turning sharing off deletes them here; there is no pool to ask.
        settings.GameStatsOff = true;
        restarted.Withdraw();
        Assert.Equal(new GameStatsState(0, 0, 0), restarted.State);
    }

    [Fact]
    public async Task A_queue_whose_destination_cannot_be_read_is_sent_nowhere_and_the_pool_gets_a_fresh_one()
    {
        var first = new GameStatsQueues(_folder);
        first.For(PoolA)!.Add(Record('a'));
        first.For(PoolA)!.Add(Record('b'));
        File.WriteAllText(Path.Combine(_folder, PoolDestination.Key(PoolA), "destination"), "{\"endpoint\":");

        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 2, 0), uploader.State);
        Assert.Contains("Game stats: queue with an unreadable destination set aside, 2 record(s) not sent", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // A destination naming another pool than the folder's is as unknown as a damaged one.
        queues.For(PoolA)!.Add(Record('c'));
        File.WriteAllText(Path.Combine(_folder, PoolDestination.Key(PoolA), "destination"), new JsonObject { ["v"] = 1, ["endpoint"] = PoolB }.ToJsonString());
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 3, 0), uploader.State);

        queues.For(PoolA)!.Add(Record('d'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(new string('d', 32), (string?)JsonNode.Parse(Assert.Single(pool.Requests).Body)!["games"]![0]!["id"]);
    }

    [Fact]
    public async Task Turning_sharing_off_empties_every_queue_and_each_forget_waits_for_its_own_pool()
    {
        var settings = Sharing(PoolB);
        var queues = new GameStatsQueues(_folder);
        queues.For(PoolA)!.Add(Record('a'));
        queues.For(PoolB)!.Add(Record('b'));
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        settings.GameStatsOff = true;
        uploader.Withdraw();

        Assert.Equal(0, queues.For(PoolA)!.Count);
        Assert.Equal(0, queues.For(PoolB)!.Count);
        Assert.Equal(new GameStatsState(0, 0, 1), uploader.State);

        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-b.test", "/v1/events/forget"), (Assert.Single(pool.Requests).Host, pool.Requests[0].Path));
        Assert.True(queues.For(PoolA)!.ForgetPending);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-a.test", "/stats/v1/events/forget"), (pool.Requests[1].Host, pool.Requests[1].Path));
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
    }

    [Fact]
    public void A_record_built_after_the_address_changed_is_queued_for_the_pool_its_game_ended_under()
    {
        var settings = Sharing(PoolA);
        var game = Ranked(null);
        var history = new HistoryStore(Path.Combine(_folder, "h.db"));
        var replays = new ReplayStore(Path.Combine(_folder, "replays"));
        history.Add(game with { Replay = replays.Save(game.Key, Replay())! });
        var queues = new GameStatsQueues(_folder);
        using var http = new HttpClient(new HoldingPool());
        using var uploader = new GameStatsUploader(settings, http, history, () => replays, Cards, null, queues);

        // What OnGameFinished hands to the background build: the address read when the game ended.
        var endedUnder = PoolDestination.Of(settings)!;
        settings.PoolEndpoint = PoolB;
        uploader.Enqueue(new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08") { Key = game.Key }, endedUnder);

        Assert.Equal(1, queues.For(PoolA)!.Count);
        Assert.Equal(0, queues.For(PoolB)!.Count);
    }

    [Fact]
    public async Task What_waits_for_another_pool_still_expires_after_fourteen_days()
    {
        var now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var queues = new GameStatsQueues(_folder) { UtcNow = () => now };
        queues.For(PoolA)!.Add(Record('a'));
        var settings = Sharing(PoolB);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(1, uploader.State.HeldRecords);

        now += GameStatsQueue.MaxAge + TimeSpan.FromDays(1);
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
        Assert.Empty(pool.Requests);
        Assert.False(Directory.Exists(Path.Combine(_folder, PoolDestination.Key(PoolA))));
    }

    /// <summary>The queue as the version before destinations left it: record files and the forget mark directly in the folder.</summary>
    private void LegacyQueue(int records, bool forget)
    {
        for (var i = 0; i < records; i++)
        {
            var record = Record(i);
            File.WriteAllText(Path.Combine(_folder, (string)record["id"]! + ".json"), record.ToJsonString());
        }

        if (forget)
        {
            File.WriteAllText(Path.Combine(_folder, "forget"), string.Empty);
        }
    }

    /// <summary>A pool that records where each request went and can keep one answer until the test releases it.</summary>
    private sealed class HoldingPool : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource<HttpStatusCode>? _hold;
        private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Host, string Path, string Body)> Requests { get; } = [];

        /// <summary>Completes when the held request has reached the pool.</summary>
        public Task Arrived => _arrived.Task;

        /// <summary>The next request waits for the returned source's status.</summary>
        public TaskCompletionSource<HttpStatusCode> HoldNext()
        {
            lock (_gate)
            {
                _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            TaskCompletionSource<HttpStatusCode>? hold;
            lock (_gate)
            {
                Requests.Add((request.RequestUri!.Host, request.RequestUri.AbsolutePath, body));
                hold = _hold;
                _hold = null;
            }

            var status = HttpStatusCode.OK;
            if (hold is not null)
            {
                _arrived.TrySetResult();
                status = await hold.Task;
            }

            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}

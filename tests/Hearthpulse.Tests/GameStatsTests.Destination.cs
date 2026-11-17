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
        new() { PoolEndpoint = endpoint, PoolSecret = new string('5', 64) };

    private GameStatsUploader Uploader(AppSettings settings, HttpMessageHandler pool, GameStatsQueues queues) =>
        new(settings, new HttpClient(pool), new HistoryStore(Path.Combine(_folder, "h.db")), () => null, () => null, null, queues) { Delay = TimeSpan.Zero, SaveSettings = () => { } };

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
        Assert.Contains("Game stats: earlier queue names no pool, set aside, 3 record(s) not sent", TestDataFolder.ReadLog(), StringComparison.Ordinal);
        Assert.Contains("Game stats: earlier deletion request names no pool, cannot be delivered, dropped", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // Naming a pool later does not adopt them, and neither does a restart.
        settings.PoolEndpoint = PoolA;
        using var restarted = Uploader(settings, pool, new GameStatsQueues(_folder));
        Assert.True(await restarted.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 3, 0), restarted.State);

        // Turning sharing off deletes them here; there is no pool to ask for them.
        settings.GameStatsOff = true;
        restarted.Withdraw();
        Assert.Equal(0, restarted.State.HeldRecords);
        Assert.Empty(Directory.GetDirectories(_folder, "unknown-*"));
        Assert.Empty(pool.Requests);
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
        uploader.Enqueue(new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08") { Key = game.Key }, endedUnder, queues.Generation);

        Assert.Equal(1, queues.For(PoolA)!.Count);
        Assert.Equal(0, queues.For(PoolB)!.Count);
    }

    [Fact]
    public void A_record_still_being_built_when_sharing_is_turned_off_is_not_queued_afterwards()
    {
        var settings = Sharing(PoolA);
        var game = Ranked(null);
        var history = new HistoryStore(Path.Combine(_folder, "h.db"));
        var replays = new ReplayStore(Path.Combine(_folder, "replays"));
        history.Add(game with { Replay = replays.Save(game.Key, Replay())! });
        var queues = new GameStatsQueues(_folder);
        using var http = new HttpClient(new HoldingPool());
        using var uploader = new GameStatsUploader(settings, http, history, () => replays, Cards, null, queues);
        var outcome = new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08") { Key = game.Key };

        // What OnGameFinished reads before the background build: the withdrawal count the game ended under.
        var endedUnder = queues.Generation;
        settings.GameStatsOff = true;
        uploader.Withdraw();
        uploader.Enqueue(outcome, PoolA, endedUnder);
        Assert.Equal(0, queues.For(PoolA)!.Count);

        // Back on before the build ends: the record of the withdrawn time still does not enter the new one.
        settings.GameStatsOff = false;
        uploader.Enqueue(outcome, PoolA, endedUnder);
        Assert.Equal(0, queues.For(PoolA)!.Count);
        Assert.Contains("Game stats: a record built across a withdrawal was not queued", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // A game that ends after sharing is back on is queued as usual.
        uploader.Enqueue(outcome, PoolA, queues.Generation);
        Assert.Equal(1, queues.For(PoolA)!.Count);
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

    [Fact]
    public async Task Removing_the_override_sends_nothing_of_the_custom_queue_to_the_default_pool_not_even_its_forget()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        queues.For(PoolA)!.Add(Record('e'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal("pool-a.test", Assert.Single(pool.Requests).Host);
        settings.GameStatsOff = true;
        uploader.Withdraw();
        settings.GameStatsOff = false;
        queues.For(PoolA)!.Add(Record('a'));
        Assert.True(queues.For(PoolA)!.ForgetPending);

        settings.PoolEndpoint = null;
        Assert.Equal(PoolClient.DefaultEndpoint, PoolDestination.Of(settings));
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Single(pool.Requests);
        Assert.Equal(new GameStatsState(0, 1, 1), uploader.State);
        Assert.True(queues.For(PoolA)!.ForgetPending);

        // A game of the default pool goes there alone; the custom pool's queue still waits.
        queues.For(PoolClient.DefaultEndpoint)!.Add(Record('d'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(DefaultHost, pool.Requests[1].Host);
        Assert.Equal(new string('d', 32), (string?)JsonNode.Parse(pool.Requests[1].Body)!["games"]![0]!["id"]);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(["/stats/v1/events/forget", "/stats/v1/events"], pool.Requests.Skip(2).Select(r => r.Path));
        Assert.All(pool.Requests.Skip(2), r => Assert.Equal("pool-a.test", r.Host));
        Assert.False(queues.For(PoolA)!.ForgetPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_queue_of_the_earlier_version_is_never_adopted_by_the_pool_the_settings_name_now(bool restartFirst)
    {
        // Filled for some pool A by the earlier version; settings.json names pool B by the time this version runs.
        LegacyQueue(records: 3, forget: true);
        var settings = Sharing(PoolB);
        var pool = new HoldingPool();
        if (restartFirst)
        {
            using var earlier = Uploader(settings, pool, new GameStatsQueues(_folder));
            Assert.True(await earlier.PassAsync(CancellationToken.None));
        }

        var queues = new GameStatsQueues(_folder);
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Equal(new GameStatsState(0, 3, 0), uploader.State);
        Assert.Empty(Directory.GetFiles(_folder, "*.json"));
        Assert.False(File.Exists(Path.Combine(_folder, "forget")));
        var aside = Assert.Single(Directory.GetDirectories(_folder, "unknown-*"));
        Assert.Equal(3, Directory.GetFiles(aside, "*.json").Length);
        Assert.Equal(0, queues.For(PoolB)!.Count);
        Assert.False(queues.For(PoolB)!.ForgetPending, "the old deletion request does not know its pool and is not passed to this one");
        var log = TestDataFolder.ReadLog();
        Assert.Contains("Game stats: earlier queue names no pool, set aside, 3 record(s) not sent", log, StringComparison.Ordinal);
        Assert.Contains("Game stats: earlier deletion request names no pool, cannot be delivered, dropped", log, StringComparison.Ordinal);

        // Pool B gets a queue of its own: only what is queued for it from now on goes there.
        queues.For(PoolB)!.Add(Record('d'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        var sent = Assert.Single(pool.Requests);
        Assert.Equal(("pool-b.test", "/v1/events"), (sent.Host, sent.Path));
        Assert.Equal(new string('d', 32), (string?)Assert.Single(JsonNode.Parse(sent.Body)!["games"]!.AsArray())!["id"]);
    }

    [Fact]
    public async Task Turning_sharing_off_empties_every_queue_and_each_forget_waits_for_its_own_pool()
    {
        var settings = Sharing(PoolB);
        var queues = new GameStatsQueues(_folder);
        queues.For(PoolA)!.Add(Record('a'));
        queues.For(PoolB)!.Add(Record('b'));
        queues.For("https://pool-c.test")!.Add(Record('c'));
        // A batch went to A and to B at some time; nothing ever went to C.
        queues.For(PoolA)!.MarkUsed();
        queues.For(PoolB)!.MarkUsed();
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        settings.GameStatsOff = true;
        uploader.Withdraw();

        Assert.Equal(0, queues.For(PoolA)!.Count);
        Assert.Equal(0, queues.For(PoolB)!.Count);
        Assert.Equal(0, queues.For("https://pool-c.test")!.Count);
        Assert.False(queues.For("https://pool-c.test")!.ForgetPending, "a pool nothing was sent to is asked nothing");
        Assert.Equal(new GameStatsState(0, 0, 1), uploader.State);

        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-b.test", "/v1/events/forget"), (Assert.Single(pool.Requests).Host, pool.Requests[0].Path));
        Assert.True(queues.For(PoolA)!.ForgetPending);

        settings.PoolEndpoint = PoolA;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(("pool-a.test", "/stats/v1/events/forget"), (pool.Requests[1].Host, pool.Requests[1].Path));
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);

        settings.PoolEndpoint = "https://pool-c.test";
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(2, pool.Requests.Count);
    }

    [Fact]
    public async Task A_first_refusal_before_anything_was_sent_makes_no_request_and_no_install_secret()
    {
        // A fresh install: sharing on by default, nothing recorded yet, no secret.
        var settings = new AppSettings { PoolEndpoint = PoolA };
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        uploader.Prepare();
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        // Both Settings → Data switches turned off, then what the hosts call.
        settings.GameStatsOff = true;
        settings.PoolConsent = false;
        uploader.Withdraw();
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Empty(pool.Requests);
        Assert.Null(settings.PoolSecret);
        Assert.False(queues.For(PoolA)!.ForgetPending);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
    }

    [Fact]
    public async Task Turning_the_switch_off_before_the_first_batch_left_makes_no_request_but_after_one_the_forget_survives_a_restart()
    {
        // Sharing is on (the first record made the secret), a game waits, nothing has left yet.
        var settings = new AppSettings { PoolEndpoint = PoolA };
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        uploader.Prepare();
        var install = GameStatsClient.InstallId(settings);
        var secret = settings.PoolSecret;
        queues.For(PoolA)!.Add(Record('a'));

        settings.GameStatsOff = true;
        uploader.Withdraw();
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(0, queues.For(PoolA)!.Count);

        // Back on; this time the batch is handed to the pool, whose answer never arrives. The pool may hold it.
        settings.GameStatsOff = false;
        queues.For(PoolA)!.Add(Record('b'));
        pool.Offline = true;
        Assert.False(await uploader.PassAsync(CancellationToken.None));
        settings.GameStatsOff = true;
        uploader.Withdraw();
        Assert.True(queues.For(PoolA)!.ForgetPending);

        // A restart: new objects over the same folder still owe pool A the deletion, with the same install id.
        pool.Offline = false;
        var restartedQueues = new GameStatsQueues(_folder);
        using var restarted = Uploader(settings, pool, restartedQueues);
        Assert.True(await restarted.PassAsync(CancellationToken.None));

        var forget = pool.Requests[^1];
        Assert.Equal(("pool-a.test", "/stats/v1/events/forget"), (forget.Host, forget.Path));
        Assert.Equal(install, (string?)JsonNode.Parse(forget.Body)!["iid"]);
        Assert.Equal(secret, settings.PoolSecret);
        Assert.False(restartedQueues.For(PoolA)!.ForgetPending);

        // Deleted and confirmed: a further refusal has nothing to ask for.
        var count = pool.Requests.Count;
        restarted.Withdraw();
        Assert.True(await restarted.PassAsync(CancellationToken.None));
        Assert.Equal(count, pool.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_install_an_earlier_version_could_already_send_from_still_asks_its_pool_to_delete(bool prepareBeforeRefusal)
    {
        // settings.json of an earlier version: sharing on, a secret; the data folder has no queue layout yet.
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        if (prepareBeforeRefusal)
        {
            Assert.True(uploader.Prepare());
        }

        settings.GameStatsOff = true;
        uploader.Withdraw();
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Equal(("pool-a.test", "/stats/v1/events/forget"), (Assert.Single(pool.Requests).Host, pool.Requests[0].Path));
    }

    [Fact]
    public async Task An_unsaved_install_secret_sends_nothing_and_retries_saving_before_the_batch()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        queues.For(PoolA)!.Add(Record('a'));
        var pool = new HoldingPool();
        var blocked = true;
        var saves = 0;
        using var uploader = new GameStatsUploader(settings, new HttpClient(pool), new HistoryStore(Path.Combine(_folder, "h.db")),
            () => null, () => null, null, queues)
        {
            SaveSettings = () => { saves++; if (blocked) throw new IOException("test save failure"); },
        };
        Assert.False(await uploader.PassAsync(CancellationToken.None));
        Assert.Empty(pool.Requests);
        Assert.Equal(1, queues.For(PoolA)!.Count);
        blocked = false;
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Equal(2, saves);
        Assert.Single(pool.Requests);
        Assert.Equal(0, queues.For(PoolA)!.Count);
    }

    [Fact]
    public async Task A_failed_legacy_sent_mark_keeps_layout_uncommitted_and_withdrawal_retries()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        queues.For(PoolA)!.Add(Record('a'));
        var sent = Path.Combine(_folder, PoolDestination.Key(PoolA), "sent");
        Directory.CreateDirectory(sent);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        settings.GameStatsOff = true;
        uploader.Withdraw(); // Before Start(), with an unwritable mark: no exception escapes to the UI.
        Assert.False(await uploader.PassAsync(CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_folder, "layout")));
        Assert.Empty(pool.Requests);
        Directory.Delete(sent);
        using var restarted = Uploader(settings, pool, new GameStatsQueues(_folder));
        Assert.True(await restarted.PassAsync(CancellationToken.None));
        Assert.Equal("/stats/v1/events/forget", Assert.Single(pool.Requests).Path);
        Assert.True(File.Exists(Path.Combine(_folder, "layout")));
    }

    [Fact]
    public async Task A_batch_whose_sent_mark_cannot_be_written_is_never_submitted()
    {
        var settings = new AppSettings { PoolEndpoint = PoolA };
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(uploader.Prepare());
        queues.For(PoolA)!.Add(Record('a'));
        Directory.CreateDirectory(Path.Combine(_folder, PoolDestination.Key(PoolA), "sent"));
        var error = await Xunit.Record.ExceptionAsync(() => uploader.PassAsync(CancellationToken.None));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Empty(pool.Requests);
        Assert.Equal(1, queues.For(PoolA)!.Count);
    }

    [Fact]
    public async Task Sharing_off_clears_what_an_earlier_version_left_whether_or_not_it_was_moved()
    {
        // Off already when this version first runs: the earlier queue is deleted, not kept aside.
        LegacyQueue(records: 2, forget: true);
        var settings = Sharing(PoolA);
        settings.GameStatsOff = true;
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_folder, "*.json"));
        Assert.Empty(Directory.GetDirectories(_folder, "unknown-*"));
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);

        // Files that are still in the root when the refusal comes (a move that failed earlier in this run): the
        // refusal does not wait for the move.
        LegacyQueue(records: 2, forget: true);
        uploader.Withdraw();
        Assert.Empty(Directory.GetFiles(_folder, "*.json"));
        Assert.False(File.Exists(Path.Combine(_folder, "forget")));

        // And what was set aside while sharing was on goes on the next pass after it is off, without a click.
        settings.GameStatsOff = false;
        LegacyQueue(records: 2, forget: false);
        using var later = Uploader(settings, pool, new GameStatsQueues(_folder));
        Assert.True(await later.PassAsync(CancellationToken.None));
        Assert.Equal(2, later.State.HeldRecords);
        settings.GameStatsOff = true;
        Assert.True(await later.PassAsync(CancellationToken.None));
        Assert.Equal(new GameStatsState(0, 0, 0), later.State);

        Assert.Equal("/stats/v1/events/forget", Assert.Single(pool.Requests).Path);
    }

    [Fact]
    public async Task A_forget_beside_an_unreadable_destination_does_not_count_as_waiting_for_ever()
    {
        var first = new GameStatsQueues(_folder);
        first.For(PoolA)!.Clear(forget: true);
        File.WriteAllText(Path.Combine(_folder, PoolDestination.Key(PoolA), "destination"), "not json");

        var settings = Sharing(PoolB);
        var queues = new GameStatsQueues(_folder);
        Assert.Equal(0, queues.Held(PoolB).Deletions);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
        Assert.False(Directory.Exists(Path.Combine(_folder, PoolDestination.Key(PoolA))));
        Assert.Empty(pool.Requests);
        Assert.Contains("Game stats: queue with an unreadable destination set aside, 0 record(s) not sent, 1 deletion request(s) dropped", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_reports_are_numbered_in_the_order_they_were_made_also_around_a_held_answer()
    {
        var settings = Sharing(PoolA);
        var queues = new GameStatsQueues(_folder);
        queues.For(PoolA)!.Add(Record('a'));
        queues.For(PoolB)!.Add(Record('b'));
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);
        var reports = new List<GameStatsState>();
        uploader.StateChanged += state =>
        {
            lock (reports)
            {
                reports.Add(state);
            }
        };

        // The pass has reported and waits for the pool; the refusal reports meanwhile; the pass reports again after it.
        var held = pool.HoldNext();
        var pass = uploader.PassAsync(CancellationToken.None);
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        settings.GameStatsOff = true;
        uploader.Withdraw();
        held.SetResult(HttpStatusCode.OK);
        await pass;
        Assert.True(await uploader.PassAsync(CancellationToken.None));

        Assert.True(reports.Count >= 2);
        Assert.Equal(reports.Select(r => r.Revision).Order(), reports.Select(r => r.Revision));
        Assert.Equal(reports.Count, reports.Select(r => r.Revision).Distinct().Count());
        Assert.Equal(uploader.State, reports[^1]);
        Assert.Equal(uploader.State.Revision, reports[^1].Revision);
        Assert.Equal(new GameStatsState(0, 0, 0), uploader.State);
    }

    /// <summary>
    /// An install of an earlier version: what its one-time notice wrote is ignored now (no "seen" gate), only the
    /// switch counts. "Turn off" on that notice saved <see cref="AppSettings.GameStatsOff"/>, which stays a refusal.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_left_by_the_old_notice_send_to_the_default_pool_unless_turned_off(bool turnedOff)
    {
        var secret = new string('5', 64);
        var settings = AppSettings.FromJson(
            "{\"GameStatsNoticeShown\":true,\"GameStatsNoticeVersion\":5,\"PoolConsentVersion\":5,\"SourceCaptureNoticeVersion\":1,"
            + "\"GameStatsOff\":" + (turnedOff ? "true" : "false") + ",\"PoolSecret\":\"" + secret + "\"}");
        var queues = new GameStatsQueues(_folder);
        var pool = new HoldingPool();
        using var uploader = Uploader(settings, pool, queues);

        Assert.Equal(!turnedOff, GameStatsClient.Sharing(settings));
        queues.For(PoolClient.DefaultEndpoint)!.Add(Record('d'));
        Assert.True(await uploader.PassAsync(CancellationToken.None));
        if (turnedOff)
        {
            Assert.DoesNotContain(pool.Requests, r => r.Path.EndsWith("/v1/events", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(DefaultHost, Assert.Single(pool.Requests).Host);
        }
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

        /// <summary>The request reaches the pool (it is recorded), the answer never comes back.</summary>
        public bool Offline { get; set; }

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

            if (Offline)
            {
                throw new HttpRequestException("offline");
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

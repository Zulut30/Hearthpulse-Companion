using System.Net;
using System.Text.Json;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// A source spool belongs to one pool address (docs/source-capture.md, "Queue destination"): no chunk, manifest or
/// deletion request of one pool goes to another, through an address change, a removed override, a held answer or a
/// restart, and what waits is visible in the state instead of going quiet.
/// </summary>
public sealed class SourceCaptureDestinationTests
{
    private const string PoolA = "https://capture-a.test/stats";
    private const string PoolB = "https://capture-b.test";
    private const string HostA = "capture-a.test";
    private const string HostB = "capture-b.test";

    private static AppSettings Settings(string? endpoint) => new()
    {
        PoolEndpoint = endpoint,
        PoolSecret = new string('7', 64),
    };

    /// <summary>
    /// One finished capture in a spool folder, generation made for <paramref name="endpoint"/>. With
    /// <paramref name="earlierVersion"/> the state is as a version before the "sent" mark wrote it: such a generation
    /// counts as one the pool may hold.
    /// </summary>
    private static async Task SpoolAsync(string folder, AppSettings settings, string endpoint, bool earlierVersion = false)
    {
        await using (var queue = await SourceCaptureQueue.OpenAsync(folder, pendingRecords: 20000))
        {
            await queue.ConfigureAsync(GameStatsClient.InstallId(settings), endpoint, sharing: true);
            SourceScratch.Complete(queue);
            await queue.FlushAsync();
        }

        if (earlierVersion) WithoutSentMark(folder);
    }

    private static void WithoutSentMark(string folder)
    {
        var path = Path.Combine(folder, "state.json");
        var state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(state.Remove("sent"));
        File.WriteAllText(path, state.ToJsonString());
    }

    private static SourceCaptureHost Host(AppSettings settings, HttpClient http, HearthstoneMemory memory, string root) =>
        new(settings, http, new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false }, root, () => { });

    private static string[] Sources(string root) => Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories);

    [Fact]
    public async Task A_failed_sent_state_write_blocks_every_retry_until_the_mark_is_durable()
    {
        using var scratch = new SourceScratch();
        await using var queue = await SourceCaptureQueue.OpenAsync(scratch.Path, pendingRecords: 20000);
        await queue.ConfigureAsync(SourceScratch.Iid, PoolA, sharing: true);
        SourceScratch.Complete(queue);
        await queue.FlushAsync();
        using var pool = new CapturePool();
        using var http = new HttpClient(pool);
        var client = new SourceCaptureClient(http);
        var obstruction = Path.Combine(scratch.Path, "state.json.tmp");
        Directory.CreateDirectory(obstruction);
        await queue.SendPendingAsync(client);
        await queue.SendPendingAsync(client);
        Assert.Empty(pool.Requests);
        Assert.Equal("disk_error", queue.State.Status);
        using (var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Path, "state.json"))))
        {
            Assert.False(state.RootElement.GetProperty("sent").GetBoolean());
        }

        Directory.Delete(obstruction);
        await queue.SendPendingAsync(client);
        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Path, "state.json")));
        Assert.True(saved.RootElement.GetProperty("sent").GetBoolean());
    }

    [Fact]
    public async Task A_capture_spooled_for_one_pool_is_not_sent_to_another_and_goes_when_its_pool_returns()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        Assert.Equal(1, host.State.PendingCaptures);

        // No settings event: the sender itself notices the address before it sends.
        settings.PoolEndpoint = PoolB;
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.Equal((0, 1, 0), (host.State.PendingCaptures, host.State.HeldCaptures, host.State.HeldDeletions));
        Assert.True(host.Capturing, "the new pool gets a spool and a generation of its own");
        Assert.Single(Sources(scratch.Path));
        Assert.Contains("Source capture: queues for other pools kept, 1 capture(s), 0 deletion request(s)", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();

        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
        Assert.Equal((0, 0), (host.State.PendingCaptures, host.State.HeldCaptures));
    }

    [Fact]
    public async Task Removing_the_override_sends_no_forget_of_the_custom_pool_to_the_default_pool()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        await host.SendPendingAsync(); // Uploaded to the custom pool: it holds something of this generation.
        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();
        Assert.Equal(1, host.State.PendingDeletions); Assert.Empty(Sources(scratch.Path));

        settings.PoolEndpoint = null; settings.GameStatsOff = false;
        Assert.Equal(PoolClient.DefaultEndpoint, PoolDestination.Of(settings));
        await host.ApplySettingsAsync(explicitEnable: true);
        await host.SendPendingAsync();

        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal((0, 1), (host.State.PendingDeletions, host.State.HeldDeletions));
        Assert.True(host.Capturing);

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();

        Assert.Equal([(HostA, "chunks"), (HostA, "complete"), (HostA, "forget")], pool.Requests);
        Assert.Equal((0, 0), (host.State.PendingDeletions, host.State.HeldDeletions));
        Assert.False(host.Capturing, "a pool left by an opt-out captures again only after an explicit enable");
    }

    [Fact]
    public async Task A_chunk_answer_held_while_the_address_changes_settles_nothing_and_nothing_goes_to_the_new_pool()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();

        var held = pool.Hold("chunks");
        var sending = host.SendPendingAsync();
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        settings.PoolEndpoint = PoolB;
        var applying = host.ApplySettingsAsync();
        held.SetResult();
        await sending; await applying;

        Assert.Equal([(HostA, "chunks")], pool.Requests);
        Assert.Single(Sources(SourceCaptureQueue.Folder(scratch.Path, PoolA)));
        Assert.Equal(1, host.State.HeldCaptures);

        await host.SendPendingAsync();
        Assert.Single(pool.Requests);

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();
        Assert.Equal([(HostA, "chunks"), (HostA, "chunks"), (HostA, "complete")], pool.Requests);
        Assert.Equal(0, host.State.PendingCaptures);
    }

    [Fact]
    public async Task A_manifest_answer_and_a_forget_wait_for_their_own_pool_in_a_bare_spool()
    {
        using var scratch = new SourceScratch(); string? current = PoolA;
        await using var queue = await SourceCaptureQueue.OpenAsync(scratch.Path, pendingRecords: 20000, currentDestination: () => current);
        await queue.ConfigureAsync(SourceScratch.Iid, PoolA, sharing: true);
        SourceScratch.Complete(queue); await queue.FlushAsync();
        using var pool = new CapturePool(); using var http = new HttpClient(pool); var client = new SourceCaptureClient(http);

        var held = pool.Hold("complete");
        var sending = queue.SendPendingAsync(client);
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        current = PoolB;
        held.SetResult();
        await sending;
        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
        Assert.Equal(1, queue.State.PendingCaptures);

        await queue.SendPendingAsync(client);
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal("other_pool", queue.State.Status);
        Assert.Contains("Source capture: queue for another pool kept, 1 capture(s), 0 deletion request(s)", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        current = PoolA;
        await queue.SendPendingAsync(client);
        Assert.Equal((HostA, "complete"), pool.Requests[^1]);
        Assert.Equal((3, 0), (pool.Requests.Count, queue.State.PendingCaptures));

        await queue.ConfigureAsync(SourceScratch.Iid, PoolA, sharing: false);
        current = PoolB;
        await queue.SendPendingAsync(client);
        Assert.Equal(3, pool.Requests.Count);
        Assert.Equal(("other_pool", 1), (queue.State.Status, queue.State.PendingDeletions));

        current = null; // No pool at all ("off"): a forget does not leave either.
        await queue.SendPendingAsync(client);
        Assert.Equal(3, pool.Requests.Count);

        current = PoolA;
        await queue.SendPendingAsync(client);
        Assert.Equal((HostA, "forget"), pool.Requests[^1]);
        Assert.Equal((4, 0), (pool.Requests.Count, queue.State.PendingDeletions));
    }

    [Fact]
    public async Task A_restart_keeps_the_spool_with_its_own_pool()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);

        settings.PoolEndpoint = PoolB;
        await using (var restarted = Host(settings, http, memory, scratch.Path))
        {
            await restarted.ApplySettingsAsync();
            await restarted.SendPendingAsync();
            Assert.Empty(pool.Requests);
            Assert.Equal(1, restarted.State.HeldCaptures);
        }

        settings.PoolEndpoint = PoolA;
        await using var again = Host(settings, http, memory, scratch.Path);
        await again.ApplySettingsAsync();
        await again.SendPendingAsync();
        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
    }

    [Fact]
    public async Task A_spool_of_the_earlier_version_stays_with_the_pool_its_state_names()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(null);
        // The layout before destinations: state and sources directly in source-capture/, generation made for pool A.
        await SpoolAsync(scratch.Path, settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.Equal(1, host.State.HeldCaptures);
        Assert.False(File.Exists(Path.Combine(scratch.Path, "state.json")));
        Assert.Single(Sources(SourceCaptureQueue.Folder(scratch.Path, PoolA)));
        Assert.Contains("Source capture: earlier queue kept for its own pool, 1 capture(s)", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();
        Assert.Equal([(HostA, "chunks"), (HostA, "complete")], pool.Requests);
    }

    [Fact]
    public async Task A_forget_in_a_spool_of_the_earlier_version_moves_to_the_pool_it_is_addressed_to()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolB); var iid = GameStatsClient.InstallId(settings);
        await using (var first = await SourceCaptureQueue.OpenAsync(scratch.Path, pendingRecords: 20000))
            await first.ConfigureAsync(iid, PoolA, sharing: true);
        WithoutSentMark(scratch.Path); // Written by the earlier version, which may have uploaded for pool A.
        await using (var legacy = await SourceCaptureQueue.OpenAsync(scratch.Path, pendingRecords: 20000))
        {
            await legacy.ConfigureAsync(iid, PoolA, sharing: false);
            await legacy.ConfigureAsync(iid, PoolB, sharing: true, explicitEnable: true);
            SourceScratch.Complete(legacy); await legacy.FlushAsync();
        }
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Equal([(HostB, "chunks"), (HostB, "complete")], pool.Requests);
        Assert.Equal((0, 1), (host.State.PendingDeletions, host.State.HeldDeletions));

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();
        Assert.Equal((HostA, "forget"), pool.Requests[^1]);
        Assert.Equal((3, 0), (pool.Requests.Count, host.State.HeldDeletions));
        Assert.False(host.Capturing);
    }

    [Fact]
    public async Task A_spool_of_the_earlier_version_with_unreadable_state_is_set_aside_and_sent_nowhere()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        Directory.CreateDirectory(Path.Combine(scratch.Path, SourceScratch.CaptureId));
        File.WriteAllText(Path.Combine(scratch.Path, SourceScratch.CaptureId, "0000.jsonl"), "{\"seq\":0}\n");
        File.WriteAllText(Path.Combine(scratch.Path, "state.json"), "{\"iid\":");
        var settings = Settings(PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.True(host.Capturing);
        Assert.Equal((0, 1), (host.State.PendingCaptures, host.State.HeldCaptures));
        Assert.False(File.Exists(Path.Combine(scratch.Path, "state.json")));
        Assert.Contains("Source capture: earlier queue has unreadable state, set aside, 1 capture(s) not sent", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();
        Assert.Empty(Sources(scratch.Path));
        Assert.Equal(0, host.State.HeldCaptures);
    }

    [Fact]
    public async Task Unreadable_state_of_the_current_pools_spool_sends_nothing_and_does_not_throw()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        File.WriteAllText(Path.Combine(SourceCaptureQueue.Folder(scratch.Path, PoolA), "state.json"), "{\"endpoint\":");
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.False(host.Capturing);
        Assert.Equal("unavailable", host.State.Status);
    }

    [Fact]
    public async Task A_spool_whose_state_names_another_pool_than_its_folder_is_kept_and_not_sent()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        // The folder of pool A holds a generation made for pool B: the state decides, not the folder name.
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolB);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.False(host.Capturing);
        Assert.Equal(("other_pool", 1), (host.State.Status, host.State.PendingCaptures));
    }

    [Fact]
    public async Task Opt_out_deletes_what_waits_for_other_pools_and_each_forget_waits_for_its_own_pool()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolB);
        // Both pools may hold sources of this install (spools of a version that uploaded to them), a third never got any.
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA, earlierVersion: true);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolB), settings, PoolB, earlierVersion: true);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, "https://capture-c.test"), settings, "https://capture-c.test");
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        Assert.True(host.Capturing); Assert.Equal(2, host.State.HeldCaptures);

        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();

        Assert.Empty(Sources(scratch.Path));
        Assert.Equal((1, 0, 1), (host.State.PendingDeletions, host.State.HeldCaptures, host.State.HeldDeletions));

        await host.SendPendingAsync();
        Assert.Equal([(HostB, "forget")], pool.Requests);
        Assert.Equal(1, host.State.HeldDeletions);

        settings.PoolEndpoint = PoolA;
        await host.SendPendingAsync();
        Assert.Equal([(HostB, "forget"), (HostA, "forget")], pool.Requests);
        Assert.Equal((0, 0), (host.State.PendingDeletions, host.State.HeldDeletions));

        // The pool nothing was sent to is asked nothing, now or when it is the current one.
        settings.PoolEndpoint = "https://capture-c.test";
        await host.SendPendingAsync();
        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(0, host.State.PendingDeletions);
    }

    [Fact]
    public async Task A_first_refusal_before_anything_was_sent_makes_no_request()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        // A fresh install: sharing is on by default, so capture starts with an install key; nothing is sent yet.
        var settings = new AppSettings { PoolEndpoint = PoolA };
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        Assert.True(host.Capturing);

        // Both Settings → Data switches turned off, then what the hosts call.
        settings.GameStatsOff = true;
        settings.PoolConsent = false;
        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.Equal((0, 0), (host.State.PendingDeletions, host.State.HeldDeletions));
    }

    [Fact]
    public async Task A_refusal_saved_before_the_first_start_makes_no_install_secret()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = new AppSettings { PoolEndpoint = PoolA, GameStatsOff = true, PoolConsent = false };
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.False(host.Capturing);
        Assert.Empty(pool.Requests);
        Assert.Null(settings.PoolSecret);
    }

    [Fact]
    public async Task Turning_the_switch_off_before_the_first_chunk_left_makes_no_request()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        // Sharing is on and a game was captured; nothing has been handed to the pool yet.
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        await host.ApplySettingsAsync();
        Assert.Equal(1, host.State.PendingCaptures);

        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.Empty(Sources(scratch.Path));
        Assert.Equal((0, 0), (host.State.PendingCaptures, host.State.PendingDeletions));
        Assert.Contains("Source capture: nothing was sent for this generation, no deletion request", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // It stays revoked: back on without an explicit enable captures nothing.
        settings.GameStatsOff = false;
        await host.ApplySettingsAsync();
        Assert.False(host.Capturing);
    }

    [Fact]
    public async Task A_state_without_its_deletion_list_fails_closed_instead_of_stopping_the_sender()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        File.WriteAllText(Path.Combine(SourceCaptureQueue.Folder(scratch.Path, PoolA), "state.json"), "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => SourceCaptureQueue.OpenAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA)));
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();

        Assert.Empty(pool.Requests);
        Assert.False(host.Capturing);
        Assert.Equal("unavailable", host.State.Status);
        Assert.Contains("Source capture: unavailable", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // The refusal still works: the sources of the unreadable spool go, and nothing is asked of any pool.
        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();
        Assert.Empty(Sources(scratch.Path));
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task Sharing_off_clears_an_earlier_spool_that_could_not_be_moved()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolB);
        // The earlier version's spool for pool A is in the root, and the folder it should move to has unreadable state.
        await SpoolAsync(scratch.Path, settings, PoolA);
        Directory.CreateDirectory(SourceCaptureQueue.Folder(scratch.Path, PoolA));
        File.WriteAllText(Path.Combine(SourceCaptureQueue.Folder(scratch.Path, PoolA), "state.json"), "{\"iid\":");
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);

        await host.ApplySettingsAsync();
        await host.SendPendingAsync();
        Assert.Empty(pool.Requests);
        Assert.Single(Sources(scratch.Path)); // Still in the root, not sent.
        Assert.Contains("Source capture: earlier queue could not be moved, not sent", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();

        Assert.Empty(Sources(scratch.Path));
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task State_reports_are_numbered_in_the_order_they_were_published()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        await using var host = Host(settings, http, memory, scratch.Path);
        var reports = new List<SourceCaptureState>();
        host.StateChanged += state => { lock (reports) reports.Add(state); };
        await host.ApplySettingsAsync();

        // A send waits for the pool while the settings task publishes; the send publishes after it.
        var held = pool.Hold("chunks");
        var sending = host.SendPendingAsync();
        await pool.Arrived.WaitAsync(TimeSpan.FromSeconds(30));
        settings.PoolEndpoint = PoolB;
        var applying = host.ApplySettingsAsync();
        held.SetResult();
        await sending; await applying;

        Assert.True(reports.Count >= 3);
        Assert.Equal(Enumerable.Range(1, reports.Count).Select(i => (long)i), reports.Select(r => r.Revision));
        Assert.Equal(1, reports[^1].HeldCaptures);
    }

    [Fact]
    public async Task An_open_spool_that_belongs_to_another_pool_still_applies_the_age_bound()
    {
        using var scratch = new SourceScratch(); string? current = PoolA;
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var queue = await SourceCaptureQueue.OpenAsync(scratch.Path, pendingRecords: 20000, timeProvider: time, currentDestination: () => current);
        await queue.ConfigureAsync(SourceScratch.Iid, PoolA, sharing: true);
        SourceScratch.Complete(queue); await queue.FlushAsync();
        using var pool = new CapturePool(); using var http = new HttpClient(pool); var client = new SourceCaptureClient(http);

        current = PoolB;
        await queue.SendPendingAsync(client);
        Assert.Equal(("other_pool", 1), (queue.State.Status, queue.State.PendingCaptures));

        time.Advance(TimeSpan.FromDays(15));
        await queue.SendPendingAsync(client);

        Assert.Equal(0, queue.State.PendingCaptures);
        Assert.Empty(Sources(scratch.Path));
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task Sources_kept_for_another_pool_still_expire_after_fourteen_days()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var settings = Settings(PoolA);
        await SpoolAsync(SourceCaptureQueue.Folder(scratch.Path, PoolA), settings, PoolA);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using var pool = new CapturePool(); using var http = new HttpClient(pool);
        settings.PoolEndpoint = PoolB;
        await using var host = new SourceCaptureHost(settings, http, new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false }, scratch.Path, () => { }) { Time = time };
        await host.ApplySettingsAsync();
        Assert.Equal(1, host.State.HeldCaptures);

        // A delivery pass, not a settings change: the bound holds in a long session too.
        time.Advance(TimeSpan.FromDays(15));
        await host.SendPendingAsync();

        Assert.Equal(0, host.State.HeldCaptures);
        Assert.Empty(Sources(scratch.Path));
        Assert.Empty(pool.Requests);
    }

    /// <summary>A pool that answers every capture route with a valid receipt, records where each request went, and can hold one route.</summary>
    private sealed class CapturePool : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private string? _heldRoute;
        private TaskCompletionSource? _hold;
        private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Host, string Route)> Requests { get; } = [];

        /// <summary>Completes when the held request has reached the pool.</summary>
        public Task Arrived => _arrived.Task;

        /// <summary>The next request to <paramref name="route"/> is answered only once the returned source completes.</summary>
        public TaskCompletionSource Hold(string route)
        {
            lock (_gate)
            {
                _heldRoute = route;
                _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = request.RequestUri!.AbsolutePath.Split('/')[^1];
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement;
            object answer = route switch
            {
                "chunks" => new { ok = true, stored = true, duplicate = false, index = body.GetProperty("index").GetInt32(), sha256 = body.GetProperty("sha256").GetString() },
                "complete" => new { ok = true, status = "stored", captureId = body.GetProperty("captureId").GetString() },
                _ => new { ok = true, status = "deleted" },
            };
            TaskCompletionSource? hold = null;
            lock (_gate)
            {
                Requests.Add((request.RequestUri.Host, route));
                if (_hold is not null && _heldRoute == route) { hold = _hold; _hold = null; }
            }

            if (hold is not null)
            {
                _arrived.TrySetResult();
                await hold.Task;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(answer)) };
        }
    }
}

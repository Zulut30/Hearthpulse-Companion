using System.Net;
using System.Net.Http.Json;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

public sealed class SourceCaptureHostTests
{
    [Fact]
    public async Task An_old_notice_cannot_start_capture_but_the_current_notice_can()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, GameStatsNoticeShown = true };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        var saved = 0;
        await using var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => saved++);
        await host.ApplySettingsAsync();
        Assert.False(host.Capturing); Assert.Equal(0, handler.Requests);
        Assert.Null(settings.PoolSecret); // No install secret before the notice: there is nothing to identify yet.
        settings.SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion;
        await host.ApplySettingsAsync();
        Assert.False(host.Capturing); Assert.Null(settings.PoolSecret); // The source notice of an earlier pool is not the current one-time notice.
        settings.PoolNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion;
        await host.ApplySettingsAsync();
        Assert.True(host.Capturing); Assert.Equal(3, saved);
        Assert.Equal(64, settings.PoolSecret?.Length);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Opt_out_persists_forget_and_does_not_claim_deletion_before_confirmation()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        await using var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => { });
        await host.ApplySettingsAsync(); Assert.True(host.Capturing);
        await UploadOneAsync(host, session); // The pool holds a source of this generation: the opt-out owes it a forget.
        settings.GameStatsOff = true;
        await host.ApplySettingsAsync();
        Assert.False(host.Capturing); Assert.Equal(1, host.State.PendingDeletions); Assert.Equal(0, handler.Requests);
        await host.SendPendingAsync();
        Assert.Equal(1, handler.Requests); Assert.Equal(0, host.State.PendingDeletions);
        settings.GameStatsOff = false;
        await host.ApplySettingsAsync(); Assert.False(host.Capturing);
        await host.ApplySettingsAsync(explicitEnable: true); Assert.True(host.Capturing);
    }

    [Fact]
    public async Task Settings_save_failure_never_grants_capture_or_source_delivery()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        await using var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => throw new IOException("synthetic_save_failure"));
        await host.ApplySettingsAsync(); await host.SendPendingAsync();
        Assert.False(host.Capturing); Assert.Equal("unavailable", host.State.Status); Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task A_real_settings_write_failure_never_enables_source_capture()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        var blocked = Path.Combine(scratch.Path, "missing", "settings.json");
        await using var host = new SourceCaptureHost(settings, http, session, Path.Combine(scratch.Path, "spool"), () => settings.SaveRequired(blocked));
        await host.ApplySettingsAsync();
        Assert.False(host.Capturing);
        Assert.Equal("unavailable", host.State.Status);
    }

    [Fact]
    public async Task Opt_out_remains_revoked_after_restart_even_when_settings_cannot_be_saved()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        var failSave = false;
        await using (var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => { if (failSave) throw new IOException("synthetic_save_failure"); }))
        {
            await host.ApplySettingsAsync();
            Assert.True(host.Capturing);
            session.OnSessionStarted("synthetic", new(2026, 11, 9), attachedMidSession: false);
            session.OnLines("Power.log", ReplayRecorderTests.Setup.Select(b => "D 12:30:00.0000000 GameState." + b).ToArray(), catchingUp: false);
            session.StopSourceCapture();
            await host.ApplySettingsAsync(); // Flush the captured source before testing withdrawal.
            Assert.Equal(1, host.State.PendingCaptures);
            // A chunk is handed to the pool, which does not take it now: the source stays, and the pool may hold a part.
            handler.Busy = true; await host.SendPendingAsync(); Assert.Equal(1, host.State.PendingCaptures);
            failSave = true; settings.GameStatsOff = true;
            await host.ApplySettingsAsync();
            Assert.False(host.Capturing);
            Assert.Equal(1, host.State.PendingDeletions);
            Assert.Empty(Directory.GetFiles(scratch.Path, "*.jsonl", SearchOption.AllDirectories));
        }
        await using var reopened = await SourceCaptureQueue.OpenAsync(SourceCaptureQueue.Folder(scratch.Path, SourceScratch.Endpoint));
        Assert.Null(reopened.Generation);
        Assert.Equal(1, reopened.State.PendingDeletions);
        settings.GameStatsOff = false; // Last successfully saved settings still said enabled.
        await reopened.ConfigureAsync(settings);
        Assert.False(reopened.Capturing);
        Assert.Null(reopened.Generation);
    }

    [Fact]
    public async Task Graceful_shutdown_preserves_the_open_unpicked_draft_offer()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false, Clock = () => new(2026, 11, 9) };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        await using (var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => { }))
        {
            await host.ApplySettingsAsync();
            session.OnLines("LoadingScreen.log", ["D 12:30:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=DRAFT"], false);
            session.SetDraftOffer(["DRAFT_A", "DRAFT_B", "DRAFT_C"]);
        }
        await using var reopened = await SourceCaptureQueue.OpenAsync(SourceCaptureQueue.Folder(scratch.Path, SourceScratch.Endpoint));
        Assert.Equal(1, reopened.State.PendingCaptures);
        var source = string.Join('\n', Directory.GetFiles(scratch.Path, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("draft_choice", source);
        Assert.Contains("DRAFT_B", source);
        Assert.Contains("\"picked\":null", source);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));
        var result = await new Hearthpulse.Core.SourceCapture.Processing.SourceCaptureParser().ParseAsync(stream);
        Assert.Equal("partial", result.Status);
        Assert.Equal(1, result.Summary.DraftChoices);
        Assert.False(result.Summary.CompleteForAggregation);
    }

    [Fact]
    public async Task Rapid_off_then_explicit_on_is_serialized_and_retains_old_generation_deletion()
    {
        using var scratch = new SourceScratch(); using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { ReadsMemory = false };
        var settings = new AppSettings { PoolEndpoint = SourceScratch.Endpoint, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = Hearthpulse.Core.Pool.PoolClient.NoticeVersion };
        using var handler = new DeletionHandler(); using var http = new HttpClient(handler);
        await using var host = new SourceCaptureHost(settings, http, session, scratch.Path, () => { });
        await host.ApplySettingsAsync();
        await UploadOneAsync(host, session);
        settings.GameStatsOff = true; var off = host.ApplySettingsAsync();
        settings.GameStatsOff = false; var on = host.ApplySettingsAsync(explicitEnable: true);
        await Task.WhenAll(off, on);
        Assert.True(host.Capturing); Assert.Equal(1, host.State.PendingDeletions);
        await host.SendPendingAsync();
        Assert.Equal(1, handler.Requests); Assert.Equal(0, host.State.PendingDeletions);
    }

    /// <summary>One synthetic game captured and uploaded: from here the pool holds a source of the current generation.</summary>
    private static async Task UploadOneAsync(SourceCaptureHost host, GameSession session)
    {
        session.OnSessionStarted("synthetic", new(2026, 11, 9), attachedMidSession: false);
        session.OnLines("Power.log", ReplayRecorderTests.Setup.Select(b => "D 12:30:00.0000000 GameState." + b).ToArray(), catchingUp: false);
        session.StopSourceCapture();
        await host.ApplySettingsAsync();
        Assert.Equal(1, host.State.PendingCaptures);
        await host.SendPendingAsync();
        Assert.Equal(0, host.State.PendingCaptures);
    }

    /// <summary>Counts deletion requests and confirms them; takes uploads with valid receipts, or with <see cref="Busy"/> answers 503 to them.</summary>
    private sealed class DeletionHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Busy { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = request.RequestUri!.AbsolutePath.Split('/')[^1];
            if (route is "chunks" or "complete")
            {
                if (Busy) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                using var json = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var body = json.RootElement;
                object receipt = route == "chunks"
                    ? new { ok = true, stored = true, duplicate = false, index = body.GetProperty("index").GetInt32(), sha256 = body.GetProperty("sha256").GetString() }
                    : new { ok = true, status = "stored", captureId = body.GetProperty("captureId").GetString() };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(receipt) };
            }

            Requests++;
            Assert.Equal("forget", route);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, status = "deleted" }) };
        }
    }
}

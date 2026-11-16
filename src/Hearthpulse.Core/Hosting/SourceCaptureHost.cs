using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Hosting;

/// <summary>
/// One lifecycle owner for the source spool, live recorder and cancellable background delivery. The spool in use is
/// the one of the pool address the settings name now (<see cref="SourceCaptureQueue.Folder"/>): when the address
/// changes, the old spool is closed with its sources and deletion requests intact and waits for its own pool, and
/// the new address gets a spool and a generation of its own (docs/source-capture.md, "Queue destination").
/// </summary>
public sealed class SourceCaptureHost : IDisposable, IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly GameSession _session;
    private readonly SourceCaptureClient _client;
    private readonly string _directory;
    private readonly Action _persistSettings;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Task _configuration = Task.CompletedTask;
    private Task? _loop;
    private volatile SourceCaptureQueue? _queue;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The active delivery operation owns this temporary reference and disposes it in LoopAsync finally after removing it under the gate.")]
    private CancellationTokenSource? _sending;
    private volatile bool _disposed;
    private long _revision;
    private SourceCaptureState? _failure;
    private volatile string? _destination;
    private volatile string _idle = "disabled";
    private volatile SourceCaptureHeld _held = new(0, 0);
    private bool _migrated;

    public SourceCaptureHost(AppSettings settings, HttpClient http, GameSession session, string directory, Action persistSettings)
    {
        _settings = settings; _session = session; _directory = directory; _persistSettings = persistSettings;
        _client = new(http);
        _session.SourceCaptureRecorded += OnFrame;
    }

    public SourceCaptureState State
    {
        get
        {
            var state = _failure ?? _queue?.State ?? new(_idle, 0, 0, 0);
            var held = _held;
            return state with { HeldCaptures = held.Captures, HeldDeletions = held.Deletions };
        }
    }

    /// <summary>Clock for the age bound of sources kept for other pools; tests move it.</summary>
    internal TimeProvider Time { get; init; } = TimeProvider.System;

    public bool Capturing => !_disposed && _failure is null && _queue?.Capturing == true;
    public event Action<SourceCaptureState>? StateChanged;

    /// <summary>Called only by live hosts; constructing services during a render creates no spool or network work.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loop is not null) return;
            _loop = Task.Run(LoopAsync);
        }
    }

    /// <summary>Capture settings at the user action, stop new recording immediately, then serialize durable changes.</summary>
    public Task ApplySettingsAsync(bool explicitEnable = false)
    {
        if (_disposed) return Task.CompletedTask;
        _session.SetSourceCaptureEnabled(false);
        var iid = GameStatsClient.InstallId(_settings);
        var destination = PoolDestination.Of(_settings);
        var endpoint = _settings.SourceCaptureNoticeVersion >= SourceCaptureClient.NoticeVersion ? destination : null;
        var sharing = !_settings.GameStatsOff;
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            _sending?.Cancel();
            var revision = ++_revision;
            var before = _configuration;
            _configuration = Task.Run(async () =>
            {
                try { await before.ConfigureAwait(false); } catch (Exception) { /* Each operation publishes its own safe failure. */ }
                try
                {
                    var queue = await OpenForAsync(destination).ConfigureAwait(false);
                    // Withdrawal must survive even when settings storage is broken. Enabling needs a durable identity first.
                    if (!sharing)
                    {
                        if (queue is not null) await queue.ConfigureAsync(iid, endpoint, sharing: false, destination: destination).ConfigureAwait(false);
                        await WithdrawOthersAsync(destination, iid).ConfigureAwait(false);
                    }
                    _persistSettings();
                    if (sharing && queue is not null) await queue.ConfigureAsync(iid, endpoint, sharing: true, explicitEnable, destination).ConfigureAwait(false);
                    _failure = null;
                    ReportHeld(destination);
                    if (!_disposed && revision == Interlocked.Read(ref _revision)) _session.SetSourceCaptureEnabled(queue?.Capturing == true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
                {
                    _failure = new("unavailable", 0, _queue?.State.PendingCaptures ?? 0, _queue?.State.PendingDeletions ?? 0);
                    if (_queue is { } queue)
                    {
                        try { await queue.ConfigureAsync(iid, null, sharing, destination: destination).ConfigureAwait(false); }
                        catch (Exception pauseError) when (pauseError is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                    }
                    Diag.Info("Source capture: unavailable");
                }
                Publish(); Wake();
            });
            return _configuration;
        }
    }

    /// <summary>
    /// The spool of <paramref name="destination"/>, opened in place of the one of an earlier address. That one is
    /// flushed and closed as it is: nothing of it is moved, and it is opened again when its address comes back.
    /// </summary>
    private async Task<SourceCaptureQueue?> OpenForAsync(string? destination)
    {
        if (!_migrated)
        {
            SourceCaptureQueue.MigrateLegacy(_directory);
            _migrated = true;
        }

        if (_queue is { } open && _destination == destination) return open;
        if (_queue is { } old)
        {
            _queue = null;
            try { await old.FlushAsync().ConfigureAwait(false); }
            finally { await old.DisposeAsync().ConfigureAwait(false); }
        }

        _destination = destination;
        _idle = destination is null ? "unavailable" : "disabled";
        if (destination is null) return null;
        var queue = await SourceCaptureQueue.OpenAsync(SourceCaptureQueue.Folder(_directory, destination), timeProvider: Time,
            currentDestination: () => PoolDestination.Of(_settings)).ConfigureAwait(false);
        _queue = queue;
        return queue;
    }

    /// <summary>
    /// Opt-out reaches the spools kept for other pools too: their sources are deleted now, and each keeps the deletion
    /// request for its own pool, which is sent when the settings name that pool again. A spool without a readable
    /// address is only deleted.
    /// </summary>
    private async Task WithdrawOthersAsync(string? destination, string iid)
    {
        var cleared = 0;
        foreach (var folder in SourceCaptureQueue.OtherFolders(_directory, destination, unknown: true))
        {
            try
            {
                if (!PoolDestination.IsKey(Path.GetFileName(folder))) { Directory.Delete(folder, recursive: true); cleared++; continue; }
                var other = await SourceCaptureQueue.OpenAsync(folder, timeProvider: Time).ConfigureAwait(false);
                try { await other.ConfigureAsync(iid, null, sharing: false).ConfigureAwait(false); }
                finally { await other.DisposeAsync().ConfigureAwait(false); }
                cleared++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                var sources = 0;
                try { sources = SourceCaptureQueue.DeleteSources(folder); }
                catch (Exception delete) when (delete is IOException or UnauthorizedAccessException) { }
                Diag.Info($"Source capture: a queue kept for another pool has unreadable state, {sources} capture(s) deleted, no deletion request possible");
            }
        }

        if (cleared > 0) Diag.Info($"Source capture: sharing turned off, cleared {cleared} queue(s) kept for other pools");
    }

    /// <summary>Counts what waits for other pools (after the age bound) and writes one log line when that changed.</summary>
    private void ReportHeld(string? destination)
    {
        SourceCaptureQueue.SweepOthers(_directory, destination, Time);
        var held = SourceCaptureQueue.Held(_directory, destination);
        var before = _held;
        _held = held;
        if (held != before && (held.Captures > 0 || held.Deletions > 0))
            Diag.Info($"Source capture: queues for other pools kept, {held.Captures} capture(s), {held.Deletions} deletion request(s)");
    }

    /// <summary>The address in the settings is no longer the open spool's: switch spools before anything is sent.</summary>
    private Task FollowDestinationAsync()
    {
        lock (_gate)
        {
            if (_disposed || PoolDestination.Of(_settings) == _destination) return _configuration;
        }

        return ApplySettingsAsync();
    }

    private void OnFrame(SourceCaptureFrame frame)
    {
        _queue?.TryAppend(frame);
        if (frame.Record.Kind == "end") Wake();
    }

    private void Wake()
    {
        lock (_gate)
            if (!_disposed && _wake.CurrentCount == 0) _wake.Release();
    }

    private void Publish() => StateChanged?.Invoke(State);

    /// <summary>Explicit durability/delivery pass, also used by diagnostics without starting a periodic loop.</summary>
    public async Task SendPendingAsync(CancellationToken cancellationToken = default)
    {
        Task configuration;
        lock (_gate) { if (_disposed) return; configuration = _configuration; }
        await configuration.ConfigureAwait(false);
        await FollowDestinationAsync().ConfigureAwait(false);
        if (_queue is not { } queue) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        try
        {
            await queue.FlushAsync().ConfigureAwait(false);
            await queue.SendPendingAsync(_client, stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or System.Threading.Channels.ChannelClosedException)
        {
            // The address changed meanwhile and this spool was closed with its contents: nothing of it was sent.
        }
        Publish();
    }

    private async Task LoopAsync()
    {
        try
        {
            await ApplySettingsAsync().ConfigureAwait(false);
            while (!_stop.IsCancellationRequested)
            {
                // An address changed without a settings event (settings.json edited, a test pool removed) switches spools here.
                await FollowDestinationAsync().ConfigureAwait(false);
                Task configuration;
                CancellationTokenSource sending;
                lock (_gate)
                {
                    configuration = _configuration;
                    sending = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    _sending = sending;
                }
                try
                {
                    await configuration.ConfigureAwait(false);
                    if (_queue is { } queue) await queue.SendPendingAsync(_client, sending.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { /* The spool of an earlier address was closed; the next turn uses the new one. */ }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { Diag.Info("Source capture: delivery postponed"); }
                finally
                {
                    lock (_gate) { if (_sending == sending) _sending = null; }
                    sending.Dispose();
                }
                Publish();
                await _wake.WaitAsync(TimeSpan.FromSeconds(30), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop; Task configuration;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            loop = _loop; configuration = _configuration;
        }
        await _stop.CancelAsync().ConfigureAwait(false);
        _session.StopSourceCapture();
        _session.SetSourceCaptureEnabled(false);
        _session.SourceCaptureRecorded -= OnFrame;
        await configuration.ConfigureAwait(false);
        if (loop is not null) await loop.ConfigureAwait(false);
        if (_queue is { } queue) { await queue.FlushAsync().ConfigureAwait(false); await queue.DisposeAsync().ConfigureAwait(false); }
        _wake.Dispose(); _stop.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "The WPF/Avalonia shutdown hooks are synchronous. All source I/O runs off-context, the sender is cancelled first, and source is flushed before the process exits.")]
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

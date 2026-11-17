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
    private readonly Lock _publishGate = new();
    private long _published;
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
        var destination = PoolDestination.Of(_settings);
        var sharing = !_settings.GameStatsOff;
        // The install key is a hash of the pool secret. It is made only for a player who shares with a pool set up; for
        // a refusal the existing one is used, or none: a refusal never makes a secret.
        var iid = _settings.PoolSecret is { Length: 64 } || sharing && destination is not null ? GameStatsClient.InstallId(_settings) : null;
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
                    SourceCaptureQueue? queue = null;
                    var unreadable = false;
                    try { queue = await OpenForAsync(destination).ConfigureAwait(false); }
                    catch (Exception ex) when (!sharing && ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
                    { unreadable = true; }
                    // Withdrawal must survive even when settings storage is broken. Enabling needs a durable identity first.
                    if (!sharing)
                    {
                        // The refusal reaches everything, whatever could not be opened or moved: the current pool's
                        // spool, the spools of other pools, and what an earlier version left in the root.
                        if (queue is not null) await queue.ConfigureAsync(iid, destination, sharing: false, destination: destination).ConfigureAwait(false);
                        else if (unreadable && destination is not null) DeleteUnreadable(SourceCaptureQueue.Folder(_directory, destination));
                        await WithdrawOthersAsync(destination, iid).ConfigureAwait(false);
                        if (!_migrated) DeleteUnreadable(_directory);
                        if (unreadable) throw new InvalidDataException("Source spool is unavailable.");
                    }
                    _persistSettings();
                    if (sharing && queue is not null) await queue.ConfigureAsync(iid, destination, sharing: true, explicitEnable, destination).ConfigureAwait(false);
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
        // Tried again on every settings pass until it works; what stays in the root meanwhile is never sent.
        _migrated = _migrated || SourceCaptureQueue.MigrateLegacy(_directory);

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
    private async Task WithdrawOthersAsync(string? destination, string? iid)
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
                DeleteUnreadable(folder);
            }
        }

        if (cleared > 0) Diag.Info($"Source capture: sharing turned off, cleared {cleared} queue(s) kept for other pools");
    }

    /// <summary>Sharing is off and this folder's state cannot be read: its sources go; no deletion request can be made for it.</summary>
    private static void DeleteUnreadable(string folder)
    {
        var sources = 0;
        try { sources = SourceCaptureQueue.DeleteSources(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (sources > 0) Diag.Info($"Source capture: sharing is off, {sources} capture(s) of a queue with unreadable state deleted, no deletion request possible");
    }

    /// <summary>
    /// The periodic part of a delivery pass: the age bound of what is kept for other pools holds in a long session too,
    /// not only when settings are applied.
    /// </summary>
    private void SweepHeld()
    {
        try { ReportHeld(_destination); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Diag.Info($"Source capture: queues kept for other pools could not be checked ({ex.GetType().Name})"); }
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

    /// <summary>
    /// Reads and raises the state under one lock and numbers it: the settings task and the delivery loop both publish,
    /// and a report must not overtake a newer one on its way to the UI.
    /// </summary>
    private void Publish()
    {
        lock (_publishGate) StateChanged?.Invoke(State with { Revision = ++_published });
    }

    /// <summary>Explicit durability/delivery pass, also used by diagnostics without starting a periodic loop.</summary>
    public async Task SendPendingAsync(CancellationToken cancellationToken = default)
    {
        Task configuration;
        lock (_gate) { if (_disposed) return; configuration = _configuration; }
        await configuration.ConfigureAwait(false);
        await FollowDestinationAsync().ConfigureAwait(false);
        if (_queue is not { } queue) { SweepHeld(); Publish(); return; }
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
        SweepHeld();
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
                SweepHeld();
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

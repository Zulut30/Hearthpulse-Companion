using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.GameStats;

/// <summary>
/// Sends every finished game the player agreed to share as a game record (<see cref="GameStatsRecord"/>,
/// docs/game-stats.md). A game is built from its history row and its saved replay off the log and UI threads, waits
/// in <see cref="GameStatsQueue"/>, and goes out in batches of up to 20 with a growing pause after a failure. Turning
/// sharing off drops what waits and asks the pool to delete what this install sent; that request is kept until the
/// pool confirms it. On by default (<see cref="AppSettings.GameStatsOff"/> is the opt-out).
/// Every queue belongs to one pool address (<see cref="GameStatsQueues"/>): a record and a deletion request go only to
/// the pool they were queued for, and wait in their own folder while the settings name another pool.
/// </summary>
public sealed class GameStatsUploader : IDisposable
{
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    /// <summary>Pause between two batches: the pool takes 6 requests a minute from one address.</summary>
    internal static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(12);

    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly HistoryStore _history;
    private readonly Func<ReplayStore?> _replays;
    private readonly Func<CardDb?> _cards;
    private readonly string? _version;
    private readonly GameStatsQueues _queues;
    private readonly Lock _stateGate = new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not disposed: the loop may still wait on it while it winds down after Dispose cancels _stop.")]
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not disposed: the loop may still read its token while it winds down.")]
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _failures;
    private readonly Lock _prepareGate = new();
    private bool _migrated;
    private bool _layoutChecked;
    private GameStatsState _state = new(0, 0, 0);

    public GameStatsUploader(AppSettings settings, HttpClient http, HistoryStore history, Func<ReplayStore?> replays, Func<CardDb?> cards, string? version, GameStatsQueues queues)
    {
        _settings = settings;
        _http = http;
        _history = history;
        _replays = replays;
        _cards = cards;
        _version = version;
        _queues = queues;
    }

    /// <summary>Records waiting to be sent to the current pool.</summary>
    public int PendingCount => PoolDestination.Of(_settings) is { } destination && _queues.For(destination) is { } queue ? queue.Count : 0;

    /// <summary>What waits for the current pool and what is kept for other pools, as of the last pass or withdrawal.</summary>
    public GameStatsState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Raised when <see cref="State"/> changed, in order, on the thread that changed it (not the UI thread). Handlers
    /// only hand the state on: they run under the sender's state lock.
    /// </summary>
    public event Action<GameStatsState>? StateChanged;

    /// <summary>Pause before a batch; tests make it zero.</summary>
    internal TimeSpan Delay { get; init; } = BatchDelay;

    /// <summary>Starts the background sender (live hosts only; a render never starts it).</summary>
    public void Start()
    {
        // Before the player can answer the notice: an install that never sent anything is told apart here.
        Prepare();

        // The install id comes from the pool secret: make and save it before the first record, so a crash before exit
        // cannot leave sent records that turning the switch off later no longer finds (LEGAL-game-stats L-m2).
        if (GameStatsClient.Sharing(_settings) && _settings.PoolSecret is not { Length: 64 })
        {
            PoolClient.Secret(_settings);
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Error("Game stats: saving the install secret failed", ex);
            }
        }

        _loop ??= Task.Run(() => LoopAsync(_stop.Token));
        Kick();
    }

    public void Kick() => _wake.Release();

    /// <summary>
    /// A game ended (the session raises it again once a Battlegrounds rating arrives; only the first counts). Builds and
    /// queues its record when the player shares game records. Called on the log thread: the work runs on the pool.
    /// </summary>
    public void OnGameFinished(GameOutcome outcome)
    {
        if (outcome.RatingAfter is not null || outcome.Key is null || !GameStatsClient.Sharing(_settings))
        {
            return;
        }

        // The pool the game ended under: the record goes to that pool's queue even when the address in the settings
        // changes while the record is being built.
        if (PoolDestination.Of(_settings) is not { } destination)
        {
            return;
        }

        // And the withdrawal count it ended under: a record still being built when sharing is turned off is not queued
        // afterwards, also when sharing is back on before the build ends.
        var generation = _queues.Generation;
        Task.Run(() =>
        {
            try
            {
                Enqueue(outcome, destination, generation);
            }
            catch (Exception ex)
            {
                Diag.Error("Game stats: building the game record failed", ex);
            }
        }).Forget("Game stats");
    }

    /// <summary>
    /// Builds the record of a finished game and queues it for <paramref name="destination"/>, the pool it ended under,
    /// unless sharing was withdrawn since <paramref name="generation"/> (<see cref="GameStatsQueues.Generation"/>).
    /// </summary>
    internal void Enqueue(GameOutcome outcome, string destination, int generation)
    {
        Prepare();
        if (Build(outcome) is { } record && _queues.Add(destination, record, generation))
        {
            Kick();
        }
    }

    /// <summary>The record of a finished game from its history row and replay file.</summary>
    internal System.Text.Json.Nodes.JsonObject? Build(GameOutcome outcome)
    {
        if (outcome.Key is not { } key || _history.Game(key) is not { } game || game.FromOtherTracker)
        {
            return null;
        }

        var cards = _cards();
        var text = game.Replay is { Length: > 0 } file ? _replays()?.Load(file) : null;
        var pool = game.IsBattlegrounds && cards is not null
            ? PoolGame.From(outcome, cards.BaseHeroId, id => cards.Get(id)?.Races ?? [], details: true, PoolClient.Secret(_settings))
            : null;
        return GameStatsRecord.Build(game, GameStatsClient.RecordId(key, _settings), text, pool, cards, _version);
    }

    /// <summary>
    /// The player turned sharing off: what waits is dropped, in every pool's queue, and each pool that may hold
    /// something of this install is asked to delete it (the request waits for that pool's address). A pool nothing
    /// was sent to is asked nothing, and no install id is made for the occasion. Works whatever state the move of an
    /// earlier version's queue is in.
    /// </summary>
    public void Withdraw()
    {
        Prepare();
        var destination = PoolDestination.Of(_settings);
        _queues.Withdraw(destination);
        Report(destination);
        Kick();
    }

    /// <summary>
    /// One pass for the pool the settings name now: a pending deletion first, then batches while there are records and
    /// the pool takes them. The address is read again before every request; once it is no longer this queue's, the
    /// pass stops and the rest waits in its folder.
    /// </summary>
    internal async Task<bool> PassAsync(CancellationToken cancellationToken)
    {
        Prepare();
        var destination = PoolDestination.Of(_settings);
        if (_settings.GameStatsOff)
        {
            // The refusal holds on every pass, not only at the click: also for what a late move or a restart brought in.
            _queues.Withdraw(destination);
        }

        _queues.Sweep(destination);
        Report(destination);
        if (destination is null || _queues.For(destination) is not { } queue)
        {
            return true;
        }

        if (queue.ForgetPending && Addressed(queue))
        {
            if (_settings.PoolSecret is not { Length: 64 })
            {
                // Without the secret there is no install id the pool knows: a request with a new one would delete nothing.
                Diag.Info("Game stats: deletion request without an install id dropped");
                queue.DropForget();
            }
            else if (!await GameStatsClient.ForgetAsync(_http, destination, GameStatsClient.InstallId(_settings), cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            else
            {
                queue.Forgotten();
            }
        }

        while (GameStatsClient.Sharing(_settings) && Addressed(queue) && queue.Take(GameStatsClient.MaxBatch) is { Count: > 0 } batch)
        {
            // Marked before the request: an answer that never arrives may still have stored the batch.
            queue.MarkUsed();
            switch (await GameStatsClient.SubmitAsync(_http, destination, GameStatsClient.InstallId(_settings), batch, cancellationToken).ConfigureAwait(false))
            {
                case GameStatsAnswer.Later:
                    Report(PoolDestination.Of(_settings));
                    return false;
                case GameStatsAnswer.Refused:
                    Diag.Info($"Game stats: dropped {batch.Count} record(s) the pool refused");
                    break;
            }

            // The answer came from this queue's pool: only this queue's files go, whatever the settings name by now.
            queue.Remove(batch);
            if (queue.Count > 0)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!Addressed(queue))
        {
            // The address changed during the pass: the next pass serves the new pool, this queue waits for its own.
            Kick();
        }

        Report(PoolDestination.Of(_settings));
        return true;
    }

    /// <summary>The settings still name the pool this queue was filled for.</summary>
    private bool Addressed(GameStatsQueue queue) => PoolDestination.Of(_settings) == queue.Destination;

    /// <summary>
    /// Brings the data folder to this layout before anything is queued, sent or withdrawn. Once per data folder: an
    /// install whose settings show that an earlier version could already send (notice seen, sharing on, a secret) has
    /// its current pool marked as used, so turning sharing off still asks that pool to delete. And until it works:
    /// the earlier version's queue is taken out of the root.
    /// </summary>
    internal void Prepare()
    {
        lock (_prepareGate)
        {
            if (!_layoutChecked)
            {
                _layoutChecked = true;
                if (_queues.FirstRun() && _settings is { GameStatsNoticeShown: true, GameStatsOff: false, PoolSecret.Length: 64 }
                    && PoolDestination.Of(_settings) is { } destination)
                {
                    _queues.For(destination)?.MarkUsed();
                }
            }

            _migrated = _migrated || _queues.MigrateLegacy(_settings.GameStatsOff);
        }
    }

    /// <summary>
    /// Publishes the state, and writes one log line when what is kept for other pools changed. Counted and raised
    /// under one lock and numbered: a report never overtakes a newer one, and a listener can tell them apart.
    /// </summary>
    private void Report(string? destination)
    {
        lock (_stateGate)
        {
            var held = _queues.Held(destination);
            var pending = destination is not null && _queues.For(destination) is { } queue ? queue.Count : 0;
            var before = _state;
            var state = new GameStatsState(pending, held.Records, held.Deletions) { Revision = before.Revision + 1 };
            if (state == before)
            {
                return;
            }

            _state = state;
            if ((state.HeldRecords, state.HeldDeletions) != (before.HeldRecords, before.HeldDeletions) && (state.HeldRecords > 0 || state.HeldDeletions > 0))
            {
                Diag.Info($"Game stats: queue for another pool kept, {state.HeldRecords} record(s), {state.HeldDeletions} deletion request(s)");
            }

            StateChanged?.Invoke(state);
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var wait = Idle;
                if (await PassAsync(cancellationToken).ConfigureAwait(false))
                {
                    _failures = 0;
                }
                else
                {
                    _failures++;
                    var ticks = FirstRetry.Ticks * (1L << Math.Min(_failures - 1, 6));
                    wait = TimeSpan.FromTicks(Math.Min(ticks, MaxRetry.Ticks));
                }

                await _wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Diag.Error("Game stats: sending failed", ex);
                await Task.Delay(FirstRetry, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}

/// <summary>
/// The game record queues in numbers: <paramref name="Pending"/> wait for the current pool; <paramref name="HeldRecords"/>
/// and <paramref name="HeldDeletions"/> wait for other pool addresses and are not sent to the current one.
/// </summary>
public sealed record GameStatsState(int Pending, int HeldRecords, int HeldDeletions)
{
    /// <summary>
    /// The number of this report, growing with every change: a listener that gets reports through its own queue drops
    /// one older than what it already shows. Not part of equality: two reports with the same numbers are the same state.
    /// </summary>
    public long Revision { get; init; }

    public bool Equals(GameStatsState? other) =>
        other is not null && Pending == other.Pending && HeldRecords == other.HeldRecords && HeldDeletions == other.HeldDeletions;

    public override int GetHashCode() => HashCode.Combine(Pending, HeldRecords, HeldDeletions);
}

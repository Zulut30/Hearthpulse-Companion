using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Hearthpulse.Core.GameStats;

namespace Hearthpulse.Core.Pool;

/// <summary>
/// What a finished game is queued under (<see cref="PoolOutbox.Ticket"/>), read when it ended: the pool address of that
/// moment and the withdrawals of each queue so far (<see cref="GameStatsQueues.Generation"/>). Its rows go to that
/// address only, and are not queued once sharing was turned off in between, also when it is back on by then.
/// </summary>
internal readonly record struct PoolOutboxTicket(string Destination, int Games, int Constructed);

/// <summary>
/// Shared games waiting for the stats pool (docs/game-stats.md, "Shared games: queue and sending"): Battlegrounds games
/// for <c>POST /v1/games</c> and the Discover-style picks of Constructed games for <c>POST /v1/constructed</c>. A game is
/// one row, built when it ended (<see cref="PoolClient.RowNode"/>), in <c>pool-outbox/games/</c> or
/// <c>pool-outbox/constructed/</c> of the data folder, in the folder of the pool address it ended under
/// (<see cref="GameStatsQueues"/>: at most 300 rows, 4 MB and 14 days). A row is sent only to that pool, also after a
/// restart. "Not now" (429, 503, a timeout, offline) keeps it: the route tries again after 30 s, doubling up to 30 min,
/// and never before the pool's <c>Retry-After</c>; a refusal (400, 413, 422) drops the batch. These routes have no
/// deletion call: turning sharing off drops what waits and asks the pool nothing. Nothing is sent before
/// <see cref="Start"/>.
/// </summary>
internal sealed class PoolOutbox : IDisposable
{
    /// <summary>Pause between two requests of a pass: the pool takes 6 requests a minute from one address.</summary>
    internal static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(12);

    /// <summary>The longest wait a <c>Retry-After</c> sets; the pool asks for 3600 s once its budget of the day is spent.</summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(6);

    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly Route _games;
    private readonly Route _constructed;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not disposed: the loop may still wait on it while it winds down after Dispose cancels _stop.")]
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not disposed: a pass still running when Dispose cancels _stop releases it on its way out.")]
    private readonly SemaphoreSlim _passGate = new(1, 1);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Not disposed: the loop may still read its token while it winds down.")]
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private volatile bool _started;
    private int _requests;

    /// <param name="settings">The app's settings (the same instance the main window edits).</param>
    /// <param name="root">The queues' folder, <c>pool-outbox/</c> of the data folder.</param>
    public PoolOutbox(AppSettings settings, HttpClient http, string root)
    {
        _settings = settings;
        _http = http;
        Root = root;
        _games = NewRoute("/v1/games", PoolClient.GamesVersion, "games");
        _constructed = NewRoute("/v1/constructed", PoolClient.ConstructedVersion, "constructed");

        Route NewRoute(string path, int version, string kind)
        {
            var folder = Path.Combine(root, kind);
            return new(path, version, folder, new GameStatsQueues(folder) { UtcNow = () => UtcNow(), Label = "Pool outbox (" + kind + ")" });
        }
    }

    /// <summary>The queues' folder.</summary>
    public string Root { get; }

    /// <summary>UTC clock, of the queues too; tests move it.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Pause between two requests of a pass (<see cref="BatchDelay"/>); tests make it zero.</summary>
    internal TimeSpan Delay { get; set; } = BatchDelay;

    /// <summary>Games waiting for the pool the settings name now, both routes together.</summary>
    public int Pending => PoolDestination.Of(_settings) is { } destination ? _games.Count(destination) + _constructed.Count(destination) : 0;

    /// <summary>When the pool the settings name now may be asked again; null when no route of it waits for a retry.</summary>
    internal DateTime? NextTry
    {
        get
        {
            var destination = PoolDestination.Of(_settings);
            var waits = new[] { _games, _constructed }.Where(r => destination is not null && r.WaitFor == destination).Select(r => r.NotBefore).ToList();
            return waits.Count > 0 ? waits.Min() : null;
        }
    }

    /// <summary>
    /// The pool and the withdrawals a game ends under; null while nothing may be queued: sharing refused
    /// (<see cref="AppSettings.PoolConsent"/>) or no pool address. Read when the game ends, before its rows are built.
    /// </summary>
    public PoolOutboxTicket? Ticket() =>
        PoolClient.Sharing(_settings) && PoolDestination.Of(_settings) is { } destination
            ? new PoolOutboxTicket(destination, _games.Queues.Generation, _constructed.Queues.Generation)
            : null;

    /// <summary>Queues a Battlegrounds game for the pool of <paramref name="ticket"/>; false when sharing was turned off since.</summary>
    public bool Add(PoolOutboxTicket ticket, PoolGame game) =>
        Queued(_games.Queues.Add(ticket.Destination, PoolClient.RowNode(game), ticket.Games));

    /// <summary>Queues a Constructed game's picks for the pool of <paramref name="ticket"/>; false when sharing was turned off since.</summary>
    public bool Add(PoolOutboxTicket ticket, ConstructedPoolGame game) =>
        Queued(_constructed.Queues.Add(ticket.Destination, PoolClient.ConstructedRowNode(game), ticket.Constructed));

    /// <summary>Starts the background sender (live hosts only; a render never starts it): what waits goes now.</summary>
    public void Start()
    {
        Open();
        _loop ??= Task.Run(() => LoopAsync(_stop.Token));
        Kick();
    }

    /// <summary>From here on a pass may send. <see cref="Start"/> opens and runs the passes; tests run them themselves.</summary>
    internal void Open() => _started = true;

    /// <summary>Wakes the sender: a game was queued or sharing is on again. A route the pool told to wait still waits.</summary>
    public void Kick() => _wake.Release();

    /// <summary>
    /// Sharing was refused: every waiting game goes, in the current pool's queues and in those kept for other addresses,
    /// and the withdrawal count moves on, so a game still being built is not queued afterwards. No pool is asked
    /// anything: these routes have no deletion call (what was sent expires there after 60 days). Safe to repeat.
    /// </summary>
    public void Withdraw()
    {
        var destination = PoolDestination.Of(_settings);
        var dropped = _games.Waiting(destination) + _constructed.Waiting(destination);
        _games.Withdraw(destination);
        _constructed.Withdraw(destination);
        if (dropped > 0)
        {
            Diag.Info($"Pool outbox: sharing is off, dropped {dropped} waiting game(s)");
        }
    }

    /// <summary>
    /// One pass. While sharing is refused the queues are emptied again, also of what a restart or a late build left.
    /// Otherwise each route of the pool the settings name now sends its rows in batches of up to 20, unless the pool
    /// told it to wait; the address is read again before every request, and once it changed the rest waits in its
    /// folder. Only after <see cref="Start"/> (<see cref="Open"/>). True when nothing waits for a retry.
    /// </summary>
    internal async Task<bool> PassAsync(CancellationToken cancellationToken)
    {
        await _passGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_settings.PoolConsent == false)
            {
                Withdraw();
                return true;
            }

            var destination = PoolDestination.Of(_settings);
            _games.Queues.Sweep(destination);
            _constructed.Queues.Sweep(destination);
            if (!_started || destination is null)
            {
                return true;
            }

            _requests = 0;
            var games = await SendAsync(_games, destination, cancellationToken).ConfigureAwait(false);
            var constructed = await SendAsync(_constructed, destination, cancellationToken).ConfigureAwait(false);
            if (PoolDestination.Of(_settings) != destination)
            {
                // The address changed during the pass: the next pass serves the new pool, these rows wait for their own.
                Kick();
            }

            return games && constructed;
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <summary>The wait after <paramref name="failures"/> "not now" answers in a row: 30 s doubling up to 30 min, at least the pool's <c>Retry-After</c>.</summary>
    internal static TimeSpan Backoff(int failures, TimeSpan? retryAfter)
    {
        var ticks = FirstRetry.Ticks * (1L << Math.Clamp(failures - 1, 0, 6));
        var wait = TimeSpan.FromTicks(Math.Min(ticks, MaxRetry.Ticks));
        return retryAfter is { } asked && asked > wait ? (asked < MaxRetryAfter ? asked : MaxRetryAfter) : wait;
    }

    /// <summary>One route's rows for <paramref name="destination"/>; false when they wait for a retry.</summary>
    private async Task<bool> SendAsync(Route route, string destination, CancellationToken cancellationToken)
    {
        if (route.WaitFor == destination && UtcNow() < route.NotBefore)
        {
            return false;
        }

        if (route.Queues.For(destination) is not { } queue)
        {
            return true;
        }

        while (PoolClient.Sharing(_settings) && PoolDestination.Of(_settings) == destination && queue.Take(PoolClient.MaxBatch) is { Count: > 0 } batch)
        {
            if (_requests++ > 0)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            var answer = await SubmitAsync(route, destination, batch, cancellationToken).ConfigureAwait(false);
            if (answer.Kind == GameStatsAnswer.Later)
            {
                var wait = Backoff(++route.Failures, answer.RetryAfter);
                route.WaitFor = destination;
                route.NotBefore = UtcNow() + wait;
                Diag.Info($"Pool outbox: {route.Path} not now ({answer.Code}), {batch.Count} game(s) kept, next try in {(int)wait.TotalSeconds} s");
                return false;
            }

            Diag.Info(answer.Kind == GameStatsAnswer.Stored
                ? $"Pool outbox: sent {batch.Count} game(s) to {route.Path}"
                : $"Pool outbox: {route.Path} refused {batch.Count} game(s) ({answer.Code}), dropped");
            // The answer came from this queue's pool: only this queue's rows go, whatever the settings name by now.
            queue.Remove(batch);
        }

        route.Failures = 0;
        route.WaitFor = null;
        return true;
    }

    /// <summary>One batch to one route; the body is the one a direct send of the same games has (<see cref="PoolClient.Body"/>).</summary>
    private async Task<Answer> SubmitAsync(Route route, string destination, IReadOnlyList<JsonObject> rows, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["v"] = route.Version,
            ["games"] = new JsonArray(rows.Select(r => (JsonNode?)r.DeepClone()).ToArray()),
        };
        try
        {
            using var response = await _http.PostAsJsonAsync(destination + route.Path, body, cancellationToken).ConfigureAwait(false);
            var retryAfter = response.Headers.RetryAfter switch
            {
                { Delta: { } delta } => delta,
                { Date: { } date } => date.UtcDateTime - UtcNow(),
                _ => (TimeSpan?)null,
            };
            return new Answer(GameStatsClient.AnswerOf(response.StatusCode), retryAfter, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Offline, or no answer within the client's 40 s: the batch waits like a 503 without a Retry-After.
            return new Answer(GameStatsAnswer.Later, null, ex.GetType().Name);
        }
    }

    private bool Queued(bool added)
    {
        if (added)
        {
            Kick();
        }

        return added;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var wait = Idle;
                if (!await PassAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Until the earliest route may try again; a kick meanwhile finds it still waiting.
                    var left = (NextTry ?? UtcNow() + FirstRetry) - UtcNow();
                    wait = left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
                }

                await _wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Diag.Error("Pool outbox: sending failed", ex);
                try
                {
                    await Task.Delay(FirstRetry, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public void Dispose() => _stop.Cancel();

    /// <summary>What the pool answered to a batch, how long it asked to wait, and the code for the log line.</summary>
    private readonly record struct Answer(GameStatsAnswer Kind, TimeSpan? RetryAfter, string Code);

    /// <summary>One route of the pool, its queues and its retry state (changed only inside a pass).</summary>
    private sealed class Route(string path, int version, string folder, GameStatsQueues queues)
    {
        public string Path { get; } = path;

        public int Version { get; } = version;

        public GameStatsQueues Queues { get; } = queues;

        /// <summary>"Not now" answers in a row.</summary>
        public int Failures { get; set; }

        /// <summary>The pool address the wait is for; null when the route does not wait.</summary>
        public string? WaitFor { get; set; }

        public DateTime NotBefore { get; set; }

        /// <summary>Rows waiting for <paramref name="destination"/>.</summary>
        public int Count(string destination) => Queues.For(destination) is { } queue ? queue.Count : 0;

        /// <summary>Rows waiting for <paramref name="destination"/> (null: none is set up now) and for every other address.</summary>
        public int Waiting(string? destination) => (destination is null ? 0 : Count(destination)) + Queues.Held(destination).Records;

        /// <summary>
        /// Empties every queue of this route and moves its withdrawal count on, also when nothing was ever queued: the
        /// store clears its root too, which therefore has to exist (a missing one would be a failure in the log).
        /// </summary>
        public void Withdraw(string? destination)
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The store's withdrawal logs what it cannot clear; the count moves on regardless.
            }

            Queues.Withdraw(destination);
        }
    }
}

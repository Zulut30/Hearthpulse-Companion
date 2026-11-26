using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Hosting;

/// <summary>
/// What both apps send to the stats pool while <see cref="PoolClient.Sharing"/> holds (on by default, until the player
/// turns it off in Settings → Data): a finished game, through the queue of shared games (<see cref="PoolOutbox"/>), and
/// once the earlier games. A finished game is queued also before <see cref="Start"/>: the log watcher starts first, so a
/// game the catch-up finds at start-up ends before it. Nothing is sent before <see cref="Start"/>, which only a live host
/// calls, so an off-screen render shares nothing.
/// </summary>
public sealed class PoolSharingHost : IDisposable
{
    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly HistoryStore _history;
    private readonly Func<CardDb> _cards;
    private volatile bool _started;
    private int _sharingHistory;

    /// <param name="settings">The app's settings (the same instance the main window edits).</param>
    /// <param name="cards">The card data the host has loaded so far (empty until the cards are in).</param>
    /// <param name="outboxRoot">The folder of the queue of shared games, <c>pool-outbox/</c> of the data folder.</param>
    public PoolSharingHost(AppSettings settings, HttpClient http, HistoryStore history, Func<CardDb> cards, string outboxRoot)
    {
        _settings = settings;
        _http = http;
        _history = history;
        _cards = cards;
        Outbox = new PoolOutbox(settings, http, outboxRoot);
    }

    /// <summary>
    /// The settings hold something new to save: the install's secret was made for a game's id, or the earlier games
    /// are shared (<see cref="AppSettings.PoolHistoryShared"/>). Raised on the thread that queued or shared, not the UI
    /// thread.
    /// </summary>
    public event Action? SettingsChanged;

    /// <summary>The pause between two batches of earlier games (<see cref="PoolClient.HistoryBatchDelay"/>).</summary>
    internal Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Local time, for the 30 days of earlier games.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>The queue of shared games and its sender.</summary>
    internal PoolOutbox Outbox { get; }

    private bool Allowed => _started && PoolClient.Sharing(_settings);

    /// <summary>
    /// Called only by live hosts, with the log watcher: from here on queued games are sent and the earlier games are
    /// shared.
    /// </summary>
    public void Start()
    {
        _started = true;
        Outbox.Start();
    }

    /// <summary>
    /// A finished game is queued for the pool unless the player turned sharing off. Called on the log thread: the pool
    /// and the withdrawals are read here, when the game ended; its rows are built and written on the thread pool.
    /// </summary>
    public void OnGameFinished(GameOutcome outcome) => QueueAsync(outcome).Forget("Queueing a game for the stats pool");

    /// <summary>What <see cref="OnGameFinished"/> does; completes when the game's rows are queued.</summary>
    internal Task QueueAsync(GameOutcome outcome) =>
        Outbox.Ticket() is { } ticket ? Task.Run(() => Queue(outcome, ticket)) : Task.CompletedTask;

    /// <summary>
    /// Builds the rows of a finished game and queues them for the pool of <paramref name="ticket"/>, unless sharing was
    /// turned off since the game ended.
    /// </summary>
    internal void Queue(GameOutcome outcome, PoolOutboxTicket ticket)
    {
        var cards = _cards();
        var queued = false;
        // The event fires again once the new rating is read; the game is queued only the first time.
        if (outcome.RatingAfter is null
            && PoolGame.From(outcome, cards.BaseHeroId, Races, details: true, PoolClient.Secret(_settings)) is { } game)
        {
            queued |= Outbox.Add(ticket, game);
        }

        // A Constructed game shares only its Discover-style picks.
        if (ConstructedPoolGame.From(outcome, PoolClient.Secret(_settings)) is { } picks)
        {
            queued |= Outbox.Add(ticket, picks);
        }

        if (queued)
        {
            SettingsChanged?.Invoke();
        }

        IReadOnlyList<string> Races(string cardId) => cards.Get(cardId)?.Races ?? [];
    }

    /// <summary>
    /// The Settings → Data switch moved (called by the host once the settings hold the new value). Off drops every
    /// shared game still waiting, for every pool address, and asks the pool nothing (these routes have no deletion
    /// call); on again wakes the sender and shares the earlier games once (<see cref="ShareHistoryAsync"/>).
    /// </summary>
    public void SharingChanged()
    {
        if (_settings.PoolConsent == false)
        {
            Outbox.Withdraw();
            return;
        }

        Outbox.Kick();
        ShareHistoryAsync().Forget("Sharing earlier games");
    }

    /// <summary>
    /// Once sharing is allowed, the player's Battlegrounds games from the last 30 days are shared once, 20 per
    /// request. Ids are stable, so a game already sent live is not stored twice. Sharing turned off between two
    /// batches stops the rest: the games are not marked as shared and go again from the start once it is back on.
    /// </summary>
    public async Task ShareHistoryAsync()
    {
        var cards = _cards();
        if (!Allowed || _settings.PoolHistoryShared || cards.Count == 0
            || Interlocked.Exchange(ref _sharingHistory, 1) == 1)
        {
            return;
        }

        try
        {
            var secret = PoolClient.Secret(_settings);
            var since = Now().AddDays(-PoolClient.HistoryDays);
            var games = await Task.Run(() => _history.Since(since)
                .Where(g => g.IsBattlegrounds)
                .Select(g => PoolGame.FromHistory(g, secret, cards.BaseHeroId, id => cards.Get(id)?.Races ?? []))
                .OfType<PoolGame>()
                .ToList()).ConfigureAwait(false);
            foreach (var chunk in games.Chunk(PoolClient.MaxBatch))
            {
                // The pause below is long enough for the player to turn sharing off: asked again before every batch.
                if (!Allowed)
                {
                    return;
                }

                if (!await PoolClient.SubmitAsync(_http, PoolClient.Endpoint(_settings), chunk).ConfigureAwait(false))
                {
                    return;
                }

                await Delay(PoolClient.HistoryBatchDelay).ConfigureAwait(false);
            }

            if (!Allowed)
            {
                return;
            }

            _settings.PoolHistoryShared = true;
            Diag.Info($"Shared {games.Count} earlier game(s) with the stats pool");
        }
        catch (Exception ex)
        {
            Diag.Error("Sharing earlier games failed", ex);
        }
        finally
        {
            Volatile.Write(ref _sharingHistory, 0);
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>Stops the sender; what waits stays in the queue for the next start.</summary>
    public void Dispose() => Outbox.Dispose();
}

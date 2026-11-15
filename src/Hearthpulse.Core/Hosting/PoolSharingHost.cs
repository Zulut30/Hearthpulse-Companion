using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Hosting;

/// <summary>
/// What both apps send to the stats pool, only with the player's consent (<see cref="AppSettings.PoolConsent"/> and the
/// version of the list they agreed to): a finished game, and once the earlier games. Nothing is sent before
/// <see cref="Start"/>, which only a live host calls, so an off-screen render shares nothing.
/// </summary>
public sealed class PoolSharingHost
{
    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly HistoryStore _history;
    private readonly Func<CardDb> _cards;
    private volatile bool _started;
    private int _sharingHistory;

    /// <param name="settings">The app's settings (the same instance the main window edits).</param>
    /// <param name="cards">The card data the host has loaded so far (empty until the cards are in).</param>
    public PoolSharingHost(AppSettings settings, HttpClient http, HistoryStore history, Func<CardDb> cards)
    {
        _settings = settings;
        _http = http;
        _history = history;
        _cards = cards;
    }

    /// <summary>
    /// The settings hold something new to save: the install's secret was made for a game's id, or the earlier games
    /// are shared (<see cref="AppSettings.PoolHistoryShared"/>). Raised on the thread that shared, not the UI thread.
    /// </summary>
    public event Action? SettingsChanged;

    /// <summary>The pause between two batches of earlier games (<see cref="PoolClient.HistoryBatchDelay"/>).</summary>
    internal Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Local time, for the 30 days of earlier games.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>Called only by live hosts, with the log watcher: from here on finished games and earlier games are shared.</summary>
    public void Start() => _started = true;

    /// <summary>A finished game goes to the pool as far as the player agreed.</summary>
    public void OnGameFinished(GameOutcome outcome) => ShareAsync(outcome).Forget("Sharing a game with the stats pool");

    /// <summary>What <see cref="OnGameFinished"/> sends; completes when the pool has answered.</summary>
    internal Task ShareAsync(GameOutcome outcome)
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }

        var cards = _cards();
        var sent = new List<Task>(2);
        // The event fires again once the new rating is read; share the game only the first time.
        if (outcome.RatingAfter is null && PoolClient.Sharing(_settings)
            && PoolGame.From(outcome, cards.BaseHeroId, Races, PoolClient.SharingDetails(_settings), PoolClient.Secret(_settings)) is { } game)
        {
            sent.Add(PoolClient.SubmitAsync(_http, PoolClient.Endpoint(_settings), [PoolClient.Limit(game, _settings)]));
            SettingsChanged?.Invoke();
        }

        // A Constructed game shares only its Discover-style picks, once the player agreed to that list (version 4).
        if (PoolClient.SharingPicks(_settings) && ConstructedPoolGame.From(outcome, PoolClient.Secret(_settings)) is { } picks)
        {
            sent.Add(PoolClient.SubmitConstructedAsync(_http, PoolClient.Endpoint(_settings), [picks]));
            SettingsChanged?.Invoke();
        }

        return Task.WhenAll(sent);

        IReadOnlyList<string> Races(string cardId) => cards.Get(cardId)?.Races ?? [];
    }

    /// <summary>
    /// Once the player agrees to the full list, their Battlegrounds games from the last 30 days are shared once,
    /// 20 per request. Ids are stable, so a game already sent live is not stored twice.
    /// </summary>
    public async Task ShareHistoryAsync()
    {
        var cards = _cards();
        if (!_started || _settings.PoolHistoryShared || !PoolClient.SharingAll(_settings) || cards.Count == 0
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
                if (!await PoolClient.SubmitAsync(_http, PoolClient.Endpoint(_settings), chunk).ConfigureAwait(false))
                {
                    return;
                }

                await Delay(PoolClient.HistoryBatchDelay).ConfigureAwait(false);
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
}

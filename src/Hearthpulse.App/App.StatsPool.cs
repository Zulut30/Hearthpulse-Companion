using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.App;

/// <summary>What the host sends to the stats pool, only with the player's consent: a finished game, and once the earlier games.</summary>
public partial class App
{
    private bool _sharingHistory;

    private IReadOnlyList<string> CardRaces(string cardId) => _cards.Get(cardId)?.Races ?? [];

    /// <summary>
    /// Once the player agrees to the full list, their Battlegrounds games from the last 30 days are shared once,
    /// 20 per request. Ids are stable, so a game already sent live is not stored twice.
    /// </summary>
    private async Task ShareHistoryAsync()
    {
        if (_sharingHistory || _settings.PoolHistoryShared || !PoolClient.SharingAll(_settings) || _cards.Count == 0 || _renderDir is not null)
        {
            return;
        }

        _sharingHistory = true;
        try
        {
            var secret = PoolClient.Secret(_settings);
            var since = DateTime.Now.AddDays(-PoolClient.HistoryDays);
            var games = await Task.Run(() => _history.Since(since)
                .Where(g => g.IsBattlegrounds)
                .Select(g => PoolGame.FromHistory(g, secret, _cards.BaseHeroId, CardRaces))
                .OfType<PoolGame>()
                .ToList());
            foreach (var chunk in games.Chunk(PoolClient.MaxBatch))
            {
                if (!await PoolClient.SubmitAsync(_http, PoolClient.Endpoint(_settings), chunk))
                {
                    return;
                }

                await Task.Delay(PoolClient.HistoryBatchDelay);
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
            _sharingHistory = false;
            _settingsDebounce.Kick();
        }
    }

    /// <summary>A finished game goes to the pool as far as the player agreed.</summary>
    private void ShareWithPool(GameOutcome outcome)
    {
        // The event fires again once the new rating is read; share the game only the first time.
        if (outcome.RatingAfter is null && PoolClient.Sharing(_settings)
            && PoolGame.From(outcome, _cards.BaseHeroId, CardRaces, PoolClient.SharingDetails(_settings), PoolClient.Secret(_settings)) is { } game)
        {
            _ = PoolClient.SubmitAsync(_http, PoolClient.Endpoint(_settings), [PoolClient.Limit(game, _settings)]);
            _settingsDebounce.Kick();
        }

        // A Constructed game shares only its Discover-style picks, once the player agreed to that list (version 4).
        if (PoolClient.SharingPicks(_settings) && ConstructedPoolGame.From(outcome, PoolClient.Secret(_settings)) is { } picks)
        {
            _ = PoolClient.SubmitConstructedAsync(_http, PoolClient.Endpoint(_settings), [picks]);
            _settingsDebounce.Kick();
        }
    }
}

#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main.Cards;
#else
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main.Cards;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Stats;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The card page: a card row of the deck page or of the meta's mulligan guide opened for its numbers
/// (<c>docs/card-page.md</c>). It stands in for the Standard or Wild page, in that page's format, until Back.
/// </summary>
public sealed partial class MainViewModel
{
    private CardPageViewModel? _cardPage;
    private bool _cardOpen;
    private string? _openCardId;
    private int _cardLoad;

    /// <summary>
    /// The card list the open page reads, kept while the page stays open: opening another card, or building the page
    /// again (a meta, a language, a data summary), asks the host for nothing. A failed load is kept too; "Try again"
    /// and opening the page anew ask again.
    /// </summary>
    private (string Format, Task<CardStatsSlice?> Load)? _cardStats;

    /// <summary>The player's games of the page's format with their card columns, read once while the page stays open and the history does not change.</summary>
    private (string Format, Task<IReadOnlyList<Core.Data.GameRecord>> Games)? _cardGames;
    private volatile bool _cardGamesStale;
    private bool _cardGamesWatching;
    private IReadOnlyList<Core.Data.GameRecord>? _cardGamesSample;

    /// <summary>
    /// The UI render check shows "Your games" on made-up games (<see cref="CardPageScene.Games"/>) instead of the data
    /// copy's history; null puts the history back. Not called outside a render.
    /// </summary>
    public void ShowCardGamesSample(IReadOnlyList<Core.Data.GameRecord>? games)
    {
        _cardGamesSample = games;
        _cardGames = null;
    }
    private RelayCommand? _openCard;

    /// <summary>
    /// Loads every card's numbers of a format ("standard", "wild"): <see cref="HearthpulseData.LoadCardStatsAsync"/>, set
    /// by the host. Without it the page shows the card, the meta rows and the player's own games. The page never cancels
    /// a load: leaving it, or opening another card, only stops waiting (the load fills the cache for the next page).
    /// </summary>
    public Func<string, CancellationToken, Task<CardStatsSlice?>>? LoadCardStats { get; set; }

    public CardPageViewModel CardPage => _cardPage ??= new CardPageViewModel(this);

    /// <summary>True while the card page replaces the Standard or Wild page (or the deck page open on it).</summary>
    public bool CardOpen { get => _cardOpen; private set => Set(ref _cardOpen, value); }

    /// <summary>Opens the card page of a card id (the parameter of a card row).</summary>
    public RelayCommand OpenCard => _openCard ??= new RelayCommand(p =>
    {
        if (p is string { Length: > 0 } cardId)
        {
            OpenCardPageAsync(cardId).Forget("Card page");
        }
    });

    /// <summary>
    /// Opens a card's page in the format of the Standard or Wild page (also used by the UI render check). False when
    /// the card data has no such card, or another card was opened before this one was built.
    /// </summary>
    public Task<bool> OpenCardPageAsync(string cardId) => LoadCardAsync(cardId, fresh: true);

    /// <summary>The open card built again: a new language, card data, meta, format or data state.</summary>
    private void RebuildCard(bool fresh = false)
    {
        if (CardOpen && _openCardId is { } cardId)
        {
            LoadCardAsync(cardId, fresh).Forget("Card page rebuild");
        }
    }

    /// <param name="fresh">Show the card with empty sections while its numbers load (a new card or format); a rebuild keeps what is shown.</param>
    private async Task<bool> LoadCardAsync(string cardId, bool fresh)
    {
        if (_cards.Get(cardId) is not { } card)
        {
            return false;
        }

        var load = ++_cardLoad;
        _openCardId = cardId;
        var format = _ctFormat;
        if (fresh)
        {
            CardPage.BeginLoading(card, format, _cards);
        }

        CardOpen = true;
        RequestPageMeta();
        var (cards, meta) = (_cards, ConstructedMetaFor(format));
        Core.Constructed.CardPage page;
        try
        {
            var stats = await CardStatsFor(format).ConfigureAwait(true);
            if (load != _cardLoad)
            {
                // Another card, format or rebuild took over while the list loaded: its own call shows the page, and the
                // history is not read for a page nobody will see.
                return false;
            }

            var games = await CardGamesFor(format).ConfigureAwait(true);
            if (load != _cardLoad)
            {
                return false;
            }

            page = await Task.Run(() => CardPages.Build(card, format, stats, meta, games, cards)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Diag.Error("Opening the card page failed", ex);
            if (load == _cardLoad)
            {
                CloseCard();
            }

            return false;
        }

        if (load != _cardLoad)
        {
            // The fence: a result for a card or format the page has left is never shown.
            return false;
        }

        CardPage.Show(page, meta, DataDelay(DataSection.CardStats), DataHealthText.Delay(_dataHealth, DataSection.ConstructedMeta));
        return true;
    }

    /// <summary>The format's card list: the one the open page already asked for, else a new load (never cancelled from here).</summary>
    private Task<CardStatsSlice?> CardStatsFor(string format)
    {
        if (ConstructedMeta.FormatKeyOf(format) is not { } key || LoadCardStats is not { } loadStats)
        {
            return Task.FromResult<CardStatsSlice?>(null);
        }

        if (_cardStats is { } held && held.Format == key)
        {
            return held.Load;
        }

        var load = loadStats(key, CancellationToken.None);
        _cardStats = (key, load);
        return load;
    }

    /// <summary>
    /// The player's own games of the format, read with their card columns (lists keep them out of memory): once for the
    /// open page, again after the history changed (a saved game, an import).
    /// </summary>
    private Task<IReadOnlyList<Core.Data.GameRecord>> CardGamesFor(string format)
    {
        if (_cardGamesSample is { } sample)
        {
            return Task.FromResult(sample);
        }

        if (!_cardGamesWatching)
        {
            _cardGamesWatching = true;
            _history.Changed += () => _cardGamesStale = true;
        }

        if (_cardGames is { } held && held.Format == format && !_cardGamesStale)
        {
            return held.Games;
        }

        _cardGamesStale = false;
        var history = _history;
        var games = Task.Run<IReadOnlyList<Core.Data.GameRecord>>(() =>
            history.WithCards(history.Recent(int.MaxValue).Where(g => StatsFilter.IsConstructed(g, format)).ToList()));
        _cardGames = (format, games);
        return games;
    }

    /// <summary>The open card's numbers asked for again, with "loading" shown meanwhile (the page's "Try again").</summary>
    internal void RetryCard()
    {
        _cardStats = null;
        RebuildCard(fresh: true);
    }

    internal void CloseCard()
    {
        _cardLoad++;
        _cardStats = null;
        _cardGames = null;
        _openCardId = null;
        CardOpen = false;
        _cardPage?.Clear();
    }
}

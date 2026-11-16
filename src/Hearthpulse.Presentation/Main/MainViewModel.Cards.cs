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
    private CancellationTokenSource? _cardCancel;
    private RelayCommand? _openCard;

    /// <summary>
    /// Loads every card's numbers of a format ("standard", "wild"): <see cref="HearthpulseData.LoadCardStatsAsync"/>, set
    /// by the host. Without it the page shows the card, the meta rows and the player's own games.
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
        _cardCancel?.Cancel();
        var cancel = _cardCancel = new CancellationTokenSource();
        _openCardId = cardId;
        var format = _ctFormat;
        if (fresh)
        {
            CardPage.BeginLoading(card, format, _cards);
        }

        CardOpen = true;
        RequestPageMeta();
        var (cards, meta, history, loadStats) = (_cards, ConstructedMetaFor(format), _history, LoadCardStats);
        Core.Constructed.CardPage page;
        try
        {
            var stats = loadStats is not null && ConstructedMeta.FormatKeyOf(format) is { } key
                ? await loadStats(key, cancel.Token).ConfigureAwait(true)
                : null;
            // The player's own games of the format, read again with their card columns (lists keep them out of memory).
            page = await Task.Run(() =>
            {
                var mine = history.Recent(int.MaxValue).Where(g => StatsFilter.IsConstructed(g, format)).ToList();
                return CardPages.Build(card, format, stats, meta, history.WithCards(mine), cards);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
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
            return false;
        }

        CardPage.Show(page, meta, DataDelay(DataSection.CardStats), DataHealthText.Delay(_dataHealth, DataSection.ConstructedMeta));
        return true;
    }

    /// <summary>The open card's numbers asked for again, with "loading" shown meanwhile (the page's "Try again").</summary>
    internal void RetryCard() => RebuildCard(fresh: true);

    internal void CloseCard()
    {
        _cardLoad++;
        _cardCancel?.Cancel();
        _openCardId = null;
        CardOpen = false;
        _cardPage?.Clear();
    }
}

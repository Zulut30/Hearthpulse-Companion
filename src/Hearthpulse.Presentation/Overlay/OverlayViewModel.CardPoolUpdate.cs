using System.Globalization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

public sealed partial class OverlayViewModel
{
    private void UpdateCardPool(ConstructedView? view, AppSettings settings)
    {
        _cardPoolView = view;
        _cardPoolSettings = settings;
        if (Math.Abs(_cardPoolBrowserWidth - settings.CardPoolBrowserWidth) > 0.5)
        {
            CardPoolBrowserWidth = settings.CardPoolBrowserWidth;
        }

        // The browser covers part of the board: it closes when the turn changes.
        if (CardPoolBrowserOpen && view is not null && view.Turn != _cardPoolBrowserTurn)
        {
            CardPoolBrowserOpen = false;
            _cardPoolHeld = false;
            _cardPoolKeptAt = DateTime.MinValue;
        }

        var pool = settings.ShowCardPool ? view?.CardPool : null;
        var list = settings.ShowCardPool && (pool is null || pool.Trigger == PoolTrigger.Hover) ? view?.MatchList : null;
        // A hand card's pool or "this game" list shows only as the hover mode allows now: with Ctrl only while it is held
        // (the session reads the card only then; a pool read just before Ctrl came up is not shown again). The card under
        // the mouse kept it until now, also when this view is the first without it: its linger counts from here.
        var wasInView = _cardPoolHoverInView;
        _cardPoolHoverInView = pool is { Trigger: PoolTrigger.Hover } || (pool is null && list is not null);
        if (!HoverShows(settings))
        {
            pool = pool is { Trigger: PoolTrigger.Hover } ? null : pool;
            list = null;
        }
        else if (_cardPoolHoverInView || wasInView)
        {
            _cardPoolKeptAt = _cardPoolNow;
        }

        UpdateCardPoolPicks(pool is null ? null : view?.DiscoverPicks);
        if (pool is null && list is null && view is not null && settings.ShowCardPool && (_cardPool is not null || ShowCardPoolList)
            && (CardPoolBrowserOpen || (_cardPoolFromHover && HoverKept(settings))))
        {
            // A hand card's pool the player is reading, holds Ctrl for or has just left; the browser keeps its pool until
            // the player closes it. A Discover's pool goes with the pick.
            return;
        }

        // An open browser belongs to its card until the player closes it (×, Esc, the turn): the pointer crossing another
        // hand card keeps it; a new "show the pool" request replaces it.
        if (CardPoolBrowserOpen && _cardPool is not null && view is not null && settings.ShowCardPool
            && (pool is null || (pool.SourceCardId != _cardPool.SourceCardId && !IsNewRequest(pool))))
        {
            return;
        }

        // Beside the Discover options; a hand card's pool and a request at the panel's own spot (no anchor).
        CardPoolAnchor = pool is null && list is null ? null : view?.CardPoolAnchor;
        _cardPoolFromHover = pool is null ? list is not null : pool.Trigger == PoolTrigger.Hover;
        UpdateMatchList(list);
        if (pool is null)
        {
            _cardPool = null;
            _lastRequestedPool = null;
            CardPoolBrowserOpen = false;
            UpdateExcavate(null);
            RebuildCardPoolBrowser();
            if (list is not null)
            {
                ShowListOnly(list);
            }

            ShowCardPool = list is not null;
            return;
        }

        if (_cardPool is null || _cardPool.SourceCardId != pool.SourceCardId)
        {
            _cardPoolSelected.Clear();
            _cardPoolSection = null;
            CardPoolBrowserOpen = false;
        }

        // "Show the pool for this card" (deck or hand row) opens the browser right away.
        var requested = pool.Trigger == PoolTrigger.Request ? pool.SourceCardId : null;
        if (IsNewRequest(pool))
        {
            OpenBrowser();
        }

        _lastRequestedPool = requested;
        if (ReferenceEquals(pool, _cardPool) && ShowCardPool && _cardPoolLanguage == Loc.Instance.Language)
        {
            // The engine hands back the same view while the pool stays up: nothing to rebuild (unless the language changed).
            RebuildCardPoolBrowser();
            return;
        }

        _cardPool = pool;
        _cardPoolLanguage = Loc.Instance.Language;
        // The panel is not beside a hand card, so it names the card; Standard or Wild is the format the player is in.
        CardPoolTitle = pool.SourceName;
        CardPoolSource = pool.SourceName;
        CardPoolDescription = pool.Description;
        CardPoolPhrase = pool.SourceName;
        CardPoolNote = pool.Excavate is { } excavate ? ExcavateTitle(excavate)
            : string.Join(" · ", new[] { Loc.P("Pool.Count", pool.Count, CardCount(pool.Count)), pool.Description }.Where(s => s.Length > 0));
        UpdateSectionChips();
        UpdateExcavate(pool.Excavate);
        // A small pool is the answer itself: every card, no browser. A large one: the most played cards, in order, and the
        // button; shares and averages over dozens of cards do not tell what will come (2026-11-26).
        var small = pool.Excavate is null && pool.Count <= SmallPool;
        IEnumerable<PoolEntry> rows = pool.Excavate is not null ? []
            : small ? pool.Cards.OrderBy(c => c.Cost).ThenBy(c => c.Name, StringComparer.CurrentCulture)
            : PoolStats.TopCards(pool, TopCardCount);
        CardPoolRows = rows.Select(c => new PoolListRow(c.CardId, c.Name, c.Cost, c.Rarity, string.Empty)).ToList();
        CardPoolRowsTitle = Loc.T(small ? "Pool.AllPoolCards" : "Pool.FeaturedReason");
        ShowCardPoolAllCards = !small && pool.Count > 0;
        CardPoolAllCardsText = Loc.F("Pool.AllCards", CardCount(pool.Count));
        CardPoolFeatured = pool.Featured;
        CardPoolFeaturedNote = PoolDisplay.FeaturedReason(pool.FeaturedReason);
        RebuildCardPoolBrowser();
        ShowCardPool = true;
    }

    /// <summary>"Show the pool for this card" for a card other than the last request (it opens the browser).</summary>
    private bool IsNewRequest(CardPoolView pool) =>
        pool.Trigger == PoolTrigger.Request && pool.SourceCardId != _lastRequestedPool;

    /// <summary>
    /// The pick hint rows: rebuilt only when a number, the options or the language changed, since the session hands a
    /// fresh view on every publish.
    /// </summary>
    private void UpdateCardPoolPicks(DiscoverPicksView? picks)
    {
        var key = picks is null ? string.Empty
            : string.Join('|', picks.Rows.Select(r => $"{r.CardId}:{r.PickRate}:{r.WinRate}:{r.LowSample}:{r.Top}")) + "|" + picks.Rank + "|" + Loc.Instance.Language;
        if (key == _cardPoolPicksKey)
        {
            return;
        }

        _cardPoolPicksKey = key;
        if (picks is null)
        {
            CardPoolPicks = [];
            CardPoolPicksNote = string.Empty;
            return;
        }

        CardPoolPicks = picks.Rows
            .Select(r => new PoolPickRowModel(
                r.CardId,
                r.Name,
                r.PickRate is { } p ? PickPercent(p) : "—",
                r.PickRate is { } f ? Math.Max(2, PickTrack * f) : 0,
                r.WinRate is { } w ? Loc.F("Pool.Picks.Win", PickPercent(w)) : string.Empty,
                r.Top,
                r.LowSample && r.PickRate is not null))
            .ToList();
        // "Faded: few games" only when a row is drawn faded (HasLowSample counts the same rows).
        CardPoolPicksNote = !picks.HasNumbers ? Loc.T("Pool.Picks.Few")
            : picks.HasLowSample ? Loc.T("Pool.Picks.LowSample")
            : string.Empty;
    }

    private static string PickPercent(double share) => OverlayFormat.Percent(share);

    /// <summary>The "this game" rows: rebuilt only when a card, a count or the language changed (a fresh view comes on every publish).</summary>
    private void UpdateMatchList(MatchListView? list)
    {
        var key = list is null ? string.Empty
            : $"{list.SourceCardId}|{list.Title}|{list.Total}|{string.Join(',', list.Rows.Select(r => $"{r.CardId}:{r.Count}"))}|{Loc.Instance.Language}";
        if (key == _cardPoolListKey)
        {
            return;
        }

        _cardPoolListKey = key;
        ShowCardPoolList = list is not null;
        CardPoolList = list?.Rows.Take(ListRowCount)
            .Select(r => new PoolListRow(r.CardId, r.Name, r.Cost, r.Rarity, r.Count > 1 ? "×" + r.Count.ToString(CultureInfo.CurrentCulture) : string.Empty))
            .ToList() ?? [];
        CardPoolListTitle = list is null ? string.Empty : ListHeadline(list);
        CardPoolListNote = list is null ? string.Empty
            : list.Rows.Count == 0 ? Loc.T("Pool.List.Empty")
            : list.Rows.Count > ListRowCount ? Loc.F("Pool.List.More", list.Rows.Count - ListRowCount)
            : string.Empty;
    }

    /// <summary>"Your minions that died · Deathrattle: 3".</summary>
    private static string ListHeadline(MatchListView list) => $"{list.Title}: {list.Total.ToString(CultureInfo.CurrentCulture)}";

    /// <summary>A card with a "this game" list and no pool: the header names the card, the list's section says the rest.</summary>
    private void ShowListOnly(MatchListView list)
    {
        CardPoolTitle = list.SourceName;
        CardPoolSource = list.SourceName;
        CardPoolPhrase = list.SourceName;
        CardPoolDescription = string.Empty;
        CardPoolNote = string.Empty;
        CardPoolSections = [];
        UpdateExcavate(null);
        CardPoolRows = [];
        CardPoolRowsTitle = string.Empty;
        ShowCardPoolAllCards = false;
        CardPoolAllCardsText = string.Empty;
        CardPoolFeatured = null;
        CardPoolFeaturedNote = string.Empty;
    }
}

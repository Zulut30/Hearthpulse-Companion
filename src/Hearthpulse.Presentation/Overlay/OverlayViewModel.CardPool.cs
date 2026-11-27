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

/// <summary>
/// One card row of the pool panel (this game, Excavate treasures, the pool's cards), drawn like a tracker row: cost gem,
/// name over a strip of art, and "×2" on the right when it counts twice.
/// </summary>
public sealed record PoolListRow(string CardId, string Name, int Cost, string Rarity, string CountText) : IImmutableView;

/// <summary>A mechanic filter chip of the pool browser, or (Section) one of the generator's pools.</summary>
public sealed record PoolChipModel(string Key, string Label, bool Selected, bool Section = false) : IImmutableView;

/// <summary>One row of the pool browser's card grid (up to three cards).</summary>
public sealed record PoolCardRowModel(IReadOnlyList<PoolEntry> Cards) : IImmutableView;

/// <summary>
/// One option of the player's open choice with what players did when it was offered (Hearthpulse data): the share of
/// offers in which it was taken (the bar) and the share of games won after taking it. A dash when there is no number.
/// </summary>
/// <param name="FillWidth">The pick bar's filled width in overlay units (the track is a fixed width).</param>
/// <param name="Top">The most taken option, clearly ahead of the others.</param>
/// <param name="LowSample">The numbers rest on few games: the panel draws them faded.</param>
public sealed record PoolPickRowModel(string CardId, string Name, string PickText, double FillWidth, string WinText, bool Top, bool LowSample) : IImmutableView;

/// <summary>
/// The card pool: a compact panel and the pool browser, a full-height side panel with every card. A card in hand shows
/// its pool at the panel's own spot (the movable panel "card-pool") as the hover mode allows (<see cref="CardPoolHover"/>:
/// with Ctrl held, always, or never); the player's own Discover shows its pool beside the options. The browser opens
/// with the panel's "All cards" button or <see cref="GameSession.ShowCardPool"/>.
/// </summary>
public sealed partial class OverlayViewModel
{
    /// <summary>Width of a pick bar's track; an option taken every time it is offered fills it.</summary>
    private const double PickTrack = 150;

    /// <summary>The most played pool cards the panel lists.</summary>
    private const int TopCardCount = 4;

    /// <summary>A pool this small is listed whole on the panel (12 rows fit) and needs no browser.</summary>
    private const int SmallPool = 12;

    /// <summary>Rows of the "this game" list at most; the rest is "+N more".</summary>
    private const int ListRowCount = 8;

    /// <summary>
    /// How long a hand card's pool stays in the "with Ctrl" mode once nothing keeps it (Ctrl let go, the pointer off the
    /// panel): long enough for a short slip of the key, short enough that the panel goes when it is no longer asked for.
    /// </summary>
    public static readonly TimeSpan CardPoolCtrlLinger = TimeSpan.FromMilliseconds(150);

    /// <summary>How long a hand card's pool stays in the "Always" mode after the mouse leaves the card (Ctrl and the pointer on the panel keep it).</summary>
    public static readonly TimeSpan CardPoolHoverLinger = TimeSpan.FromMilliseconds(300);

    private bool _showCardPool;
    private CardPoolView? _cardPool;
    private string _cardPoolTitle = string.Empty;
    private string _cardPoolSource = string.Empty;
    private string _cardPoolDescription = string.Empty;
    private string _cardPoolPhrase = string.Empty;
    private string _cardPoolNote = string.Empty;
    private IReadOnlyList<PoolListRow> _cardPoolRows = [];
    private string _cardPoolRowsTitle = string.Empty;
    private bool _cardPoolBrowsable;
    private bool _cardPoolPopupOverChoices;
    private string _cardPoolAllCardsText = string.Empty;
    private IReadOnlyList<PoolListRow> _cardPoolList = [];
    private string _cardPoolListTitle = string.Empty;
    private string _cardPoolListNote = string.Empty;
    private bool _showCardPoolList;
    private string _cardPoolListKey = string.Empty;
    private CardPoolAnchor? _cardPoolAnchor;
    private PoolEntry? _cardPoolFeatured;
    private string _cardPoolFeaturedNote = string.Empty;
    private IReadOnlyList<PoolChipModel> _cardPoolSections = [];
    private bool _showCardPoolExcavate;
    private string _cardPoolExcavateNext = string.Empty;
    private IReadOnlyList<PoolListRow> _cardPoolExcavateRows = [];
    private string? _cardPoolSection;
    private RelayCommand? _toggleCardPoolSection;
    private bool _cardPoolHeld;
    private bool _cardPoolHeldByPointer;
    private ConstructedView? _cardPoolView;
    private AppSettings? _cardPoolSettings;
    private string _cardPoolLanguage = string.Empty;
    private IReadOnlyList<PoolPickRowModel> _cardPoolPicks = [];
    private string _cardPoolPicksNote = string.Empty;
    private string _cardPoolPicksKey = string.Empty;

    // The hand card's pool at the panel's own spot, and what keeps it up: Ctrl held (the overlay tick), the hand card still
    // under the mouse (the last view), the pointer on the panel; the linger counts from the last moment one of them did.
    private bool _cardPoolControl;
    private bool _cardPoolFromHover;
    private bool _cardPoolHoverInView;
    private DateTime _cardPoolNow;
    private DateTime _cardPoolKeptAt;

    private bool _cardPoolBrowserOpen;
    private bool _cardPoolFiltersOpen;
    private string _cardPoolBrowserTitle = string.Empty;
    private string _cardPoolBrowserNote = string.Empty;
    private IReadOnlyList<PoolChipModel> _cardPoolFilterChips = [];
    private IReadOnlyList<PoolCardRowModel> _cardPoolBrowserRows = [];
    private double _cardPoolBrowserWidth = AppSettings.DefaultCardPoolBrowserWidth;
    private readonly HashSet<string> _cardPoolSelected = new(StringComparer.Ordinal);
    private string _cardPoolBrowserKey = string.Empty;
    private string? _lastRequestedPool;
    private int _cardPoolBrowserTurn;
    private RelayCommand? _openCardPoolBrowser;
    private RelayCommand? _closeCardPoolBrowser;
    private RelayCommand? _toggleCardPoolFilters;
    private RelayCommand? _toggleCardPoolMechanic;

    /// <summary>The player resized the pool browser (its width is stored in the settings).</summary>
    public event Action? CardPoolBrowserResized;

    /// <summary>
    /// Ctrl went down or up over the game (or the overlay hid): the host hands it to <see cref="GameSession.SetCardPoolControl"/>,
    /// which reads the card under the mouse in the "with Ctrl" mode only while Ctrl is held.
    /// </summary>
    public event Action<bool>? CardPoolControlChanged;

    public bool ShowCardPool
    {
        get => _showCardPool;
        private set
        {
            if (Set(ref _showCardPool, value))
            {
                RaiseCardPoolShown();
            }
        }
    }

    /// <summary>
    /// The compact panel at its own (movable) spot: the pool of a card in hand, or of a request. Hidden while the browser
    /// (which repeats its header) is open, so they never overlap.
    /// </summary>
    public bool ShowCardPoolPanel => ShowCardPool && !CardPoolBrowserOpen && CardPoolAnchor is null;

    /// <summary>The same panel beside the options of the player's own Discover.</summary>
    public bool ShowCardPoolPopup => ShowCardPool && !CardPoolBrowserOpen && CardPoolAnchor is not null;

    /// <summary>Where the pool's card sits (the Discover options); the overlay places <see cref="ShowCardPoolPopup"/> beside it.</summary>
    public CardPoolAnchor? CardPoolAnchor
    {
        get => _cardPoolAnchor;
        private set
        {
            if (Set(ref _cardPoolAnchor, value))
            {
                RaiseCardPoolShown();
                Raise(nameof(ShowCardPoolAllCards));
            }
        }
    }

    /// <summary>The player's overlay scale, which the popup undoes to stand next to the game's Discover options.</summary>
    public double CardPoolOverlayScale => _cardPoolSettings?.OverlayScale ?? 1;

    /// <summary>The panel's title: the generator's name ("Thief's Tools"), or the name of a card with only a "this game" list.</summary>
    public string CardPoolTitle { get => _cardPoolTitle; private set => Set(ref _cardPoolTitle, value); }

    /// <summary>The generator's name.</summary>
    public string CardPoolSource { get => _cardPoolSource; private set => Set(ref _cardPoolSource, value); }

    /// <summary>What the pool is, for the browser's header: "Shaman + Neutral" (Standard or Wild is the player's own format).</summary>
    public string CardPoolDescription { get => _cardPoolDescription; private set => Set(ref _cardPoolDescription, value); }

    /// <summary>The generator's name in the player's language (the parsed phrase is English card text).</summary>
    public string CardPoolPhrase { get => _cardPoolPhrase; private set => Set(ref _cardPoolPhrase, value); }

    /// <summary>
    /// The line under the title: the pool's size and classes ("51 cards · any class"), an Excavate pool's tier ("Excavate:
    /// tier 2 of 4"); empty for a card with only a "this game" list (its section says it).
    /// </summary>
    public string CardPoolNote { get => _cardPoolNote; private set => Set(ref _cardPoolNote, value); }

    /// <summary>
    /// The pool's card rows on the panel: every card of a pool of 12 or fewer (cost, then name), else the four played in
    /// the most meta-deck games (none without meta data); empty for an Excavate pool, whose next tier has its own rows.
    /// </summary>
    public IReadOnlyList<PoolListRow> CardPoolRows { get => _cardPoolRows; private set => Set(ref _cardPoolRows, value); }

    /// <summary>"Every card in the pool" over a small pool's rows, else "Most played in meta decks".</summary>
    public string CardPoolRowsTitle { get => _cardPoolRowsTitle; private set => Set(ref _cardPoolRowsTitle, value); }

    /// <summary>
    /// The "All cards · N" button that opens the browser: a pool of more than 12 cards (a small one is listed whole). Not
    /// on the popup while it lies over the Discover options (<see cref="SetCardPoolPopupOverChoices"/>).
    /// </summary>
    public bool ShowCardPoolAllCards => _cardPoolBrowsable && !(CardPoolAnchor is not null && _cardPoolPopupOverChoices);

    /// <summary>"All cards · 51".</summary>
    public string CardPoolAllCardsText { get => _cardPoolAllCardsText; private set => Set(ref _cardPoolAllCardsText, value); }

    /// <summary>The hovered card's look back at this game ("Your minions that died · Deathrattle") is shown.</summary>
    public bool ShowCardPoolList { get => _showCardPoolList; private set => Set(ref _showCardPoolList, value); }

    /// <summary>The cards of this game the hovered card looks back at, by cost then name.</summary>
    public IReadOnlyList<PoolListRow> CardPoolList { get => _cardPoolList; private set => Set(ref _cardPoolList, value); }

    /// <summary>"Your minions that died · Deathrattle: 3", the section over the rows.</summary>
    public string CardPoolListTitle { get => _cardPoolListTitle; private set => Set(ref _cardPoolListTitle, value); }

    /// <summary>"None this game yet" or "+3 more"; empty otherwise.</summary>
    public string CardPoolListNote { get => _cardPoolListNote; private set => Set(ref _cardPoolListNote, value); }

    /// <summary>The highlighted pool card (most played in meta decks), or null.</summary>
    public PoolEntry? CardPoolFeatured { get => _cardPoolFeatured; private set => Set(ref _cardPoolFeatured, value); }

    public string CardPoolFeaturedNote { get => _cardPoolFeaturedNote; private set => Set(ref _cardPoolFeaturedNote, value); }

    /// <summary>The options of the player's open choice with the stats pool's picks; empty outside a choice or without the pool.</summary>
    public IReadOnlyList<PoolPickRowModel> CardPoolPicks { get => _cardPoolPicks; private set => Set(ref _cardPoolPicks, value); }

    /// <summary>"Faded: few games" when a faded row is shown, or that there are no numbers yet; empty otherwise.</summary>
    public string CardPoolPicksNote { get => _cardPoolPicksNote; private set => Set(ref _cardPoolPicksNote, value); }

    /// <summary>The generator's separate pools ("Blood Rune 29"): the browser's chips, the selected one filters the grid.</summary>
    public IReadOnlyList<PoolChipModel> CardPoolSections
    {
        get => _cardPoolSections;
        private set
        {
            if (Set(ref _cardPoolSections, value))
            {
                Raise(nameof(CardPoolHasSections));
            }
        }
    }

    public bool CardPoolHasSections => _cardPoolSections.Count > 0;

    /// <summary>The pool is the Excavate treasures: the panel shows the next tier's rows.</summary>
    public bool ShowCardPoolExcavate { get => _showCardPoolExcavate; private set => Set(ref _showCardPoolExcavate, value); }

    /// <summary>"Next treasure · rare" over the next tier's rows; empty outside a game (no next tier known).</summary>
    public string CardPoolExcavateNext { get => _cardPoolExcavateNext; private set => Set(ref _cardPoolExcavateNext, value); }

    /// <summary>The treasures the player's next Excavate can give, as tracker rows; the other tiers are in the browser.</summary>
    public IReadOnlyList<PoolListRow> CardPoolExcavateRows { get => _cardPoolExcavateRows; private set => Set(ref _cardPoolExcavateRows, value); }

    /// <summary>Click a section in the browser: show only its cards (again: all of them).</summary>
    public RelayCommand ToggleCardPoolSection => _toggleCardPoolSection ??= new RelayCommand(p =>
    {
        _cardPoolSection = p is string label && label != _cardPoolSection ? label : null;
        RebuildCardPoolBrowser();
    });

    /// <summary>
    /// The pointer is on a pool panel (the compact panel at its own spot, or the browser): keep the current pool while the
    /// player reads or clicks it; once it leaves, a hand card's pool stays only for its linger.
    /// </summary>
    public bool CardPoolHeld
    {
        get => _cardPoolHeld;
        set
        {
            if (Set(ref _cardPoolHeld, value) && !value && _cardPoolSettings is not null)
            {
                // The pointer left: drop a pool that was only kept for reading (after its linger, UpdateCardPoolKeys).
                UpdateCardPool(_cardPoolView, _cardPoolSettings);
            }
        }
    }

    /// <summary>The pool browser (full-height side panel) is open.</summary>
    public bool CardPoolBrowserOpen
    {
        get => _cardPoolBrowserOpen;
        private set
        {
            if (Set(ref _cardPoolBrowserOpen, value))
            {
                RaiseCardPoolShown();
            }
        }
    }

    /// <summary>"Thief's Tools Pool: 47 cards".</summary>
    public string CardPoolBrowserTitle { get => _cardPoolBrowserTitle; private set => Set(ref _cardPoolBrowserTitle, value); }

    /// <summary>"12 cards match" while filters are on; empty otherwise.</summary>
    public string CardPoolBrowserNote { get => _cardPoolBrowserNote; private set => Set(ref _cardPoolBrowserNote, value); }

    /// <summary>The Filters block is expanded.</summary>
    public bool CardPoolFiltersOpen { get => _cardPoolFiltersOpen; private set => Set(ref _cardPoolFiltersOpen, value); }

    /// <summary>At least one mechanic filter is on (the dot next to Filters).</summary>
    public bool CardPoolFilterActive => _cardPoolSelected.Count > 0;

    /// <summary>Mechanics found in this pool; selected ones must all be on a card (AND).</summary>
    public IReadOnlyList<PoolChipModel> CardPoolFilterChips { get => _cardPoolFilterChips; private set => Set(ref _cardPoolFilterChips, value); }

    /// <summary>The card grid, three per row, by cost then name.</summary>
    public IReadOnlyList<PoolCardRowModel> CardPoolBrowserRows { get => _cardPoolBrowserRows; private set => Set(ref _cardPoolBrowserRows, value); }

    /// <summary>Width of the pool browser in overlay units (the player drags its left edge).</summary>
    public double CardPoolBrowserWidth
    {
        get => _cardPoolBrowserWidth;
        set
        {
            if (!Set(ref _cardPoolBrowserWidth, Math.Clamp(value, AppSettings.MinCardPoolBrowserWidth, AppSettings.MaxCardPoolBrowserWidth)))
            {
                return;
            }

            Raise(nameof(CardPoolCardHeight));
            if (_cardPoolSettings is not null)
            {
                _cardPoolSettings.CardPoolBrowserWidth = _cardPoolBrowserWidth;
            }
        }
    }

    /// <summary>Height of a card in the grid: three card renders (215x303 without margins) side by side fill the width.</summary>
    public double CardPoolCardHeight => Math.Floor((CardPoolBrowserWidth - 40) / 3 * 303 / 215);

    /// <summary>The panel's "All cards · N" button: opens the browser on the shown pool (the panel hides while it is open).</summary>
    public RelayCommand OpenCardPoolBrowser => _openCardPoolBrowser ??= new RelayCommand(() =>
    {
        if (!CardPoolBrowserOpen && _cardPool is not null)
        {
            OpenBrowser();
            RebuildCardPoolBrowser();
        }
    });

    /// <summary>
    /// The popup's spot, from the panel that places it: whether it lies over the Discover options
    /// (<see cref="PoolPlacement.CoversChoices"/>). The "All cards" button would take the click meant for an option there,
    /// so the popup goes without it.
    /// </summary>
    public void SetCardPoolPopupOverChoices(bool over)
    {
        if (_cardPoolPopupOverChoices != over)
        {
            _cardPoolPopupOverChoices = over;
            Raise(nameof(ShowCardPoolAllCards));
        }
    }

    /// <summary>The × button or Esc.</summary>
    public RelayCommand CloseCardPoolBrowser => _closeCardPoolBrowser ??= new RelayCommand(CloseCardPoolBrowserCore);

    public RelayCommand ToggleCardPoolFilters => _toggleCardPoolFilters ??= new RelayCommand(() => CardPoolFiltersOpen = !CardPoolFiltersOpen);

    /// <summary>Turns a mechanic filter (tag) on or off.</summary>
    public RelayCommand ToggleCardPoolMechanic => _toggleCardPoolMechanic ??= new RelayCommand(p =>
    {
        if (p is string tag && !_cardPoolSelected.Remove(tag))
        {
            _cardPoolSelected.Add(tag);
        }

        RebuildCardPoolBrowser();
    });

    private void OpenBrowser()
    {
        // The turn first: opening hides the panel, and a hide handler may re-enter UpdateCardPool, which closes a
        // browser opened on another turn.
        _cardPoolBrowserTurn = _cardPoolView?.Turn ?? 0;
        CardPoolBrowserOpen = true;
    }

    /// <summary>
    /// The pointer over the compact panel, which lets clicks through to the game (the host reads the pointer globally).
    /// Resting on the panel at its own spot holds a hand card's pool while the player reads it or reaches for its button;
    /// the panel beside a Discover is not held: it goes with the pick.
    /// </summary>
    public void UpdateCardPoolPointer(bool over) => UpdateCardPoolPointer(over, DateTime.UtcNow);

    /// <summary>The same at a given time (tests).</summary>
    /// <param name="utcNow">Now (UTC), for the hand card's pool linger.</param>
    public void UpdateCardPoolPointer(bool over, DateTime utcNow)
    {
        _cardPoolNow = utcNow;
        var hold = over && ShowCardPoolPanel;
        if (hold != _cardPoolHeldByPointer)
        {
            // The pointer kept the pool until now: its linger counts from leaving the panel.
            if (!hold)
            {
                _cardPoolKeptAt = utcNow;
            }

            _cardPoolHeldByPointer = hold;
            CardPoolHeld = hold;
        }
    }

    /// <summary>
    /// The overlay's tick (about 30 times a second while it shows over the game; once with false when it hides): whether
    /// Ctrl is held. Ctrl is what lets the "with Ctrl" mode read and show a hand card's pool, and while it is held the pool
    /// stays as the pointer travels to the panel; a hand card's pool that nothing keeps any more goes after its linger.
    /// </summary>
    public void UpdateCardPoolKeys(bool controlDown) => UpdateCardPoolKeys(controlDown, DateTime.UtcNow);

    /// <summary>The same at a given time (tests).</summary>
    /// <param name="utcNow">Now (UTC), for the hand card's pool linger.</param>
    public void UpdateCardPoolKeys(bool controlDown, DateTime utcNow)
    {
        _cardPoolNow = utcNow;
        if (controlDown != _cardPoolControl)
        {
            // Ctrl kept the pool until now: its linger counts from letting go.
            if (!controlDown)
            {
                _cardPoolKeptAt = utcNow;
            }

            _cardPoolControl = controlDown;
            CardPoolControlChanged?.Invoke(controlDown);
        }

        if (!_cardPoolFromHover || !ShowCardPool || _cardPoolSettings is not { } settings)
        {
            return;
        }

        if ((_cardPoolHoverInView && HoverShows(settings)) || _cardPoolControl || CardPoolHeld || CardPoolBrowserOpen)
        {
            _cardPoolKeptAt = utcNow;
        }
        else if (utcNow - _cardPoolKeptAt >= Linger(settings))
        {
            UpdateCardPool(_cardPoolView, settings);
        }
    }

    /// <summary>The hover mode shows a hand card's pool now: always, or with Ctrl only while it is held.</summary>
    private bool HoverShows(AppSettings settings) => settings.CardPoolHover switch
    {
        CardPoolHover.Always => true,
        CardPoolHover.Ctrl => _cardPoolControl,
        _ => false,
    };

    /// <summary>A hand card's pool that nothing keeps goes after <see cref="CardPoolCtrlLinger"/>, or <see cref="CardPoolHoverLinger"/> in the "Always" mode.</summary>
    private static TimeSpan Linger(AppSettings settings) => settings.CardPoolHover == CardPoolHover.Always ? CardPoolHoverLinger : CardPoolCtrlLinger;

    /// <summary>A hand card's pool is still wanted: the pointer on the panel, Ctrl held, or within its linger.</summary>
    private bool HoverKept(AppSettings settings) => CardPoolHeld || _cardPoolControl || _cardPoolNow - _cardPoolKeptAt < Linger(settings);

    /// <summary>Called when the drag on the browser's edge ends: saves the width.</summary>
    public void CommitCardPoolBrowserWidth() => CardPoolBrowserResized?.Invoke();

    private void CloseCardPoolBrowserCore()
    {
        CardPoolBrowserOpen = false;
        // The browser disappears under the mouse: no "mouse left" event follows, so the hold ends here, and the hand
        // card's pool comes back only if something still keeps it (Ctrl, the card under the mouse), with no linger.
        _cardPoolHeld = false;
        _cardPoolKeptAt = DateTime.MinValue;
        if (_cardPoolSettings is not null)
        {
            UpdateCardPool(_cardPoolView, _cardPoolSettings);
        }

        RebuildCardPoolBrowser();
    }

    private void RaiseCardPoolShown()
    {
        Raise(nameof(ShowCardPoolPanel));
        Raise(nameof(ShowCardPoolPopup));
    }

    private void RebuildCardPoolBrowser()
    {
        var pool = CardPoolBrowserOpen ? _cardPool : null;
        var key = pool is null ? string.Empty
            : $"{Loc.Instance.Language}|{pool.SourceCardId}|{pool.Description}|{pool.Count}|{_cardPoolSection}|{string.Join(',', _cardPoolSelected.Order(StringComparer.Ordinal))}";
        if (key == _cardPoolBrowserKey)
        {
            return;
        }

        // Only a new pool or other filters rebuild the grid, so its scroll position survives overlay updates.
        _cardPoolBrowserKey = key;
        Raise(nameof(CardPoolFilterActive));
        UpdateSectionChips();
        if (pool is null)
        {
            CardPoolBrowserRows = [];
            CardPoolFilterChips = [];
            return;
        }

        _cardPoolSelected.IntersectWith(pool.Filters.Select(f => f.Tag));
        CardPoolBrowserTitle = Loc.P("Pool.BrowserTitle", pool.Count, CardCount(pool.Count), pool.SourceName);
        CardPoolFilterChips = pool.Filters.Select(f => new PoolChipModel(f.Tag, PoolDisplay.Mechanic(f).Label, _cardPoolSelected.Contains(f.Tag))).ToList();
        // Excavate treasures come in the order the cycle gives them, the next tier first.
        var order = pool.Excavate?.FromNext.Select(t => t.Label).ToList();
        var cards = pool.Cards
            .Where(c => _cardPoolSection is null || c.Sections.Contains(_cardPoolSection))
            .Where(c => _cardPoolSelected.All(c.Mechanics.Contains))
            .OrderBy(c => order is null ? 0 : order.IndexOf(c.Sections.FirstOrDefault() ?? string.Empty))
            .ThenBy(c => c.Cost).ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        CardPoolBrowserNote = _cardPoolSelected.Count > 0 || _cardPoolSection is not null ? Loc.P("Pool.Match", cards.Count, CardCount(cards.Count)) : string.Empty;
        CardPoolBrowserRows = cards.Chunk(3).Select(row => new PoolCardRowModel(row)).ToList();
    }

    /// <summary>"Excavate: tier 2 of 4" in a game; "Excavate: 4 tiers" when the next tier is not known.</summary>
    private static string ExcavateTitle(ExcavatePool excavate) => excavate.Next is { } next
        ? Loc.F("Pool.Excavate.Title", next, excavate.Tiers.Count)
        : Loc.P("Pool.Excavate.TitleAll", excavate.Tiers.Count);

    /// <summary>The Excavate rows: the next tier's treasures (the other tiers are the browser's sections).</summary>
    private void UpdateExcavate(ExcavatePool? excavate)
    {
        ShowCardPoolExcavate = excavate is not null;
        var next = excavate?.Next is not null ? excavate.FromNext[0] : null;
        CardPoolExcavateNext = next is null ? string.Empty : Loc.F("Pool.Excavate.Next", Loc.T("Pool.Excavate.Rarity." + next.Rarity));
        CardPoolExcavateRows = next?.Treasures.Select(t => new PoolListRow(t.CardId, t.Name, t.Cost, t.Rarity, string.Empty)).ToList() ?? [];
    }

    /// <summary>The section chips of the current pool; the selected one only while the browser is open.</summary>
    private void UpdateSectionChips() =>
        CardPoolSections = _cardPool?.Sections
            .Select(s => new PoolChipModel(s.Label, $"{s.Label} {s.Count.ToString("N0", CultureInfo.InvariantCulture)}", CardPoolBrowserOpen && s.Label == _cardPoolSection, Section: true))
            .ToList() ?? [];

    /// <summary>A card count as the panel writes it ("1,234"), for the plural texts' <c>{1}</c>.</summary>
    private static string CardCount(int count) => count.ToString("N0", CultureInfo.InvariantCulture);
}

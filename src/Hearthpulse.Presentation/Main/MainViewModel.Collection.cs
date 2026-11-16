#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The player's collection in the meta deck lists and on the own deck page (docs/collection.md, "In the interface"):
/// the build label of each deck, the "can build" filter, the order by missing dust and the collection's status line.
/// The collection stays on this machine: nothing here hands it to a client that talks to the network.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Up to this many deck lists are priced at once on the calling thread; more are priced on a pool thread.</summary>
    internal const int InlineDeckCosts = 80;

    private static readonly IReadOnlyDictionary<string, DeckCost> NoDeckCosts = new Dictionary<string, DeckCost>(StringComparer.Ordinal);

    private CardCollection? _collection;
    private bool _collectionReadable = HearthstoneMemory.IsSupported;
    private bool _canBuildWanted;
    private bool _deckCostsPending;
    private string _metaDeckSort = "WinRate";
    private IReadOnlyDictionary<string, DeckCost> _deckCosts = NoDeckCosts;
    private (ConstructedMeta? Meta, CardCollection? Collection, CardDb? Cards) _deckCostsOf;
    private int _deckCostsBuild;
    private RelayCommand? _setMetaDeckSort;

    /// <summary>
    /// Whether this system can read the collection from the game client at all (Windows). The host leaves the default;
    /// a test sets it to see the other system's empty state.
    /// </summary>
    public bool CollectionReadable
    {
        get => _collectionReadable;
        set
        {
            if (Set(ref _collectionReadable, value))
            {
                RaiseCollectionStatus();
            }
        }
    }

    /// <summary>A collection is known, confirmed or saved.</summary>
    public bool HasCollection => _collection is not null;

    /// <summary>The known collection is a saved one: not read from the game client that is running now.</summary>
    public bool CollectionSaved => _collection is { Confirmed: false };

    /// <summary>
    /// Where the collection's numbers come from and of what time, or why there are none: read from the game today at
    /// 14:05; a saved collection of a date; none yet; not read; a system without the read.
    /// </summary>
    public string CollectionStatus
    {
        get
        {
            if (_collection is not { } collection)
            {
                return Loc.T(!_collectionReadable ? "Collection.Status.Unsupported" : HearthstoneRunning ? "Collection.Status.NotRead" : "Collection.Status.None");
            }

            var read = collection.ReadAt.Kind == DateTimeKind.Local ? collection.ReadAt : DateTime.SpecifyKind(collection.ReadAt, DateTimeKind.Utc).ToLocalTime();
            if (collection.Confirmed)
            {
                return Loc.F("Collection.Status.Confirmed", Loc.F(read.Date == DateTime.Today ? "Collection.When.Today" : "Collection.When.Date", read));
            }

            // A saved collection is always named with its full date: it is "as of then", whoever's it was.
            return Loc.F(
                !_collectionReadable ? "Collection.Status.SavedUnsupported" : HearthstoneRunning ? "Collection.Status.SavedNotRead" : "Collection.Status.Saved",
                Loc.F("Collection.When.Date", read));
        }
    }

    /// <summary>
    /// The "can build" filter may be used: only with a collection read from the running game. A filter hides decks, and
    /// hiding by a saved collection would state "you cannot build the rest" from numbers nobody vouches for now; the
    /// labels stay (dimmed and marked), the filter does not.
    /// </summary>
    public bool CanBuildAvailable => _collection is { Confirmed: true };

    /// <summary>Why the filter is off limits, shown beside it; empty while it can be used or the status line says it all.</summary>
    public string CanBuildNote => CanBuildAvailable || !_collectionReadable ? string.Empty
        : Loc.T(_collection is null ? "Collection.Filter.NeedsCollection" : "Collection.Filter.NeedsConfirmed");

    public bool HasCanBuildNote => CanBuildNote.Length > 0;

    /// <summary>
    /// "Can build": the top decks keep only the lists that are owned or can be crafted with the dust at hand. The
    /// player's choice is kept while the filter is off limits and applies again once the collection is confirmed.
    /// </summary>
    public bool CanBuildOnly
    {
        get => _canBuildWanted && CanBuildAvailable;
        set
        {
            if (_canBuildWanted != value)
            {
                _canBuildWanted = value;
                Raise(nameof(CanBuildOnly));
                ShowMetaDecks();
            }
        }
    }

    /// <summary>"Can build" is on and no list passes it: its own empty state, not "not enough games".</summary>
    public bool MetaCanBuildEmpty => MetaStatusShown && CanBuildOnly && !_deckCostsPending && _metaDecks.Count == 0;

    /// <summary>The order of the top decks: by win rate, or by the dust the player lacks for each (with a collection).</summary>
    public IReadOnlyList<MetaChoice> MetaDeckSorts =>
    [
        new("WinRate", Loc.T("Collection.Sort.WinRate"), !SortsByDust),
        new("Dust", Loc.T("Collection.Sort.Dust"), SortsByDust),
    ];

    public RelayCommand SetMetaDeckSort => _setMetaDeckSort ??= new RelayCommand(p =>
    {
        var sort = p as string == "Dust" ? "Dust" : "WinRate";
        if (sort != _metaDeckSort)
        {
            _metaDeckSort = sort;
            Raise(nameof(MetaDeckSorts));
            ShowMetaDecks();
        }
    });

    private bool SortsByDust => _metaDeckSort == "Dust" && _collection is not null;

    /// <summary>
    /// The newest known collection, from the host on the UI thread (it listens to <c>GameSession.CollectionChanged</c>,
    /// which is raised on another thread, and reads <c>GameSession.Collection</c> after dispatching). Null: none known.
    /// </summary>
    public void SetCollection(CardCollection? collection)
    {
        if (ReferenceEquals(_collection, collection))
        {
            return;
        }

        _collection = collection;
        RaiseCollectionStatus();
        Raise(nameof(HasCollection));
        Raise(nameof(CollectionSaved));
        Raise(nameof(CanBuildAvailable));
        Raise(nameof(CanBuildOnly));
        Raise(nameof(MetaDeckSorts));
        ShowMetaDecks();
        _deckPage?.RefreshCollection();
    }

    /// <summary>
    /// Opens the deck page on a deck the history does not have (the UI render check: the collection block on a made-up
    /// deck). The next read of the history puts the player's own decks back.
    /// </summary>
    public void OpenSampleDeck(Hearthpulse.Core.Stats.DeckFamily deck)
    {
        _deckFamilies = [deck];
        OpenDeckPage(deck.Key);
    }

    /// <summary>The collection block of the own deck page for a deck code (null or empty: a deck known by its name only).</summary>
    internal DeckCollectionView DeckCollectionFor(string? deckCode)
    {
        var status = CollectionStatus;
        if (_collection is not { } collection)
        {
            return new DeckCollectionView(status, null, string.Empty);
        }

        var cost = DeckCost.For(DeckString.TryDecode(deckCode ?? string.Empty), collection, _cards);
        return cost.State != DeckBuild.Unknown
            ? new DeckCollectionView(status, DeckBuildViews.Of(cost, _cards), string.Empty)
            : new DeckCollectionView(status, null, Loc.T(cost.Gap switch
            {
                DeckCostGap.EmptyDeck => "Collection.Deck.NoCode",
                DeckCostGap.NoCardData => "Collection.Deck.NoCardData",
                _ => "Collection.Deck.UnknownCards",
            }));
    }

    /// <summary>The Hearthstone client started or closed, or the system's ability changed: the status line names another next step.</summary>
    private void RaiseCollectionStatus()
    {
        Raise(nameof(CollectionStatus));
        Raise(nameof(CanBuildNote));
        Raise(nameof(HasCanBuildNote));
        _deckPage?.RefreshCollection();
    }

    /// <summary>The card data changed: every price was made from the old one.</summary>
    private void OnCollectionCardsChanged()
    {
        ShowMetaDecks();
        _deckPage?.RefreshCollection();
    }

    /// <summary>The label of a meta deck list; null while it is not priced or its answer is unknown.</summary>
    private DeckBuildView? BuildOf(string deckCode) =>
        _deckCosts.TryGetValue(deckCode, out var cost) ? DeckBuildViews.Of(cost, _cards) : null;

    /// <summary>
    /// The top decks of the page's format: the class filter, enough games, "can build", then the chosen order. Prices
    /// come from <see cref="_deckCosts"/>, made again here when the stats, the collection or the card data changed.
    /// </summary>
    private void ShowMetaDecks()
    {
        var priced = EnsureDeckCosts();
        if (PageMeta is not { } meta)
        {
            MetaDecks = [];
            return;
        }

        // Prefer lists with 200+ games; a thin slice (high rank, few days) falls back to every list.
        var minGames = meta.Decks.Count(d => d.Games >= 200) >= 20 ? 200 : 0;
        var decks = meta.Decks
            .Where(d => _metaClass.Length == 0 || string.Equals(d.Class, _metaClass, StringComparison.OrdinalIgnoreCase))
            .Where(d => d.Games >= minGames);
        if (CanBuildOnly)
        {
            decks = decks.Where(d => _deckCosts.TryGetValue(d.DeckString, out var cost) && cost.State is DeckBuild.Owned or DeckBuild.CraftableNow);
        }

        var ordered = SortsByDust
            ? decks.OrderBy(d => DustRank(d).Group).ThenBy(d => DustRank(d).Short).ThenBy(d => DustRank(d).Needed).ThenByDescending(d => d.WinRate)
            : decks.OrderByDescending(d => d.WinRate);
        MetaDecks = ordered.Take(60).Select(DeckRowOf).ToList();
        Raise(nameof(MetaCanBuildEmpty));
        Raise(nameof(MetaDecksEmpty));
        if (priced)
        {
            ShowArchetypeDeckBuilds();
        }
    }

    /// <summary>
    /// Where a deck stands in the order by missing dust: decks that need no more dust than the player has first (by
    /// what they cost), then by how much dust is lacking, then the ones no dust completes, then the unknown.
    /// </summary>
    private (int Group, long Short, long Needed) DustRank(MetaDeck deck) =>
        !_deckCosts.TryGetValue(deck.DeckString, out var cost) ? (3, 0, 0) : cost.State switch
        {
            DeckBuild.Owned or DeckBuild.CraftableNow or DeckBuild.NeedsDust => (0, cost.DustShort ?? 0, cost.DustNeeded ?? 0),
            DeckBuild.NotCraftable => (1, 0, cost.DustForCraftable ?? 0),
            _ => (2, 0, 0),
        };

    /// <summary>The side card's best lists with the labels of the prices known now (the lists themselves stay).</summary>
    private void ShowArchetypeDeckBuilds()
    {
        if (_archetypeDecks.Count > 0)
        {
            ArchetypeDecks = _archetypeDecks.Select(row => row with { Build = BuildOf(row.Code) }).ToList();
        }
    }

    /// <summary>
    /// Prices the page's deck lists when the stats, the collection or the card data are not the ones the known prices
    /// were made from. A short list is priced here; a long one on a pool thread, and the lists show their labels when
    /// it is done (<see cref="_deckCostsBuild"/> drops a result that something newer has replaced).
    /// </summary>
    /// <returns>True when <see cref="_deckCosts"/> was replaced before returning.</returns>
    private bool EnsureDeckCosts()
    {
        var cards = _cards;
        if (ReferenceEquals(PageMeta, _deckCostsOf.Meta) && ReferenceEquals(_collection, _deckCostsOf.Collection) && ReferenceEquals(cards, _deckCostsOf.Cards))
        {
            return false;
        }

        _deckCostsOf = (PageMeta, _collection, cards);
        var build = ++_deckCostsBuild;
        if (PageMeta is not { } meta || _collection is not { } collection)
        {
            _deckCosts = NoDeckCosts;
            _deckCostsPending = false;
            return true;
        }

        var codes = meta.Decks.Select(d => d.DeckString).Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count <= InlineDeckCosts)
        {
            _deckCosts = PriceDecks(codes, collection, cards);
            _deckCostsPending = false;
            return true;
        }

        // The labels of the old collection do not stand in for the new one's while it is priced.
        _deckCosts = NoDeckCosts;
        _deckCostsPending = true;
        PriceDecksLaterAsync(build, codes, collection, cards).Forget("Deck costs");
        return true;
    }

    private async Task PriceDecksLaterAsync(int build, IReadOnlyList<string> codes, CardCollection collection, CardDb cards)
    {
        var costs = await Task.Run(() => PriceDecks(codes, collection, cards)).ConfigureAwait(true);
        if (build != _deckCostsBuild)
        {
            return;
        }

        _deckCosts = costs;
        _deckCostsPending = false;
        ShowMetaDecks();
        ShowArchetypeDeckBuilds();
    }

    private static Dictionary<string, DeckCost> PriceDecks(IReadOnlyList<string> codes, CardCollection collection, CardDb cards)
    {
        var costs = new Dictionary<string, DeckCost>(codes.Count, StringComparer.Ordinal);
        foreach (var code in codes)
        {
            costs[code] = DeckCost.For(DeckString.TryDecode(code), collection, cards);
        }

        return costs;
    }
}

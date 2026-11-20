#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The Collection page (docs/collection-page.md): how full each expansion is, the hero skins by class, the coins and
/// the card backs by group, each tab with its status line and its empty states. The sets and skins are counted on a pool
/// thread once per collection and card data (a newer count replaces an older one by its number); the tiles of one
/// class or group are built at a time. Everything stays on this machine.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The page's tabs in order, as <see cref="ShowCollectionTab"/> takes them.</summary>
    public static IReadOnlyList<string> CollectionTabs { get; } = ["Sets", "Skins", "Coins", "Backs"];

    /// <summary>The set filters in order: Standard, Wild (Standard and Wild), All (Classic too).</summary>
    public static IReadOnlyList<string> CollectionFilters { get; } = ["Standard", "Wild", "All"];

    /// <summary>A tile not in the collection shows its art at this strength (its words stay full strength).</summary>
    internal const double NotOwnedOpacity = 0.4;

    // The classes in the order of the game's collection.
    private static readonly string[] CollectionClassOrder =
        ["DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR"];

    private static readonly string[] ShownRarities = ["COMMON", "RARE", "EPIC", "LEGENDARY"];

    private string _collectionTab = "Sets";
    private string _collectionFilter = "Standard";
    private string _skinClass = string.Empty;
    private bool _collectionPageWanted;
    private bool _collectionGoldenShown = CollectionFinish.GoldenVerified;
    private CollectionPageData? _collectionPageData;
    private (CardCollection? Collection, CardDb? Cards) _collectionPageOf;
    private int _collectionPageBuild;
    private bool _collectionPageFailed;

    private LibraryStatus? _collectionPageStatus;
    private string _collectionPageEmpty = string.Empty;
    private string _collectionPageEmptyGlyph = string.Empty;
    private IReadOnlyList<CollectionSumTile> _collectionSum = [];
    private double _collectionSumFraction;
    private IReadOnlyList<CollectionSetView> _collectionSets = [];
    private IReadOnlyList<MetaChoice> _skinClasses = [];
    private CollectionGroupView? _skinGroup;
    private IReadOnlyList<CollectionItemView> _skins = [];
    private string _skinsOwned = string.Empty;
    private double _skinsOwnedFraction;
    private double _collectionArtOpacity = 1;
    private RelayCommand? _setCollectionSkinClass;

    /// <summary>The sets and hero skins counted from one collection and card data (on a pool thread).</summary>
    private sealed record CollectionPageData(IReadOnlyList<SetCompletion> Sets, IReadOnlyList<HeroSkin> Skins);

    /// <summary>The tab shown: Sets, Skins, Coins or Backs (<see cref="CollectionTabs"/>).</summary>
    public string CollectionTab => _collectionTab;

    public bool CollectionTabSets { get => _collectionTab == "Sets"; set => PickCollectionTab("Sets", value); }

    public bool CollectionTabSkins { get => _collectionTab == "Skins"; set => PickCollectionTab("Skins", value); }

    public bool CollectionTabCoins { get => _collectionTab == "Coins"; set => PickCollectionTab("Coins", value); }

    public bool CollectionTabBacks { get => _collectionTab == "Backs"; set => PickCollectionTab("Backs", value); }

    /// <summary>The set filter: Standard, Wild or All (<see cref="CollectionFilters"/>).</summary>
    public string CollectionFilter => _collectionFilter;

    public bool CollectionFilterStandard { get => _collectionFilter == "Standard"; set => PickCollectionFilter("Standard", value); }

    public bool CollectionFilterWild { get => _collectionFilter == "Wild"; set => PickCollectionFilter("Wild", value); }

    public bool CollectionFilterAll { get => _collectionFilter == "All"; set => PickCollectionFilter("All", value); }

    /// <summary>The status line of the tab's data (the collection, or the cosmetics for coins and card backs); null with no data.</summary>
    public LibraryStatus? CollectionPageStatus { get => _collectionPageStatus; private set => Set(ref _collectionPageStatus, value); }

    public bool HasCollectionPageStatus => _collectionPageStatus is not null;

    /// <summary>Why the tab shows nothing (no data, not read, another system, card data loading, counting, an empty filter); empty while it shows its content.</summary>
    public string CollectionPageEmpty { get => _collectionPageEmpty; private set => Set(ref _collectionPageEmpty, value); }

    public string CollectionPageEmptyGlyph { get => _collectionPageEmptyGlyph; private set => Set(ref _collectionPageEmptyGlyph, value); }

    public bool HasCollectionPageEmpty => _collectionPageEmpty.Length > 0;

    /// <summary>The set filter is offered: on the sets tab once the sets are counted (an empty filter keeps it, to pick All).</summary>
    public bool CollectionFilterShown => _collectionTab == "Sets" && _collection is not null && _collectionPageData is not null && _cards.Count > 0;

    public bool CollectionSetsShown => _collectionTab == "Sets" && !HasCollectionPageEmpty;

    public bool CollectionSkinsShown => _collectionTab == "Skins" && !HasCollectionPageEmpty;

    public bool CollectionCoinsShown => _collectionTab == "Coins" && !HasCollectionPageEmpty;

    public bool CollectionBacksShown => _collectionTab == "Backs" && !HasCollectionPageEmpty;

    /// <summary>The pictures (bars, art, medallions) dim with saved data; the words never do.</summary>
    public double CollectionArtOpacity { get => _collectionArtOpacity; private set => Set(ref _collectionArtOpacity, value); }

    /// <summary>The sum of the filter's sets: playable, unique, dust to complete (golden once verified).</summary>
    public IReadOnlyList<CollectionSumTile> CollectionSum { get => _collectionSum; private set => Set(ref _collectionSum, value); }

    /// <summary>The playable copies of the filter's sets owned, 0..1: the wide bar under the sum.</summary>
    public double CollectionSumFraction { get => _collectionSumFraction; private set => Set(ref _collectionSumFraction, value); }

    /// <summary>The filter's sets, newest first.</summary>
    public IReadOnlyList<CollectionSetView> CollectionSets { get => _collectionSets; private set => Set(ref _collectionSets, value); }

    /// <summary>The classes with hero skins, as chips "Mage · 14/58", in the game's order.</summary>
    public IReadOnlyList<MetaChoice> CollectionSkinClasses { get => _skinClasses; private set => Set(ref _skinClasses, value); }

    public CollectionGroupView? CollectionSkinGroup { get => _skinGroup; private set => Set(ref _skinGroup, value); }

    /// <summary>The skins of the chosen class: owned first, then by name.</summary>
    public IReadOnlyList<CollectionItemView> CollectionSkins { get => _skins; private set => Set(ref _skins, value); }

    public string CollectionSkinsOwned { get => _skinsOwned; private set => Set(ref _skinsOwned, value); }

    public double CollectionSkinsOwnedFraction { get => _skinsOwnedFraction; private set => Set(ref _skinsOwnedFraction, value); }

    /// <summary>Shows the hero skins of a class (its key, "MAGE").</summary>
    public RelayCommand SetCollectionSkinClass => _setCollectionSkinClass ??= new RelayCommand(p =>
    {
        if (p is string key && key != _skinClass)
        {
            _skinClass = key;
            ShowCollectionPage();
        }
    });

    /// <summary>
    /// The golden copies are shown: only once the split by finish is verified (<see cref="CollectionFinish.GoldenVerified"/>);
    /// the render scene turns it on for its <c>-golden</c> frame, never a setting.
    /// </summary>
    internal bool CollectionGoldenShown
    {
        get => _collectionGoldenShown;
        set
        {
            if (_collectionGoldenShown != value)
            {
                _collectionGoldenShown = value;
                ShowCollectionPage();
            }
        }
    }

    /// <summary>How many times the sets and skins were counted (a test counts the counts one change costs).</summary>
    internal int CollectionPageCounts { get; private set; }

    /// <summary>Opens a tab of the page (<see cref="CollectionTabs"/>); an unknown name opens the sets.</summary>
    public void ShowCollectionTab(string tab)
    {
        var name = CollectionTabs.Contains(tab, StringComparer.Ordinal) ? tab : CollectionTabs[0];
        if (name == _collectionTab)
        {
            return;
        }

        _collectionTab = name;
        Raise(nameof(CollectionTab));
        Raise(nameof(CollectionTabSets));
        Raise(nameof(CollectionTabSkins));
        Raise(nameof(CollectionTabCoins));
        Raise(nameof(CollectionTabBacks));
        ShowCollectionPage();
    }

    /// <summary>Picks the set filter (<see cref="CollectionFilters"/>); an unknown name picks Standard.</summary>
    public void ShowCollectionFilter(string filter)
    {
        var name = CollectionFilters.Contains(filter, StringComparer.Ordinal) ? filter : CollectionFilters[0];
        if (name == _collectionFilter)
        {
            return;
        }

        _collectionFilter = name;
        Raise(nameof(CollectionFilter));
        Raise(nameof(CollectionFilterStandard));
        Raise(nameof(CollectionFilterWild));
        Raise(nameof(CollectionFilterAll));
        ShowCollectionPage();
    }

    private void PickCollectionTab(string tab, bool on)
    {
        if (on)
        {
            ShowCollectionTab(tab);
        }
    }

    private void PickCollectionFilter(string filter, bool on)
    {
        if (on)
        {
            ShowCollectionFilter(filter);
        }
    }

    partial void CollectionPageShown()
    {
        _collectionPageWanted = true;
        EnsureCollectionPage();
    }

    partial void RebuildCollectionPage()
    {
        if (_collectionPageWanted)
        {
            ShowCollectionPage();
        }
    }

    /// <summary>The collection or the card data changed: the page counts again now if it is shown, else when it is.</summary>
    private void CollectionPageInputsChanged()
    {
        if (_collectionPageWanted && _shownPage == "Collection")
        {
            EnsureCollectionPage();
        }
        else if (_collectionPageWanted)
        {
            // Not shown: the old numbers stay out of sight until the page is shown and counts anew.
            ShowCollectionPage();
        }
    }

    /// <summary>The client started or closed, or the system's ability changed: the status line and empty state name another next step.</summary>
    private void RaiseCollectionPageStatus()
    {
        if (_collectionPageWanted)
        {
            ShowCollectionStatus();
        }
    }

    /// <summary>
    /// Counts the sets and skins when the collection or card data are not the ones the known count was made from. The
    /// count runs on a pool thread; the old one stays on screen until the new one replaces it in one step.
    /// </summary>
    private void EnsureCollectionPage()
    {
        var collection = _collection;
        var cards = _cards;
        if (collection is null || cards.Count == 0)
        {
            _collectionPageOf = (collection, cards);
            _collectionPageBuild++;
            _collectionPageData = null;
            _collectionPageFailed = false;
            ShowCollectionPage();
            return;
        }

        if (ReferenceEquals(collection, _collectionPageOf.Collection) && ReferenceEquals(cards, _collectionPageOf.Cards) && !_collectionPageFailed)
        {
            ShowCollectionPage();
            return;
        }

        _collectionPageOf = (collection, cards);
        var build = ++_collectionPageBuild;
        _collectionPageFailed = false;
        ShowCollectionPage();
        CountCollectionPageAsync(build, collection, cards).Forget("Collection page");
    }

    private async Task CountCollectionPageAsync(int build, CardCollection collection, CardDb cards)
    {
        CollectionPageData? data = null;
        try
        {
            data = await Background.RunAsync(() => new CollectionPageData(ExpansionCompletion.Build(collection, cards), HeroSkins.Build(collection, cards)))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Diag.Error("Counting the collection page failed", ex);
        }

        if (build != _collectionPageBuild)
        {
            return;
        }

        CollectionPageCounts++;
        _collectionPageFailed = data is null;
        if (data is not null)
        {
            _collectionPageData = data;
        }

        ShowCollectionPage();
    }

    /// <summary>The status line and empty state of the shown tab, and the flags that follow them.</summary>
    private void ShowCollectionStatus()
    {
        var cosmeticsTab = _collectionTab is "Coins" or "Backs";
        CollectionPageStatus = cosmeticsTab
            ? _cosmetics is { } c ? StatusLine(c.ReadAt, c.Confirmed, cosmetics: true) : null
            : _collection is { } k ? StatusLine(k.ReadAt, k.Confirmed, cosmetics: false) : null;
        var (empty, glyph) = cosmeticsTab ? CosmeticsEmpty() : CollectionEmpty();
        CollectionPageEmpty = empty;
        CollectionPageEmptyGlyph = glyph;
        Raise(nameof(HasCollectionPageStatus));
        Raise(nameof(HasCollectionPageEmpty));
        Raise(nameof(CollectionFilterShown));
        Raise(nameof(CollectionSetsShown));
        Raise(nameof(CollectionSkinsShown));
        Raise(nameof(CollectionCoinsShown));
        Raise(nameof(CollectionBacksShown));
    }

    /// <summary>Builds the shown tab's content (its texts in the current language) and its status.</summary>
    private void ShowCollectionPage()
    {
        switch (_collectionTab)
        {
            case "Sets":
                ShowCollectionSets();
                break;
            case "Skins":
                ShowCollectionSkins();
                break;
            case "Coins":
                ShowCollectionCoins();
                break;
            default:
                ShowCollectionBacks();
                break;
        }

        ShowCollectionStatus();
    }

    private (string Text, string Glyph) CollectionEmpty()
    {
        if (_collection is null)
        {
            return !_collectionReadable ? (Loc.T("CollectionPage.Empty.Unsupported"), CollectionGlyphs.Unsupported)
                : HearthstoneRunning ? (Loc.T("CollectionPage.Empty.NotRead"), CollectionGlyphs.Waiting)
                : (Loc.T("CollectionPage.Empty.None"), CollectionGlyphs.Empty);
        }

        if (_cards.Count == 0)
        {
            return (Loc.T("CollectionPage.Empty.NoCards"), CollectionGlyphs.Waiting);
        }

        if (_collectionPageData is not { } data)
        {
            return _collectionPageFailed ? (Loc.T("CollectionPage.Failed"), CollectionGlyphs.Unsupported) : (Loc.T("CollectionPage.Loading"), CollectionGlyphs.Waiting);
        }

        return _collectionTab switch
        {
            "Sets" when _collectionSets.Count == 0 => (Loc.T("CollectionPage.Empty.Filter"), CollectionGlyphs.Filter),
            // Card data without hero skins is not loaded through: never "0 of 0".
            "Skins" when data.Skins.Count == 0 => (Loc.T("CollectionPage.Empty.NoCards"), CollectionGlyphs.Waiting),
            _ => (string.Empty, string.Empty),
        };
    }

    private (string Text, string Glyph) CosmeticsEmpty() =>
        _cosmetics is { } c && (_collectionTab == "Coins" ? c.Coins.Count : c.CardBacks.Count) > 0 ? (string.Empty, string.Empty)
        : !_collectionReadable ? (Loc.T("CollectionPage.Empty.CosmeticsUnsupported"), CollectionGlyphs.Unsupported)
        : HearthstoneRunning ? (Loc.T("CollectionPage.Empty.CosmeticsNotRead"), CollectionGlyphs.Waiting)
        : (Loc.T("CollectionPage.Empty.NoCosmetics"), CollectionGlyphs.Empty);

    /// <summary>"Read from the game today at 14:05." for data from the running client; a saved copy with its full date and the mark.</summary>
    private LibraryStatus StatusLine(DateTime readAt, bool confirmed, bool cosmetics)
    {
        var read = readAt.Kind == DateTimeKind.Local ? readAt : DateTime.SpecifyKind(readAt, DateTimeKind.Utc).ToLocalTime();
        if (confirmed)
        {
            var when = Loc.F(read.Date == DateTime.Today ? "Collection.When.Today" : "Collection.When.Date", read);
            return new LibraryStatus(CollectionGlyphs.Check, Loc.F("CollectionPage.Status.Confirmed", when), string.Empty, string.Empty);
        }

        var key = !_collectionReadable ? "CollectionPage.Status.SavedUnsupported"
            : HearthstoneRunning ? cosmetics ? "CollectionPage.Status.CosmeticsSavedNotRead" : "CollectionPage.Status.SavedNotRead"
            : "CollectionPage.Status.Saved";
        return new LibraryStatus(CollectionGlyphs.Saved, Loc.F(key, Loc.F("Collection.When.Date", read)), Loc.T("CollectionPage.Mark.Saved"), Loc.T("CollectionPage.Mark.SavedTip"));
    }

    private double SavedDim(bool saved) => saved ? DeckBuildView.SavedOpacity : 1;

    private void ShowCollectionSets()
    {
        CollectionArtOpacity = SavedDim(CollectionSaved);
        if (_collectionPageData is not { } data || _collection is null)
        {
            CollectionSum = [];
            CollectionSumFraction = 0;
            CollectionSets = [];
            return;
        }

        var sets = data.Sets.Where(s => _collectionFilter switch
        {
            "Standard" => s.Group == SetGroup.Standard,
            "Wild" => s.Group is SetGroup.Standard or SetGroup.Wild,
            _ => true,
        }).ToList();
        if (sets.Count == 0)
        {
            CollectionSum = [];
            CollectionSumFraction = 0;
            CollectionSets = [];
            return;
        }

        var sum = ExpansionCompletion.Sum(sets);
        var tiles = new List<CollectionSumTile>
        {
            new(Loc.T("CollectionPage.Sum.Playable"), Percent(sum.PlayableOwned, sum.PlayableTotal),
                Loc.P("CollectionPage.Sum.PlayableDetail", sum.PlayableTotal, sum.PlayableOwned), Loc.T("CollectionPage.Sum.PlayableTip")),
            new(Loc.T("CollectionPage.Sum.Unique"), Loc.F("CollectionPage.OfTotal", sum.UniqueOwned, sum.UniqueTotal), string.Empty, Loc.T("CollectionPage.Sum.UniqueTip")),
            new(Loc.T("CollectionPage.Sum.Dust"),
                sum.PlayableOwned >= sum.PlayableTotal ? Loc.T("CollectionPage.Sum.Done") : sum.DustToComplete.ToString("N0", CultureInfo.CurrentCulture),
                SumDustDetail(sum), Loc.T("CollectionPage.Sum.DustTip")),
        };
        if (_collectionGoldenShown)
        {
            tiles.Add(new(Loc.T("CollectionPage.Sum.Golden"), Loc.F("CollectionPage.OfTotal", sum.GoldenOwned, sum.PlayableTotal), string.Empty, string.Empty));
        }

        CollectionSum = tiles;
        CollectionSumFraction = Fraction(sum.PlayableOwned, sum.PlayableTotal);
        var dim = SavedDim(CollectionSaved);
        CollectionSets = sets.Select(s => SetView(s, dim)).ToList();
    }

    private static string SumDustDetail(SetCompletion sum)
    {
        var marks = new List<string>(2);
        if (sum.DustEstimated && sum.DustToComplete > 0)
        {
            marks.Add(Loc.T("Collection.Mark.Estimate"));
        }

        if (sum.UncraftableMissing > 0)
        {
            marks.Add(Loc.P("CollectionPage.Set.Uncraftable", sum.UncraftableMissing));
        }

        return string.Join(" · ", marks);
    }

    private CollectionSetView SetView(SetCompletion set, double dim)
    {
        var name = CardSets.DisplayName(set.Set);
        var percent = Percent(set.PlayableOwned, set.PlayableTotal);
        var complete = set.PlayableOwned >= set.PlayableTotal;
        var dust = complete ? Loc.T("CollectionPage.Set.Done")
            : set.DustToComplete == 0 ? Loc.T("CollectionPage.Set.UncraftableOnly")
            : Loc.F("CollectionPage.Set.Dust", set.DustToComplete);
        var glyph = complete ? CollectionGlyphs.Check : set.DustToComplete == 0 ? CollectionGlyphs.Lock : CollectionGlyphs.Dust;
        var group = _collectionFilter == "Standard" ? string.Empty : set.Group switch
        {
            SetGroup.Standard => Loc.T("CollectionPage.Group.Standard"),
            SetGroup.Wild => Loc.T("CollectionPage.Group.Wild"),
            _ => Loc.T("CollectionPage.Group.Classic"),
        };
        var rarities = set.ByRarity
            .Where(r => ShownRarities.Contains(r.Rarity, StringComparer.Ordinal) && r.UniqueTotal > 0)
            .Select(r =>
            {
                var rarity = RarityName(r.Rarity);
                return new CollectionRarityView(r.Rarity, rarity, Fraction(r.PlayableOwned, r.PlayableTotal),
                    Loc.F("CollectionPage.Fraction", r.PlayableOwned, r.PlayableTotal),
                    Loc.F("CollectionPage.Rarity.Tip", rarity, r.PlayableOwned, r.PlayableTotal, r.UniqueOwned, r.UniqueTotal, r.DustToComplete), dim);
            })
            .ToList();
        return new CollectionSetView(
            set.Set, name, percent, group, Fraction(set.PlayableOwned, set.PlayableTotal),
            Loc.F("CollectionPage.Fraction", set.PlayableOwned, set.PlayableTotal), Loc.F("CollectionPage.Fraction", set.UniqueOwned, set.UniqueTotal),
            dust, glyph, complete, !complete && set.DustEstimated && set.DustToComplete > 0,
            set.UncraftableMissing > 0 ? Loc.P("CollectionPage.Set.Uncraftable", set.UncraftableMissing) : string.Empty,
            rarities,
            _collectionGoldenShown ? Loc.F("CollectionPage.Set.Golden", set.GoldenOwned, set.PlayableTotal) : string.Empty,
            Loc.F("CollectionPage.Set.A11y", name, percent, set.UniqueOwned, set.UniqueTotal, dust),
            dim);
    }

    private static string RarityName(string rarity) => rarity switch
    {
        "COMMON" => Loc.T("CollectionPage.Rarity.COMMON"),
        "RARE" => Loc.T("CollectionPage.Rarity.RARE"),
        "EPIC" => Loc.T("CollectionPage.Rarity.EPIC"),
        _ => Loc.T("CollectionPage.Rarity.LEGENDARY"),
    };

    private void ShowCollectionSkins()
    {
        CollectionArtOpacity = SavedDim(CollectionSaved);
        if (_collectionPageData is not { } data || _collection is null || data.Skins.Count == 0)
        {
            CollectionSkinClasses = [];
            CollectionSkinGroup = null;
            CollectionSkins = [];
            CollectionSkinsOwned = string.Empty;
            CollectionSkinsOwnedFraction = 0;
            return;
        }

        var classes = data.Skins.Select(s => s.CardClass).Distinct(StringComparer.Ordinal)
            .OrderBy(c => Array.IndexOf(CollectionClassOrder, c) is var i and >= 0 ? i : CollectionClassOrder.Length)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();
        if (!classes.Contains(_skinClass, StringComparer.Ordinal))
        {
            _skinClass = classes[0];
        }

        CollectionSkinClasses = classes.Select(c =>
        {
            var of = data.Skins.Where(s => s.CardClass == c).ToList();
            return new MetaChoice(c, Loc.F("CollectionPage.Chip", ConstructedMeta.ClassName(c), of.Count(s => s.Owned), of.Count), c == _skinClass);
        }).ToList();

        var owned = data.Skins.Count(s => s.Owned);
        CollectionSkinsOwned = Loc.F("CollectionPage.Owned", owned, data.Skins.Count);
        CollectionSkinsOwnedFraction = Fraction(owned, data.Skins.Count);

        var dim = SavedDim(CollectionSaved);
        var shown = data.Skins.Where(s => s.CardClass == _skinClass)
            .Select(s => (Skin: s, Name: _cards.Get(s.CardId)?.Name is { Length: > 0 } n ? n : s.CardId))
            .OrderBy(s => s.Skin.Owned ? 0 : 1)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
        var shownOwned = shown.Count(s => s.Skin.Owned);
        CollectionSkinGroup = new CollectionGroupView(ConstructedMeta.ClassName(_skinClass), ClassHero(_skinClass),
            Loc.F("CollectionPage.OfTotal", shownOwned, shown.Count), Fraction(shownOwned, shown.Count), dim);
        CollectionSkins = shown.Select(s => ItemView(s.Skin.CardId, s.Skin.CardId, s.Name, s.Skin.Owned, favourite: false, dim)).ToList();
    }

    private static CollectionItemView ItemView(string key, string cardId, string name, bool owned, bool favourite, double dim)
    {
        var state = favourite ? Loc.T("CollectionPage.Item.Favourite") : owned ? Loc.T("CollectionPage.Item.In") : Loc.T("CollectionPage.Item.Out");
        var a11y = favourite ? Loc.F("CollectionPage.Item.A11yFavourite", name, Loc.T("CollectionPage.Item.In")) : Loc.F("CollectionPage.Item.A11y", name, state);
        return new CollectionItemView(key, cardId, name, owned, favourite, owned ? string.Empty : Loc.T("CollectionPage.Item.Out"),
            name + "\n" + state, a11y, (owned ? 1 : NotOwnedOpacity) * dim);
    }

    /// <summary>The share of <paramref name="total"/>, rounded down: 100 % only when everything is there.</summary>
    private static string Percent(long owned, long total) =>
        (total <= 0 ? 0 : owned * 100 / total / 100.0).ToString("P0", CultureInfo.CurrentCulture);

    private static double Fraction(long owned, long total) => total <= 0 ? 0 : Math.Clamp((double)owned / total, 0, 1);
}

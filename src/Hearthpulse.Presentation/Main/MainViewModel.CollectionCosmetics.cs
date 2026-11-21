#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The Collection page's coins and card backs (docs/collection-page.md): the cosmetics read from the game client, the
/// coins with their cards' art and names, the card backs by group drawn without art. The page's frame, states and the
/// sets and skins: <c>MainViewModel.CollectionPage.cs</c>.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The medallion shapes of the card back tiles, picked by the id: six, so a row of six shows each once.</summary>
    private static readonly int[] MedallionLobes = [9, 4, 12, 6, 5, 8];

    private CosmeticsCollection? _cosmetics;
    private CardBackCategory? _backGroup;
    private RelayCommand? _setCollectionBackGroup;
    private IReadOnlyList<CollectionItemView> _coins = [];
    private string _coinsOwned = string.Empty;
    private double _coinsOwnedFraction;
    private IReadOnlyList<MetaChoice> _backGroups = [];
    private CollectionGroupView? _backGroupView;
    private IReadOnlyList<CardBackView> _backs = [];
    private string _backsOwned = string.Empty;
    private double _backsOwnedFraction;

    /// <summary>
    /// The newest known card backs and coins, from the host on the UI thread (it listens to
    /// <c>GameSession.CosmeticsChanged</c>, raised on another thread). Null: none known.
    /// </summary>
    public void SetCosmetics(CosmeticsCollection? cosmetics)
    {
        if (ReferenceEquals(_cosmetics, cosmetics))
        {
            return;
        }

        _cosmetics = cosmetics;
        if (_collectionPageWanted)
        {
            ShowCollectionPage();
        }
    }

    /// <summary>Every coin: favourites, the other owned, then the rest.</summary>
    public IReadOnlyList<CollectionItemView> CollectionCoins { get => _coins; private set => Set(ref _coins, value); }

    public string CollectionCoinsOwned { get => _coinsOwned; private set => Set(ref _coinsOwned, value); }

    public double CollectionCoinsOwnedFraction { get => _coinsOwnedFraction; private set => Set(ref _coinsOwnedFraction, value); }

    /// <summary>The card back groups with backs, as chips "Ranked seasons · 120/154"; None and unknown ones are "Other", last.</summary>
    public IReadOnlyList<MetaChoice> CollectionBackGroups { get => _backGroups; private set => Set(ref _backGroups, value); }

    public CollectionGroupView? CollectionBackGroup { get => _backGroupView; private set => Set(ref _backGroupView, value); }

    /// <summary>The card backs of the chosen group, in the game's order.</summary>
    public IReadOnlyList<CardBackView> CollectionBacks { get => _backs; private set => Set(ref _backs, value); }

    public string CollectionBacksOwned { get => _backsOwned; private set => Set(ref _backsOwned, value); }

    public double CollectionBacksOwnedFraction { get => _backsOwnedFraction; private set => Set(ref _backsOwnedFraction, value); }

    /// <summary>Shows the card backs of a group (its <see cref="CardBackCategory"/> name).</summary>
    public RelayCommand SetCollectionBackGroup => _setCollectionBackGroup ??= new RelayCommand(p =>
    {
        if (p is string key && Enum.TryParse<CardBackCategory>(key, out var group) && group != _backGroup)
        {
            _backGroup = group;
            ShowCollectionPage();
        }
    });

    private void ShowCollectionCoins()
    {
        var cosmetics = _cosmetics;
        var dim = SavedDim(cosmetics is { Confirmed: false });
        CollectionArtOpacity = dim;
        if (cosmetics is null || cosmetics.Coins.Count == 0)
        {
            CollectionCoins = [];
            CollectionCoinsOwned = string.Empty;
            CollectionCoinsOwnedFraction = 0;
            return;
        }

        var owned = cosmetics.Coins.Count(c => cosmetics.OwnedCoins.Contains(c.Id));
        CollectionCoinsOwned = Loc.F("CollectionPage.Owned", owned, cosmetics.Coins.Count);
        CollectionCoinsOwnedFraction = Fraction(owned, cosmetics.Coins.Count);

        // Most coin cards are named just "The Coin": a card's name is used only where it tells its coin apart from the
        // others, else the client's own name of the cosmetic (in the client's language, as card backs are).
        var cardNames = cosmetics.Coins
            .Select(c => _cards.Get(c.CardDbfId)?.Name)
            .OfType<string>()
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);
        CollectionCoins = cosmetics.Coins
            .Select((coin, index) =>
            {
                var have = cosmetics.OwnedCoins.Contains(coin.Id);
                var favourite = have && cosmetics.FavoriteCoins.Contains(coin.Id);
                // The coin's own card: its art, and its name in the app's language when that name is its own.
                var card = _cards.Get(coin.CardDbfId);
                var name = card?.Name is { Length: > 0 } n && (cardNames[n] == 1 || coin.Name.Length == 0) ? n : coin.Name;
                return (Order: favourite ? 0 : have ? 1 : 2, Index: index, View: ItemView(coin.Id.ToString(CultureInfo.InvariantCulture), card?.Id ?? string.Empty, name, have, favourite, dim));
            })
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Index)
            .Select(c => c.View)
            .ToList();
    }

    private void ShowCollectionBacks()
    {
        var cosmetics = _cosmetics;
        var dim = SavedDim(cosmetics is { Confirmed: false });
        CollectionArtOpacity = dim;
        if (cosmetics is null || cosmetics.CardBacks.Count == 0)
        {
            CollectionBackGroups = [];
            CollectionBackGroup = null;
            CollectionBacks = [];
            CollectionBacksOwned = string.Empty;
            CollectionBacksOwnedFraction = 0;
            return;
        }

        var groups = cosmetics.CardBacks
            .GroupBy(b => BackGroupOf(b.Category))
            .OrderBy(g => g.Key)
            .ToList();
        var group = _backGroup is { } chosen && groups.Exists(g => g.Key == chosen) ? chosen : groups[0].Key;
        _backGroup = group;
        CollectionBackGroups = groups.Select(g =>
            new MetaChoice(Enum.GetName(g.Key)!, Loc.F("CollectionPage.Chip", BackGroupName(g.Key), g.Count(b => cosmetics.OwnedCardBacks.Contains(b.Id)), g.Count()), g.Key == group))
            .ToList();
        var owned = cosmetics.CardBacks.Count(b => cosmetics.OwnedCardBacks.Contains(b.Id));
        CollectionBacksOwned = Loc.F("CollectionPage.Owned", owned, cosmetics.CardBacks.Count);
        CollectionBacksOwnedFraction = Fraction(owned, cosmetics.CardBacks.Count);

        var shown = groups.First(g => g.Key == group).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToList();
        var shownOwned = shown.Count(b => cosmetics.OwnedCardBacks.Contains(b.Id));
        var glyph = BackGroupGlyph(group);
        CollectionBackGroup = new CollectionGroupView(BackGroupName(group), string.Empty,
            Loc.F("CollectionPage.OfTotal", shownOwned, shown.Count), Fraction(shownOwned, shown.Count), dim);
        CollectionBacks = shown.Select(b =>
        {
            var have = cosmetics.OwnedCardBacks.Contains(b.Id);
            var favourite = have && cosmetics.FavoriteCardBacks.Contains(b.Id);
            var state = favourite ? Loc.T("CollectionPage.Item.Favourite") : have ? Loc.T("CollectionPage.Item.In") : Loc.T("CollectionPage.Item.Out");
            var a11y = b.Description.Length > 0 ? Loc.F("CollectionPage.Item.A11yDescribed", b.Name, state, b.Description) : Loc.F("CollectionPage.Item.A11y", b.Name, state);
            // The medallion's shape and turn come from the id: many tiles without art still look different.
            var id = Math.Abs(b.Id);
            return new CardBackView(b.Id, b.Name, b.Description, glyph, MedallionLobes[id % MedallionLobes.Length], id % 8 * 11.25,
                have, favourite, have ? string.Empty : Loc.T("CollectionPage.Item.Out"), state, a11y, (have ? 1 : NotOwnedOpacity) * dim);
        }).ToList();
    }

    /// <summary>The chip a card back's category goes under: None and unknown ones under "Other".</summary>
    private static CardBackCategory BackGroupOf(CardBackCategory category) => category == CardBackCategory.None ? CardBackCategory.Unknown : category;

    private static string BackGroupName(CardBackCategory group) => group switch
    {
        CardBackCategory.Base => Loc.T("CollectionPage.Backs.Group.Base"),
        CardBackCategory.Fireside => Loc.T("CollectionPage.Backs.Group.Fireside"),
        CardBackCategory.Expansions => Loc.T("CollectionPage.Backs.Group.Expansions"),
        CardBackCategory.HeroSkins => Loc.T("CollectionPage.Backs.Group.HeroSkins"),
        CardBackCategory.Seasonal => Loc.T("CollectionPage.Backs.Group.Seasonal"),
        CardBackCategory.Legend => Loc.T("CollectionPage.Backs.Group.Legend"),
        CardBackCategory.Esports => Loc.T("CollectionPage.Backs.Group.Esports"),
        CardBackCategory.GameLicenses => Loc.T("CollectionPage.Backs.Group.GameLicenses"),
        CardBackCategory.Promotions => Loc.T("CollectionPage.Backs.Group.Promotions"),
        CardBackCategory.PrePurchase => Loc.T("CollectionPage.Backs.Group.PrePurchase"),
        CardBackCategory.Blizzcon => Loc.T("CollectionPage.Backs.Group.Blizzcon"),
        CardBackCategory.GoldenCelebration => Loc.T("CollectionPage.Backs.Group.GoldenCelebration"),
        CardBackCategory.Events => Loc.T("CollectionPage.Backs.Group.Events"),
        _ => Loc.T("CollectionPage.Backs.Group.Other"),
    };

    /// <summary>The MDI glyph in a card back group's medallion.</summary>
    private static string BackGroupGlyph(CardBackCategory group) => group switch
    {
        CardBackCategory.Base => "\U000F0638",
        CardBackCategory.Fireside => "\U000F0238",
        CardBackCategory.Expansions => "\U000F0639",
        CardBackCategory.HeroSkins => "\U000F0004",
        CardBackCategory.Seasonal => "\U000F00ED",
        CardBackCategory.Legend => "\U000F01A5",
        CardBackCategory.Esports => "\U000F0538",
        CardBackCategory.GameLicenses => "\U000F0297",
        CardBackCategory.Promotions => "\U000F0E44",
        CardBackCategory.PrePurchase => "\U000F0110",
        CardBackCategory.Blizzcon => "\U000F0516",
        CardBackCategory.GoldenCelebration => "\U000F1056",
        CardBackCategory.Events => "\U000F09D3",
        _ => "\U000F0625",
    };
}

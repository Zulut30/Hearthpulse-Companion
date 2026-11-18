using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Hearthpulse.App.Main;

/// <summary>The pages built on first use (Battlegrounds, Standard/Wild, History, a deck) and the tabs inside them.</summary>
public partial class MainWindow
{
    private Pages.BattlegroundsPage? _battlegrounds;
    private Pages.ConstructedPageView? _constructed;
    private Pages.HistoryPage? _history;

    /// <summary>A tab asked for before its page was built: the page opens on it.</summary>
    private string? _pendingBattlegroundsTab;
    private string? _pendingConstructedTab;

    /// <summary>The Battlegrounds page, built on first use (a visit, a tab, a render) rather than with the window.</summary>
    public Pages.BattlegroundsPage PageBattlegrounds => _battlegrounds ?? BuildBattlegrounds();

    /// <summary>The Standard/Wild page, built on first use.</summary>
    public Pages.ConstructedPageView PageConstructed => _constructed ?? BuildConstructed();

    /// <summary>The History page, built on first use.</summary>
    public Pages.HistoryPage PageHistory => _history ??= Build(SlotHistory, new Pages.HistoryPage { Name = nameof(PageHistory) });

    private Pages.BattlegroundsPage BuildBattlegrounds()
    {
        _battlegrounds = Build(SlotBattlegrounds, new Pages.BattlegroundsPage { Name = nameof(PageBattlegrounds) });
        _battlegrounds.TabChanged += OnBgTab;
        if (_pendingBattlegroundsTab is { } tab)
        {
            _pendingBattlegroundsTab = null;
            BattlegroundsTab = tab;
        }

        return _battlegrounds;
    }

    private Pages.ConstructedPageView BuildConstructed()
    {
        _constructed = Build(SlotConstructed, new Pages.ConstructedPageView { Name = nameof(PageConstructed) });
        _constructed.TabChanged += OnCtTab;
        if (_pendingConstructedTab is { } tab)
        {
            _pendingConstructedTab = null;
            ShowConstructedTab(tab);
        }

        return _constructed;
    }

    /// <summary>Puts a page built late into its slot, hidden until the navigation shows it.</summary>
    private static T Build<T>(ContentControl slot, T page) where T : FrameworkElement
    {
        page.Visibility = Visibility.Collapsed;
        slot.Content = page;
        return page;
    }

    /// <summary>The Battlegrounds tabs in <see cref="MainViewModel.BattlegroundsTabs"/> order.</summary>
    private ToggleButton[] BattlegroundsTabButtons => [PageBattlegrounds.TabMine, PageBattlegrounds.TabHeroes, PageBattlegrounds.TabComps, PageBattlegrounds.TabTrinkets, PageBattlegrounds.TabCards];

    /// <summary>The Constructed tabs in <see cref="MainViewModel.ConstructedTabs"/> order.</summary>
    private ToggleButton[] ConstructedTabButtons => [PageConstructed.TabMyDecks, PageConstructed.TabMetaTiers, PageConstructed.TabMetaDecks, PageConstructed.TabMetaMatchups];

    /// <param name="chosen">A tab button was just checked (not unchecked): the shown view is scrolled into sight.</param>
    private void OnBgTab(bool chosen)
    {
        if (PageBattlegrounds.BgTiers is null)
        {
            return;
        }

        var mine = PageBattlegrounds.TabMine.IsChecked == true;
        // The view model follows the tab: the MMR slices and "Solo / Duos" belong to some tabs only. A tier tab's card
        // is built here, on its first opening; My stats builds none.
        _viewModel.BgTabShown(BattlegroundsTab);
        var card = PageBattlegrounds.BgTiers.ShowTab(BattlegroundsTab);
        FrameworkElement shown = mine ? PageBattlegrounds.BgStats : card;
        PageBattlegrounds.BracketPicker.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
        if (chosen)
        {
            Reveal(shown, 8);
        }
    }

    /// <param name="chosen">A tab button was just checked (not unchecked): the shown view is scrolled into sight.</param>
    private void OnCtTab(bool chosen)
    {
        if (PageConstructed.MyDecksPanel is null || PageConstructed.CtMeta is null)
        {
            return;
        }

        var meta = PageConstructed.TabMyDecks.IsChecked != true;
        PageConstructed.MyDecksPanel.Visibility = meta ? Visibility.Collapsed : Visibility.Visible;
        PageConstructed.CtStats.Visibility = PageConstructed.MyDecksPanel.Visibility;
        var tab = ConstructedTab;
        var shown = PageConstructed.CtMeta.ShowTab(tab == MainViewModel.ConstructedTabs[0] ? null : tab);
        if (chosen)
        {
            Reveal(shown ?? PageConstructed.CtStats, 8);
        }
    }

    /// <summary>The deck page view (used by the UI render check).</summary>
    public Decks.DeckDetailsView DeckDetailsPage => _deck ??= Fill(PageDeck, new Decks.DeckDetailsView(), nameof(MainViewModel.DeckPage));

    private Decks.DeckDetailsView? _deck;

    /// <summary>The card page view (used by the UI render check).</summary>
    public Cards.CardPageView CardDetailsPage => _card ??= Fill(PageCard, new Cards.CardPageView(), nameof(MainViewModel.CardPage));

    private Cards.CardPageView? _card;

    /// <summary>True while the card page closes: the page under it keeps its scroll position.</summary>
    private bool _cardClosing;

    /// <summary>A page view built late, bound to its view model and placed in its scrolling page.</summary>
    private static T Fill<T>(ScrollViewer page, T view, string path) where T : FrameworkElement
    {
        view.Margin = new Thickness(40, 0, 40, 120);
        view.SetBinding(DataContextProperty, new Binding(path));
        page.Content = view;
        return view;
    }

    /// <summary>Opens a Constructed tab: "Decks", "Tiers", "Top" or "Matchups" (used by the UI render check).</summary>
    public void ShowConstructedTab(string tab) =>
        ConstructedTabButtons[MainViewModel.NavIndex(MainViewModel.ConstructedTabs, tab)].IsChecked = true;

    /// <summary>The open Battlegrounds tab ("Mine", "Heroes", "Comps", "Trinkets", "Cards"), kept when the window is rebuilt.</summary>
    public string BattlegroundsTab
    {
        get => _battlegrounds is null
            ? _pendingBattlegroundsTab ?? MainViewModel.BattlegroundsTabs[0]
            : MainViewModel.NavChecked(MainViewModel.BattlegroundsTabs, States(BattlegroundsTabButtons)) ?? MainViewModel.BattlegroundsTabs[0];
        set
        {
            if (_battlegrounds is null)
            {
                _pendingBattlegroundsTab = value;
                return;
            }

            BattlegroundsTabButtons[MainViewModel.NavIndex(MainViewModel.BattlegroundsTabs, value)].IsChecked = true;
        }
    }

    /// <summary>The open Constructed tab ("Decks", "Tiers", "Top", "Matchups"), kept when the window is rebuilt.</summary>
    public string ConstructedTab
    {
        get => _constructed is null
            ? _pendingConstructedTab ?? MainViewModel.ConstructedTabs[0]
            : MainViewModel.NavChecked(MainViewModel.ConstructedTabs, States(ConstructedTabButtons)) ?? MainViewModel.ConstructedTabs[0];
        set
        {
            if (_constructed is null)
            {
                _pendingConstructedTab = value;
                return;
            }

            ShowConstructedTab(value);
        }
    }

    /// <summary>
    /// Opens a Battlegrounds tier list tab ("Heroes", "Comps", "Trinkets", "Cards") and scrolls the tabs to the top of the
    /// page (used by the UI render check).
    /// </summary>
    public void ShowBattlegroundsTab(string tab)
    {
        (tab switch
        {
            "Comps" => PageBattlegrounds.TabComps,
            "Trinkets" => PageBattlegrounds.TabTrinkets,
            "Cards" => PageBattlegrounds.TabCards,
            _ => PageBattlegrounds.TabHeroes,
        }).IsChecked = true;
        PageBattlegrounds.TabHeroes.BringIntoView(new Rect(0, 0, PageBattlegrounds.TabHeroes.ActualWidth, 700));
    }
}

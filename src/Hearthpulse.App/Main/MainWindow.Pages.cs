using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Hearthpulse.App.Infrastructure;
using St = Hearthpulse.App.Main.SettingsTabs;

namespace Hearthpulse.App.Main;

/// <summary>The pages built on first use (Battlegrounds, Standard/Wild, History, the library pages, a deck) and the tabs inside them.</summary>
public partial class MainWindow
{
    private Pages.BattlegroundsPage? _battlegrounds;
    private Pages.ConstructedPageView? _constructed;
    private Pages.HistoryPage? _history;
    private Pages.CollectionPage? _collectionPage;
    private Pages.AchievementsPage? _achievementsPage;
    private Pages.WrappedPage? _wrappedPage;

    /// <summary>A tab asked for before its page was built: the page opens on it.</summary>
    private string? _pendingBattlegroundsTab;
    private string? _pendingConstructedTab;

    /// <summary>The Battlegrounds page, built on first use (a visit, a tab, a render) rather than with the window.</summary>
    public Pages.BattlegroundsPage PageBattlegrounds => _battlegrounds ?? BuildBattlegrounds();

    /// <summary>The Standard/Wild page, built on first use.</summary>
    public Pages.ConstructedPageView PageConstructed => _constructed ?? BuildConstructed();

    /// <summary>The History page, built on first use.</summary>
    public Pages.HistoryPage PageHistory => _history ??= Build(SlotHistory, new Pages.HistoryPage { Name = nameof(PageHistory) });

    /// <summary>The Collection page, built on first use.</summary>
    public Pages.CollectionPage PageCollection => _collectionPage ??= Build(SlotCollection, new Pages.CollectionPage { Name = nameof(PageCollection) });

    /// <summary>The Achievements page, built on first use.</summary>
    public Pages.AchievementsPage PageAchievements => _achievementsPage ??= Build(SlotAchievements, new Pages.AchievementsPage { Name = nameof(PageAchievements) });

    /// <summary>The Recap page, built on first use.</summary>
    public Pages.WrappedPage PageWrapped => _wrappedPage ??= Build(SlotWrapped, new Pages.WrappedPage { Name = nameof(PageWrapped) });

    private Pages.BattlegroundsPage BuildBattlegrounds()
    {
        _battlegrounds = Build(SlotBattlegrounds, new Pages.BattlegroundsPage { Name = nameof(PageBattlegrounds) });
        _battlegrounds.TabChanged += _ => OnBgTab();
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

    /// <summary>A scrolling page of the window (Arena, Tavern Brawl) with its content made from <paramref name="template"/> on the first visit.</summary>
    private ScrollViewer Filled(ScrollViewer page, string template)
    {
        if (page.Content is null)
        {
            page.Content = ((DataTemplate)FindResource(template)).LoadContent();
        }

        return page;
    }

    private Pages.SettingsPage? _settingsView;
    private Setup.SetupWizard? _setup;

    /// <summary>
    /// The Settings page's content (tabs and search), built on the first visit, a tab asked for, or a render, rather than
    /// with the window: about a hundred milliseconds of the start-up (TASK-2026-11-23-perf-arch-round2, AC3). It opens on
    /// the tab shown last.
    /// </summary>
    internal Pages.SettingsPage SettingsView => _settingsView ?? BuildSettings();

    /// <summary>The Settings page's frame, its content built.</summary>
    private ScrollViewer SettingsFrame
    {
        get
        {
            _ = SettingsView;
            return PageSettings;
        }
    }

    internal St.GeneralSettings SettingsGeneral => SettingsView.SettingsGeneral;

    internal St.AppearanceSettings SettingsAppearance => SettingsView.SettingsAppearance;

    internal St.OverlaySettings SettingsOverlay => SettingsView.SettingsOverlay;

    internal St.TrackerSettings SettingsTracker => SettingsView.SettingsTracker;

    internal St.StreamSettings SettingsStream => SettingsView.SettingsStream;

    internal St.AccountSettings SettingsAccount => SettingsView.SettingsAccount;

    internal St.DataSettings SettingsData => SettingsView.SettingsData;

    internal St.AboutSettings SettingsAbout => SettingsView.SettingsAbout;

    internal St.SettingsSearchBox SettingsSearchBar => SettingsView.SettingsSearchBar;

    private Pages.SettingsPage BuildSettings()
    {
        var page = new Pages.SettingsPage { Name = nameof(SettingsView) };
        _settingsView = page;
        PageSettings.Content = page;
        // Its names stay reachable through the window, as when the page was part of its XAML (FindName, renders, tools).
        foreach (var element in page.TabButtons.Concat<FrameworkElement>([page.SettingsSearchBar, page.SettingsTabsArea, page.SettingsGeneral, page.SettingsAppearance,
                     page.SettingsOverlay, page.SettingsTracker, page.SettingsStream, page.SettingsAccount, page.SettingsData, page.SettingsAbout]))
        {
            RegisterName(element.Name, element);
        }

        // A switch changed by the player saves and applies the settings; the tab buttons stop their own Checked.
        page.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnSettingChanged));
        page.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnSettingChanged));
        page.TabChanged += UpdateSettingsTab;
        // The Tracker tab's "overlay is off" banner follows the Overlay tab's switch (two controls, one binding).
        BindingOperations.SetBinding(page.SettingsTracker, St.TrackerSettings.OverlayOnProperty,
            new Binding(nameof(ToggleButton.IsChecked)) { Source = page.SettingsOverlay.OverlaySetting });
        page.SettingsGeneral.SettingClicked += e => OnSettingChanged(page.SettingsGeneral, e);
        page.SettingsAppearance.SliderChanged += e => OnSliderChanged(page.SettingsAppearance, e);
        page.SettingsOverlay.SliderChanged += e => OnSliderChanged(page.SettingsOverlay, e);
        page.SettingsOverlay.DensityChanged += () =>
        {
            if (IsLoaded)
            {
                SettingsChanged?.Invoke();
            }
        };
        page.SettingsOverlay.OpenTrackerTabClicked += e => OnOpenTrackerTab(page.SettingsOverlay, e);
        page.SettingsOverlay.ArrangeOverlayClicked += e => OnArrangeOverlay(page.SettingsOverlay, e);
        page.SettingsTracker.OpenOverlayTabClicked += e => OnOpenOverlayTab(page.SettingsTracker, e);
        page.SettingsData.ReimportClicked += e => OnReimport(page.SettingsData, e);
        page.SettingsData.OpenDataClicked += e => OnOpenData(page.SettingsData, e);
        page.SettingsData.ExportSettingsClicked += OnExportSettings;
        page.SettingsData.ImportSettingsClicked += OnImportSettings;
        ShowSettingsTab(_viewModel.Settings.SettingsTab);
        return page;
    }

    /// <summary>The setup wizard over the page, built when it first opens (a new player, or "Run setup again").</summary>
    private Setup.SetupWizard SetupView
    {
        get
        {
            if (_setup is null)
            {
                _setup = new Setup.SetupWizard { Name = "Setup" };
                _setup.ArrangeRequested += () => ArrangeOverlayRequested?.Invoke();
                // In the window first: the binding then finds the view model at once.
                SlotSetup.Content = _setup;
                _setup.SetBinding(VisibilityProperty, new Binding(nameof(MainViewModel.SetupOpen)) { Converter = (IValueConverter)FindResource("Vis") });
            }

            return _setup;
        }
    }

    /// <summary>The Battlegrounds tabs in <see cref="MainViewModel.BattlegroundsTabs"/> order.</summary>
    private ToggleButton[] BattlegroundsTabButtons => [PageBattlegrounds.TabMine, PageBattlegrounds.TabHeroes, PageBattlegrounds.TabComps, PageBattlegrounds.TabTrinkets, PageBattlegrounds.TabCards];

    /// <summary>The Constructed tabs in <see cref="MainViewModel.ConstructedTabs"/> order.</summary>
    private ToggleButton[] ConstructedTabButtons => [PageConstructed.TabMyDecks, PageConstructed.TabMetaTiers, PageConstructed.TabMetaDecks, PageConstructed.TabMetaMatchups];

    /// <summary>
    /// A Battlegrounds tab was checked. The tier tabs' cards cross-fade in their cell (<c>BattlegroundsTiersView</c>);
    /// My stats and the tier lists are stacked, and the one that comes reveals itself (<c>Motion.RevealOnShow</c> on
    /// both in the page) while the other collapses.
    /// </summary>
    private void OnBgTab()
    {
        if (PageBattlegrounds.BgTiers is null)
        {
            return;
        }

        var mine = PageBattlegrounds.TabMine.IsChecked == true;
        // The view model follows the tab: the MMR slices and "Solo / Duos" belong to some tabs only. A tier tab's card
        // is built here, on its first opening; My stats builds none.
        _viewModel.BgTabShown(BattlegroundsTab);
        PageBattlegrounds.BgTiers.ShowTab(BattlegroundsTab);
        PageBattlegrounds.BracketPicker.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="chosen">A tab button was just checked (not unchecked): the view it shows comes in with a reveal.</param>
    private void OnCtTab(bool chosen)
    {
        if (PageConstructed.MyDecksPanel is null || PageConstructed.CtMeta is null)
        {
            return;
        }

        var meta = PageConstructed.TabMyDecks.IsChecked != true;
        var mineWasShown = PageConstructed.MyDecksPanel.Visibility == Visibility.Visible;
        PageConstructed.MyDecksPanel.Visibility = meta ? Visibility.Collapsed : Visibility.Visible;
        PageConstructed.CtStats.Visibility = PageConstructed.MyDecksPanel.Visibility;
        var tab = ConstructedTab;
        var shown = PageConstructed.CtMeta.ShowTab(tab == MainViewModel.ConstructedTabs[0] ? null : tab);
        if (!chosen)
        {
            return;
        }

        // My decks and the meta views are stacked: the side that comes reveals itself, the other collapsed at once.
        if (shown is not null)
        {
            Reveal(shown, Motion.TabRise);
        }
        else if (!mineWasShown)
        {
            Reveal(PageConstructed.MyDecksPanel, Motion.TabRise);
            Reveal(PageConstructed.CtStats, Motion.TabRise);
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

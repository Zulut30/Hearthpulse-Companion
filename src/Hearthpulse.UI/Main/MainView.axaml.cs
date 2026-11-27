using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hearthpulse.Core;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main;

/// <summary>The dashboard: navigation, the five pages and the floating live bar. Hosted by a window or a mobile shell.</summary>
public partial class MainView : UserControl
{
    private const double CompactWidth = 900;

    private MainViewModel? _viewModel;

    /// <summary>The page the sidebar went to last (<see cref="MainViewModel.NavPages"/>).</summary>
    private string _page;

    /// <summary>The page shown and the one leaving: a cross-fade, instant with motion off.</summary>
    private readonly PageSwitch _pages = new();

    public MainView()
    {
        InitializeComponent();
        PageOverview.ImportRequested += () => ReimportRequested?.Invoke();
        PageOverview.ArrangeRequested += () => ArrangeOverlayRequested?.Invoke();
        Setup.ArrangeRequested += () => ArrangeOverlayRequested?.Invoke();
        PageOverview.HistoryRequested += () => NavHistory.IsChecked = true;
        // The Tracker tab's "overlay is off" banner follows the Overlay tab's switch (two controls now, one binding).
        SettingsTracker.Bind(global::Hearthpulse.UI.Main.SettingsTabs.TrackerSettings.OverlayOnProperty,
            SettingsOverlay.OverlaySetting.GetObservable(ToggleButton.IsCheckedProperty, on => on == true));
        SettingsOverlay.ArrangeOverlayClicked += e => OnArrangeOverlay(SettingsOverlay, e);
        SettingsOverlay.DensityChanged += () => SettingsChanged?.Invoke();
        SettingsAccount.CopyAccountCodeClicked += e => OnCopyAccountCode(SettingsAccount, e);
        SettingsData.ReimportClicked += e => OnReimport(SettingsData, e);
        SettingsData.OpenDataClicked += e => OnOpenData(SettingsData, e);
        // A card row was clicked (Controls.CardLink): its card page opens.
        AddHandler(global::Hearthpulse.UI.Controls.CardLink.OpenEvent, OnCardLink);
        _page = CurrentPage;
        var pages = PageButtons;
        for (var i = 0; i < pages.Length; i++)
        {
            var (nav, page) = (pages[i], MainViewModel.NavPages[i]);
            // Avalonia raises this for the newly checked button while the old one is still checked: the page shown is
            // the sender's, and the old button's uncheck that follows only updates what is visible.
            nav.IsCheckedChanged += (_, _) =>
            {
                if (nav.IsChecked == true)
                {
                    _page = page;
                    UpdatePages(page);
                }
                else
                {
                    UpdatePages(null);
                }
            };
        }

        foreach (var tab in BattlegroundsTabButtons)
        {
            tab.IsCheckedChanged += (_, _) => UpdateTabs();
        }

        UpdateTabs();

        foreach (var tab in new[] { PageConstructed.TabMyDecks, PageConstructed.TabMetaTiers, PageConstructed.TabMetaDecks, PageConstructed.TabMetaMatchups })
        {
            tab.IsCheckedChanged += (_, _) => UpdateConstructedTabs();
        }

        foreach (var tab in SettingsTabs)
        {
            tab.IsCheckedChanged += (_, _) => UpdateSettingsTab();
        }

        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        UseShortcutTips();
    }

    /// <summary>
    /// The overlay's global shortcuts (Ctrl+Shift+H, Ctrl+Shift+L) are registered on Windows only: there the live bar's
    /// tips name them and Settings lists them; elsewhere the short tips stay and the rows are hidden.
    /// </summary>
    private void UseShortcutTips()
    {
        var global = Hearthpulse.UI.Overlay.OverlayView.GlobalShortcuts;
        SettingsGeneral.ShortcutHideRow.IsVisible = global;
        SettingsGeneral.ShortcutLayoutRow.IsVisible = global;
        if (global)
        {
            ArrangeButton.Bind(ToolTip.TipProperty, new LocExtension("Main.Live.ArrangeTip").ProvideValue(null!));
            OverlaySwitch.Bind(ToolTip.TipProperty, new LocExtension("Main.Live.OverlayTip").ProvideValue(null!));
        }
    }

    /// <summary>Raised after any setting changes from the UI.</summary>
    public event Action? SettingsChanged;

    public event Action? ReimportRequested;

    public event Action? ArrangeOverlayRequested;

    /// <summary>Opens the data folder; hosts can replace it (e.g. on mobile, where there is no file manager).</summary>
    public Action<string>? OpenFolder { get; set; } = path =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>The open page, as <see cref="ShowPage"/> takes it.</summary>
    public string CurrentPage => MainViewModel.NavChecked(MainViewModel.NavPages, States(PageButtons)) ?? MainViewModel.NavPages[0];

    /// <summary>The sidebar buttons in <see cref="MainViewModel.NavPages"/> order.</summary>
    private ToggleButton[] PageButtons => [NavOverview, NavBattlegrounds, NavStandard, NavWild, NavArena, NavBrawl, NavHistory, NavCollection, NavAchievements, NavWrapped, NavSettings];

    private static bool?[] States(ToggleButton[] buttons) => Array.ConvertAll(buttons, b => b.IsChecked);

    // Keys are taken from the window: with nothing focused yet they never reach the view itself.
    private TopLevel? _keyHost;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyHost = TopLevel.GetTopLevel(this);
        _keyHost?.AddHandler(KeyDownEvent, OnWindowKeyDown);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _keyHost?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _keyHost = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Ctrl+1…8 open the sidebar pages; Esc and Alt+Left leave a deck or match page; Ctrl+V on History imports a replay
    /// (not while typing), Ctrl+F goes to its search.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _viewModel is null)
        {
            return;
        }

        // The setup wizard covers the page: Esc closes it like Skip, and the page shortcuts (Ctrl+1..8) wait until it is closed.
        if (_viewModel.SetupOpen)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = _viewModel.CloseSetupOnEscape();
            }
            else if (e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
            }

            return;
        }

        var digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D0 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad0 : 0;
        if (e.KeyModifiers == KeyModifiers.Control && MainViewModel.NavPageForDigit(digit) is { } page)
        {
            ShowPage(page);
            // Focus follows the page: the control it was on may sit on the page just hidden.
            PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, page)].Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
        else if (((e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt))
                 && e.Source is not TextBox)
        {
            e.Handled = _viewModel.GoBack(CurrentPage);
        }
        else if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && CurrentPage == "History" && !_viewModel.MatchOpen && e.Source is not TextBox)
        {
            // Ctrl+V on History imports the file (or the path) on the clipboard into the library.
            PageHistory.LibraryView.PasteFromClipboardAsync().Forget("Replay paste");
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && CurrentPage == "History" && !_viewModel.MatchOpen)
        {
            // Ctrl+F on History: the library's search box.
            PageHistory.LibraryView.FocusSearch();
            e.Handled = true;
        }
    }

    /// <summary>The mouse's Back button leaves a deck or match page.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!e.Handled && _viewModel is not null && e.InitialPressMouseButton == MouseButton.XButton1)
        {
            e.Handled = _viewModel.GoBack(CurrentPage);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.HistoryRequested -= ShowHistoryGame;
            _viewModel.ThemeChanged -= OnThemeChanged;
            _viewModel.PanelToggled -= OnPanelToggled;
            ReleaseSettingsTools(_viewModel);
            StopMenuListeners();
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelChanged;
        ShowPageHero(null);
        _viewModel.HistoryRequested += ShowHistoryGame;
        _viewModel.ThemeChanged += OnThemeChanged;
        _viewModel.PanelToggled += OnPanelToggled;
        InitSettingsTools(_viewModel);
        BuildPanelsMenu(_viewModel);
        ApplyTheme();
        SyncOverlaySwitch();
        ShowSettingsTab(_viewModel.Settings.SettingsTab);
        foreach (var box in this.GetLogicalDescendantsOfType<CheckBox>())
        {
            box.IsCheckedChanged += (_, _) => OnSettingChanged(box);
        }

        foreach (var slider in this.GetLogicalDescendantsOfType<Slider>())
        {
            slider.ValueChanged += (_, _) => SettingsChanged?.Invoke();
        }

        // Segmented choices on the settings page ("Pick suggestions", "Game plan comp"): each radio's Tag names the value
        // it selects, e.g. "PickHints.StatsOnly". The value is applied here, before SettingsChanged, rather than through a
        // two-way binding: Avalonia writes a binding back only after IsCheckedChanged, and a radio group stops writing
        // back once it has unchecked an option. Only the newly checked option reports, so one click applies once.
        foreach (var radio in PageSettings.GetLogicalDescendantsOfType<RadioButton>())
        {
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && _viewModel is not null
                    && (ApplyChoice(_viewModel.Settings, radio.Tag as string) || ApplyStreamChoice(_viewModel, radio.Tag as string)))
                {
                    SettingsChanged?.Invoke();
                }
            };
        }
    }

    /// <summary>Opens a page by name ("Overview", "Battlegrounds", "Standard", "Wild", ...); the old "Constructed" opens Standard.</summary>
    public void ShowPage(string page) => PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, ConstructedPage.Migrate(page))].IsChecked = true;

    /// <summary>Shows the hover card of one point of the Battlegrounds rating chart (-1 hides it), for the render check.</summary>
    public void HoverBattlegroundsGame(int index) => PageBattlegrounds.BgStats.HoverGame(index);

    /// <summary>Shows the hover card of one point of the rank chart (-1 hides it), for the render check.</summary>
    public void HoverConstructedGame(int index) => PageConstructed.CtStats.HoverGame(index);

    /// <summary>Shows the hover card of one point of the deck page's win-rate chart (-1 hides it), for the render check.</summary>
    public void HoverDeckGame(int index) => DeckDetails.HoverGame(index);

    /// <summary>The deck page's Versions card (UI render check).</summary>
    public Control DeckVersionsCard => DeckDetails.Versions;

    /// <summary>Opens the ⋯ menu of the deck page's last version row (UI render check).</summary>
    public MenuFlyout? OpenDeckVersionMenu() => DeckDetails.OpenLastVersionMenu();

    private bool Compact => Classes.Contains("compact");

    private void ApplyWidth(double width)
    {
        var compact = width < CompactWidth;
        Classes.Set("compact", compact);
        UpdateTilePanels();
        Frame.ColumnDefinitions[0].Width = new GridLength(compact ? 76 : 250);
        PageConstructed.CtMeta.Stack(compact);
    }

    /// <summary>Stat tiles sit in one row on wide screens and two columns on narrow ones (grids classed "fixed" keep theirs).</summary>
    private void UpdateTilePanels()
    {
        foreach (var grid in this.GetVisualDescendants().OfType<UniformGrid>().Where(g => !g.Classes.Contains("fixed")))
        {
            grid.Rows = Compact ? 0 : 1;
            grid.Columns = Compact ? 2 : 0;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        ShowPageHero(e.PropertyName);
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.HearthstoneRunning):
            case nameof(MainViewModel.MemoryAttached):
                // One chip, as in WPF: gold while Hearthstone runs, green once its memory is read.
                StatusDot.Fill = ThemeBrushes.Get(_viewModel.MemoryAttached ? "Positive" : _viewModel.HearthstoneRunning ? "Accent" : "Faint");
                break;
            case nameof(MainViewModel.DeckOpen):
                // A deck opened from the Overview's deck banners goes to its format's page, where the deck page lives.
                if (_viewModel.DeckOpen && !ConstructedShown)
                {
                    ShowPage(ConstructedPage.PageOf(_viewModel.ConstructedFormat));
                }

                UpdatePages(_page);
                PageDeck.Offset = default;
                break;
            case nameof(MainViewModel.CardOpen):
                if (_viewModel.CardOpen && !ConstructedShown)
                {
                    ShowPage(ConstructedPage.PageOf(_viewModel.ConstructedFormat));
                }

                // Read before the page is hidden: a focus the player moved elsewhere (a sidebar button) is left alone.
                var cardHadFocus = FocusIsOnCardPage();
                // Back from a card leaves the page under it where the player was reading.
                UpdatePages(null);
                if (_viewModel.CardOpen)
                {
                    PageCard.Offset = default;
                    FocusCardPage();
                }
                else if (ConstructedShown && cardHadFocus)
                {
                    RestoreCardFocus();
                }
                else
                {
                    _cardOrigin.Take();
                }

                break;
            case nameof(MainViewModel.ConstructedFormat):
                // A deck opened on the other format's page moves the nav to its own (a Wild deck to Wild).
                if (ConstructedShown)
                {
                    ShowPage(ConstructedPage.PageOf(_viewModel.ConstructedFormat));
                }

                break;
            case nameof(MainViewModel.MatchOpen):
                // A game opened from the Overview's recent games goes to History, where its page lives.
                if (_viewModel.MatchOpen && NavHistory.IsChecked != true)
                {
                    NavHistory.IsChecked = true;
                }

                UpdatePages(_page);
                PageMatch.Offset = default;
                break;
        }
    }

    /// <summary>True while the Standard or Wild page (or a deck page standing in for it) is open.</summary>
    private bool ConstructedShown => NavStandard.IsChecked == true || NavWild.IsChecked == true;

    /// <summary>Tells the view model the page <paramref name="shown"/> (null: no page change) and shows the checked pages.</summary>
    private void UpdatePages(string? shown)
    {
        _viewModel?.PageShown(shown);

        // The old page fades out under the new one, which comes in once it has been laid out, as in the WPF app
        // (PageSwitch; instant with motion off).
        var page = PageToShow();
        _pages.Show(page, [PageOverview, PageBattlegrounds, PageConstructed, PageDeck, PageCard, PageArena, PageBrawl, PageHistory, PageMatch, PageCollection, PageAchievements, PageWrapped, PageSettings]);
        if (page == PageConstructed)
        {
            // The meta tabs' fixed part (filters, card frames) is built ahead of the first click on one of them.
            PageConstructed.CtMeta.Prebuild();
        }

        // Panels of a page that was hidden are created on its first layout.
        Dispatcher.UIThread.Post(UpdateTilePanels, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The page the sidebar item checked last and the open deck, card or match call for. By <see cref="_page"/>, not the
    /// buttons: Avalonia checks the new button while the old one is still checked.
    /// </summary>
    private Control PageToShow()
    {
        var constructed = _page is ConstructedPage.Standard or ConstructedPage.Wild;
        return _page switch
        {
            "Battlegrounds" => PageBattlegrounds,
            // The card page stands over the Standard or Wild page and the deck page: Back returns to the page it was
            // opened from. The deck page stands in for the Standard or Wild page while a deck is open (the nav stays).
            _ when constructed && _viewModel?.CardOpen == true => PageCard,
            _ when constructed && _viewModel?.DeckOpen == true => PageDeck,
            _ when constructed => PageConstructed,
            "Arena" => PageArena,
            "Brawl" => PageBrawl,
            // The match page stands in for the History list while a game is open.
            "History" => _viewModel?.MatchOpen == true ? PageMatch : PageHistory,
            "Collection" => PageCollection,
            "Achievements" => PageAchievements,
            "Wrapped" => PageWrapped,
            "Settings" => PageSettings,
            _ => PageOverview,
        };
    }

    /// <summary>Opens History on one game (from a deck page): scrolled to and marked as selected.</summary>
    private void ShowHistoryGame(string key) => ShowHistoryGameAsync(key).Forget("Show a game in History");

    private async Task ShowHistoryGameAsync(string key)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        viewModel.CloseMatch();
        NavHistory.IsChecked = true;
        if (viewModel.HistoryFilter != "All")
        {
            PageHistory.HistoryAll.IsChecked = true;
            // Ends once the "All" rows are there: at once from the games in memory, else after a reload.
            await viewModel.ShowAllGamesAsync();
        }

        SelectHistoryRow(viewModel, key);
    }

    private void SelectHistoryRow(MainViewModel viewModel, string key)
    {
        // The library view scrolls to the card it marks selected.
        Dispatcher.UIThread.Post(() => viewModel.SelectLibraryCard(key), DispatcherPriority.Loaded);
    }

    private void OnReimport(object? sender, RoutedEventArgs e) => ReimportRequested?.Invoke();

    private void OnArrangeOverlay(object? sender, RoutedEventArgs e) => ArrangeOverlayRequested?.Invoke();

    private void OnOpenData(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            OpenFolder?.Invoke(_viewModel.DataFolder);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Core.Diag.Error("Opening the data folder failed", ex);
        }
    }
}

internal static class LogicalTreeHelpers
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this StyledElement root)
    {
        foreach (var child in ((Avalonia.LogicalTree.ILogical)root).LogicalChildren)
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is StyledElement element)
            {
                foreach (var nested in element.GetLogicalDescendantsOfType<T>())
                {
                    yield return nested;
                }
            }
        }
    }
}

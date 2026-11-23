using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Stats;

namespace Hearthpulse.App.Main;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        AddHandler(Controls.CardLink.OpenEvent, new EventHandler<Controls.CardLinkEventArgs>(OnCardLink));
        PageOverview.ImportRequested += () => ReimportRequested?.Invoke();
        PageOverview.ArrangeRequested += () => ArrangeOverlayRequested?.Invoke();
        PageOverview.HistoryRequested += () => NavHistory.IsChecked = true;
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        ShowPageHero(null);
        viewModel.HistoryRequested += ShowHistoryGame;
        viewModel.PanelToggled += OnPanelToggled;
        InitSettingsTools(viewModel);
        // The Settings page is built on its first visit and opens on the tab shown last (AppSettings.SettingsTab); the
        // setup wizard when it first opens.
        if (viewModel.SetupOpen)
        {
            _ = SetupView;
        }

        SyncStatus();
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            MaxButton.Content = maximized ? "\U000F05B2" : "\U000F05AF";
            // A maximized chrome-less window overhangs the screen by the resize border.
            Frame.Margin = maximized ? new Thickness(7) : new Thickness(0);
        };
    }

    /// <summary>Raised after any setting changes from the UI.</summary>
    public event Action? SettingsChanged;

    public event Action? ReimportRequested;

    public event Action? ArrangeOverlayRequested;

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Opens a page by name (see <see cref="CurrentPage"/>); the old "Constructed" opens Standard.</summary>
    public void ShowPage(string page) => PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, ConstructedPage.Migrate(page))].IsChecked = true;

    /// <summary>The open page, as <see cref="ShowPage"/> takes it.</summary>
    public string CurrentPage => MainViewModel.NavChecked(MainViewModel.NavPages, States(PageButtons)) ?? MainViewModel.NavPages[0];

    /// <summary>The sidebar buttons in <see cref="MainViewModel.NavPages"/> order.</summary>
    private ToggleButton[] PageButtons => [NavOverview, NavBattlegrounds, NavStandard, NavWild, NavArena, NavBrawl, NavHistory, NavCollection, NavAchievements, NavWrapped, NavSettings];

    private static bool?[] States(ToggleButton[] buttons) => Array.ConvertAll(buttons, b => b.IsChecked);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkChrome(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Ctrl+1…8 open the sidebar pages; Esc and Alt+Left leave a deck or match page; Ctrl+V on History imports a replay
    /// (not while typing), Ctrl+F goes to its search.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // The setup wizard covers the page: Esc closes it like Skip, and the page shortcuts (Ctrl+1..8) wait until it is closed.
        if (_viewModel.SetupOpen)
        {
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = _viewModel.CloseSetupOnEscape();
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && MainViewModel.NavPageForDigit(key is >= Key.D1 and <= Key.D9 ? key - Key.D0 : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad0 : 0) is { } page)
        {
            ShowPage(page);
            // Focus follows the page: the control it was on may sit on the page just hidden.
            PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, page)].Focus();
            e.Handled = true;
        }
        else if (((key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) || (key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt))
                 && e.OriginalSource is not TextBox)
        {
            e.Handled = _viewModel.GoBack(CurrentPage);
        }
        else if (key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && CurrentPage == "History" && !_viewModel.MatchOpen && e.OriginalSource is not TextBox)
        {
            // Ctrl+V on History imports the file (or the path) on the clipboard into the library.
            PageHistory.LibraryView.PasteFromClipboard();
            e.Handled = true;
        }
        else if (key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && CurrentPage == "History" && !_viewModel.MatchOpen)
        {
            // Ctrl+F on History: the library's search box.
            PageHistory.LibraryView.FocusSearch();
            e.Handled = true;
        }
    }

    /// <summary>The mouse's Back button leaves a deck or match page.</summary>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!e.Handled && e.ChangedButton == MouseButton.XButton1)
        {
            e.Handled = _viewModel.GoBack(CurrentPage);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _viewModel.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.HistoryRequested -= ShowHistoryGame;
        _viewModel.PanelToggled -= OnPanelToggled;
        ReleaseSettingsTools();
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ShowPageHero(e.PropertyName);
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.HearthstoneRunning):
            case nameof(MainViewModel.MemoryAttached):
                SyncStatus();
                break;
            case nameof(MainViewModel.DeckOpen):
                if (ConstructedShown)
                {
                    OnNavigate(this, new RoutedEventArgs());
                }
                else if (_viewModel.DeckOpen)
                {
                    // A deck opened from the Overview's deck banners goes to its format's page, where the deck page lives.
                    ShowPage(ConstructedPage.PageOf(_viewModel.ConstructedFormat));
                }

                break;
            case nameof(MainViewModel.CardOpen):
                // Read before the page is hidden: a focus the player moved elsewhere (a sidebar button) is left alone.
                var cardHadFocus = FocusIsOnCardPage();
                if (ConstructedShown)
                {
                    // Back from a card leaves the page under it where the player was reading.
                    _cardClosing = !_viewModel.CardOpen;
                    OnNavigate(this, new RoutedEventArgs());
                    _cardClosing = false;
                }
                else if (_viewModel.CardOpen)
                {
                    ShowPage(ConstructedPage.PageOf(_viewModel.ConstructedFormat));
                }

                if (_viewModel.CardOpen)
                {
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
            case nameof(MainViewModel.SetupOpen) when _viewModel.SetupOpen:
                _ = SetupView;
                break;
            case nameof(MainViewModel.MatchOpen):
                // A game opened from the Overview's recent games goes to History, where its page lives.
                if (_viewModel.MatchOpen && NavHistory.IsChecked != true)
                {
                    NavHistory.IsChecked = true;
                }
                else if (NavHistory.IsChecked == true)
                {
                    OnNavigate(this, new RoutedEventArgs());
                }

                break;
        }
    }

    private void SyncStatus() =>
        StatusDot.Fill = (Brush)FindResource(_viewModel.MemoryAttached ? "Positive" : _viewModel.HearthstoneRunning ? "Accent" : "Faint");

    /// <summary>True while the Standard or Wild page (or a deck page standing in for it) is open.</summary>
    private bool ConstructedShown => NavStandard.IsChecked == true || NavWild.IsChecked == true;

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (PageOverview is null)
        {
            return;
        }

        _viewModel.PageShown(MainViewModel.NavChecked(MainViewModel.NavPages, States(PageButtons)));

        // The deck page stands in for the Standard or Wild page while a deck is open (the nav stays on its format).
        // The card page stands over both: Back returns to the page it was opened from.
        var cardOpen = ConstructedShown && _viewModel.CardOpen;
        if (ConstructedShown && _viewModel.DeckOpen)
        {
            _ = DeckDetailsPage;
        }

        if (cardOpen)
        {
            _ = CardDetailsPage;
        }

        var page = PageToShow(cardOpen);
        var wasShown = ReferenceEquals(page, _pages.Current);
        // The old page fades out under the new one, which comes in once it has been laid out (PageSwitch); the pill
        // glides with it, so a page that lays out long the first time eats neither motion.
        _pages.Show(page, new FrameworkElement?[] { PageOverview, _battlegrounds, _constructed, PageDeck, PageCard, PageArena, PageBrawl, _history, PageMatch, _collectionPage, _achievementsPage, _wrappedPage, PageSettings }
            .OfType<FrameworkElement>(), () => PlacePill(animate: true));
        if (page == PageDeck && !_cardClosing)
        {
            PageDeck.ScrollToTop();
        }
        else if (page == PageCard && !wasShown)
        {
            PageCard.ScrollToTop();
        }
        else if (page == PageMatch)
        {
            PageMatch.ScrollToTop();
        }
    }

    /// <summary>The page the checked sidebar item and the open deck, card or match call for (built on first use).</summary>
    private FrameworkElement PageToShow(bool cardOpen) =>
        NavBattlegrounds.IsChecked == true ? PageBattlegrounds
        : cardOpen ? PageCard
        : ConstructedShown ? _viewModel.DeckOpen ? PageDeck : PageConstructed
        : NavArena.IsChecked == true ? Filled(PageArena, "ArenaPageContent")
        : NavBrawl.IsChecked == true ? Filled(PageBrawl, "BrawlPageContent")
        // The match page stands in for the History list while a game is open.
        : NavHistory.IsChecked == true ? _viewModel.MatchOpen ? PageMatch : PageHistory
        : NavCollection.IsChecked == true ? PageCollection
        : NavAchievements.IsChecked == true ? PageAchievements
        : NavWrapped.IsChecked == true ? PageWrapped
        : NavSettings.IsChecked == true ? SettingsFrame
        : PageOverview;

    private void OnSidebarSize(object sender, SizeChangedEventArgs e) => PlacePill(animate: false);

    /// <summary>The pages scrolled under the status card or over the foot (a short window): the pill moves with them.</summary>
    private void OnNavScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0)
        {
            PlacePill(animate: false);
        }
    }

    /// <summary>
    /// Moves the selection pill behind the checked sidebar item. An item of the scrolling list keeps its pill inside
    /// the list's viewport, so a page scrolled half out of view never lights the status card or the foot.
    /// </summary>
    private void PlacePill(bool animate)
    {
        var nav = PageButtons.FirstOrDefault(n => n.IsChecked == true);
        if (nav is null || Sidebar is null || nav.ActualHeight <= 0)
        {
            return;
        }

        NavPillLayer.Clip = NavScroll.IsAncestorOf(nav)
            ? new RectangleGeometry(new Rect(NavScroll.TranslatePoint(default, Sidebar), new Size(Sidebar.ActualWidth, NavScroll.ViewportHeight)))
            : null;
        var y = nav.TranslatePoint(new Point(0, 2), Sidebar).Y;
        NavPill.Height = nav.ActualHeight - 4;
        // Glides over Motion.Settle with a quintic ease-out (no overshoot); the first placement is instant.
        Motion.Glide(NavPillShift, y, animate && NavPill.Opacity > 0);
        NavPill.Opacity = 1;
    }

    /// <summary>The page shown and the one leaving: a cross-fade, instant with motion off.</summary>
    private readonly PageSwitch _pages = new();

    /// <summary>A tab's view that opens fades in with a short rise once laid out (Motion.Reveal); instant with motion off.</summary>
    private static void Reveal(FrameworkElement page, double rise = Motion.Rise) => Motion.Reveal(page, rise);

    /// <summary>Where this window is: page, tabs and bounds.</summary>
    public MainPlace Place => new(
        CurrentPage,
        BattlegroundsTab,
        ConstructedTab,
        WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds,
        WindowState == WindowState.Maximized);

    /// <summary>
    /// Opens on the page, tabs and bounds of <paramref name="place"/>; before the window is shown (maximize after). The
    /// Settings tab, an open deck and an open match come back by themselves (settings and view model).
    /// </summary>
    public void MoveTo(MainPlace place)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = place.Bounds.Left;
        Top = place.Bounds.Top;
        Width = place.Bounds.Width;
        Height = place.Bounds.Height;
        ShowPage(place.Page);
        BattlegroundsTab = place.BattlegroundsTab;
        ConstructedTab = place.ConstructedTab;
    }

    /// <summary>Opens History on one game (from a deck page): scrolled to and marked as selected.</summary>
    private void ShowHistoryGame(string key) => ShowHistoryGameAsync(key).Forget("Show a game in History");

    private async Task ShowHistoryGameAsync(string key)
    {
        _viewModel.CloseMatch();
        NavHistory.IsChecked = true;
        if (_viewModel.HistoryFilter != "All")
        {
            PageHistory.HistoryAll.IsChecked = true;
            // Ends once the "All" rows are there: at once from the games in memory, else after a reload.
            await _viewModel.ShowAllGamesAsync();
        }

        SelectHistoryRow(key);
    }

    private void SelectHistoryRow(string key)
    {
        // The library view scrolls to the card it marks selected.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => _viewModel.SelectLibraryCard(key));
    }

    private void OnReimport(object sender, RoutedEventArgs e) => ReimportRequested?.Invoke();

    private void OnArrangeOverlay(object sender, RoutedEventArgs e) => ArrangeOverlayRequested?.Invoke();

    /// <summary>The overlay card's ⋯: every overlay panel with its state, opened beside the sidebar.</summary>
    private void OnPanelsMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Right;
            menu.IsOpen = true;
        }
    }

    private void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_viewModel.DataFolder}\"") { UseShellExecute = true })?.Dispose();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Where a main window is, so a rebuilt one (theme change, back from the tray) opens the same.</summary>
/// <param name="Bounds">The normal (not maximized) bounds.</param>
public sealed record MainPlace(string Page, string BattlegroundsTab, string ConstructedTab, Rect Bounds, bool Maximized);

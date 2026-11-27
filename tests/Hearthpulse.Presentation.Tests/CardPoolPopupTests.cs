using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Hearthpulse.App.Overlay;
using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// TASK-2026-11-26 AC4 in both compiled view models: a card in hand shows its pool at the panel's own spot only as the
/// hover mode says (with Ctrl held by default, always, or never) and goes a moment after nothing keeps it; the player's
/// own Discover shows its pool beside the options and it goes with the pick, or only its pick hint when the catalog
/// cannot answer the pool; "All cards" opens the browser, but not from a popup lying over the options; the panel's
/// content (P-1..P-14). The WPF panel is laid out in a hidden window, nothing on screen.
/// </summary>
public sealed class CardPoolPopupTests
{
    private static readonly DateTime T0 = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Without_ctrl_a_hand_cards_pool_does_not_show_in_both_view_models()
    {
        foreach (var vm in Models())
        {
            vm.Keys(false, T0);
            vm.Show(Pool("TEST_GEN", 40));
            Assert.False(vm.Panel, vm.Name);
            Assert.False(vm.Popup, vm.Name);
            vm.Show(null, List());
            Assert.False(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void With_ctrl_a_hand_cards_pool_shows_at_the_panels_own_spot_and_goes_150_ms_after_ctrl_is_let_go()
    {
        foreach (var vm in Models())
        {
            vm.Keys(true, T0);
            Assert.Equal([true], vm.ControlChanges);
            vm.Show(Pool("TEST_GEN", 40));
            Assert.True(vm.Panel, vm.Name);
            Assert.False(vm.Popup, vm.Name);

            // The pointer goes on along the hand with Ctrl held: another card's pool takes the same panel, a card with
            // nothing to show (or the way to the panel) keeps the last one.
            vm.Show(Pool("OTHER_GEN", 30));
            Assert.Equal("Generator OTHER_GEN", vm.Title);
            vm.Show(null);
            vm.Keys(true, T0 + Ms(2000));
            Assert.True(vm.Panel, vm.Name);
            Assert.Equal("Generator OTHER_GEN", vm.Title);

            // Ctrl let go with the pointer off the panel: gone 150 ms later, not before.
            vm.Keys(false, T0 + Ms(3000));
            Assert.Equal([true, false], vm.ControlChanges);
            vm.Keys(false, T0 + Ms(3100));
            Assert.True(vm.Panel, vm.Name);
            vm.Keys(false, T0 + Ms(3160));
            Assert.False(vm.Panel, vm.Name);

            // A pool the session still hands just after Ctrl came up is not shown again.
            vm.Show(Pool("TEST_GEN", 40));
            Assert.False(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void The_pointer_on_the_panel_keeps_it_without_ctrl_and_it_goes_after_the_pointer_leaves()
    {
        foreach (var vm in Models())
        {
            vm.Keys(true, T0);
            vm.Show(Pool("TEST_GEN", 40));
            vm.Pointer(true, T0 + Ms(500));
            Assert.True(vm.Held, vm.Name);
            vm.Keys(false, T0 + Ms(600));
            vm.Show(null);
            vm.Keys(false, T0 + Ms(5000));
            Assert.True(vm.Panel, vm.Name);

            vm.Pointer(false, T0 + Ms(5000));
            Assert.False(vm.Held, vm.Name);
            vm.Keys(false, T0 + Ms(5100));
            Assert.True(vm.Panel, vm.Name);
            vm.Keys(false, T0 + Ms(5160));
            Assert.False(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void Always_shows_without_ctrl_and_goes_300_ms_after_the_card_is_left_unless_ctrl_keeps_it()
    {
        foreach (var vm in Models(new AppSettings { CardPoolHover = CardPoolHover.Always }))
        {
            vm.Keys(false, T0);
            vm.Show(Pool("TEST_GEN", 40));
            Assert.True(vm.Panel, vm.Name);
            vm.Keys(false, T0 + Ms(100));

            // The card is left: the panel stays 300 ms (the way to it is long; Ctrl or the pointer on it keep it).
            vm.Show(null);
            vm.Keys(false, T0 + Ms(350));
            Assert.True(vm.Panel, vm.Name);
            vm.Keys(false, T0 + Ms(410));
            Assert.False(vm.Panel, vm.Name);

            vm.Show(Pool("TEST_GEN", 40));
            vm.Keys(true, T0 + Ms(1000));
            vm.Show(null);
            vm.Keys(true, T0 + Ms(3000));
            Assert.True(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void Off_never_shows_a_hand_cards_pool()
    {
        foreach (var vm in Models(new AppSettings { CardPoolHover = CardPoolHover.Off }))
        {
            vm.Keys(true, T0);
            vm.Show(Pool("TEST_GEN", 40));
            Assert.False(vm.Panel, vm.Name);
            vm.Show(null, List());
            Assert.False(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void A_discover_pool_stands_beside_the_options_and_goes_with_the_pick()
    {
        foreach (var vm in Models())
        {
            vm.Keys(true, T0);
            vm.Show(Pool("DISCOVER_GEN", 40) with { Trigger = PoolTrigger.Choice }, anchor: new CardPoolAnchor(PoolAnchorZone.Choice, 0, 3));
            Assert.True(vm.Popup, vm.Name);
            Assert.False(vm.Panel, vm.Name);

            // Not held by the pointer, nor kept by Ctrl: the pick ends it at once.
            vm.Pointer(true, T0 + Ms(500));
            Assert.False(vm.Held, vm.Name);
            vm.Show(null);
            Assert.False(vm.Popup, vm.Name);
            Assert.False(vm.Panel, vm.Name);
        }
    }

    [Fact]
    public void The_all_cards_button_opens_the_browser_and_a_small_pool_lists_every_card_instead()
    {
        var previous = Loc.Instance.Language;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);
                vm.Show(Pool("TEST_GEN", 40));
                Assert.Equal((true, "All cards · 40"), vm.AllCards);

                // The button: the browser opens on the shown pool and the compact panel hides while it is open.
                vm.OpenAll();
                Assert.True(vm.BrowserOpen, vm.Name);
                Assert.False(vm.Panel, vm.Name);
                Assert.Contains("40", vm.BrowserTitle, StringComparison.Ordinal);
                vm.Close();
                Assert.False(vm.BrowserOpen, vm.Name);
                // Ctrl still held: the panel is back.
                Assert.True(vm.Panel, vm.Name);

                // Twelve cards or fewer: every card, by cost then name, and no button.
                vm.Show(View("SMALL", [Card("S_C", "MINION", 3, 1, 1), Card("S_A", "SPELL", 1, null, null), Card("S_B", "SPELL", 1, null, null)], new Dictionary<string, int>()));
                Assert.Equal((false, "All cards · 3"), vm.AllCards);
                Assert.Equal("Every card in the pool", vm.RowsTitle);
                Assert.Equal([("S_A", ""), ("S_B", ""), ("S_C", "")], vm.Rows);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void The_panel_names_the_card_its_size_and_classes_and_lists_the_most_played_cards_without_shares()
    {
        var previous = Loc.Instance.Language;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var cards = Enumerable.Range(0, 20).Select(i => Card($"C{i:00}", "MINION", i % 9, 1, 1)).ToList();
            var meta = new Dictionary<string, int> { ["C05"] = 300, ["C02"] = 100 };
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);
                vm.Show(View("TEST_GEN", cards, meta));
                Assert.Equal(("Generator TEST_GEN", "20 cards · Mage + Neutral"), (vm.Title, vm.Note));
                Assert.Equal("Most played in meta decks", vm.RowsTitle);
                // In order of meta-deck games, and no share: the order says "most played".
                Assert.Equal([("C05", ""), ("C02", "")], vm.Rows);

                // Without meta data there is nothing to list; the button stays.
                vm.Show(View("PLAIN_GEN", cards, new Dictionary<string, int>()));
                Assert.Empty(vm.Rows);
                Assert.True(vm.AllCards.Shown, vm.Name);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            Loc.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void A_this_game_list_shows_on_its_own_or_above_the_pool_in_both_view_models()
    {
        var previous = Loc.Instance.Language;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            var list = List();
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);

                // No pool: the panel names the card, the section names the list.
                vm.Show(null, list);
                Assert.True(vm.Panel, vm.Name);
                Assert.Equal(("Undeath Sentence", ""), (vm.Title, vm.Note));
                Assert.Equal("Your minions that died · Deathrattle: 3", vm.ListTitle);
                Assert.Equal([("DR_1", "×2"), ("DR_2", "")], vm.ListRows);
                Assert.False(vm.AllCards.Shown, vm.Name);

                // With the card's own pool: the list sits above it under the same label.
                vm.Show(Pool("JAIL_940", 12), list);
                Assert.Equal(("Generator JAIL_940", "Your minions that died · Deathrattle: 3"), (vm.Title, vm.ListTitle));

                // Nothing died yet: an empty list says so.
                vm.Show(null, list with { Total = 0, Rows = [] });
                Assert.Empty(vm.ListRows);
                Assert.Equal("None this game yet", vm.ListNote);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void An_excavate_pool_shows_its_tier_and_the_next_tiers_rows_only_in_both_view_models()
    {
        var previous = Loc.Instance.Language;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            // Rock in a Death Knight's hand, one Excavate done: the next treasure is rare.
            var view = Excavate(new ExcavateState(2, 4));
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);
                vm.Show(view);
                Assert.True(vm.Panel, vm.Name);
                Assert.Equal("Excavate: tier 2 of 4", vm.Note);
                Assert.Equal("Next treasure · rare", vm.ExcavateNext);
                Assert.Equal(Hearthpulse.Core.CardPool.Excavate.TierTreasures(2, "DEATHKNIGHT").Select(t => t.Id).Order(), vm.ExcavateRows.Order());
                // The other tiers are the browser's sections; the panel lists no pool rows of its own.
                Assert.Empty(vm.Rows);
                Assert.Equal((true, $"All cards · {view.Count}"), vm.AllCards);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void An_open_browser_stays_open_until_the_player_closes_it_in_both_view_models()
    {
        var previous = Loc.Instance.Language;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);
                vm.Show(Pool("TEST_GEN", 40));
                vm.OpenAll();
                Assert.True(vm.BrowserOpen, vm.Name);

                // The wiki list comes for the same card: the browser shows the new count.
                vm.Show(Pool("TEST_GEN", 42));
                Assert.True(vm.BrowserOpen, vm.Name);
                Assert.Contains("42", vm.BrowserTitle, StringComparison.Ordinal);

                // Ctrl let go, another card with a pool or a list, a Discover of another card: the browser keeps its pool.
                vm.Keys(false, T0 + Ms(1000));
                vm.Show(null);
                vm.Keys(false, T0 + Ms(2000));
                Assert.True(vm.BrowserOpen, vm.Name);
                vm.Show(Pool("OTHER_GEN", 30));
                vm.Show(null, List());
                vm.Show(Pool("DISCOVER_GEN", 9) with { Trigger = PoolTrigger.Choice }, anchor: new CardPoolAnchor(PoolAnchorZone.Choice, 0, 3));
                Assert.True(vm.BrowserOpen, vm.Name);
                Assert.Contains("42", vm.BrowserTitle, StringComparison.Ordinal);
                Assert.False(vm.Popup, vm.Name);

                // Only the player closes it (×, Esc, the turn): then what the game shows now - the Discover's pool.
                vm.Close();
                Assert.False(vm.BrowserOpen, vm.Name);
                Assert.True(vm.Popup, vm.Name);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(previous);
        }
    }

    /// <summary>
    /// M1: a Discover whose pool the catalog cannot answer comes with its pick hint only: beside the options, the card's
    /// name over "What players take", no count, rows or button. Without a hint it shows nothing.
    /// </summary>
    [Fact]
    public void A_discover_the_catalog_cannot_answer_shows_only_its_pick_hint_in_both_view_models()
    {
        var map = Pool("TLC_464", 0) with { Trigger = PoolTrigger.Choice, Uncomputed = "the pool depends on the game state" };
        var picks = new DiscoverPicksView("TLC_464", "legend", [new DiscoverPickRow("OPT_A", "Wolf", 0.7, 0.55, false, true), new DiscoverPickRow("OPT_B", "Bear", 0.3, 0.5, false, false)]);
        var choice = new CardPoolAnchor(PoolAnchorZone.Choice, 0, 2);
        foreach (var vm in Models())
        {
            vm.Show(map, anchor: choice, picks: picks);
            Assert.True(vm.Popup, vm.Name);
            Assert.Equal(("Generator TLC_464", string.Empty), (vm.Title, vm.Note));
            Assert.Equal(["OPT_A", "OPT_B"], vm.Picks);
            Assert.Empty(vm.Rows);
            Assert.Equal(string.Empty, vm.RowsTitle);
            Assert.Equal((false, string.Empty), vm.AllCards);

            // The pick ends it.
            vm.Show(null);
            Assert.False(vm.Popup, vm.Name);
            Assert.Empty(vm.Picks);

            vm.Show(map, anchor: choice);
            Assert.False(vm.Popup, vm.Name);
            Assert.False(vm.Panel, vm.Name);
        }
    }

    /// <summary>
    /// m1: with no room beside the Discover options the popup lies over them (the panel says so, from PoolPlacement) and
    /// shows without the "All cards" button, which would take an option's click. The panel at its own spot keeps it.
    /// </summary>
    [Fact]
    public void A_popup_over_the_discover_options_goes_without_its_button_in_both_view_models()
    {
        foreach (var vm in Models())
        {
            vm.Keys(true, T0);
            vm.Show(Pool("DISCOVER_GEN", 40) with { Trigger = PoolTrigger.Choice }, anchor: new CardPoolAnchor(PoolAnchorZone.Choice, 0, 4));
            Assert.True(vm.AllCards.Shown, vm.Name);

            vm.Changes.Clear();
            vm.OverChoices(true);
            Assert.True(vm.Popup, vm.Name);
            Assert.False(vm.AllCards.Shown, vm.Name);
            Assert.Contains(nameof(OverlayViewModel.ShowCardPoolAllCards), vm.Changes);
            vm.OverChoices(false);
            Assert.True(vm.AllCards.Shown, vm.Name);

            // The panel at its own spot: only the anchor's change can say the button is back (both pools are large).
            vm.OverChoices(true);
            vm.Changes.Clear();
            vm.Show(Pool("TEST_GEN", 40));
            Assert.True(vm.Panel, vm.Name);
            Assert.True(vm.AllCards.Shown, vm.Name);
            Assert.Contains(nameof(OverlayViewModel.ShowCardPoolAllCards), vm.Changes);
        }
    }

    /// <summary>Q2: twelve cards are listed whole with no button; thirteen get the button and the most played cards.</summary>
    [Fact]
    public void Twelve_cards_are_listed_whole_and_thirteen_get_the_all_cards_button_in_both_view_models()
    {
        var previous = Loc.Instance.Language;
        try
        {
            Loc.Instance.SetLanguage(Loc.English);
            var thirteen = Enumerable.Range(0, 13).Select(i => Card($"T_{i:00}", "SPELL", i % 9, null, null)).ToList();
            var meta = thirteen.Select((c, i) => (c.Id, Games: 100 - i)).ToDictionary(p => p.Id, p => p.Games, StringComparer.Ordinal);
            foreach (var vm in Models())
            {
                vm.Keys(true, T0);
                vm.Show(Pool("TWELVE", 12));
                Assert.Equal((false, "Every card in the pool", 12), (vm.AllCards.Shown, vm.RowsTitle, vm.Rows.Count));

                vm.Show(View("THIRTEEN", thirteen, meta));
                Assert.Equal((true, "All cards · 13"), vm.AllCards);
                Assert.Equal("Most played in meta decks", vm.RowsTitle);
                Assert.Equal(["T_00", "T_01", "T_02", "T_03"], vm.Rows.Select(r => r.Item1));
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(previous);
        }
    }

    /// <summary>Q2: a new turn closes the browser, and without Ctrl the hand card's pool does not come back.</summary>
    [Fact]
    public void A_new_turn_closes_the_browser_and_without_ctrl_the_panel_stays_away_in_both_view_models()
    {
        foreach (var vm in Models())
        {
            vm.Keys(true, T0);
            vm.Show(Pool("TEST_GEN", 40));
            vm.OpenAll();
            vm.Keys(false, T0 + Ms(1000));
            vm.Show(null);
            Assert.True(vm.BrowserOpen, vm.Name);

            vm.Turn = 6;
            vm.Show(null);
            Assert.False(vm.BrowserOpen, vm.Name);
            Assert.False(vm.Panel, vm.Name);
            vm.Keys(false, T0 + Ms(3000));
            Assert.False(vm.Panel, vm.Name);

            // The session still hands the card's pool: without Ctrl it stays away.
            vm.Show(Pool("TEST_GEN", 40));
            Assert.False(vm.Panel, vm.Name);
            Assert.False(vm.BrowserOpen, vm.Name);
        }
    }

    [Fact]
    public void The_wpf_panel_lays_out_a_this_game_list() => WpfThread.RunThemed(() =>
    {
        var (vm, panel, source) = WpfPanel();
        using (source)
        {
            vm.UpdateCardPoolKeys(true, T0);
            vm.Update(Snapshot(null, List()), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();

            Assert.True(panel.IsVisible);
            // Header, the section, two rows, the bottom space: a short panel.
            Assert.InRange(panel.ActualHeight, 80, 180);
        }
    });

    [Fact]
    public void The_wpf_panel_lays_out_an_excavate_pool() => WpfThread.RunThemed(() =>
    {
        var (vm, panel, source) = WpfPanel();
        using (source)
        {
            vm.UpdateCardPoolKeys(true, T0);
            vm.Update(Snapshot(Excavate(new ExcavateState(1, 4))), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();

            Assert.True(panel.IsVisible);
            // Header, six rows of the next tier and the button: no taller than a pool with its top cards.
            Assert.InRange(panel.ActualHeight, 200, 330);
        }
    });

    [Fact]
    public void The_wpf_all_cards_button_is_the_panels_one_interactive_part_and_opens_the_browser() => WpfThread.RunThemed(() =>
    {
        var (vm, panel, source) = WpfPanel();
        using (source)
        {
            vm.UpdateCardPoolKeys(true, T0);
            vm.Update(Snapshot(Pool("TEST_GEN", 40)), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();

            var button = (Button)panel.FindName("AllCards");
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.True(OverlayHit.GetInteractive(button));
            Assert.Same(panel.FindResource("LockButton"), button.Style);
            // Nothing else of the panel takes the mouse: clicks reach the game.
            Assert.Equal(new DependencyObject[] { button }, Descendants(panel).Where(OverlayHit.GetInteractive));

            button.Command.Execute(button.CommandParameter);
            Assert.True(vm.CardPoolBrowserOpen);
            Assert.False(panel.IsVisible);
        }
    });

    /// <summary>
    /// Q5 (m1) in the WPF overlay: the Discover popup placed over four options on 4:3 tells the view model and shows
    /// without its button; then the panel at its own spot shows the button again. Three options on 16:9 leave room.
    /// </summary>
    [Fact]
    public void The_wpf_popup_over_the_options_goes_without_its_button_and_the_panel_at_its_own_spot_shows_it_again() => WpfThread.RunThemed(() =>
    {
        var discover = Pool("DISCOVER_GEN", 40) with { Trigger = PoolTrigger.Choice };
        var (vm, popup, panel, source) = WpfSurface(1440);
        using (source)
        {
            vm.UpdateCardPoolKeys(true, T0);
            vm.Update(Snapshot(discover, anchor: new CardPoolAnchor(PoolAnchorZone.Choice, 0, 4)), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();
            Assert.True(popup.IsVisible);
            Assert.Equal(Visibility.Collapsed, ((Button)popup.FindName("AllCards")).Visibility);

            vm.Update(Snapshot(Pool("TEST_GEN", 40)), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();
            Assert.True(panel.IsVisible);
            Assert.Equal(Visibility.Visible, ((Button)panel.FindName("AllCards")).Visibility);
        }

        (vm, popup, _, source) = WpfSurface(1920);
        using (source)
        {
            vm.Update(Snapshot(discover, anchor: new CardPoolAnchor(PoolAnchorZone.Choice, 0, 3)), CardDb.Empty, new AppSettings(), null);
            ((FrameworkElement)source.RootVisual).UpdateLayout();
            Assert.True(popup.IsVisible);
            Assert.Equal(Visibility.Visible, ((Button)popup.FindName("AllCards")).Visibility);
        }
    });

    /// <summary>The Discover popup and the panel at its own spot on a surface of <paramref name="width"/>, as the overlay holds them.</summary>
    private static (OverlayViewModel Vm, CardPoolPanel Popup, CardPoolPanel Panel, HwndSource Source) WpfSurface(double width)
    {
        var vm = new OverlayViewModel();
        var popup = new CardPoolPanel { DataContext = vm, Anchored = true };
        popup.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(OverlayViewModel.ShowCardPoolPopup)) { Converter = new BooleanToVisibilityConverter() });
        var panel = new CardPoolPanel { DataContext = vm };
        panel.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(OverlayViewModel.ShowCardPoolPanel)) { Converter = new BooleanToVisibilityConverter() });
        var root = new Canvas { Width = width, Height = 1080 };
        root.Children.Add(popup);
        root.Children.Add(panel);
        return (vm, popup, panel, new HwndSource(new HwndSourceParameters("pool popup test", (int)width, 1080) { WindowStyle = 0 }) { RootVisual = root });
    }

    private static (OverlayViewModel Vm, CardPoolPanel Panel, HwndSource Source) WpfPanel()
    {
        var vm = new OverlayViewModel();
        // The movable panel's content, shown as the "card-pool" panel shows it.
        var panel = new CardPoolPanel { DataContext = vm };
        panel.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(OverlayViewModel.ShowCardPoolPanel)) { Converter = new BooleanToVisibilityConverter() });
        var root = new Canvas { Width = 1920, Height = 1080 };
        root.Children.Add(panel);
        // A window that is never shown: the panel is in a live visual tree (IsVisible works).
        return (vm, panel, new HwndSource(new HwndSourceParameters("pool panel test", 1920, 1080) { WindowStyle = 0 }) { RootVisual = root });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }

    private static CardPoolView Excavate(ExcavateState? state) =>
        new CardPoolEngine(CardPoolCatalog.Empty, CardDb.Empty)
            .Build("WW_001t", new PoolContext("FT_WILD", "DEATHKNIGHT", "WARRIOR") { Excavate = state }, PoolTrigger.Hover)!;

    private static MatchListView List() =>
        new("JAIL_940", "Undeath Sentence", "Your minions that died · Deathrattle", 3,
            [new MatchListRow("DR_1", "Loot Hoarder", 2, "COMMON", 2), new MatchListRow("DR_2", "Abomination", 5, "RARE", 1)]);

    /// <summary>The same calls on the WPF and the Avalonia build of the shared view model.</summary>
    private sealed class Model
    {
        public required string Name { get; init; }
        public required Action<CardPoolView?, MatchListView?, CardPoolAnchor?, DiscoverPicksView?, int> ShowWith { get; init; }
        public required Action<bool, DateTime> Keys { get; init; }
        public required Action<bool, DateTime> Pointer { get; init; }
        public required Func<bool> HeldNow { get; init; }
        public required Func<bool> PanelNow { get; init; }
        public required Func<bool> PopupNow { get; init; }
        public required Func<bool> BrowserNow { get; init; }
        public required Func<string> BrowserTitleNow { get; init; }
        public required Action OpenBrowser { get; init; }
        public required Action CloseBrowser { get; init; }
        public required Func<(string Title, string Note)> Header { get; init; }
        public required Func<IReadOnlyList<(string, string)>> RowsNow { get; init; }
        public required Func<string> RowsTitleNow { get; init; }
        public required Func<(bool Shown, string Text)> AllCardsNow { get; init; }
        public required Func<string> ListTitleNow { get; init; }
        public required Func<IReadOnlyList<(string, string)>> ListRowsNow { get; init; }
        public required Func<string> ListNoteNow { get; init; }
        public required Func<(string Next, IReadOnlyList<string> Rows)> ExcavateNow { get; init; }
        public required Func<IReadOnlyList<string>> PicksNow { get; init; }
        public required Action<bool> OverChoices { get; init; }
        public List<bool> ControlChanges { get; } = [];
        public List<string> Changes { get; } = [];

        /// <summary>The game's turn of the next <see cref="Show"/>.</summary>
        public int Turn { get; set; } = 5;

        public void Show(CardPoolView? pool, MatchListView? list = null, CardPoolAnchor? anchor = null, DiscoverPicksView? picks = null) =>
            ShowWith(pool, list, anchor, picks, Turn);

        public bool Held => HeldNow();
        public bool Panel => PanelNow();
        public bool Popup => PopupNow();
        public bool BrowserOpen => BrowserNow();
        public string BrowserTitle => BrowserTitleNow();
        public string Title => Header().Title;
        public string Note => Header().Note;
        public IReadOnlyList<(string, string)> Rows => RowsNow();
        public string RowsTitle => RowsTitleNow();
        public (bool Shown, string Text) AllCards => AllCardsNow();
        public string ListTitle => ListTitleNow();
        public IReadOnlyList<(string, string)> ListRows => ListRowsNow();
        public string ListNote => ListNoteNow();
        public string ExcavateNext => ExcavateNow().Next;
        public IReadOnlyList<string> ExcavateRows => ExcavateNow().Rows;
        public IReadOnlyList<string> Picks => PicksNow();

        public void OpenAll() => OpenBrowser();

        public void Close() => CloseBrowser();
    }

    private static IEnumerable<Model> Models(AppSettings? settings = null)
    {
        var wpfSettings = settings ?? new AppSettings();
        var wpf = new OverlayViewModel();
        var a = new Model
        {
            Name = "WPF",
            ShowWith = (pool, list, anchor, picks, turn) => wpf.Update(Snapshot(pool, list, anchor, picks, turn), CardDb.Empty, wpfSettings, null),
            Keys = wpf.UpdateCardPoolKeys,
            Pointer = wpf.UpdateCardPoolPointer,
            HeldNow = () => wpf.CardPoolHeld,
            PanelNow = () => wpf.ShowCardPoolPanel,
            PopupNow = () => wpf.ShowCardPoolPopup,
            BrowserNow = () => wpf.CardPoolBrowserOpen,
            BrowserTitleNow = () => wpf.CardPoolBrowserTitle,
            OpenBrowser = () => wpf.OpenCardPoolBrowser.Execute(null),
            CloseBrowser = () => wpf.CloseCardPoolBrowser.Execute(null),
            Header = () => (wpf.CardPoolTitle, wpf.CardPoolNote),
            RowsNow = () => wpf.CardPoolRows.Select(r => (r.CardId, r.CountText)).ToList(),
            RowsTitleNow = () => wpf.CardPoolRowsTitle,
            AllCardsNow = () => (wpf.ShowCardPoolAllCards, wpf.CardPoolAllCardsText),
            ListTitleNow = () => wpf.CardPoolListTitle,
            ListRowsNow = () => wpf.CardPoolList.Select(r => (r.CardId, r.CountText)).ToList(),
            ListNoteNow = () => wpf.CardPoolListNote,
            ExcavateNow = () => (wpf.CardPoolExcavateNext, wpf.CardPoolExcavateRows.Select(r => r.CardId).ToList()),
            PicksNow = () => wpf.CardPoolPicks.Select(r => r.CardId).ToList(),
            OverChoices = wpf.SetCardPoolPopupOverChoices,
        };
        wpf.CardPoolControlChanged += a.ControlChanges.Add;
        wpf.PropertyChanged += (_, e) => a.Changes.Add(e.PropertyName ?? string.Empty);
        yield return a;

        var avaSettings = settings ?? new AppSettings();
        var ava = new AvaOverlay();
        var b = new Model
        {
            Name = "Avalonia",
            ShowWith = (pool, list, anchor, picks, turn) => ava.Update(Snapshot(pool, list, anchor, picks, turn), CardDb.Empty, avaSettings, null),
            Keys = ava.UpdateCardPoolKeys,
            Pointer = ava.UpdateCardPoolPointer,
            HeldNow = () => ava.CardPoolHeld,
            PanelNow = () => ava.ShowCardPoolPanel,
            PopupNow = () => ava.ShowCardPoolPopup,
            BrowserNow = () => ava.CardPoolBrowserOpen,
            BrowserTitleNow = () => ava.CardPoolBrowserTitle,
            OpenBrowser = () => ava.OpenCardPoolBrowser.Execute(null),
            CloseBrowser = () => ava.CloseCardPoolBrowser.Execute(null),
            Header = () => (ava.CardPoolTitle, ava.CardPoolNote),
            RowsNow = () => ava.CardPoolRows.Select(r => (r.CardId, r.CountText)).ToList(),
            RowsTitleNow = () => ava.CardPoolRowsTitle,
            AllCardsNow = () => (ava.ShowCardPoolAllCards, ava.CardPoolAllCardsText),
            ListTitleNow = () => ava.CardPoolListTitle,
            ListRowsNow = () => ava.CardPoolList.Select(r => (r.CardId, r.CountText)).ToList(),
            ListNoteNow = () => ava.CardPoolListNote,
            ExcavateNow = () => (ava.CardPoolExcavateNext, ava.CardPoolExcavateRows.Select(r => r.CardId).ToList()),
            PicksNow = () => ava.CardPoolPicks.Select(r => r.CardId).ToList(),
            OverChoices = ava.SetCardPoolPopupOverChoices,
        };
        ava.CardPoolControlChanged += b.ControlChanges.Add;
        ava.PropertyChanged += (_, e) => b.Changes.Add(e.PropertyName ?? string.Empty);
        yield return b;
    }

    private static LiveSnapshot Snapshot(CardPoolView? pool, MatchListView? list = null, CardPoolAnchor? anchor = null, DiscoverPicksView? picks = null, int turn = 5)
    {
        var constructed = new ConstructedView(turn, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null)
        {
            CardPool = pool,
            MatchList = list,
            CardPoolAnchor = anchor,
            DiscoverPicks = picks,
        };
        return LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
    }

    private static PoolCard Card(string id, string type, int cost, int? attack, int? health) =>
        new(id, 0, $"Card {id} with a long name", string.Empty, type, "COMMON", cost, attack, health, ["MAGE"], [], null, [], [], "CORE", true);

    private static CardPoolView Pool(string id, int count) =>
        View(id, Enumerable.Range(0, count).Select(i => i % 3 == 0 ? Card($"{id}_{i}", "SPELL", i % 9, null, null) : Card($"{id}_{i}", "MINION", i % 9, i % 7, 1 + (i % 8))).ToList(), new Dictionary<string, int>());

    private static CardPoolView View(string id, IReadOnlyList<PoolCard> cards, IReadOnlyDictionary<string, int> meta)
    {
        var (cost, attack, health) = PoolStats.Histograms(cards);
        var entries = cards.Select(c => new PoolEntry(c.Id, c.Name, c.Cost, c.Attack, c.Health, c.Type, c.Rarity, "MAGE", meta.GetValueOrDefault(c.Id))).ToList();
        return new CardPoolView(id, "Generator " + id, PoolTrigger.Hover, "Discover a minion", "Mage + Neutral", cards.Count, cost, attack, health, [], null, null, entries);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC4 in Avalonia: the pool panel names the card and its "All cards" button is the one part that takes
/// the mouse, but not on a Discover popup lying over the options; Settings → Tracker has the hover choice and the
/// Discover switch under "Card pool", off with it.
/// </summary>
public sealed partial class LocBindingTests
{
    [Fact]
    public void The_pool_panel_names_the_card_and_only_its_all_cards_button_takes_the_mouse() => Headless.Run(PoolPanelButton);

    private static void PoolPanelButton()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var vm = new OverlayViewModel();
        vm.UpdateCardPoolKeys(true);
        vm.Update(PoolSnapshot(40), Core.Cards.CardDb.Empty, new AppSettings(), null, null);
        // The theme first: the panel's markup looks its tokens up when it is created.
        var theme = Theme();
        var panel = new CardPoolPanel { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = panel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowCardPoolPanel);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Generator" && t.Classes.Contains("headerTitle"));
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "40 cards · Mage + Neutral" && t.Classes.Contains("headerNote"));

            var button = panel.FindControl<Button>("AllCards")!;
            Assert.True(button.IsVisible);
            Assert.True(OverlayHit.GetInteractive(button));
            Assert.Equal(new Visual[] { button }, panel.GetVisualDescendants().Where(OverlayHit.GetInteractive));
            Assert.Equal("All cards · 40", button.GetVisualDescendants().OfType<TextBlock>().Single().Text);

            button.Command!.Execute(button.CommandParameter);
            Assert.True(vm.CardPoolBrowserOpen);
            vm.CloseCardPoolBrowser.Execute(null);

            // A pool small enough to list whole has no button.
            vm.Update(PoolSnapshot(6), Core.Cards.CardDb.Empty, new AppSettings(), null, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Q5 (m1): the Discover popup placed over four options on a 4:3 surface tells the view model and shows without its
    /// button; a 16:9 surface gives it back; then the panel at its own spot shows the button again.
    /// </summary>
    [Fact]
    public void The_discover_popup_over_the_options_goes_without_its_button_and_the_panel_at_its_own_spot_shows_it() => Headless.Run(PopupOverOptions);

    private static void PopupOverOptions()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var theme = Theme();
        var vm = new OverlayViewModel();
        var popup = new CardPoolPanel { DataContext = vm, Anchored = true, IsVisible = false };
        var panel = new CardPoolPanel { DataContext = vm, IsVisible = false };
        var surface = new Canvas { Width = 1440, Height = 1080, Children = { popup, panel } };
        var window = new Window { Width = 1920, Height = 1080, Content = surface };

        // OverlayView binds the two panels' IsVisible to these; here they follow by hand.
        void Show(Core.Tracking.LiveSnapshot snapshot)
        {
            vm.Update(snapshot, Core.Cards.CardDb.Empty, new AppSettings(), null, null);
            popup.IsVisible = vm.ShowCardPoolPopup;
            panel.IsVisible = vm.ShowCardPoolPanel;
            Dispatcher.UIThread.RunJobs();
        }

        try
        {
            window.Show();
            vm.UpdateCardPoolKeys(true);
            Show(PoolSnapshot(40, new CardPoolAnchor(PoolAnchorZone.Choice, 0, 4)));
            Assert.True(popup.IsVisible);
            Assert.False(popup.FindControl<Button>("AllCards")!.IsVisible);

            surface.Width = 1920;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.FindControl<Button>("AllCards")!.IsVisible);
            surface.Width = 1440;
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.FindControl<Button>("AllCards")!.IsVisible);

            Show(PoolSnapshot(40));
            Assert.True(panel.IsVisible);
            Assert.True(panel.FindControl<Button>("AllCards")!.IsVisible);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>A hand card's pool of <paramref name="count"/> cards, or a Discover's beside its <paramref name="anchor"/>.</summary>
    private static Core.Tracking.LiveSnapshot PoolSnapshot(int count, CardPoolAnchor? anchor = null)
    {
        var entries = Enumerable.Range(0, count).Select(i => new PoolEntry($"P_{i}", $"Card {i}", i % 9, 1, 1, "MINION", "COMMON", "MAGE", 0)).ToList();
        var (cost, attack, health) = PoolStats.Histograms([]);
        var trigger = anchor is null ? PoolTrigger.Hover : PoolTrigger.Choice;
        var pool = new CardPoolView("GEN", "Generator", trigger, "Discover a minion", "Mage + Neutral", entries.Count, cost, attack, health, [], null, null, entries);
        var constructed = new Core.Tracking.ConstructedView(5, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null) { CardPool = pool, CardPoolAnchor = anchor };
        return Core.Tracking.LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
    }

    [Fact]
    public void Card_pool_settings_set_the_hover_mode_and_turn_off_with_the_pool() => Headless.Run(CardPoolSettingsRows);

    private static void CardPoolSettingsRows()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var app = Application.Current!;
        var theme = Theme();
        var window = new Window { Width = 1260, Height = 840 };
        var settings = new AppSettings();
        var model = new MainViewModel(new HistoryStore(Path.Combine(Path.GetTempPath(), $"hp-pool-settings-{Guid.NewGuid():N}.db")), settings, TestDataFolder.App);
        try
        {
            var view = new MainView { DataContext = model };
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            view.ShowSettingsTab("Tracker");
            Dispatcher.UIThread.RunJobs();

            var pool = Named<CheckBox>(view, "CardPoolSetting");
            var row = Named<Grid>(view, "CardPoolHoverRow");
            var discover = Named<CheckBox>(view, "CardPoolDiscoverSetting");
            Assert.Equal(Loc.T("Settings.Tracker.CardPoolTip"), ToolTip.GetTip(pool));
            Assert.DoesNotContain("right-click", Loc.T("Settings.Tracker.CardPoolTip"), StringComparison.OrdinalIgnoreCase);
            Assert.True(Named<RadioButton>(view, "CardPoolHoverCtrlOption").IsChecked);
            Assert.True(discover.IsChecked);
            Assert.True(row.IsEnabled);
            Assert.True(discover.IsEnabled);
            // The card under the mouse comes from game memory (Windows): elsewhere the hover row is not shown.
            Assert.Equal(Core.Memory.HearthstoneMemory.IsSupported, Named<StackPanel>(view, "CardPoolHoverBlock").IsVisible);

            Named<RadioButton>(view, "CardPoolHoverAlwaysOption").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(CardPoolHover.Always, settings.CardPoolHover);
            Named<RadioButton>(view, "CardPoolHoverOffOption").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(CardPoolHover.Off, settings.CardPoolHover);
            discover.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.CardPoolOnDiscover);

            // The pool off: both rows are off with it (and the choice row dims).
            pool.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.ShowCardPool);
            Assert.False(row.IsEnabled);
            Assert.False(discover.IsEnabled);

            // Reading the game client off (Settings → Data, P5): the hover row is off and says so; the Discover switch
            // stays, a Discover comes from the logs.
            pool.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            var note = Named<TextBlock>(view, "CardPoolHoverOffNote");
            Assert.True(row.IsEffectivelyEnabled);
            Assert.False(note.IsVisible);
            model.ReadGameMemory = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsEffectivelyEnabled);
            Assert.True(note.IsVisible);
            Assert.Equal(Loc.T("Settings.Tracker.CardPoolHoverOff"), note.Text);
            Assert.True(discover.IsEffectivelyEnabled);
            model.ReadGameMemory = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsEffectivelyEnabled);
            Assert.False(note.IsVisible);
        }
        finally
        {
            window.Close();
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }
}

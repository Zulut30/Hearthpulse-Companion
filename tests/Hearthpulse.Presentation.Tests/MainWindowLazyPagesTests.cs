using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF main window builds the Arena, Tavern Brawl and Settings pages and the setup wizard on first use, not with the
/// window (TASK-2026-11-23-perf-arch-round2, AC3): a visit, a tab or the wizard opening builds them, the Settings page on
/// the tab shown last, its names still found through the window.
/// </summary>
public sealed class MainWindowLazyPagesTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void Arena_brawl_and_settings_are_built_on_their_first_visit() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings { SettingsTab = "Data" }, TestApp.Data);
        var window = new WpfWindow(model);
        ScrollViewer Page(string name) => (ScrollViewer)window.FindName(name);

        Assert.Null(Page("PageArena").Content);
        Assert.Null(Page("PageBrawl").Content);
        Assert.Null(Page("PageSettings").Content);
        Assert.Null(window.FindName("SettingsGeneral"));
        // The live bar's overlay switch keeps the settings page's switches in step only once it exists.
        window.SyncOverlaySwitch();
        Assert.Null(Page("PageSettings").Content);

        window.ShowPage("Arena");
        Assert.Single(((StackPanel)Page("PageArena").Content).Children.OfType<Hearthpulse.App.Main.Stats.ArenaStatsView>());
        Assert.Null(Page("PageBrawl").Content);

        window.ShowPage("Brawl");
        Assert.Single(((StackPanel)Page("PageBrawl").Content).Children.OfType<Hearthpulse.App.Main.Stats.BrawlView>());
        Assert.Null(Page("PageSettings").Content);

        window.ShowPage("Settings");
        Assert.NotNull(Page("PageSettings").Content);
        // Opened on the tab shown last; the tabs are found through the window as when they were part of its XAML.
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("SettingsData")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SettingsGeneral")).Visibility);
        Assert.Same(window.SettingsTracker, window.FindName("SettingsTracker"));
        Assert.Equal("Data", model.Settings.SettingsTab);
        window.Close();
    });

    [Fact]
    public void A_settings_tab_asked_for_before_a_visit_builds_the_page_on_that_tab() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var window = new WpfWindow(model);
        window.ShowSettingsTab("Tracker");
        Assert.Equal(Visibility.Visible, window.SettingsTracker.Visibility);
        Assert.Equal(Visibility.Collapsed, window.SettingsGeneral.Visibility);
        Assert.Equal("Tracker", model.Settings.SettingsTab);
        window.ShowPage("Settings");
        Assert.Equal(Visibility.Visible, window.SettingsTracker.Visibility);
        window.Close();
    });

    [Fact]
    public void The_setup_wizard_is_built_when_it_first_opens() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        if (model.SetupOpen)
        {
            model.CloseSetup.Execute(null);
        }

        var window = new WpfWindow(model);
        var slot = (ContentControl)window.FindName("SlotSetup");
        Assert.Null(slot.Content);

        model.OpenSetup.Execute(null);
        var wizard = Assert.IsType<Hearthpulse.App.Main.Setup.SetupWizard>(slot.Content);
        Assert.Equal(Visibility.Visible, wizard.Visibility);
        model.CloseSetup.Execute(null);
        Assert.Equal(Visibility.Collapsed, wizard.Visibility);
        model.OpenSetup.Execute(null);
        Assert.Same(wizard, slot.Content);
        Assert.Equal(Visibility.Visible, wizard.Visibility);
        window.Close();
    });
}

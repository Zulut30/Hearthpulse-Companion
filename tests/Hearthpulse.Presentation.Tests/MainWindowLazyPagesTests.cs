using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF main window builds the Arena, Tavern Brawl and Settings pages and the setup wizard on first use, not with the
/// window (TASK-2026-11-23-perf-arch-round2, AC3): a visit, a tab or the wizard opening builds them, the Settings page on
/// the tab shown last and its other tabs in the background, their names still found through the window.
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
        // Opened on the tab shown last, the only one built; the others follow in the background. The tabs are found
        // through the window as when they were part of its XAML.
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("SettingsData")).Visibility);
        Assert.Null(window.FindName("SettingsGeneral"));
        Assert.Equal("Data", model.Settings.SettingsTab);
        Idle();
        string[] tabs = ["SettingsGeneral", "SettingsAppearance", "SettingsOverlay", "SettingsTracker", "SettingsStream", "SettingsAccount", "SettingsAbout"];
        Assert.All(tabs, name => Assert.Equal(Visibility.Collapsed, Assert.IsAssignableFrom<UserControl>(window.FindName(name)).Visibility));
        Assert.Same(window.SettingsTracker, window.FindName("SettingsTracker"));
        Assert.Equal(Visibility.Visible, window.SettingsData.Visibility);
        window.Close();
    });

    /// <summary>
    /// The Settings page built after the window is shown: its switches and sliders take their values from their bindings
    /// without the window telling the host a setting changed (each time the host saves and re-applies every setting); a
    /// slider the player moves still does.
    /// </summary>
    [Fact]
    public void Building_the_settings_page_on_a_shown_window_changes_no_setting() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Idle();
        var changed = 0;
        window.SettingsChanged += () => changed++;
        try
        {
            window.ShowPage("Settings");
            Idle();
            foreach (var tab in new[] { "Appearance", "Overlay", "Tracker", "Stream", "Account", "Data", "About", "General" })
            {
                window.ShowSettingsTab(tab);
                Idle();
            }

            Assert.Equal(0, changed);
            window.ShowSettingsTab("Overlay");
            Idle();
            var size = (Slider)window.SettingsOverlay.FindName("SizeSlider");
            size.Value = size.Value == size.Maximum ? size.Minimum : size.Maximum;
            Assert.True(changed > 0, "a moved slider is a changed setting");
        }
        finally
        {
            window.CloseForReal();
        }
    });

    /// <summary>Runs what the dispatcher has queued, idle work included.</summary>
    private static void Idle()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

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

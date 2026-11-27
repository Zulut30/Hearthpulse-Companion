using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using WpfMain = Hearthpulse.App.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// TASK-2026-11-26 AC4 in the WPF Overlay tab (the deck tracker's group since G-1): under "Card pool" the hover choice
/// (With Ctrl by default) and the Discover switch, both off with the pool; the hover row also off, and saying so, while
/// reading the game client is off (P5, AC7), the Discover switch not (a Discover comes from the logs). The Avalonia
/// twin: LocBindingTests.CardPool.
/// </summary>
public sealed class CardPoolSettingsContractTests
{
    [Fact]
    public void The_wpf_overlay_tab_has_the_pool_rows_and_follows_the_pool_and_client_switches() => WpfThread.RunFresh(() =>
    {
        var settings = new AppSettings();
        var model = new WpfMain(new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db")), settings, TestApp.Data);
        var tab = new Hearthpulse.App.Main.SettingsTabs.OverlaySettings { DataContext = model };
        tab.Measure(new Size(780, double.PositiveInfinity));
        tab.Arrange(new Rect(tab.DesiredSize));
        tab.UpdateLayout();
        T Named<T>(string name) => Assert.IsAssignableFrom<T>(LogicalTreeHelper.FindLogicalNode(tab, name));

        var pool = Named<CheckBox>("CardPoolSetting");
        var row = Named<Grid>("CardPoolHoverRow");
        var discover = Named<CheckBox>("CardPoolDiscoverSetting");
        var note = Named<TextBlock>("CardPoolHoverOffNote");
        var group = Named<StackPanel>("TrackerPanelSwitches");
        Assert.All(new FrameworkElement[] { pool, row, discover, note }, e => Assert.Contains(group, Ancestors(e)));
        Assert.Equal(Loc.T("Settings.Tracker.CardPoolTip"), pool.ToolTip);
        Assert.True(Named<RadioButton>("CardPoolHoverCtrlOption").IsChecked);
        Assert.Equal((true, true, true), (row.IsEnabled, discover.IsEnabled, discover.IsChecked == true));
        Assert.Equal(Visibility.Collapsed, note.Visibility);
        // Two "Off" segments on one tab: the pool's says which row it belongs to.
        var off = Named<RadioButton>("CardPoolHoverOffOption");
        Assert.Equal(Loc.T("Settings.Tracker.CardPoolHover.OffName"), AutomationProperties.GetName(off));
        Assert.StartsWith(Loc.T("Settings.Tracker.CardPoolHover.Off"), AutomationProperties.GetName(off), StringComparison.Ordinal);
        Assert.NotEqual(AutomationProperties.GetName(off), Named<RadioButton>("PickHintsOffOption").Content);

        Named<RadioButton>("CardPoolHoverAlwaysOption").IsChecked = true;
        Assert.Equal(CardPoolHover.Always, settings.CardPoolHover);
        discover.IsChecked = false;
        Assert.False(settings.CardPoolOnDiscover);

        // The pool off: both rows off, the choice row dimmed.
        pool.IsChecked = false;
        tab.UpdateLayout();
        Assert.Equal((false, false), (row.IsEnabled, discover.IsEnabled));
        Assert.Equal(0.5, row.Opacity);
        pool.IsChecked = true;
        tab.UpdateLayout();
        Assert.Equal((true, true, 1.0), (row.IsEnabled, discover.IsEnabled, row.Opacity));

        // Reading the game client off: the hover row off and the line why, the Discover switch on.
        model.ReadGameMemory = false;
        tab.UpdateLayout();
        Assert.False(row.IsEnabled);
        Assert.Equal(0.5, row.Opacity);
        Assert.Equal(Visibility.Visible, note.Visibility);
        Assert.Equal(Loc.T("Settings.Tracker.CardPoolHoverOff"), note.Text);
        Assert.True(discover.IsEnabled);
        model.ReadGameMemory = true;
        tab.UpdateLayout();
        Assert.True(row.IsEnabled);
        Assert.Equal(Visibility.Collapsed, note.Visibility);
    });

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var e = LogicalTreeHelper.GetParent(element); e is not null; e = LogicalTreeHelper.GetParent(e))
        {
            yield return e;
        }
    }
}

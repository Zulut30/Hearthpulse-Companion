using System.Windows;
using System.Windows.Controls;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main.SettingsTabs;

namespace Hearthpulse.App.Main.Pages;

/// <summary>
/// The Settings page's content, built on the first visit (MainWindow.SettingsView) instead of with the window; each tab
/// is built when first asked for (shown, searched, or by the window in the background once the page is drawn). What its
/// tabs and switches do stays with the window (MainWindow.Settings.cs, MainWindow.SettingsTools.cs).
/// </summary>
public partial class SettingsPage : UserControl
{
    /// <summary>The tabs' names, in the order of their buttons.</summary>
    private static readonly string[] TabNames =
        ["SettingsGeneral", "SettingsAppearance", "SettingsOverlay", "SettingsTracker", "SettingsStream", "SettingsAccount", "SettingsData", "SettingsAbout"];

    private readonly UserControl?[] _tabs = new UserControl?[TabNames.Length];

    public SettingsPage() => InitializeComponent();

    /// <summary>A tab button was checked.</summary>
    public event Action? TabChanged;

    /// <summary>A tab was built (hidden): the window wires it.</summary>
    public event Action<UserControl>? TabBuilt;

    /// <summary>The tab buttons, General first.</summary>
    internal RadioButton[] TabButtons =>
        [SettingsTabGeneral, SettingsTabAppearance, SettingsTabOverlay, SettingsTabTracker, SettingsTabStream, SettingsTabAccount, SettingsTabData, SettingsTabAbout];

    internal GeneralSettings SettingsGeneral => Tab<GeneralSettings>(0);

    internal AppearanceSettings SettingsAppearance => Tab<AppearanceSettings>(1);

    internal OverlaySettings SettingsOverlay => Tab<OverlaySettings>(2);

    internal TrackerSettings SettingsTracker => Tab<TrackerSettings>(3);

    internal StreamSettings SettingsStream => Tab<StreamSettings>(4);

    internal AccountSettings SettingsAccount => Tab<AccountSettings>(5);

    internal DataSettings SettingsData => Tab<DataSettings>(6);

    internal AboutSettings SettingsAbout => Tab<AboutSettings>(7);

    /// <summary>The tab of the button at <paramref name="index"/>, built if it was not.</summary>
    internal UserControl TabAt(int index) => index switch
    {
        0 => SettingsGeneral,
        1 => SettingsAppearance,
        2 => SettingsOverlay,
        3 => SettingsTracker,
        4 => SettingsStream,
        5 => SettingsAccount,
        6 => SettingsData,
        _ => SettingsAbout,
    };

    /// <summary>The tab at <paramref name="index"/> if it is built, else null (nothing is built).</summary>
    internal UserControl? BuiltTab(int index) => _tabs[index];

    /// <summary>The Overlay tab if it is built.</summary>
    internal OverlaySettings? BuiltOverlay => _tabs[2] as OverlaySettings;

    /// <summary>Builds the first tab not built yet; false when every tab is.</summary>
    internal bool BuildNextTab()
    {
        var next = Array.IndexOf(_tabs, null);
        if (next < 0)
        {
            return false;
        }

        _ = TabAt(next);
        return true;
    }

    /// <summary>A tab built hidden after the tab bar, among the others in the bar's order, revealing itself when shown.</summary>
    private T Tab<T>(int index) where T : UserControl, new()
    {
        if (_tabs[index] is T built)
        {
            return built;
        }

        var tab = new T { Name = TabNames[index], Visibility = Visibility.Collapsed };
        Motion.SetRevealOnShow(tab, true);
        // After the tab bar (the area's first child) and the tabs before it in the bar.
        SettingsTabsArea.Children.Insert(1 + _tabs.Take(index).Count(t => t is not null), tab);
        _tabs[index] = tab;
        TabBuilt?.Invoke(tab);
        return tab;
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        // A tab is not a setting: stop Checked here so the window's OnSettingChanged does not re-apply every setting. The
        // tab is saved with the next change or on exit.
        e.Handled = true;
        TabChanged?.Invoke();
    }
}

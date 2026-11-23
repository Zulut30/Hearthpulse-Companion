using System.Windows;
using System.Windows.Controls;

namespace Hearthpulse.App.Main.Pages;

/// <summary>
/// The Settings page's content, built on the first visit (MainWindow.SettingsView) instead of with the window. What its
/// tabs and switches do stays with the window (MainWindow.Settings.cs, MainWindow.SettingsTools.cs).
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    /// <summary>A tab button was checked.</summary>
    public event Action? TabChanged;

    /// <summary>The tab buttons, General first.</summary>
    internal RadioButton[] TabButtons =>
        [SettingsTabGeneral, SettingsTabAppearance, SettingsTabOverlay, SettingsTabTracker, SettingsTabStream, SettingsTabAccount, SettingsTabData, SettingsTabAbout];

    private void OnTab(object sender, RoutedEventArgs e)
    {
        // A tab is not a setting: stop Checked here so the window's OnSettingChanged does not re-apply every setting. The
        // tab is saved with the next change or on exit.
        e.Handled = true;
        TabChanged?.Invoke();
    }
}

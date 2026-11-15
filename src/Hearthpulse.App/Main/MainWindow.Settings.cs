using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.App.Main;

/// <summary>
/// The Settings page in the window: its tabs, what a changed switch or slider does, and keeping the switches that bind
/// to AppSettings (which does not notify) in step with a change made elsewhere.
/// </summary>
public partial class MainWindow
{
    private bool _syncingSettings;

    /// <summary>Shows a Settings tab by its tag ("General", "Appearance", "Overlay", "Tracker", "Stream", "Account", "Data", "About"); unknown names show General.</summary>
    public void ShowSettingsTab(string tab)
    {
        var radio = new[] { SettingsTabAppearance, SettingsTabOverlay, SettingsTabTracker, SettingsTabStream, SettingsTabAccount, SettingsTabData, SettingsTabAbout }
            .FirstOrDefault(r => (string)r.Tag == tab) ?? SettingsTabGeneral;
        radio.IsChecked = true;
        UpdateSettingsTab(radio);
    }

    /// <summary>
    /// Both overlay switches show the setting after it changed elsewhere (the tray, the live bar): the live bar's, and the
    /// Settings page's, which binds to AppSettings (it does not notify) and drives the "overlay is off" note. The
    /// panels' lock switch is refreshed the same way after the overlay's own button.
    /// </summary>
    public void SyncOverlaySwitch()
    {
        OverlaySwitch.IsChecked = _viewModel.Settings.OverlayEnabled;
        var syncing = _syncingSettings;
        _syncingSettings = true;
        try
        {
            BindingOperations.GetBindingExpression(SettingsOverlay.OverlaySetting, ToggleButton.IsCheckedProperty)?.UpdateTarget();
            BindingOperations.GetBindingExpression(SettingsOverlay.PanelLockSetting, ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }
        finally
        {
            _syncingSettings = syncing;
        }
    }

    private void OnOverlaySwitch(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.OverlayEnabled = OverlaySwitch.IsChecked == true;
        SyncOverlaySwitch();
        SettingsChanged?.Invoke();
    }

    private void OnSettingsTab(object sender, RoutedEventArgs e)
    {
        // A tab is not a setting: stop Checked here so the page's OnSettingChanged does not re-apply every setting. The
        // tab is saved with the next change or on exit.
        e.Handled = true;
        UpdateSettingsTab(sender);
    }

    private void UpdateSettingsTab(object sender)
    {
        // Checked fires while InitializeComponent is still building the page.
        if (SettingsAbout is null || _viewModel is null || sender is not RadioButton { Tag: string tab })
        {
            return;
        }

        _viewModel.Settings.SettingsTab = tab;
        SettingsGeneral.Visibility = SettingsTabGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsAppearance.Visibility = SettingsTabAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsOverlay.Visibility = SettingsTabOverlay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsTracker.Visibility = SettingsTabTracker.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsStream.Visibility = SettingsTabStream.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsAccount.Visibility = SettingsTabAccount.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsData.Visibility = SettingsTabData.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsAbout.Visibility = SettingsTabAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsTabData.IsChecked == true)
        {
            // The search for HDT / Firestone data, when the start did not run it (the home card was answered).
            _viewModel.Migration.EnsureScanned();
        }
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        // Only what the player changed. A toggle a list builds when its tab is first shown takes its value from a
        // binding before it is loaded (the Tracker tab's counter rows raised 95 Checked at once, each re-applying and
        // saving every setting), and the tab buttons' Unchecked is not a setting either.
        if (IsLoaded && !_syncingSettings && e.OriginalSource is FrameworkElement { IsLoaded: true } source
                     && (source as RadioButton)?.GroupName != SettingsTabGeneral.GroupName)
        {
            Loc.Instance.SetLanguage(_viewModel.Settings.InterfaceLanguage());
            SyncOverlaySwitch();
            _viewModel.RefreshPanelToggles();
            SettingsChanged?.Invoke();
        }
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            SettingsChanged?.Invoke();
        }
    }

    private void OnOpenTrackerTab(object sender, RoutedEventArgs e) => ShowSettingsTab("Tracker");

    private void OnOpenOverlayTab(object sender, RoutedEventArgs e) => ShowSettingsTab("Overlay");

    /// <summary>The top bar's account indicator: Settings, Account (the account center).</summary>
    private void OnOpenAccountCenter(object sender, RoutedEventArgs e)
    {
        ShowPage("Settings");
        ShowSettingsTab("Account");
    }

    /// <summary>A live-bar button switched a panel: the settings page shows it too (its switches bind to AppSettings, which does not notify), then save and apply.</summary>
    private void OnPanelToggled(PanelToggle toggle)
    {
        _syncingSettings = true;
        try
        {
            foreach (var box in LogicalDescendants<ToggleButton>(PageSettings))
            {
                BindingOperations.GetBindingExpression(box, ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }
        finally
        {
            _syncingSettings = false;
        }

        SettingsChanged?.Invoke();
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in LogicalDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}

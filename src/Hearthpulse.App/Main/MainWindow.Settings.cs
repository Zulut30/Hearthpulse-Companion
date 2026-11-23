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

    /// <summary>
    /// Shows a Settings tab by its tag ("General", "Appearance", "Overlay", "Tracker", "Stream", "Account", "Data", "About");
    /// unknown names show General. The page is built if it was not yet.
    /// </summary>
    public void ShowSettingsTab(string tab)
    {
        var buttons = SettingsView.TabButtons;
        var radio = buttons.Skip(1).FirstOrDefault(r => (string)r.Tag == tab) ?? buttons[0];
        radio.IsChecked = true;
        UpdateSettingsTab();
    }

    /// <summary>
    /// Both overlay switches show the setting after it changed elsewhere (the tray, the live bar): the live bar's, and the
    /// Settings page's, which binds to AppSettings (it does not notify) and drives the "overlay is off" note. The
    /// panels' lock switch is refreshed the same way after the overlay's own button.
    /// </summary>
    public void SyncOverlaySwitch()
    {
        OverlaySwitch.IsChecked = _viewModel.Settings.OverlayEnabled;
        if (_settingsView?.BuiltOverlay is not { } overlay)
        {
            // The Overlay tab is not built yet: its switches read the settings when it is.
            return;
        }

        var syncing = _syncingSettings;
        _syncingSettings = true;
        try
        {
            BindingOperations.GetBindingExpression(overlay.OverlaySetting, ToggleButton.IsCheckedProperty)?.UpdateTarget();
            BindingOperations.GetBindingExpression(overlay.PanelLockSetting, ToggleButton.IsCheckedProperty)?.UpdateTarget();
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

    /// <summary>The checked tab button shows its tab and is kept in the settings.</summary>
    private void UpdateSettingsTab()
    {
        if (_settingsView is not { } page || page.TabButtons.FirstOrDefault(r => r.IsChecked == true) is not { Tag: string tab })
        {
            return;
        }

        _viewModel.Settings.SettingsTab = tab;
        var buttons = page.TabButtons;
        for (var i = 0; i < buttons.Length; i++)
        {
            // The chosen tab is built if it was not; the others built already are hidden.
            if (buttons[i].IsChecked == true)
            {
                page.TabAt(i).Visibility = Visibility.Visible;
            }
            else if (page.BuiltTab(i) is { } other)
            {
                other.Visibility = Visibility.Collapsed;
            }
        }

        if (page.SettingsTabData.IsChecked == true)
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
                     && (source as RadioButton)?.GroupName != SettingsView.SettingsTabGeneral.GroupName)
        {
            Loc.Instance.SetLanguage(_viewModel.Settings.InterfaceLanguage());
            SyncOverlaySwitch();
            _viewModel.RefreshPanelToggles();
            SettingsChanged?.Invoke();
        }
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only a slider the player moved: one built after the window was loaded (the Settings page is built on its first
        // visit) takes its value from its binding before it is loaded itself.
        if (IsLoaded && e.OriginalSource is FrameworkElement { IsLoaded: true })
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

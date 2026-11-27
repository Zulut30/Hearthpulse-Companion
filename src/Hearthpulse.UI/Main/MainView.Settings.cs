using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main;

/// <summary>
/// The Settings page in the view: its tabs, what a changed switch or choice does (the theme and the language at once),
/// and keeping the switches that bind to AppSettings (which does not notify) in step with a change made elsewhere.
/// </summary>
public partial class MainView
{
    private bool _syncingSettings;

    private RadioButton[] SettingsTabs => [SettingsTabGeneral, SettingsTabAppearance, SettingsTabOverlay, SettingsTabTracker, SettingsTabStream, SettingsTabAccount, SettingsTabData, SettingsTabAbout];

    /// <summary>Shows a Settings tab by its tag ("General", "Overlay", "Tracker", "Stream", "Account", "Data", "About"); unknown names show General.</summary>
    public void ShowSettingsTab(string tab)
    {
        (SettingsTabs.FirstOrDefault(r => r.Tag as string == tab) ?? SettingsTabGeneral).IsChecked = true;
        UpdateSettingsTab();
    }

    private void UpdateSettingsTab()
    {
        SettingsGeneral.IsVisible = SettingsTabGeneral.IsChecked == true;
        SettingsAppearance.IsVisible = SettingsTabAppearance.IsChecked == true;
        SettingsOverlay.IsVisible = SettingsTabOverlay.IsChecked == true;
        SettingsTracker.IsVisible = SettingsTabTracker.IsChecked == true;
        SettingsStream.IsVisible = SettingsTabStream.IsChecked == true;
        SettingsAccount.IsVisible = SettingsTabAccount.IsChecked == true;
        SettingsData.IsVisible = SettingsTabData.IsChecked == true;
        SettingsAbout.IsVisible = SettingsTabAbout.IsChecked == true;
        if (SettingsData.IsVisible)
        {
            // The search for HDT / Firestone data, when the start did not run it (the home card was answered).
            _viewModel?.Migration.EnsureScanned();
        }

        if (_viewModel is not null && SettingsTabs.FirstOrDefault(r => r.IsChecked == true)?.Tag is string tab && _viewModel.Settings.SettingsTab != tab)
        {
            _viewModel.Settings.SettingsTab = tab;
            SettingsChanged?.Invoke();
        }
    }

    public void SyncOverlaySwitch()
    {
        if (_viewModel is not null)
        {
            OverlaySwitch.IsChecked = _viewModel.Settings.OverlayEnabled;
            SettingsOverlay.OverlaySetting.IsChecked = _viewModel.Settings.OverlayEnabled;
            // The overlay's own lock button changes this one too.
            SettingsOverlay.PanelLockSetting.IsChecked = _viewModel.Settings.OverlayPanelsUnlocked;
        }
    }

    private void OnSettingChanged(CheckBox box)
    {
        if (_viewModel is null || box == OverlaySwitch || _syncingSettings)
        {
            return;
        }

        // Avalonia writes the switch back to AppSettings only after IsCheckedChanged: the bar re-reads it once it has.
        Dispatcher.UIThread.Post(_viewModel.RefreshPanelToggles);

        if (box == SettingsOverlay.OverlaySetting)
        {
            OverlaySwitch.IsChecked = _viewModel.Settings.OverlayEnabled;
        }

        SettingsChanged?.Invoke();
    }

    private void OnOverlaySwitch(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Settings.OverlayEnabled = OverlaySwitch.IsChecked == true;
        SettingsOverlay.OverlaySetting.IsChecked = _viewModel.Settings.OverlayEnabled;
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Applies a Stream tab radio's Tag ("StreamBackground.Green") through the main model, which tells the host to repaint the
    /// OBS windows; false when nothing changed.
    /// </summary>
    private static bool ApplyStreamChoice(MainViewModel viewModel, string? choice) =>
        choice?.Split('.') is ["StreamBackground", var background] && viewModel.SetStreamBackground(background);

    /// <summary>Applies a settings radio's Tag ("PickHints.Off", "Language.ru"); false when nothing changed.</summary>
    private static bool ApplyChoice(AppSettings settings, string? choice)
    {
        switch (choice?.Split('.'))
        {
            case ["PickHints", var name] when Enum.TryParse<PickHints>(name, out var hints) && settings.PickHints != hints:
                settings.PickHints = hints;
                return true;
            case ["CardPoolHover", var name] when Enum.TryParse<CardPoolHover>(name, out var hover) && settings.CardPoolHover != hover:
                settings.CardPoolHover = hover;
                return true;
            case ["Language", var language] when settings.Language != language:
                settings.Language = language;
                Loc.Instance.SetLanguage(settings.InterfaceLanguage());
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A theme was picked in Settings: repaint the app in place (brushes are shared and recoloured) under a picture of the
    /// old look that then fades out (MainView.ThemeSwap.cs), and save it.
    /// </summary>
    private void OnThemeChanged()
    {
        CoverForTheme();
        ApplyTheme();
        FadeThemeCover();
        SettingsChanged?.Invoke();
    }

    private void ApplyTheme()
    {
        if (_viewModel is null || Application.Current is not { } app)
        {
            return;
        }

        try
        {
            ThemeEngine.Apply(app, _viewModel.CurrentTheme);
            ThemeEngine.ApplyLook(app, _viewModel.CurrentLook);
            ThemeEngine.Dress(this);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diag.Error("Theme could not be applied; keeping the current colours", ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Event handler: the signature is fixed and the body catches its own errors.")]
    private async void OnCopyAccountCode(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { AccountUserCode.Length: > 0 } viewModel)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        try
        {
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(viewModel.AccountUserCode);
            }

            viewModel.AccountCodeCopied(clipboard is not null);
        }
        catch (Exception ex)
        {
            // An async event handler: anything that escaped would reach the crash handler for a failed clipboard copy.
            if (ex is not (InvalidOperationException or System.Runtime.InteropServices.ExternalException))
            {
                Diag.Error("Copying the account code failed", ex);
            }

            viewModel.AccountCodeCopied(false);
        }
    }

    /// <summary>The top bar's account indicator: Settings, Account (the account center).</summary>
    private void OnOpenAccountCenter(object? sender, RoutedEventArgs e)
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
            foreach (var box in PageSettings.GetLogicalDescendants().OfType<ToggleButton>())
            {
                BindingOperations.GetBindingExpressionBase(box, ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }
        finally
        {
            _syncingSettings = false;
        }

        SettingsChanged?.Invoke();
    }
}

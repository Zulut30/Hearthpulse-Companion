using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia theme cross-fade (MainView.ThemeSwap.cs): with motion off (the headless run has no animations, see
/// PoolInput.NoAnimationsSwitch) a theme change repaints at once and leaves no picture over the view, however often the
/// player clicks through the swatches; the Settings tab and its scroll stay where they were.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class ThemeCrossFadeTests
{
    public ThemeCrossFadeTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Fact]
    public void Theme_changes_with_motion_off_leave_no_picture_and_keep_the_settings_scroll() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-theme-fade") + ".db"), new AppSettings(), TestDataFolder.App);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1260, Height = 840 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            Assert.False(Motion.On);
            view.ShowPage("Settings");
            view.ShowSettingsTab("Appearance");
            PageRowsPanelAvaloniaTests.Settle(window);
            var page = view.FindControl<ScrollViewer>("PageSettings")!;
            page.Offset = new Vector(0, Math.Min(240, page.Extent.Height - page.Viewport.Height));
            PageRowsPanelAvaloniaTests.Settle(window);
            var offset = page.Offset;
            Assert.True(offset.Y > 0, "the Appearance tab scrolls in an 840 px window");

            foreach (var name in new[] { "Frost", "Fel", "Arcane", ThemePalette.DefaultName })
            {
                model.SelectTheme.Execute(name);
                PageRowsPanelAvaloniaTests.Settle(window);
                Assert.Equal(name, model.Settings.Theme);
                Assert.False(view.ThemeCovered);
                Assert.Null(AdornerLayer.GetAdorner(view));
            }

            Assert.Equal(offset, page.Offset);
            Assert.Equal("Appearance", model.Settings.SettingsTab);
        }
        finally
        {
            model.SelectTheme.Execute(ThemePalette.DefaultName);
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });
}

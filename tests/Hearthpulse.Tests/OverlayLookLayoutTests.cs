using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// Settings → Overlay, the look block in Avalonia (TASK-2026-11-25-polish D4, bug-hunt d5): the Russian opacity label
/// stays on one line; when the block is too narrow for the preview beside the controls (a narrow window, or labels too
/// wide for a slider: the headless test font is wider than the app's) the preview goes under them, and every slider
/// keeps a usable width either way.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class OverlayLookLayoutTests
{
    public OverlayLookLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.Russian));

    [Theory]
    [InlineData(1260, null)]
    [InlineData(1000, true)]
    public void The_opacity_label_keeps_one_line_and_the_preview_moves_under_when_narrow(double width, bool? narrowWindow) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App) { SaveSettings = _ => { } };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 900 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Settings");
            view.ShowSettingsTab("Overlay");
            PageRowsPanelAvaloniaTests.Settle(window);

            var look = view.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "LookGrid");
            var narrow = look.Classes.Contains("lookNarrow");
            if (narrowWindow is { } expected)
            {
                Assert.Equal(expected, narrow);
            }

            var preview = look.Children.OfType<StackPanel>().Single(s => s.Classes.Contains("overlayLookPreview"));
            Assert.Equal(narrow ? 1 : 0, Grid.GetRow(preview));

            var label = look.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.T("Settings.Overlay.Opacity"));
            var oneLine = look.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.T("Settings.Overlay.Size")).Bounds.Height;
            Assert.Equal(oneLine, label.Bounds.Height, 1);
            foreach (var slider in look.GetVisualDescendants().OfType<Slider>())
            {
                Assert.True(slider.Bounds.Width >= 120, $"{slider.Name} is {slider.Bounds.Width} wide");
            }
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });
}

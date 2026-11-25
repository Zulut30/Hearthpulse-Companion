using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Setup;

namespace Hearthpulse.Tests;

/// <summary>
/// The setup wizard in Avalonia, laid out headless (TASK-2026-11-25-polish D4, K1): every theme of the look step can
/// be reached inside the step's fixed height with the switch under the gallery, and a step change that hides the
/// focused button gives the keyboard to the shown one. Also n5: a card heading is a level-two heading for UI Automation,
/// and L-M3: About's licence line is bound in the view.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class SetupWizardLayoutTests
{
    public SetupWizardLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.Russian));

    private static (Window Window, MainView View, MainViewModel Model, Avalonia.Styling.Styles Theme) Open()
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(HistoryStore.InMemory(), new AppSettings { MigrationOffered = true }, TestDataFolder.App)
        {
            SaveSettings = _ => { },
        };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1260, Height = 840 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        PageRowsPanelAvaloniaTests.Settle(window);
        return (window, view, model, theme);
    }

    private static void Close(Window window, Avalonia.Styling.Styles theme)
    {
        window.Close();
        Application.Current!.Styles.Remove(theme);
    }

    private static SetupWizard Wizard(MainView view) => view.GetVisualDescendants().OfType<SetupWizard>().Single();

    [Fact]
    public void Every_theme_of_the_look_step_is_reachable_and_the_switch_stays_inside_the_step() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open();
        try
        {
            model.ShowSetup(3);
            PageRowsPanelAvaloniaTests.Settle(window);
            var wizard = Wizard(view);
            var gallery = wizard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "LookGallery");
            Assert.True(gallery.IsEffectivelyVisible);
            var swatches = gallery.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("themeSwatch")).ToList();
            Assert.Equal(model.Themes.Count, swatches.Count);
            Assert.True(gallery.Extent.Height > gallery.Viewport.Height, "thirteen themes are taller than the step: the gallery scrolls");

            // The step keeps its fixed height and the switch sits inside it, above the Back / Next row.
            var step = (Grid)((Control)gallery.Parent!).Parent!;
            Assert.Equal(320, step.Height);
            var toggle = wizard.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible);
            var bottom = toggle.TranslatePoint(new Point(0, toggle.Bounds.Height), step)!.Value.Y;
            Assert.InRange(bottom, 0, step.Bounds.Height);
        }
        finally
        {
            Close(window, theme);
        }
    });

    [Fact]
    public void Next_on_the_next_to_last_step_hands_the_keyboard_to_start() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open();
        try
        {
            model.ShowSetup(3);
            PageRowsPanelAvaloniaTests.Settle(window);
            var wizard = Wizard(view);
            var next = wizard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NextButton");
            var start = wizard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartButton");
            Assert.True(next.Focus());

            next.Command!.Execute(null);
            PageRowsPanelAvaloniaTests.Settle(window);

            Assert.False(next.IsEffectivelyVisible);
            Assert.Same(start, TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement());
        }
        finally
        {
            Close(window, theme);
        }
    });

    [Fact]
    public void A_card_heading_is_a_level_two_heading_with_its_title_as_the_name() => LocBindingTests.Headless.Run(() =>
    {
        var heading = new Ornament { Text = "BATTLEGROUNDS RATING" };
        var peer = ControlAutomationPeer.CreatePeerForElement(heading);
        Assert.Equal("Battlegrounds rating", peer.GetName());
        Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
        Assert.Equal(2, peer.GetHeadingLevel());

        var sub = new Ornament { Text = "Tracked" };
        AutomationProperties.SetHeadingLevel(sub, 3);
        Assert.Equal(3, ControlAutomationPeer.CreatePeerForElement(sub).GetHeadingLevel());
    });

    [Fact]
    public void About_licence_links_open_the_shipped_files_and_say_when_one_is_missing() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open();
        try
        {
            var opened = new List<string>();
            model.OpenDocument = path =>
            {
                opened.Add(path);
                return true;
            };
            model.ShippedFilesFolder = Path.Combine(Path.GetTempPath(), "hp-no-licence-" + Guid.NewGuid().ToString("N"));
            view.ShowPage("Settings");
            view.ShowSettingsTab("About");
            PageRowsPanelAvaloniaTests.Settle(window);

            var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
            var licence = buttons.Single(b => b.Name == "LicenceLink");
            var notices = buttons.Single(b => b.Name == "NoticesLink");
            var status = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "LicenceStatusLine");
            Assert.True(licence.IsEffectivelyVisible && notices.IsEffectivelyVisible);
            Assert.Same(model.OpenLicence, licence.Command);
            Assert.Same(model.OpenNotices, notices.Command);
            Assert.False(status.IsVisible);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));

            notices.Command!.Execute(null);
            PageRowsPanelAvaloniaTests.Settle(window);

            Assert.Empty(opened);
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(Loc.F("Settings.About.FileMissing", MainViewModel.NoticesFileName), status.Text);
        }
        finally
        {
            Close(window, theme);
        }
    });
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC7: the "Game client" card of Settings → Data in the Avalonia view, in both languages at the
/// narrowest settings width (620): the switch binds the setting both ways, the card is offered where the client can be
/// read (and anywhere reading is off), and its caption wraps inside the card.
/// </summary>
public sealed partial class LocBindingTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void The_game_client_switch_binds_the_setting_and_its_caption_fits(string language) => Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(language);
        var theme = Theme();
        var app = Application.Current!;
        var model = new MainViewModel(new HistoryStore(Path.Combine(AppPaths.Root, $"hp-game-memory-{Guid.NewGuid():N}.db")), new AppSettings(), TestDataFolder.App);
        var changes = 0;
        model.GameMemoryChanged += () => changes++;
        var view = new Hearthpulse.UI.Main.SettingsTabs.DataSettings { DataContext = model };
        var window = new Window { Width = 620, Height = 1600, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var card = Named<Border>(view, "GameClientCard");
            var box = Named<CheckBox>(view, "GameMemorySwitch");
            Assert.Equal(HearthstoneMemory.IsSupported, card.IsVisible);
            Assert.Equal(Loc.T("Settings.Data.GameMemory"), box.Content);
            Assert.True(box.IsChecked);

            // The switch writes the setting and tells the host; off, the card shows on any system. (A click sets the
            // current value, as here, and keeps the binding.)
            box.SetCurrentValue(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(model.Settings.ReadGameMemory);
            Assert.Equal(1, changes);
            Assert.True(card.IsVisible);

            // The model turned it back on (as a reset would not): the switch follows.
            model.ReadGameMemory = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsChecked);
            model.ReadGameMemory = false;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            Assert.Contains(texts, t => t.Text == Loc.T("Settings.Data.GameMemoryNote"));
            Assert.Equal(Loc.T("Settings.Data.ClientTitle"), card.GetVisualDescendants().OfType<Hearthpulse.UI.Controls.Ornament>().Single().Text);
            foreach (var text in texts)
            {
                var end = text.TranslatePoint(new Point(text.Bounds.Width, 0), card)!.Value.X;
                Assert.True(end <= card.Bounds.Width + 0.5, $"{language}: \"{text.Text}\" ends at {end}, the card is {card.Bounds.Width} wide");
                Assert.Equal(Avalonia.Media.TextTrimming.None, text.TextTrimming);
            }
        }
        finally
        {
            window.Close();
            app.Styles.Remove(theme);
            Loc.Instance.SetLanguage(Loc.English);
            Dispatcher.UIThread.RunJobs();
        }
    });
}

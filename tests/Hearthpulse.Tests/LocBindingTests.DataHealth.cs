using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The "Data status" card of Settings → Data in the Avalonia view (roadmap 2026-11-15, 2.5), in both languages. The
/// Data tab is shown by itself (the whole main window needs fonts the headless platform lacks) at the two widths the
/// settings content gets: 780, its maximum in the usual window, and 620, the room left in the compact window at its
/// narrowest. Every binding of the card reaches its text, and no line of it is cut or runs past the card.
/// </summary>
public sealed partial class LocBindingTests
{
    [Theory]
    [InlineData("en", 780)]
    [InlineData("ru", 780)]
    [InlineData("en", 620)]
    [InlineData("ru", 620)]
    public void The_data_status_card_binds_every_line_and_cuts_none(string language, double width) => Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(language);
        var theme = Theme();
        var app = Application.Current!;
        var model = new MainViewModel(new HistoryStore(Path.Combine(AppPaths.Root, $"hp-data-health-{Guid.NewGuid():N}.db")), new AppSettings(), TestDataFolder.App);
        model.SetDataHealth(DataHealthScene.Report());
        var view = new Hearthpulse.UI.Main.SettingsTabs.DataSettings { DataContext = model };
        var window = new Window { Width = width, Height = 1600, Content = view };
        try
        {
            window.Show();
            for (var pass = 0; pass < 2; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            Assert.InRange(view.Bounds.Width, width - 1, width + 1);
            var card = Named<Border>(view, "DataHealthCard");
            Assert.True(card.IsEffectivelyVisible);
            Assert.True(card.Bounds.Width > 0);

            // The rows: one per section that has a source, with the model's own texts.
            var list = Named<ItemsControl>(view, "DataHealthList");
            var rows = list.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is DataHealthRow).ToList();
            Assert.Equal(13, rows.Count);
            Assert.Equal(model.DataHealthRows, rows.Select(g => (DataHealthRow)g.DataContext!));
            foreach (var grid in rows)
            {
                var row = (DataHealthRow)grid.DataContext!;
                var texts = grid.Children.OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
                // Name, date, and one of the two state lines (plain, or the marked one for a late section).
                Assert.Equal([row.Name, row.Date, row.State], texts.Select(t => t.Text));
                Assert.Equal(row.Attention, !ReferenceEquals(texts[2], grid.Children.OfType<TextBlock>().ElementAt(2)));
            }

            Assert.Contains(model.DataHealthRows, r => r.Attention && r.Name == Loc.T("Source.Health.Section.Arena") && r.State == Loc.T("Source.Health.State.Stale"));
            Assert.Contains(model.DataHealthRows, r => !r.Attention && r.State == Loc.T("Source.Health.State.Pending"));

            var note = Named<TextBlock>(view, "DataHealthNote");
            Assert.Equal(Loc.F("Source.Health.Checked", DataHealthScene.CheckedAt.ToLocalTime()), note.Text);
            var button = Named<Button>(view, "DataHealthCheck");
            Assert.Equal(Loc.T("Source.Health.Check"), button.Content);
            Assert.Same(model.CheckDataHealth, button.Command);
            var title = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Equal(Loc.T("Source.Health.Title"), card.GetVisualDescendants().OfType<Hearthpulse.UI.Controls.Ornament>().Single().Text);
            Assert.Contains(Loc.T("Source.Health.Caption"), title);

            // The date and state columns line up across the rows, and the section name keeps room of its own.
            Assert.Single(rows.Select(g => Math.Round(g.ColumnDefinitions[1].ActualWidth, 1)).Distinct());
            Assert.Single(rows.Select(g => Math.Round(g.ColumnDefinitions[2].ActualWidth, 1)).Distinct());
            Assert.All(rows, g => Assert.True(g.ColumnDefinitions[0].ActualWidth >= 120, $"{language} {width}: the name column is {g.ColumnDefinitions[0].ActualWidth}"));

            // Nothing cut: every shown line ends inside the card, and a line that cannot wrap has the room its text asks for.
            foreach (var text in card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                var end = text.TranslatePoint(new Point(text.Bounds.Width, 0), card)!.Value.X;
                Assert.True(end <= card.Bounds.Width + 0.5, $"{language} {width}: \"{text.Text}\" ends at {end}, the card is {card.Bounds.Width} wide");
                Assert.True(text.Bounds.Height > 0, text.Text);
                Assert.Equal(Avalonia.Media.TextTrimming.None, text.TextTrimming);
                if (text.TextWrapping == Avalonia.Media.TextWrapping.NoWrap)
                {
                    var free = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight };
                    free.Measure(Size.Infinity);
                    Assert.True(free.DesiredSize.Width <= text.Bounds.Width + 0.5,
                        $"{language} {width}: \"{text.Text}\" needs {free.DesiredSize.Width}, has {text.Bounds.Width}");
                }
            }

            // The button's label fits the button.
            Assert.True(button.TranslatePoint(new Point(button.Bounds.Width, 0), card)!.Value.X <= card.Bounds.Width + 0.5);

            // "Check now": the note says so until the host answers.
            button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.T("Source.Health.Checking"), note.Text);
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

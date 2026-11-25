using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main.Decks;
using Hearthpulse.UI.Main.Share;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// The 16:9 deck cover (TASK-2026-11-09-deck-cover): both columns start on one line and end inside the field (15 and 17
    /// rows a short block, 21 and 30 nearly the whole field), and every name of a column takes one size that fits it, never
    /// under 10, instead of an ellipsis. The list and the class, name, facts and curve stand on the field's middle line
    /// (TASK-2026-11-25-polish S5), so a short deck leaves no hole between them.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(21)]
    [InlineData(17)]
    [InlineData(15)]
    public void Share_deck_cover_columns_stand_on_the_middle_line_and_names_share_a_size_that_fits(int count) => Headless.Run(() => DeckCoverLayout(count));

    /// <summary>A fitted text fits again when its text changes at the same width.</summary>
    [Fact]
    public void Share_text_fit_follows_a_new_text() => Headless.Run(() =>
    {
        var text = new TextBlock { Width = 100, FontSize = 12, Text = "short" };
        ShareTextFit.SetMode(text, ShareTextFit.Line);
        var window = new Window { Width = 200, Height = 100, Content = new StackPanel { Children = { text } } };
        try
        {
            Show(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(12, text.FontSize);
            text.Text = "a much longer text";
            Dispatcher.UIThread.RunJobs();
            Assert.True(text.FontSize < 12, $"font {text.FontSize}");
            text.Text = "short";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(12, text.FontSize);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Share_text_fit_steps_down_to_the_width_and_stops_at_the_floor()
    {
        Assert.Equal(13, ShareTextFit.Fit(13, 9, 200, s => s * 10));
        Assert.Equal(10, ShareTextFit.Fit(13, 9, 100, s => s * 10));
        Assert.Equal(9, ShareTextFit.Fit(13, 9, 10, s => s * 10));
    }

    [Fact]
    public void Share_sample_decks_have_their_row_counts_of_known_cards()
    {
        var json = new StringBuilder("[");
        for (var i = 0; i < 40; i++)
        {
            json.Append(i == 0 ? string.Empty : ",")
                .Append($$"""{"id":"X_{{i:00}}","dbfId":{{1000 + i}},"name":"Card {{new string('a', i)}}","cost":1,"type":"MINION","cardClass":"NEUTRAL","set":"BE","collectible":true}""");
        }

        json.Append(",{\"id\":\"END_020\",\"dbfId\":2000,\"name\":\"Known\",\"cost\":1,\"type\":\"SPELL\",\"cardClass\":\"WARRIOR\",\"set\":\"END\",\"collectible\":true}]");
        var cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json.ToString())));
        var decks = ShareSampleDecks.All(cards).ToList();
        Assert.Equal([21, 30, 15, 12, 8, 17], decks.Select(d => d.Rows));
        Assert.Equal("PRIEST", decks[5].HeroClass);
        Assert.Empty(decks[5].Deck);
        Assert.Equal(["END_020"], decks[0].Deck.Keys);
        Assert.Equal(30, decks[1].Deck.Count);
        Assert.Contains("X_39", decks[1].Deck.Keys);
        Assert.DoesNotContain("X_00", decks[1].Deck.Keys);
    }

    private static void DeckCoverLayout(int count)
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var app = Application.Current!;
        var theme = Theme();
        const string longName = "Сорвавшийся с цепи";
        var rows = Enumerable.Range(0, count)
            .Select(i => new DeckListRow($"C{i}", i == 1 ? longName : $"Карта {i}", i, i == 1 ? "LEGENDARY" : "COMMON", i == 1 ? "★" : "2"))
            .ToList();
        var half = (count + 1) / 2;
        var card = new DeckPictureCard("Воин", "Воин", "HERO_01", "30 карт", "CODE", rows, rows.Take(half).ToList(), rows.Skip(half).ToList(), [new ManaCurveBar("1", 1, 1)]);
        var share = new ShareViewModel(ShareKind.Deck, card, "Deck", "file", new AppSettings(), null) { Cover = true };
        var window = new ShareWindow(share);
        try
        {
            Show(window);
            Dispatcher.UIThread.RunJobs();
            var cover = Assert.IsType<DeckCoverView>(window.Card);
            var columns = cover.GetVisualDescendants().OfType<ItemsControl>().Where(c => c.ItemsSource is IReadOnlyList<DeckListRow>).ToList();
            Assert.Equal(2, columns.Count);
            var tops = columns.Select(c => c.ContainerFromIndex(0)!.TranslatePoint(default, cover)!.Value.Y).ToList();
            var top = 32 + card.CoverListTop;
            var shown = card.CoverListHeight * card.CoverScale;
            Assert.All(tops, t => Assert.InRange(t, top - 1, top + 1));
            Assert.Equal(tops[0], tops[1], 0.5);
            var last = columns[0].ContainerFromIndex(half - 1)!;
            var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), cover)!.Value.Y;
            Assert.InRange(bottom, top + shown - 8, top + shown + 1);
            Assert.True(bottom <= 540 - 32 + 0.5, $"bottom {bottom}");

            var curve = cover.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.ItemsSource is IReadOnlyList<ManaCurveBar>);
            var block = Assert.IsType<StackPanel>(curve.GetVisualParent());
            var blockTop = block.TranslatePoint(default, cover)!.Value.Y;
            Assert.Equal(270, blockTop + (block.Bounds.Height / 2), 1);
            Assert.Equal(270, top + (shown / 2), 1);

            foreach (var column in columns)
            {
                var names = column.GetVisualDescendants().OfType<TextBlock>().Where(t => ShareTextFit.GetMode(t) == ShareTextFit.Line).ToList();
                Assert.Single(names.Select(n => n.FontSize).Distinct());
                Assert.True(names[0].FontSize >= DeckPictureCard.NameMinFontSize, $"font {names[0].FontSize}");
                Assert.All(names, n => Assert.True(Measure(n) <= n.Bounds.Width + 1, $"{n.Text}: {Measure(n)} > {n.Bounds.Width} at {n.FontSize}"));
            }

            Assert.True(columns[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == longName).FontSize < 12);
        }
        finally
        {
            window.Close();
            Loc.Instance.SetLanguage(Loc.English);
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static double Measure(TextBlock text) =>
        new FormattedText(text.Text!, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), text.FontSize, null).WidthIncludingTrailingWhitespace;
}

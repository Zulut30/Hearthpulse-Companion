using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfLink = Hearthpulse.App.Controls.CardLink;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The card page in both apps (docs/card-page.md): the shared view model gives the WPF and the Avalonia host the same
/// page for the same data, Back and the navigation around it behave alike, and the WPF window shows the page when a
/// card row is clicked, with every text whole, and returns to the page under it.
/// </summary>
public sealed class CardPageContractTests
{
    private static readonly DateTime Collected = new(2026, 11, 16, 6, 4, 0, DateTimeKind.Utc);

    private static HistoryStore History()
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        var at = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        Assert.True(history.Add(new GameRecord
        {
            Key = "card-contract",
            StartedAt = at,
            EndedAt = at.AddMinutes(8),
            Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
            PlayerClass = "DEMONHUNTER", OpponentClass = "PRIEST",
            Mulligan = GameCards.MulliganJson(new MulliganFacts(["T_LONG"], [], [])),
            CardsDrawn = GameCards.TurnsJson([new CardTurn("T_LONG", 0)]),
            CardsPlayed = GameCards.TurnsJson([new CardTurn("T_LONG", 4)]),
        }));
        return history;
    }

    private static Func<string, CancellationToken, Task<CardStatsSlice?>> Loader(List<string> asked) => (format, _) =>
    {
        lock (asked)
        {
            asked.Add(format);
        }

        return Task.FromResult<CardStatsSlice?>(new CardStatsSlice(format, format == "wild" ? "LEGEND" : "GOLD", "LAST_14_DAYS", Collected,
            new Dictionary<string, CardStat>(StringComparer.Ordinal) { ["T_LONG"] = new("T_LONG", 1001, 0.2236, 0.5513, 1.99, 143721, 0.5361, 0.5407, 0.502, 0.561, 6.41) }));
    };

    private static dynamic Model(bool wpf, List<string> asked)
    {
        var cards = Cards();
        dynamic model = wpf ? new WpfMain(History(), new AppSettings(), TestApp.Data) : new AvaMain(History(), new AppSettings(), TestApp.Data);
        model.SetCards(cards);
        model.SetConstructedMeta(Meta(cards));
        model.LoadCardStats = Loader(asked);
        return model;
    }

    [Fact]
    public async Task Both_hosts_build_the_same_page_from_the_same_data()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            var wpf = Model(true, []);
            var ava = Model(false, []);

            Assert.True(await (Task<bool>)wpf.OpenCardPageAsync("T_LONG"));
            Assert.True(await (Task<bool>)ava.OpenCardPageAsync("T_LONG"));

            Assert.True((bool)wpf.CardOpen && (bool)ava.CardOpen);
            var (w, a) = (wpf.CardPage, ava.CardPage);
            Assert.Equal((string)w.Name, (string)a.Name);
            Assert.Equal((string)w.Text, (string)a.Text);
            Assert.Equal((IEnumerable<string>)w.Facts, (IEnumerable<string>)a.Facts);
            List<string> lines = Lines(w);
            Assert.Equal(lines, (List<string>)Lines(a));
            // The source by its name in the interface, the slice the answer names, the sample of every block.
            Assert.Contains(lines, l => l.StartsWith("Данные Hearthpulse · обновлено ", StringComparison.Ordinal) && l.EndsWith("· Золото · за 14 дней", StringComparison.Ordinal));
            Assert.Contains(Loc.P("Card.Stats.PlayedDetail", 143721), lines);
            Assert.Contains(Loc.P("Card.Archetypes.Lists", 2, 2), lines);
            Assert.Contains(Loc.P("Card.Mine.Note", 1, 1), lines);
            Assert.DoesNotContain(lines, l => l.Contains("HSReplay", StringComparison.OrdinalIgnoreCase) || l.Contains("HSGuru", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Back_and_the_navigation_around_the_card_are_the_same_in_both_hosts(bool wpf)
    {
        var asked = new List<string>();
        var model = Model(wpf, asked);
        model.PageShown(ConstructedPage.Standard);

        Assert.False(await (Task<bool>)model.OpenCardPageAsync("NOPE_001"));
        Assert.False((bool)model.CardOpen);

        Assert.True(await (Task<bool>)model.OpenCardPageAsync("T_LONG"));
        Assert.Equal(["standard"], asked);
        Assert.True((bool)model.GoBack(ConstructedPage.Standard));
        Assert.False((bool)model.CardOpen);
        Assert.False((bool)model.GoBack(ConstructedPage.Standard));

        Assert.True(await (Task<bool>)model.OpenCardPageAsync("T_LONG"));
        model.CardPage.Back.Execute(null);
        Assert.False((bool)model.CardOpen);

        Assert.True(await (Task<bool>)model.OpenCardPageAsync("T_LONG"));
        model.PageShown("Arena");
        Assert.False((bool)model.CardOpen);
    }

    [Fact]
    public void The_wpf_window_opens_the_page_from_a_card_row_shows_every_text_whole_and_goes_back() => WpfThread.RunFresh(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = (WpfMain)Model(true, []);
        var window = OffScreen(new WpfWindow(model));
        window.Show();
        Pump();
        try
        {
            window.ShowPage(ConstructedPage.Standard);
            Pump();
            var pageCard = (ScrollViewer)window.FindName("PageCard");
            Assert.Equal(Visibility.Collapsed, pageCard.Visibility);

            // A row as the deck page and the mulligan guide build theirs, somewhere in the window.
            var row = new Border();
            var empty = new Border();
            var host = (Panel)pageCard.Parent;
            host.Children.Add(row);
            host.Children.Add(empty);
            WpfLink.SetCardId(row, "T_LONG");
            Assert.Same(System.Windows.Input.Cursors.Hand, row.Cursor);
            Assert.False(WpfLink.Open(empty));
            Assert.False(model.CardOpen);

            Assert.True(WpfLink.Open(row));
            var until = System.Diagnostics.Stopwatch.StartNew();
            while (model.CardPage.Loading && until.Elapsed < TimeSpan.FromSeconds(30))
            {
                Pump();
            }

            Pump();
            window.UpdateLayout();
            Assert.True(model.CardOpen);
            Assert.Equal(Visibility.Visible, pageCard.Visibility);
            Assert.Equal(ConstructedPage.Standard, window.CurrentPage);
            var view = window.CardDetailsPage;
            Assert.Same(view, pageCard.Content);
            Assert.Equal(4, view.Sections.Count);
            Assert.All(view.Sections, s => Assert.True(s.IsVisible && s.ActualWidth > 300 && s.ActualWidth <= view.ActualWidth + 0.5));

            // Every text of the page is there and whole: nothing trimmed, nothing past the page's edge.
            var blocks = Descendants<TextBlock>(view).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            var texts = blocks.Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
            var page = model.CardPage;
            string[] expected =
            [
                page.Name, page.Text, .. page.Facts, page.StatsSource, page.MetaSource, page.MineNote,
                .. page.Tiles.Concat(page.MineTiles).SelectMany(t => new[] { t.Label, t.Value, t.Detail }),
                .. page.Archetypes.SelectMany(a => new[] { a.Name, a.ClassName, a.Share, a.Lists, a.Copies, a.WinRate, a.Games }),
                .. page.Mulligan.SelectMany(m => new[] { m.Name, m.KeepRate, m.WinRate, m.Impact, m.Hands }),
                Loc.T("Card.Back"), Loc.T("Card.Col.Archetype"), Loc.T("Card.Col.Lists"), Loc.T("Card.Col.Copies"), Loc.T("Card.Col.WinRate"), Loc.T("Card.Col.Games"),
                Loc.T("Card.Col.Keep"), Loc.T("Card.Col.HandWinRate"), Loc.T("Card.Col.Impact"), Loc.T("Card.Col.Hands"),
            ];
            Assert.All(expected, text => Assert.Contains(text, texts));
            Assert.Contains(page.Archetypes, a => a.Name.Length > 30);
            foreach (var block in blocks)
            {
                Assert.True(block.TextTrimming == TextTrimming.None, $"\"{block.Text}\" is trimmed");
                var left = block.TranslatePoint(default, view).X;
                Assert.True(left >= -0.5 && left + block.ActualWidth <= view.ActualWidth + 0.5, $"\"{block.Text}\" at {left}..{left + block.ActualWidth} of {view.ActualWidth}");
                if (block.TextWrapping == TextWrapping.NoWrap)
                {
                    var free = new TextBlock { Text = block.Text, FontSize = block.FontSize, FontFamily = block.FontFamily, FontWeight = block.FontWeight };
                    free.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(free.DesiredSize.Width <= block.ActualWidth + 0.5, $"\"{block.Text}\" needs {free.DesiredSize.Width}, has {block.ActualWidth}");
                }
            }

            // A number ends where its head ends.
            double Right(string text)
            {
                var block = blocks.First(t => t.Text == text);
                return block.TranslatePoint(new Point(block.ActualWidth, 0), view).X;
            }

            Assert.Equal(Right(Loc.T("Card.Col.WinRate")), Right(page.Archetypes[0].WinRate), 1.0);
            Assert.Equal(Right(Loc.T("Card.Col.Impact")), Right(page.Mulligan[0].Impact), 1.0);
            Assert.Equal("T_LONG", Hearthpulse.App.Infrastructure.CardArt.GetCard((DependencyObject)view.FindName("CardImage")));

            // Back (the button, as Esc does through GoBack): the page under the card returns.
            ((Button)view.FindName("BackButton")).Command.Execute(null);
            Pump();
            Assert.False(model.CardOpen);
            Assert.Equal(Visibility.Collapsed, pageCard.Visibility);
            Assert.Equal(ConstructedPage.Standard, window.CurrentPage);
        }
        finally
        {
            window.CloseForReal();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>Every line a page shows, in order: tiles, sources, rows, notes.</summary>
    private static List<string> Lines(dynamic page)
    {
        var lines = new List<string> { (string)page.StatsSource, (string)page.StatsEmpty, (string)page.MetaSource, (string)page.MineNote, (string)page.MineEmpty };
        foreach (var tile in page.Tiles)
        {
            lines.AddRange([(string)tile.Label, (string)tile.Value, (string)tile.Detail, ((int)tile.Sign).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        foreach (var row in page.Archetypes)
        {
            lines.AddRange([(string)row.Name, (string)row.ClassName, (string)row.HeroId, (string)row.Lists, (string)row.Share, (string)row.Copies, (string)row.WinRate, (string)row.Games]);
        }

        foreach (var row in page.Mulligan)
        {
            lines.AddRange([(string)row.Name, (string)row.KeepRate, (string)row.WinRate, (string)row.Impact, (string)row.Hands]);
        }

        foreach (var tile in page.MineTiles)
        {
            lines.AddRange([(string)tile.Label, (string)tile.Value, (string)tile.Detail]);
        }

        return lines;
    }

    private static CardDb Cards() => CardDb.Parse("ruRU", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id": "HERO_10", "dbfId": 56550, "name": "Иллидан Ярость Бури", "type": "HERO", "cardClass": "DEMONHUNTER", "set": "HERO_SKINS"},
          {"id": "T_LONG", "dbfId": 1001, "name": "Повелитель кошмаров Ксавий, владыка Изумрудного Кошмара", "cost": 4, "type": "MINION", "cardClass": "NEUTRAL",
           "set": "EMERALD_DREAM", "text": "<b>Боевой клич:</b> перемешайте в колоду 2 |4(копию,копии,копий) карты из руки.", "collectible": true},
          {"id": "T_FILL", "dbfId": 1002, "name": "Наполнитель", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true}
        ]
        """)));

    private static ConstructedMeta Meta(CardDb cards)
    {
        MetaDeck Deck(int copies) => new(
            DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_LONG"] = copies, ["T_FILL"] = 2 }, "DEMONHUNTER", "FT_STANDARD", cards)!,
            1, string.Empty, "DEMONHUNTER", 100, 50, 0.5, default);
        return new ConstructedMeta("standard", "legend", "past_week", new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), 148300,
            [
                new MetaArchetype(1, "cthun-dh", "Охотник на демонов на К'Туне с порождениями Бездны", "DEMONHUNTER", 148300, 79207, 0.5341, 1, 0, 0, 0, 0, [],
                    [new MetaMulliganCard("T_LONG", 1000, 0.8, 1200, 0.6)]),
            ],
            [Deck(2), Deck(1)]);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var deeper in Descendants<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private static WpfWindow OffScreen(WpfWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1260;
        window.Height = 840;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

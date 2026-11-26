using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia main pages at the compact window's narrowest (420) and at the usual width, laid out headless, in
/// Russian and English (bug-hunt #3, #4, #6, #7, #24, #25, #29, #30): Home's recent games keep the hero's name and
/// details, and the date, result and rating drop under them in compact; Home's deck banners stand one under the other
/// with the name beside the art; the Battlegrounds rating title is whole; Standard's deck rows keep the deck name; the
/// group rows wrap the class name instead of cutting it; the KPI captions wrap; the Battlegrounds hero table's switch
/// does not cover its title and the sort bar stays inside its card. On the usual width each row stays one line.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class MainCompactLayoutTests
{
    /// <summary>Headless glyphs are as wide as the font size; the app's fonts average about half of that (BgOwnLayoutTests).</summary>
    private const double GlyphScale = 0.6;

    public MainCompactLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Theory]
    [InlineData(420, "ru")]
    [InlineData(420, "en")]
    [InlineData(1280, "ru")]
    [InlineData(1280, "en")]
    public void Home_standard_and_battlegrounds_keep_their_names_whole(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var cards = Cards();
        var model = new MainViewModel(History(cards), new AppSettings(), TestDataFolder.App);
        model.SetCards(cards);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1400 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            Pump(model.RefreshAsync());
            var compact = width < 900;
            view.ShowPage("Overview");
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Equal(compact, view.Classes.Contains("compact"));
            Assert.NotEmpty(model.Recent);
            Assert.NotEmpty(model.HomeDecks);

            // Recent games: the name and details take the row's width in compact, the other cells drop under them.
            var rows = Shown<Grid>(view, "gameRow");
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var title = Child<StackPanel>(row, "gameTitle");
                Assert.True(title.Bounds.Width >= (compact ? 150 : 300), $"game title {title.Bounds.Width}");
                foreach (var cell in new Control[] { Child<TextBlock>(row, "gameWhen"), Child<Panel>(row, "gameResult"), Child<TextBlock>(row, "gameRating") })
                {
                    Assert.True(cell.Bounds.Right <= row.Bounds.Width + 0.5, $"{cell.Classes[^1]} ends at {cell.Bounds.Right} in {row.Bounds.Width}");
                    Assert.Equal(compact, cell.Bounds.Top >= title.Bounds.Bottom - 0.5);
                }
            }

            // Your decks: one banner per line in compact (three in a row otherwise), the name beside the art.
            var decks = Shown<UniformGrid>(view, "homeDecks").Single();
            Assert.Equal(compact ? 1 : 3, decks.Columns);
            foreach (var deck in Shown<Button>(view, "homeDeck"))
            {
                var name = deck.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == ((HomeDeck)deck.DataContext!).Name);
                Assert.True(name.Bounds.Width >= 80, $"deck name {name.Bounds.Width}");
                Assert.True(Right(name, deck) <= deck.Bounds.Width + 0.5, $"deck name ends at {Right(name, deck)} in {deck.Bounds.Width}");
            }

            // The rating card: the title with its icon on a wide page, without it (and whole) in compact.
            var ornament = Named<Ornament>(view, "BgRatingTitle");
            var plain = Named<Ornament>(view, "BgRatingTitleCompact");
            Assert.Equal((!compact, compact), (ornament.IsEffectivelyVisible, plain.IsEffectivelyVisible));
            var shownTitle = compact ? plain : ornament;
            var icon = compact ? 0 : Math.Round(shownTitle.Size * 2.3) + 10;
            Assert.True(icon + (TitleWidth(shownTitle) * GlyphScale) + 12 <= shownTitle.Bounds.Width,
                $"rating title {icon + (TitleWidth(shownTitle) * GlyphScale) + 12} in {shownTitle.Bounds.Width}");

            // Standard, My stats: the deck name keeps its room; record and win rate under it in compact.
            view.ShowPage("Standard");
            PageRowsPanelAvaloniaTests.Settle(window);
            var deckRows = Shown<Grid>(view, "deckRow");
            Assert.NotEmpty(deckRows);
            foreach (var row in deckRows)
            {
                var title = Child<StackPanel>(row, "deckTitle");
                Assert.True(title.Bounds.Width >= (compact ? 180 : 300), $"deck title {title.Bounds.Width}");
                var record = Child<TextBlock>(row, "deckRecord");
                var win = Child<Panel>(row, "deckWin");
                Assert.Equal(compact, record.Bounds.Top >= title.Bounds.Bottom - 0.5);
                Assert.Equal(compact, win.Bounds.Top >= title.Bounds.Bottom - 0.5);
                Assert.True(win.Bounds.Right <= row.Bounds.Width + 0.5, $"win rate ends at {win.Bounds.Right} in {row.Bounds.Width}");
            }

            // The rest of My stats: the class rows wrap their names in compact (no bar), cut with a tooltip otherwise.
            view.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "CtMore" && t.IsEffectivelyVisible).IsChecked = true;
            PageRowsPanelAvaloniaTests.Settle(window);
            var labels = Shown<TextBlock>(view, "groupText");
            Assert.NotEmpty(labels);
            foreach (var label in labels)
            {
                Assert.Equal(compact ? (TextWrapping.Wrap, TextTrimming.None) : (TextWrapping.NoWrap, TextTrimming.CharacterEllipsis), (label.TextWrapping, label.TextTrimming));
                Assert.Equal(label.Text, ToolTip.GetTip(label));
            }

            Assert.All(Shown<TextBlock>(view, "tileDetail"), t => Assert.Equal(compact ? TextWrapping.Wrap : TextWrapping.NoWrap, t.TextWrapping));

            // Battlegrounds, My stats: the "all columns" switch beside the title or under it, never over it; the sort bar
            // inside its card.
            view.ShowPage("Battlegrounds"); // My stats is the page's first tab
            PageRowsPanelAvaloniaTests.Settle(window);
            var heroTitle = Named<Ornament>(view, "BgHeroPerfTitle");
            var allColumns = Named<CheckBox>(view, "BgHeroAllColumnsSwitch");
            var head = (Control)heroTitle.GetVisualParent()!;
            Assert.False(Box(heroTitle, head).Intersects(Box(allColumns, head)), $"title {Box(heroTitle, head)} and switch {Box(allColumns, head)}");
            Assert.Equal(compact, allColumns.Bounds.Top >= heroTitle.Bounds.Bottom - 0.5);
            var bar = Named<Border>(view, "BgSortBar");
            var card = bar.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("card"));
            Assert.True(Right(bar, card) <= card.Bounds.Width - card.Padding.Right + 0.5, $"sort bar ends at {Right(bar, card)} in {card.Bounds.Width - card.Padding.Right}");
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    private static List<T> Shown<T>(Visual root, string cls) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Where(c => c.Classes.Contains(cls) && c.IsEffectivelyVisible).ToList();

    private static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static T Child<T>(Grid row, string cls) where T : Control => row.Children.OfType<T>().Single(c => c.Classes.Contains(cls));

    private static double Right(Visual child, Visual ancestor) => child.TranslatePoint(new Point(child.Bounds.Width, 0), ancestor)!.Value.X;

    private static Rect Box(Visual child, Visual ancestor) => new(child.TranslatePoint(default, ancestor)!.Value, child.Bounds.Size);

    /// <summary>The title as the ornament measures it: bold, three points over its size (a large heading).</summary>
    private static double TitleWidth(Ornament ornament) => new FormattedText(ornament.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), ornament.Size + 3, Brushes.Black).Width;

    private static void Pump(Task task)
    {
        var until = Stopwatch.StartNew();
        while (!task.IsCompleted && until.Elapsed < TimeSpan.FromSeconds(30))
        {
            Dispatcher.UIThread.RunJobs();
            task.Wait(TimeSpan.FromMilliseconds(15));
        }

        Assert.True(task.IsCompleted, "the history did not load in time");
        task.GetAwaiter().GetResult();
    }

    private static CardDb Cards()
    {
        const string json = """
            [
              {"id": "HERO_10", "dbfId": 56550, "name": "Illidan Stormrage", "type": "HERO", "cardClass": "DEMONHUNTER", "set": "HERO_SKINS"},
              {"id": "T_LONG", "dbfId": 1001, "name": "Nightmare Lord Xavius", "cost": 4, "type": "MINION", "cardClass": "NEUTRAL", "set": "EMERALD_DREAM", "collectible": true},
              {"id": "T_FILL", "dbfId": 1002, "name": "Filler", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true}
            ]
            """;
        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Three Ranked games with one deck under a long name, and two Battlegrounds games with a rating.</summary>
    private static HistoryStore History(CardDb cards)
    {
        var history = new HistoryStore(TestDataFolder.Scratch("hp-main-compact") + ".db");
        var code = DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_LONG"] = 2, ["T_FILL"] = 2 }, "DEMONHUNTER", "FT_STANDARD", cards)!;
        var at = DateTime.Now.AddDays(-1);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(history.Add(new GameRecord
            {
                Key = "main-compact-" + i, StartedAt = at.AddMinutes(i * 10), EndedAt = at.AddMinutes((i * 10) + 8), Mode = "Ranked", GameType = "GT_RANKED",
                Format = "FT_STANDARD", Result = i == 1 ? "Loss" : "Win", Turns = 9, PlayerClass = "DEMONHUNTER", OpponentClass = "DEATHKNIGHT",
                DeckName = "Охотник на демонов на К'Туне с порождениями Бездны", DeckCode = code,
            }));
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.True(history.Add(new GameRecord
            {
                Key = "main-compact-bg-" + i, StartedAt = at.AddHours(1 + i), EndedAt = at.AddHours(1 + i).AddMinutes(20), Mode = "Battlegrounds",
                GameType = "GT_BATTLEGROUNDS", Result = "Win", Placement = 2 + i, HeroCardId = "TB_BaconShop_HERO_56", RatingBefore = 6000 + (i * 50),
                RatingAfter = 6050 + (i * 50),
            }));
        }

        return history;
    }
}

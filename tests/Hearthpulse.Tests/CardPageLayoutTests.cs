using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Cards;

namespace Hearthpulse.Tests;

/// <summary>
/// The card page in the Avalonia main view, laid out headless (no app render): at the compact window's narrowest (420)
/// and at the usual width, in Russian and English, with a card and archetypes named as long as the game's longest.
/// Every binding reaches its text, no text is cut by an ellipsis or runs out of the page, the numbers stand under
/// their heads, and a card row's click opens the page.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class CardPageLayoutTests
{
    /// <summary>The headless platform gives every character the width of its font size; the app's fonts average about half (see BgOwnLayoutTests).</summary>
    private const double GlyphScale = 0.6;

    public CardPageLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Theory]
    [InlineData(420, "ru")]
    [InlineData(420, "en")]
    [InlineData(1280, "ru")]
    [InlineData(1280, "en")]
    public void Every_text_of_the_page_is_whole_and_the_numbers_stand_under_their_heads(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var history = new HistoryStore(TestDataFolder.Scratch("hp-card-layout") + ".db");
        AddGame(history);
        var model = new MainViewModel(history, new AppSettings(), TestDataFolder.App);
        var cards = Cards(language);
        model.SetCards(cards);
        model.SetConstructedMeta(Meta(cards, language));
        model.LoadCardStats = (format, _) => Task.FromResult<CardStatsSlice?>(new CardStatsSlice(format, "GOLD", "LAST_14_DAYS",
            new DateTime(2026, 11, 16, 6, 4, 0, DateTimeKind.Utc),
            new Dictionary<string, CardStat>(StringComparer.Ordinal) { ["T_LONG"] = new("T_LONG", 1001, 0.2236, 0.5513, 1.99, 143721, 0.5361, 0.5407, 0.502, 0.561, 6.41) }));
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 2400 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Standard");
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(Pump(model.OpenCardPageAsync("T_LONG")));
            PageRowsPanelAvaloniaTests.Settle(window);
            var compact = width < 900;
            Assert.Equal(compact, view.Classes.Contains("compact"));

            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var scroll = Named<ScrollViewer>("PageCard");
            var page = Named<CardPageView>("CardDetails");
            Assert.True(scroll.IsVisible);
            Assert.False(Named<ScrollViewer>("PageDeck").IsVisible);
            Assert.True(page.Bounds.Width > 250, $"page {page.Bounds.Width}");
            Assert.Equal(4, page.Sections.Count);
            Assert.All(page.Sections, s => Assert.True(s.IsEffectivelyVisible && s.Bounds.Width <= page.Bounds.Width + 0.5));

            // Every binding reached its text: the card, each tile, each row, each source line.
            var vm = model.CardPage;
            var shown = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            var texts = shown.Select(t => t.Text!).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(6, vm.Tiles.Count);
            Assert.Equal(2, vm.Archetypes.Count);
            Assert.Equal(2, vm.Mulligan.Count);
            Assert.Equal(3, vm.MineTiles.Count);
            string[] expected =
            [
                vm.Name, vm.Text, .. vm.Facts, vm.StatsSource, vm.MetaSource, vm.MineNote,
                .. vm.Tiles.Concat(vm.MineTiles).SelectMany(t => new[] { t.Label, t.Value, t.Detail }),
                .. vm.Archetypes.SelectMany(a => new[] { a.Name, a.ClassName, a.Share, a.Lists, a.WinRate }),
                .. vm.Mulligan.SelectMany(m => new[] { m.Name, m.KeepRate, m.Impact }),
                Loc.T("Card.Back"), Loc.T("Card.Col.Archetype"), Loc.T("Card.Col.Lists"), Loc.T("Card.Col.WinRate"), Loc.T("Card.Col.Keep"), Loc.T("Card.Col.Impact"),
            ];
            Assert.All(expected, text => Assert.Contains(text, texts));
            Assert.Contains(vm.Archetypes, a => a.Name.Length > 30);
            Assert.True(vm.Name.Length > 40);

            // The columns the compact page has no room for are gone there, and only there.
            string[] wideOnly = [Loc.T("Card.Col.Copies"), Loc.T("Card.Col.Games"), Loc.T("Card.Col.HandWinRate"), Loc.T("Card.Col.Hands")];
            Assert.All(wideOnly, text => Assert.Equal(!compact, texts.Contains(text)));

            // Nothing cut: no ellipsis, no line wider than its block, no block past the page's edge.
            foreach (var block in shown)
            {
                Assert.True(block.TextTrimming == TextTrimming.None, $"{language} {width}: \"{block.Text}\" is trimmed");
                Assert.All(block.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, block.Text));
                Assert.All(block.TextLayout.TextLines, line => Assert.True(line.Width * GlyphScale <= block.Bounds.Width + 0.5,
                    $"{language} {width}: \"{block.Text}\" line {line.Width * GlyphScale} in {block.Bounds.Width}"));
                var left = block.TranslatePoint(default, page)!.Value.X;
                Assert.True(left >= -0.5 && left + block.Bounds.Width <= page.Bounds.Width + 0.5,
                    $"{language} {width}: \"{block.Text}\" at {left}..{left + block.Bounds.Width}, the page is {page.Bounds.Width} wide");
            }

            // A number ends where its head ends; the archetype's name keeps a readable width (its own line in compact).
            TextBlock Shown(string text) => shown.First(t => t.Text == text);
            double Right(TextBlock block) => block.TranslatePoint(new Point(block.Bounds.Width, 0), page)!.Value.X;
            Assert.Equal(Right(Shown(Loc.T("Card.Col.WinRate"))), Right(Shown(vm.Archetypes[0].WinRate)), 1.0);
            Assert.Equal(Right(Shown(Loc.T("Card.Col.Lists"))), Right(Shown(vm.Archetypes[0].Share)), 1.0);
            Assert.Equal(Right(Shown(Loc.T("Card.Col.Impact"))), Right(Shown(vm.Mulligan[0].Impact)), 1.0);
            var name = Shown(vm.Archetypes[0].Name);
            var rate = Shown(vm.Archetypes[0].WinRate);
            Assert.True(name.Bounds.Width >= 140, $"{language} {width}: the name has {name.Bounds.Width}");
            var (nameTop, rateTop) = (name.TranslatePoint(default, page)!.Value.Y, rate.TranslatePoint(default, page)!.Value.Y);
            Assert.Equal(compact, rateTop >= nameTop + name.Bounds.Height - 0.5);

            // The card's name and text: beside the card, or under it in compact.
            var image = Named<Image>("CardImage");
            var title = Named<TextBlock>("CardName");
            var frame = Named<Panel>("CardFrame");
            Assert.Equal(new Size(200, 290), frame.Bounds.Size);
            var (titleAt, frameAt) = (title.TranslatePoint(default, page)!.Value, frame.TranslatePoint(default, page)!.Value);
            Assert.True(compact == titleAt.Y >= frameAt.Y + frame.Bounds.Height - 0.5, $"{language} {width}: title {titleAt}, card {frameAt}");
            Assert.True(compact || titleAt.X >= frameAt.X + frame.Bounds.Width, $"{language} {width}: title {titleAt}, card {frameAt}");
            Assert.Equal("T_LONG", Hearthpulse.UI.Infrastructure.CardArt.GetCard(image));

            // Back: the page under the card returns.
            Named<Button>("BackButton").Command!.Execute(null);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(model.CardOpen);
            Assert.False(scroll.IsVisible);
            Assert.True(view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PageConstructed").IsVisible);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    [Fact]
    public void A_click_on_a_card_row_opens_its_page_and_a_row_without_a_card_does_nothing() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-card-link") + ".db"), new AppSettings(), TestDataFolder.App);
        model.SetCards(Cards("en"));
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 900 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Standard");
            PageRowsPanelAvaloniaTests.Settle(window);
            // A row as the deck page and the mulligan guide build theirs, placed in the page.
            var row = new Border();
            var empty = new Border();
            var host = view.GetVisualDescendants().OfType<Panel>().First(p => p.IsEffectivelyVisible);
            host.Children.Add(row);
            host.Children.Add(empty);
            CardLink.SetCardId(row, "T_LONG");
            Assert.Equal(Avalonia.Input.StandardCursorType.Hand.ToString(), row.Cursor?.ToString());
            Assert.Null(empty.Cursor);

            Assert.False(CardLink.Open(empty));
            Assert.False(model.CardOpen);
            Assert.True(CardLink.Open(row));
            var until = Stopwatch.StartNew();
            while (model.CardPage.Loading && until.Elapsed < TimeSpan.FromSeconds(30))
            {
                Dispatcher.UIThread.RunJobs();
            }

            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(model.CardOpen);
            Assert.Equal("T_LONG", model.CardPage.CardId);
            Assert.True(view.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "PageCard").IsVisible);

            // The link taken off the row: no cursor, no page.
            model.CardPage.Back.Execute(null);
            CardLink.SetCardId(row, null);
            Assert.Null(row.Cursor);
            Assert.False(CardLink.Open(row));
            Assert.False(model.CardOpen);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Runs the UI thread's jobs until the task is done (its continuations come back to this thread).</summary>
    private static T Pump<T>(Task<T> task)
    {
        var until = Stopwatch.StartNew();
        while (!task.IsCompleted && until.Elapsed < TimeSpan.FromSeconds(30))
        {
            Dispatcher.UIThread.RunJobs();
            task.Wait(TimeSpan.FromMilliseconds(15));
        }

        Assert.True(task.IsCompleted, "the card page did not open in time");
        return task.GetAwaiter().GetResult();
    }

    private static CardDb Cards(string language)
    {
        var (name, text) = language == "ru"
            ? ("Повелитель кошмаров Ксавий, владыка Изумрудного Кошмара", "<b>Боевой клич:</b> выберите карту в руке. Перемешайте в колоду 2 |4(копию,копии,копий) этой карты, которые стоят на (2) меньше.")
            : ("Nightmare Lord Xavius, Master of the Emerald Nightmare", "<b>Battlecry:</b> Choose a card in your hand. Shuffle 2 |4(copy,copies) of it into your deck that cost (2) less.");
        var json = $$"""
            [
              {"id": "HERO_10", "dbfId": 56550, "name": "Illidan Stormrage", "type": "HERO", "cardClass": "DEMONHUNTER", "set": "HERO_SKINS"},
              {"id": "HERO_07", "dbfId": 893, "name": "Gul'dan", "type": "HERO", "cardClass": "WARLOCK", "set": "HERO_SKINS"},
              {"id": "T_LONG", "dbfId": 1001, "name": "{{name}}", "cost": 4, "type": "MINION", "cardClass": "NEUTRAL", "set": "EMERALD_DREAM",
               "text": "{{text}}", "collectible": true},
              {"id": "T_FILL", "dbfId": 1002, "name": "Filler", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true}
            ]
            """;
        return CardDb.Parse(language == "ru" ? "ruRU" : "enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Two archetypes under names as long as the Russian labels get, each with lists that run the card and mulligan numbers for it.</summary>
    private static ConstructedMeta Meta(CardDb cards, string language)
    {
        var (first, second) = language == "ru"
            ? ("Охотник на демонов на К'Туне с порождениями Бездны", "Чернокнижник на Ткачихе Бездны с дискардом")
            : ("C'Thun Demon Hunter with Spawns of the Abyss", "Abyss Weaver Discard Warlock");
        MetaDeck Deck(int archetype, string heroClass, int copies) => new(
            DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_LONG"] = copies, ["T_FILL"] = 2 }, heroClass, "FT_STANDARD", cards)!,
            archetype, string.Empty, heroClass, 100, 50, 0.5, default);
        MetaArchetype Archetype(int id, string name, string heroClass, int games, double winRate) => new(
            id, name, name, heroClass, games, (int)(games * winRate), winRate, 0.5, 0, 0, 0, 0, [], [new MetaMulliganCard("T_LONG", 1000, 0.8, 1200 * id, 0.6)]);
        return new ConstructedMeta("standard", "legend", "past_week", new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), 250000,
            [Archetype(1, first, "DEMONHUNTER", 148300, 0.5341), Archetype(2, second, "WARLOCK", 101700, 0.4718)],
            [Deck(1, "DEMONHUNTER", 2), Deck(1, "DEMONHUNTER", 1), Deck(2, "WARLOCK", 1)]);
    }

    private static void AddGame(HistoryStore history)
    {
        var at = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        Assert.True(history.Add(new GameRecord
        {
            Key = "card-layout",
            StartedAt = at,
            EndedAt = at.AddMinutes(8),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            Result = "Win",
            PlayerClass = "DEMONHUNTER",
            OpponentClass = "PRIEST",
            Mulligan = GameCards.MulliganJson(new MulliganFacts(["T_LONG"], [], [])),
            CardsDrawn = GameCards.TurnsJson([new CardTurn("T_LONG", 0)]),
            CardsPlayed = GameCards.TurnsJson([new CardTurn("T_LONG", 4)]),
        }));
    }
}

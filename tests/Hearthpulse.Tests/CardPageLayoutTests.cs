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
            // ... and there the sample they held stands under its number: no win rate or difference without its count.
            string[] compactOnly = [.. vm.Archetypes.Select(a => a.GamesLabel), .. vm.Mulligan.Select(m => m.HandsLabel)];
            Assert.All(compactOnly, text => Assert.Equal(compact, texts.Contains(text)));
            Assert.Equal(Loc.P("Stats.Games", 148300), vm.Archetypes[0].GamesLabel);
            if (compact)
            {
                var sample = shown.First(t => t.Text == vm.Mulligan[0].HandsLabel);
                var number = shown.First(t => t.Text == vm.Mulligan[0].Impact);
                Assert.True(sample.TranslatePoint(default, page)!.Value.Y >= number.TranslatePoint(default, page)!.Value.Y + number.Bounds.Height - 0.5);
                Assert.Equal(sample.TranslatePoint(new Point(sample.Bounds.Width, 0), page)!.Value.X, number.TranslatePoint(new Point(number.Bounds.Width, 0), page)!.Value.X, 1.0);
            }

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

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void A_card_row_is_a_named_button_the_keyboard_reaches_and_opens(string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-card-link") + ".db"), new AppSettings(), TestDataFolder.App);
        var cards = Cards(language);
        model.SetCards(cards);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 900 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Standard");
            PageRowsPanelAvaloniaTests.Settle(window);
            // Rows as the deck page and the mulligan guide build theirs: a bare button around the row, in a list.
            string[] ids = ["T_LONG", "T_FILL", "T_LONG"];
            var list = new ItemsControl
            {
                ItemsSource = ids,
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((id, _) =>
                {
                    var row = new Button { Content = new TextBlock { Text = id } };
                    row.Classes.Add("bare");
                    CardLink.SetCardId(row, id);
                    CardLink.SetCardName(row, cards.NameOf(id));
                    return row;
                }),
            };
            var plain = new Button { Content = "no card" };
            var host = view.GetVisualDescendants().OfType<Panel>().First(p => p.IsEffectivelyVisible);
            host.Children.Add(list);
            host.Children.Add(plain);
            PageRowsPanelAvaloniaTests.Settle(window);
            var rows = list.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(3, rows.Count);

            // A screen reader hears the action and the card; the list is one Tab stop and the rows can take focus.
            Assert.Equal(Loc.F("Card.OpenName", cards.NameOf("T_LONG")), Avalonia.Automation.AutomationProperties.GetName(rows[0]));
            Assert.Contains(cards.NameOf("T_LONG"), Avalonia.Automation.AutomationProperties.GetName(rows[0])!, StringComparison.Ordinal);
            Assert.StartsWith(language == "ru" ? "Открыть страницу карты " : "Open the card page of ", Avalonia.Automation.AutomationProperties.GetName(rows[1])!, StringComparison.Ordinal);
            Assert.Equal(Avalonia.Input.KeyboardNavigationMode.Once, Avalonia.Input.KeyboardNavigation.GetTabNavigation(list));
            Assert.All(rows, r => Assert.True(r.Focusable && r.IsTabStop));
            Assert.NotNull(rows[0].FocusAdorner);
            Assert.Null(Avalonia.Automation.AutomationProperties.GetName(plain));

            // Down and Up move between the rows and stop at the ends.
            void Press(Button on, Avalonia.Input.Key key) =>
                on.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key, Source = on });
            Assert.True(rows[0].Focus());
            Press(rows[0], Avalonia.Input.Key.Down);
            Assert.True(rows[1].IsFocused);
            Press(rows[1], Avalonia.Input.Key.Down);
            Press(rows[2], Avalonia.Input.Key.Down);
            Assert.True(rows[2].IsFocused);
            Press(rows[2], Avalonia.Input.Key.Up);
            Press(rows[1], Avalonia.Input.Key.Up);
            Press(rows[0], Avalonia.Input.Key.Up);
            Assert.True(rows[0].IsFocused);

            // Enter on the focused row opens its card's page; a button without a card opens nothing.
            Assert.False(CardLink.Open(plain));
            Assert.False(model.CardOpen);
            Press(rows[1], Avalonia.Input.Key.Down);
            Press(rows[1], Avalonia.Input.Key.Enter);
            var until = Stopwatch.StartNew();
            while (model.CardPage.Loading && until.Elapsed < TimeSpan.FromSeconds(30))
            {
                Dispatcher.UIThread.RunJobs();
            }

            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(model.CardOpen);
            Assert.Equal("T_FILL", model.CardPage.CardId);
            Assert.True(view.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "PageCard").IsVisible);

            // No list was loaded here (no loader): the section says so and its button asks again.
            var retry = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RetryButton");
            Assert.True(retry.IsEffectivelyVisible);
            Assert.Equal(Loc.T("Card.Stats.Retry"), retry.Content);
            Assert.Same(model.CardPage.Retry, retry.Command);

            // A click does the same as Enter; the link taken off a row leaves a plain button.
            model.CardPage.Back.Execute(null);
            Assert.True(CardLink.Open(rows[0]));
            Assert.True(model.CardOpen);
            model.CardPage.Back.Execute(null);
            CardLink.SetCardId(rows[0], null);
            CardLink.SetCardName(rows[0], null);
            Assert.Null(Avalonia.Automation.AutomationProperties.GetName(rows[0]));
            Assert.False(CardLink.Open(rows[0]));
            Assert.False(model.CardOpen);
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
    public void Enter_on_a_deck_row_moves_the_focus_to_the_page_and_Esc_or_Back_returns_it_to_that_row() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var cards = Cards("en");
        var history = new HistoryStore(TestDataFolder.Scratch("hp-card-focus") + ".db");
        var code = DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_LONG"] = 2, ["T_FILL"] = 2 }, "DEMONHUNTER", "FT_STANDARD", cards)!;
        for (var i = 0; i < 3; i++)
        {
            var at = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local).AddMinutes(i * 10);
            Assert.True(history.Add(new GameRecord
            {
                Key = "card-focus-" + i, StartedAt = at, EndedAt = at.AddMinutes(8), Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
                PlayerClass = "DEMONHUNTER", OpponentClass = "PRIEST", DeckName = "Focus deck", DeckCode = code,
            }));
        }

        var model = new MainViewModel(history, new AppSettings(), TestDataFolder.App);
        model.SetCards(cards);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 1400 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            Pump(Done(model.RefreshAsync()));
            view.ShowPage("Standard");
            var until = Stopwatch.StartNew();
            while (model.DeckFamiliesShown.Count == 0 && until.Elapsed < TimeSpan.FromSeconds(30))
            {
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(model.OpenDeckPage(model.DeckFamiliesShown.Single().Key));
            // The deck page is built off the UI thread.
            until.Restart();
            while (model.DeckPage.DeckList.Count == 0 && until.Elapsed < TimeSpan.FromSeconds(30))
            {
                Dispatcher.UIThread.RunJobs();
            }

            PageRowsPanelAvaloniaTests.Settle(window);
            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var deck = Named<ScrollViewer>("PageDeck");
            var cardPage = Named<ScrollViewer>("PageCard");
            Assert.True(deck.IsVisible);

            // The real rows of the deck page's list: buttons the deck list template built, in a list that is one Tab stop.
            List<Button> Rows() => deck.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && CardLink.GetCardId(b) is { Length: > 0 }).ToList();
            var rows = Rows();
            Assert.True(rows.Count >= 2, $"card rows on the deck page: {rows.Count}");
            var row = rows.First(r => CardLink.GetCardId(r) == "T_LONG");
            var list = row.FindAncestorOfType<ItemsControl>()!;
            Assert.Equal(Avalonia.Input.KeyboardNavigationMode.Once, Avalonia.Input.KeyboardNavigation.GetTabNavigation(list));
            Assert.Equal(Loc.F("Card.OpenName", cards.NameOf("T_LONG")), Avalonia.Automation.AutomationProperties.GetName(row));
            Avalonia.Input.IInputElement? Focused() => TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement();
            void Press(Avalonia.Input.InputElement on, Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers = Avalonia.Input.KeyModifiers.None) =>
                on.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = on });
            void Loaded()
            {
                var wait = Stopwatch.StartNew();
                while (model.CardPage.Loading && wait.Elapsed < TimeSpan.FromSeconds(30))
                {
                    Dispatcher.UIThread.RunJobs();
                }

                PageRowsPanelAvaloniaTests.Settle(window);
            }

            // Enter on the row: the card page is shown and the focus is on its first control, Back.
            Assert.True(row.Focus(Avalonia.Input.NavigationMethod.Tab));
            Press(row, Avalonia.Input.Key.Enter);
            Loaded();
            Assert.True(model.CardOpen);
            Assert.True(cardPage.IsVisible);
            Assert.False(deck.IsVisible);
            var back = Named<Button>("BackButton");
            Assert.Same(back, Focused());
            Assert.Equal(Loc.T("Card.Back"), back.GetVisualDescendants().OfType<TextBlock>().Last().Text);

            // Esc, handled by the window: the deck page returns and the focus is on the row the page was opened from.
            Press(back, Avalonia.Input.Key.Escape);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(model.CardOpen);
            Assert.True(deck.IsVisible);
            Assert.Same(row, Focused());

            // The Back button and Alt+Left do the same.
            Press(row, Avalonia.Input.Key.Enter);
            Loaded();
            Assert.Same(back, Focused());
            back.Command!.Execute(null);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Same(row, Focused());
            Press(row, Avalonia.Input.Key.Enter);
            Loaded();
            Press(back, Avalonia.Input.Key.Left, Avalonia.Input.KeyModifiers.Alt);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(model.CardOpen);
            Assert.Same(row, Focused());

            // The list was built again while the card was open: the focus lands on the same card's row of that list.
            Press(row, Avalonia.Input.Key.Enter);
            Loaded();
            list.ItemsSource = model.DeckPage.DeckList.ToList();
            Press(back, Avalonia.Input.Key.Escape);
            PageRowsPanelAvaloniaTests.Settle(window);
            var landed = Assert.IsType<Button>(Focused());
            Assert.Equal("T_LONG", CardLink.GetCardId(landed));
            Assert.Same(list, landed.FindAncestorOfType<ItemsControl>());

            // A focus the player moved off the card page (the sidebar) is not pulled back to the row.
            Press(landed, Avalonia.Input.Key.Enter);
            Loaded();
            var nav = Named<RadioButton>("NavStandard");
            nav.Focus(Avalonia.Input.NavigationMethod.Tab);
            model.CardPage.Back.Execute(null);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Same(nav, Focused());
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static async Task<bool> Done(Task task)
    {
        await task.ConfigureAwait(true);
        return true;
    }

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

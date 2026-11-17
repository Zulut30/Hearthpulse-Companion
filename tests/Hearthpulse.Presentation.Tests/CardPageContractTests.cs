using System.Text;
using System.Windows.Media;
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

            // Rows as the deck page and the mulligan guide build theirs: a chromeless button around the row, in a list.
            var style = (Style)window.FindResource("CardRowButton");
            Button Row(string id, string name)
            {
                var button = new Button { Style = style, Content = new TextBlock { Text = name } };
                WpfLink.SetCardId(button, id);
                WpfLink.SetCardName(button, name);
                return button;
            }

            var list = new ItemsControl { VerticalAlignment = VerticalAlignment.Top };
            Button[] rows = [Row("T_FILL", "Наполнитель"), Row("T_LONG", "Ксавий"), Row("T_FILL", "Наполнитель")];
            foreach (var button in rows)
            {
                list.Items.Add(button);
            }

            var empty = new Button { Style = style, Content = "no card" };
            var host = (Panel)pageCard.Parent;
            host.Children.Add(list);
            host.Children.Add(empty);
            Pump();
            window.UpdateLayout();

            // A screen reader hears the action and the card; the keyboard reaches the row and sees where it is.
            Assert.Equal(Loc.F("Card.OpenName", "Наполнитель"), System.Windows.Automation.AutomationProperties.GetName(rows[0]));
            Assert.Equal("Открыть страницу карты Наполнитель", System.Windows.Automation.AutomationProperties.GetName(rows[0]));
            Assert.Equal(string.Empty, System.Windows.Automation.AutomationProperties.GetName(empty));
            Assert.All(rows, r => Assert.True(r.Focusable && r.IsTabStop));
            Assert.Same(window.FindResource("FocusRingRow"), rows[0].FocusVisualStyle);
            Assert.Same(System.Windows.Input.Cursors.Hand, rows[0].Cursor);
            // The list is one Tab stop; Down and Up move between its rows and stop at the ends.
            Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(list));
            void Press(Button on, System.Windows.Input.Key key, RoutedEvent routed) => on.RaiseEvent(
                new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(on)!, 0, key) { RoutedEvent = routed });
            rows[0].Focus();
            Press(rows[0], System.Windows.Input.Key.Up, UIElement.PreviewKeyDownEvent);
            Assert.True(rows[0].IsFocused);
            Press(rows[0], System.Windows.Input.Key.Down, UIElement.PreviewKeyDownEvent);
            Assert.True(rows[1].IsFocused);
            Press(rows[1], System.Windows.Input.Key.Down, UIElement.PreviewKeyDownEvent);
            Press(rows[2], System.Windows.Input.Key.Down, UIElement.PreviewKeyDownEvent);
            Assert.True(rows[2].IsFocused);
            Press(rows[2], System.Windows.Input.Key.Up, UIElement.PreviewKeyDownEvent);
            Assert.True(rows[1].IsFocused);

            // The focused row tells a screen reader its place ("2 of 3"); typing a card's first letter jumps to the next
            // row of a card that starts with it, and again to the one after (the shared list behaviors, docs/ui-behaviors.md).
            Assert.Equal((2, 3), (System.Windows.Automation.AutomationProperties.GetPositionInSet(rows[1]), System.Windows.Automation.AutomationProperties.GetSizeOfSet(rows[1])));
            bool Type(Button on, string text)
            {
                var typed = new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, on, text))
                {
                    RoutedEvent = UIElement.TextInputEvent,
                };
                on.RaiseEvent(typed);
                return typed.Handled;
            }

            Assert.True(Type(rows[1], "н"));
            Assert.True(rows[2].IsFocused);
            Assert.True(Type(rows[2], "Н"));
            Assert.True(rows[0].IsFocused);
            Assert.Equal(1, System.Windows.Automation.AutomationProperties.GetPositionInSet(rows[0]));
            // No card starts with "z", and a space stays the button's own: neither is taken.
            Assert.False(Type(rows[0], "z"));
            Assert.False(Type(rows[0], " "));
            Assert.True(rows[0].IsFocused);
            rows[1].Focus();

            Assert.False(WpfLink.Open(empty));
            Assert.False(model.CardOpen);

            // Enter on the focused row opens its card's page.
            Press(rows[1], System.Windows.Input.Key.Enter, UIElement.KeyDownEvent);
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
                    // Match the displayed element's glyph metrics: a detached TextBlock uses the default DPI and
                    // formatting mode, which can report a different width on the CI runner's scaled desktop.
                    var dpi = VisualTreeHelper.GetDpi(block);
                    var text = new FormattedText(block.Text, block.Language.GetSpecificCulture(), block.FlowDirection,
                        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
                        block.FontSize, block.Foreground, null, TextOptions.GetTextFormattingMode(block), dpi.PixelsPerDip);
                    Assert.True(text.WidthIncludingTrailingWhitespace <= block.ActualWidth + 0.5,
                        $"\"{block.Text}\" needs {text.WidthIncludingTrailingWhitespace}, has {block.ActualWidth}; DPI={dpi.PixelsPerDip}");
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

    [Fact]
    public void In_the_wpf_window_Enter_on_a_deck_row_moves_the_focus_to_the_page_and_Esc_or_Back_returns_it_to_that_row() => WpfThread.RunFresh(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        var cards = Cards();
        var history = History();
        var code = DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_LONG"] = 2, ["T_FILL"] = 2 }, "DEMONHUNTER", "FT_STANDARD", cards)!;
        for (var i = 0; i < 3; i++)
        {
            var at = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Local).AddMinutes(i * 10);
            Assert.True(history.Add(new GameRecord
            {
                Key = "card-focus-" + i, StartedAt = at, EndedAt = at.AddMinutes(8), Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
                PlayerClass = "DEMONHUNTER", OpponentClass = "PRIEST", DeckName = "Focus deck", DeckCode = code,
            }));
        }

        var model = new WpfMain(history, new AppSettings(), TestApp.Data);
        model.SetCards(cards);
        model.LoadCardStats = Loader([]);
        var window = OffScreen(new WpfWindow(model));
        window.Show();
        Pump();
        try
        {
            void Until(Func<bool> done)
            {
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!done() && wait.Elapsed < TimeSpan.FromSeconds(30))
                {
                    Pump();
                }

                Assert.True(done());
                Pump();
                window.UpdateLayout();
            }

            var refresh = model.RefreshAsync();
            Until(() => refresh.IsCompleted);
            window.ShowPage(ConstructedPage.Standard);
            Until(() => model.DeckFamiliesShown.Count > 0);
            Assert.True(model.OpenDeckPage(model.DeckFamiliesShown.Single().Key));
            Until(() => model.DeckPage.DeckList.Count > 0);
            var deck = (ScrollViewer)window.FindName("PageDeck");
            var cardPage = (ScrollViewer)window.FindName("PageCard");
            Assert.Equal(Visibility.Visible, deck.Visibility);

            // The real rows of the deck page's list: buttons the deck list template built, in a list that is one Tab stop.
            var rows = Descendants<Button>(deck).Where(b => b.IsVisible && WpfLink.GetCardId(b) is { Length: > 0 }).ToList();
            Assert.True(rows.Count >= 2, $"card rows on the deck page: {rows.Count}");
            var row = rows.First(r => WpfLink.GetCardId(r) == "T_LONG");
            var list = WpfLink.ListOf(row)!;
            Assert.False(list.Focusable);
            Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(list));
            Assert.Same(window.FindResource("FocusRingRow"), row.FocusVisualStyle);
            Assert.Equal(Loc.F("Card.OpenName", cards.NameOf("T_LONG")), System.Windows.Automation.AutomationProperties.GetName(row));
            // Every list of card rows on the page is a list, not a stop of its own.
            Assert.All(Descendants<Button>(deck).Where(b => WpfLink.GetCardId(b) is { Length: > 0 }).Select(b => WpfLink.ListOf(b)!).Distinct(),
                l => Assert.False(l.Focusable));
            object? Focused() => System.Windows.Input.FocusManager.GetFocusedElement(window);
            void Press(UIElement on, System.Windows.Input.Key key) => on.RaiseEvent(
                new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(on)!, 0, key) { RoutedEvent = UIElement.KeyDownEvent });

            // Enter on the row: the card page is shown and the focus is on its first control, Back.
            System.Windows.Input.FocusManager.SetFocusedElement(window, row);
            Press(row, System.Windows.Input.Key.Enter);
            Until(() => model.CardOpen && !model.CardPage.Loading);
            Assert.Equal(Visibility.Visible, cardPage.Visibility);
            Assert.Equal(Visibility.Collapsed, deck.Visibility);
            var back = (Button)window.CardDetailsPage.FindName("BackButton");
            Assert.Same(back, Focused());

            // Esc, handled by the window: the deck page returns and the focus is on the row the page was opened from.
            Press(back, System.Windows.Input.Key.Escape);
            Until(() => !model.CardOpen);
            Assert.Equal(Visibility.Visible, deck.Visibility);
            Assert.Same(row, Focused());

            // The Back button does the same.
            Press(row, System.Windows.Input.Key.Enter);
            Until(() => model.CardOpen && !model.CardPage.Loading);
            Assert.Same(back, Focused());
            back.Command.Execute(null);
            Until(() => !model.CardOpen);
            Assert.Same(row, Focused());

            // The list was built again while the card was open: the focus lands on the same card's row of that list.
            Press(row, System.Windows.Input.Key.Enter);
            Until(() => model.CardOpen && !model.CardPage.Loading);
            list.SetCurrentValue(ItemsControl.ItemsSourceProperty, model.DeckPage.DeckList.ToList());
            Press(back, System.Windows.Input.Key.Escape);
            Until(() => !model.CardOpen);
            var landed = Assert.IsType<Button>(Focused());
            Assert.NotSame(row, landed);
            Assert.Equal("T_LONG", WpfLink.GetCardId(landed));
            Assert.Same(list, WpfLink.ListOf(landed));

            // A focus the player moved off the card page (the sidebar) is not pulled back to the row.
            Press(landed, System.Windows.Input.Key.Enter);
            Until(() => model.CardOpen && !model.CardPage.Loading);
            var nav = (UIElement)window.FindName("NavStandard");
            System.Windows.Input.FocusManager.SetFocusedElement(window, nav);
            model.CardPage.Back.Execute(null);
            Until(() => !model.CardOpen);
            Assert.Same(nav, Focused());
        }
        finally
        {
            window.CloseForReal();
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

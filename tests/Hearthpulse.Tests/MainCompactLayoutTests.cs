using System.ComponentModel;
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
using Hearthpulse.Core.Memory;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia main pages laid out headless at the compact window's narrowest (420), in compact (860), at the
/// narrowest wide window (900), at the WPF minimum (1040) and at the usual width (1280), in Russian and English: what a
/// player reads there is not cut and does not run into its neighbour (bug-hunt #3, #4, #6, #7, #23-#25, #29, #30 and
/// pixel-hunt #12, #13 of TASK-2026-11-25-polish; AC1 of TASK-2026-11-26-release-cleanup and its QA round). What adapts
/// goes by its own width (i:Tight), not the window's, and gives it back when it widens: a recent game's details wrap,
/// and a row narrower than 560 puts its date, result and rating on a line under them that wraps rather than run the
/// date into the result; a deck row narrower than 560 puts its record and win rate under a wrapped name and meta line;
/// Home shows as many deck banners in a row as keep 260 each, their names cut in the middle; two cards side by side
/// stack below 760; a group row narrower than 400 drops its record and bar and wraps its name and games line, a wider one
/// keeps its bar. The rating title is whole, the KPI captions wrap, the "all columns" switch never covers its title, the
/// sort bar stays inside its card and a hero's name has its tip. A game's details never start a line with "·" or split
/// a rank.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class MainCompactLayoutTests
{
    /// <summary>Headless glyphs are as wide as the font size; the app's fonts average about half of that (BgOwnLayoutTests).</summary>
    private const double GlyphScale = 0.6;

    /// <summary>The width under which a group row and its head are tight (StatsResources.axaml, i:Tight).</summary>
    private const double TightBelow = 400;

    /// <summary>The widths under which a recent game, a deck row and a pair of cards are tight.</summary>
    private const double GameRowBelow = 560, DeckRowBelow = 560, PairBelow = 760;

    public MainCompactLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    public static TheoryData<double, string> Widths => new()
    {
        { 420, "ru" },
        { 420, "en" },
        { 860, "ru" },
        { 900, "ru" },
        { 1040, "ru" },
        { 1040, "en" },
        { 1280, "ru" },
    };

    [Theory]
    [MemberData(nameof(Widths))]
    public void Home_keeps_the_games_the_decks_and_the_rating_title_whole(double width, string language) => OnPage(width, language, "Overview", (view, model, compact) =>
    {
        Assert.NotEmpty(model.Recent);
        Assert.NotEmpty(model.HomeDecks);

        // Recent games: the name and details keep their room; the date, the result and the rating sit beside them, or
        // under them in a tight row, whole and apart.
        var rows = Shown<Grid>(view, "gameRow");
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var tight = Tight(row, GameRowBelow);
            var title = Child<StackPanel>(row, "gameTitle");
            var facts = Child<WrapPanel>(row, "gameFacts");
            Assert.True(title.Bounds.Width >= (tight ? row.Bounds.Width - 60 : 220), $"game title {title.Bounds.Width} in {row.Bounds.Width}");
            Assert.Equal(tight, facts.Bounds.Top >= title.Bounds.Bottom - 0.5);
            // The details wrap on any row, whole; "·" sticks to the fact before it and a rank is never split.
            var summary = title.Children.OfType<TextBlock>().Single(t => t.Classes.Contains("gameSummary"));
            Assert.Equal(summary.Text, ToolTip.GetTip(summary));
            Assert.Equal((TextWrapping.Wrap, TextTrimming.None), (summary.TextWrapping, summary.TextTrimming));
            Whole(summary);
            Assert.All(Enumerable.Range(0, summary.Text!.Length).Where(i => summary.Text[i] == '·'), i => Assert.Equal('\u00A0', summary.Text[i - 1]));

            Assert.True(facts.Bounds.Right <= row.Bounds.Width + 0.5, $"the facts end at {facts.Bounds.Right} in {row.Bounds.Width}");
            var cells = facts.Children.Where(c => c.IsVisible).ToList();
            for (var i = 0; i < cells.Count; i++)
            {
                for (var j = i + 1; j < cells.Count; j++)
                {
                    Assert.False(Box(cells[i], row).Intersects(Box(cells[j], row)), $"{Name(cells[i])} {Box(cells[i], row)} runs into {Name(cells[j])} {Box(cells[j], row)}");
                }
            }

            Assert.True(Needs(Child<TextBlock>(facts, "gameWhen")) <= Child<TextBlock>(facts, "gameWhen").Bounds.Width + 0.5, "the date is cut");
            var result = Child<Panel>(facts, "gameResult");
            var plate = (Border)result.Children.Single();
            var plateText = (TextBlock)plate.Child!;
            Assert.True(Needs(plateText) + plate.Padding.Left + plate.Padding.Right + 2 <= result.Bounds.Width + 0.5, $"the result {Needs(plateText)} in {result.Bounds.Width}");
            var rating = Child<TextBlock>(facts, "gameRating");
            Assert.True(!rating.IsVisible || Needs(rating) <= rating.Bounds.Width + 0.5, "the rating is cut");
        }

        var ranked = model.Recent.Single(r => r.Key == "main-compact-0");
        Assert.Contains(MainViewModel.Unbroken(RankInfo.Localize("Platinum 5")!), ranked.Summary, StringComparison.Ordinal);
        Assert.Contains(MainViewModel.Unbroken(Loc.P("Main.Game.Turns", 9)), ranked.Summary, StringComparison.Ordinal);

        // A tight row leaves an empty rating out of its line instead of keeping a blank cell at its end.
        var empty = rows.Select(r => (Row: r, Rating: Child<TextBlock>(Child<WrapPanel>(r, "gameFacts"), "gameRating")))
            .Where(x => string.IsNullOrEmpty(x.Rating.Text)).ToList();
        Assert.NotEmpty(empty);
        Assert.All(empty, x => Assert.Equal(!x.Row.Classes.Contains("tight"), x.Rating.IsVisible));

        // Your decks: as many banners in a row as keep 260 each (three at most), the name beside the art.
        var decks = Shown<UniformGrid>(view, "homeDecks").Single();
        var list = decks.GetVisualAncestors().OfType<ItemsControl>().First();
        Assert.Equal(Math.Clamp((int)(list.Bounds.Width / 272), 1, 3), decks.Columns);
        Assert.Equal(width >= 1280 ? 3 : width is 1040 or 860 ? 2 : 1, decks.Columns);
        foreach (var deck in Shown<Button>(view, "homeDeck"))
        {
            Assert.True(deck.Bounds.Width >= 248, $"deck banner {deck.Bounds.Width}");
            // One in a row takes the whole row; in a row of more, each keeps its 12 to the right.
            var expected = (list.Bounds.Width / decks.Columns) - (decks.Columns == 1 ? 0 : 12);
            Assert.True(Math.Abs(expected - deck.Bounds.Width) <= 1, $"deck banner {deck.Bounds.Width}, expected {expected}");
            var name = deck.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == ((HomeDeck)deck.DataContext!).Name);
            Assert.True(name.Bounds.Width >= 80, $"deck name {name.Bounds.Width}");
            // Cut in the middle, so "… 2" and "… 3" of one deck stay apart (QA Q5).
            Assert.Equal(TextTrimming.PrefixCharacterEllipsis, name.TextTrimming);
            Assert.True(Right(name, deck) <= deck.Bounds.Width + 0.5, $"deck name ends at {Right(name, deck)} in {deck.Bounds.Width}");
            Assert.Equal(name.Text, ToolTip.GetTip(name));
        }

        // The rating card: the title with its icon on a wide page, without it (and whole) in compact.
        var ornament = Named<Ornament>(view, "BgRatingTitle");
        var plain = Named<Ornament>(view, "BgRatingTitleCompact");
        Assert.Equal((!compact, compact), (ornament.IsEffectivelyVisible, plain.IsEffectivelyVisible));
        TitleFits(compact ? plain : ornament);
    });

    [Theory]
    [MemberData(nameof(Widths))]
    public void Standard_keeps_deck_names_group_rows_and_tile_captions_whole(double width, string language) => OnPage(width, language, "Standard", (view, model, compact) =>
    {
        // My stats: the deck name keeps its room; record and win rate under it in a tight row.
        var deckRows = Shown<Grid>(view, "deckRow");
        Assert.NotEmpty(deckRows);
        foreach (var row in deckRows)
        {
            var tight = Tight(row, DeckRowBelow);
            var title = Child<StackPanel>(row, "deckTitle");
            Assert.True(title.Bounds.Width >= (tight ? row.Bounds.Width - 23 : 300), $"deck title {title.Bounds.Width} in {row.Bounds.Width}");
            var record = Child<TextBlock>(row, "deckRecord");
            var win = Child<Panel>(row, "deckWin");
            Assert.Equal(tight, record.Bounds.Top >= title.Bounds.Bottom - 0.5);
            Assert.Equal(tight, win.Bounds.Top >= title.Bounds.Bottom - 0.5);

            // The name (with its tip) and the meta line: cut on a wide row, whole in a tight one.
            var texts = title.Children.OfType<TextBlock>().Where(t => t.Classes.Contains("deckText") && t.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(texts);
            Assert.Equal(texts[0].Text, ToolTip.GetTip(texts[0]));
            Assert.All(texts, t => Assert.Equal(tight ? TextTrimming.None : TextTrimming.CharacterEllipsis, t.TextTrimming));
            if (tight)
            {
                Assert.All(texts, Whole);
            }
            Assert.True(win.Bounds.Right <= row.Bounds.Width + 0.5, $"win rate ends at {win.Bounds.Right} in {row.Bounds.Width}");
        }

        // The KPI captions: whole, on as many lines as they need.
        var details = Shown<TextBlock>(view, "tileDetail");
        Assert.NotEmpty(details);
        Assert.All(details, Whole);

        // The rest of My stats: classes, opponents, archetypes, length, in pairs of cards.
        Open(view, "CtMore");
        PairsStackWhenNarrow(view);
        GroupRowsKeepTheirText(view, model.CtClasses.Count + model.CtOpponents.Count + model.CtArchetypes.Count + model.CtLengths.Count);
    });

    [Theory]
    [MemberData(nameof(Widths))]
    public void Arena_and_brawl_group_rows_keep_class_names_whole(double width, string language) => OnPage(width, language, "Arena", (view, model, compact) =>
    {
        Open(view, "ArenaMore");
        Assert.NotEmpty(model.ArenaClasses);
        Assert.NotEmpty(model.ArenaOpponents);
        PairsStackWhenNarrow(view);
        GroupRowsKeepTheirText(view, model.ArenaClasses.Count + model.ArenaOpponents.Count);
        Assert.All(Shown<TextBlock>(view, "tileDetail"), Whole);

        // The Brawl tables are built off the UI thread (no BackgroundWork seam): their change is the signal.
        view.ShowPage("Brawl");
        Until(model, () => model.BrawlMine.Count > 0, "the Brawl rows");
        Settle(view);
        PairsStackWhenNarrow(view);
        GroupRowsKeepTheirText(view, model.BrawlMine.Count);
        var mine = Named<Ornament>(view, "BrawlMineTitle");
        var plain = Named<Ornament>(view, "BrawlMineTitleCompact");
        Assert.Equal((!compact, compact), (mine.IsEffectivelyVisible, plain.IsEffectivelyVisible));
        TitleFits(compact ? plain : mine);
    });

    [Theory]
    [MemberData(nameof(Widths))]
    public void Battlegrounds_head_sort_bar_and_hero_names_keep_their_room(double width, string language) => OnPage(width, language, "Battlegrounds", (view, model, compact) =>
    {
        // My stats is the page's first tab. The "all columns" switch beside the title or under it, never over it.
        var heroTitle = Named<Ornament>(view, "BgHeroPerfTitle");
        var allColumns = Named<CheckBox>(view, "BgHeroAllColumnsSwitch");
        var head = (Control)heroTitle.GetVisualParent()!;
        Assert.False(Box(heroTitle, head).Intersects(Box(allColumns, head)), $"title {Box(heroTitle, head)} and switch {Box(allColumns, head)}");
        Assert.Equal(compact, allColumns.Bounds.Top >= heroTitle.Bounds.Bottom - 0.5);
        TitleFits(heroTitle);

        // Rating over time beside where you finish, or one under the other on a narrow page.
        PairsStackWhenNarrow(view);
        TitleFits(view.GetVisualDescendants().OfType<Ornament>().Single(o => o.Text == Loc.T("Stats.Bg.PlacesTitle") && o.IsEffectivelyVisible));
        Assert.All(Shown<TextBlock>(view, "tileDetail"), Whole);

        // The sort bar inside its card.
        var bar = Named<Border>(view, "BgSortBar");
        var card = bar.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("card"));
        Assert.True(Right(bar, card) <= card.Bounds.Width - card.Padding.Right + 0.5, $"sort bar ends at {Right(bar, card)} in {card.Bounds.Width - card.Padding.Right}");

        // A hero's name: cut with its tip in both the wide table and the compact one (pixel-hunt #13).
        Assert.NotEmpty(model.BgHeroPerf);
        var names = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && model.BgHeroPerf.Any(h => h.Name == t.Text)).ToList();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Equal(n.Text, ToolTip.GetTip(n)));
    });

    /// <summary>
    /// A heading whole only without the gap before its rule takes that gap (no rule is drawn) instead of ending in an
    /// ellipsis; shorter still, it is cut and carries the whole title as its tip.
    /// </summary>
    [Fact]
    public void A_heading_whole_only_without_its_rule_is_not_cut() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        try
        {
            const string Text = "Ваши игры в этой Потасовке";
            var title = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 16, Brushes.Black).Width;
            foreach (var (width, cut) in new[] { (title + 40, false), (title + 6, false), (title - 6, true) })
            {
                var heading = new Ornament { Text = Text, Size = 13 };
                var window = new Window { Width = width, Height = 120, Content = heading };
                window.Resources["DisplayFont"] = FontFamily.Default;
                window.Show();
                PageRowsPanelAvaloniaTests.Settle(window);
                Assert.Equal(width, heading.Bounds.Width, 1);
                Assert.Equal(cut ? Text : null, ToolTip.GetTip(heading));
                window.Close();
            }
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>
    /// The pairs of cards shown on the page (Grid.statsPair): side by side from <see cref="PairBelow"/> wide, else (and
    /// in compact) the second under the first, each as wide as the pair.
    /// </summary>
    private static void PairsStackWhenNarrow(MainView view)
    {
        var pairs = view.GetVisualDescendants().OfType<Grid>().Where(g => g.IsEffectivelyVisible && g.Classes.Contains("statsPair")).ToList();
        Assert.NotEmpty(pairs);
        foreach (var pair in pairs)
        {
            var stacked = Tight(pair, PairBelow) || view.Classes.Contains("compact");
            var left = pair.Children.Single(c => c.Classes.Contains("statsLeft"));
            var right = pair.Children.Single(c => c.Classes.Contains("statsRight"));
            Assert.Equal(stacked, right.Bounds.Top >= left.Bounds.Bottom - 0.5);
            Assert.Equal(stacked, Math.Abs(right.Bounds.Width - pair.Bounds.Width) < 1);
        }
    }

    /// <summary>The element has its <c>tight</c> class exactly when narrower than <paramref name="below"/>.</summary>
    private static bool Tight(Control element, double below)
    {
        var tight = element.Bounds.Width < below;
        Assert.Equal(tight, element.Classes.Contains("tight"));
        return tight;
    }

    /// <summary>
    /// The group rows (and their heads) shown on the page: tight exactly when narrower than
    /// <see cref="TightBelow"/>; tight, the record and the bar are gone and the name and its line wrap, whole; else
    /// they stay one line, cut with the whole text in the tip. Every name and line keeps its tip.
    /// </summary>
    private static void GroupRowsKeepTheirText(MainView view, int expected)
    {
        Assert.True(expected > 0, "no group rows to check");
        var rows = view.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("groupRow")).ToList();
        Assert.Equal(expected, rows.Count);
        var heads = view.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("groupHead")).ToList();
        Assert.NotEmpty(heads);
        foreach (var row in rows.Concat(heads))
        {
            if (Tight(row, TightBelow))
            {
                Assert.DoesNotContain(row.GetVisualDescendants().OfType<Control>(), c => c.Classes.Contains("wide") && c.IsEffectivelyVisible);
            }
        }

        foreach (var row in rows)
        {
            var texts = row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("groupText")).ToList();
            Assert.Equal(2, texts.Count);
            foreach (var text in texts)
            {
                Assert.Equal(text.Text, ToolTip.GetTip(text));
                if (row.Classes.Contains("tight"))
                {
                    Assert.Equal((TextWrapping.Wrap, TextTrimming.None), (text.TextWrapping, text.TextTrimming));
                    Whole(text);
                }
                else
                {
                    Assert.Equal((TextWrapping.NoWrap, TextTrimming.CharacterEllipsis), (text.TextWrapping, text.TextTrimming));
                }
            }

            // The bar goes with a tight row only: a row of 400 and more keeps it, in compact too (QA Q1).
            var bar = row.GetVisualDescendants().OfType<Panel>().Single(c => c.Classes.Contains("groupBar"));
            Assert.Equal(!row.Classes.Contains("tight"), bar.IsEffectivelyVisible);

            // The win rate stays inside the row.
            var win = row.GetVisualDescendants().OfType<Control>().Single(c => c.Classes.Contains("groupWin"));
            Assert.True(Right(win, row) <= row.Bounds.Width - row.Padding.Right + 0.5, $"win rate ends at {Right(win, row)} in {row.Bounds.Width}");
        }
    }

    /// <summary>
    /// One window narrowed and widened again (1280, 900, 420, 900, 1280): at each width every part that goes by its own
    /// width has its <c>tight</c> class exactly when it is narrower than its threshold, so the class goes again as the
    /// window widens, and Home lays out as many deck banners in a row as the width keeps (QA P0 Q2).
    /// </summary>
    [Fact]
    public void Parts_take_tight_and_give_it_back_as_one_window_narrows_and_widens() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open(1280, "ru");
        try
        {
            view.ShowPage("Standard");
            Settle(view);
            Open(view, "CtMore");
            foreach (var width in new double[] { 1280, 900, 420, 900, 1280 })
            {
                window.Width = width;
                view.ShowPage("Overview");
                Settle(view);
                var games = Shown<Grid>(view, "gameRow");
                Assert.NotEmpty(games);
                Assert.All(games, g => Tight(g, GameRowBelow));
                var decks = Shown<UniformGrid>(view, "homeDecks").Single();
                Assert.Equal(width >= 1280 ? 3 : 1, decks.Columns);

                view.ShowPage("Standard");
                Settle(view);
                var deckRows = Shown<Grid>(view, "deckRow");
                Assert.NotEmpty(deckRows);
                Assert.All(deckRows, r => Tight(r, DeckRowBelow));
                PairsStackWhenNarrow(view);
                GroupRowsKeepTheirText(view, model.CtClasses.Count + model.CtOpponents.Count + model.CtArchetypes.Count + model.CtLengths.Count);
            }

            // Back at full width nothing shown is tight any more, on either page.
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), c => c.IsEffectivelyVisible && c.Classes.Contains("tight"));
            view.ShowPage("Overview");
            Settle(view);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), c => c.IsEffectivelyVisible && c.Classes.Contains("tight"));
        }
        finally
        {
            Close(window, theme);
        }
    });

    /// <summary>
    /// Lays out <paramref name="page"/> of a main view of the test history at <paramref name="width"/> in
    /// <paramref name="language"/>, then runs <paramref name="check"/> (view, model, compact).
    /// </summary>
    private static void OnPage(double width, string language, string page, Action<MainView, MainViewModel, bool> check) => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open(width, language);
        try
        {
            view.ShowPage(page);
            Settle(view);
            var compact = width < 900;
            Assert.Equal(compact, view.Classes.Contains("compact"));
            check(view, model, compact);
        }
        finally
        {
            Close(window, theme);
        }
    });

    /// <summary>
    /// A main view of the test history in a window <paramref name="width"/> wide, in <paramref name="language"/>. The
    /// history and My stats are read on the caller (<see cref="BackgroundWork.Inline"/>); the deck list is built off the
    /// UI thread (no seam), and its change is the signal it is in.
    /// </summary>
    private static (Window Window, MainView View, MainViewModel Model, Avalonia.Styling.Styles Theme) Open(double width, string language)
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var cards = Cards();
        var model = new MainViewModel(History(cards), new AppSettings(), TestDataFolder.App) { Background = BackgroundWork.Inline };
        model.SetCards(cards);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1600 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Resources["DisplayFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        var refresh = model.RefreshAsync();
        Assert.True(refresh.IsCompletedSuccessfully, "the refresh ran on the caller");
        Until(model, () => model.HomeDecks.Count > 0 && model.Decks.Count > 0, "the deck list");
        return (window, view, model, theme);
    }

    private static void Close(Window window, Avalonia.Styling.Styles theme)
    {
        window.Close();
        Application.Current!.Styles.Remove(theme);
        Dispatcher.UIThread.RunJobs();
        Loc.Instance.SetLanguage(Loc.English);
    }

    /// <summary>
    /// Runs the UI thread until <paramref name="done"/> holds, checked on every property change of the view model (work
    /// it does off the UI thread reports back through them); the timer only bounds a hang.
    /// </summary>
    private static void Until(MainViewModel model, Func<bool> done, string what)
    {
        if (!done())
        {
            using var stop = new CancellationTokenSource();
            void Changed(object? sender, PropertyChangedEventArgs e)
            {
                if (done())
                {
                    stop.Cancel();
                }
            }

            model.PropertyChanged += Changed;
            try
            {
                using var backstop = DispatcherTimer.RunOnce(stop.Cancel, TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.MainLoop(stop.Token);
            }
            finally
            {
                model.PropertyChanged -= Changed;
            }
        }

        Assert.True(done(), what + " did not come");
    }

    private static void Settle(MainView view) => PageRowsPanelAvaloniaTests.Settle((Window)TopLevel.GetTopLevel(view)!);

    /// <summary>Opens a "More" part (a ToggleButton.more) of the shown page.</summary>
    private static void Open(MainView view, string toggle)
    {
        view.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == toggle && t.IsEffectivelyVisible).IsChecked = true;
        Settle(view);
    }

    /// <summary>The text laid out whole: no ellipsis, and each line (at the app's glyph widths) within the block.</summary>
    private static void Whole(TextBlock block)
    {
        var layout = block.TextLayout;
        Assert.All(layout.TextLines, line => Assert.False(line.HasCollapsed, $"\"{block.Text}\" is cut"));
        Assert.All(layout.TextLines, line => Assert.True(line.Width * GlyphScale <= block.Bounds.Width + 0.5, $"\"{block.Text}\": a line of {line.Width * GlyphScale} in {block.Bounds.Width}"));
    }

    /// <summary>The width a one-line text needs at the app's glyph widths.</summary>
    private static double Needs(TextBlock block) => string.IsNullOrEmpty(block.Text) ? 0 : new FormattedText(block.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight), block.FontSize, Brushes.Black).Width * GlyphScale;

    /// <summary>
    /// A large heading's title (bold, three points over its size, in the display font, which the test window sets to the
    /// default font) fits beside its icon, if shown, at the app's glyph widths; the rule after it may go.
    /// </summary>
    private static void TitleFits(Ornament title)
    {
        var icon = title.Icon.Length > 0 ? Math.Round(title.Size * 2.3) + 10 : 0;
        var text = new FormattedText(title.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), title.Size + 3, Brushes.Black).Width * GlyphScale;
        Assert.True(icon + text <= title.Bounds.Width + 0.5, $"\"{title.Text}\": {icon + text} in {title.Bounds.Width}");
    }

    private static List<T> Shown<T>(Visual root, string cls) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Where(c => c.Classes.Contains(cls) && c.IsEffectivelyVisible).ToList();

    private static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static T Child<T>(Panel parent, string cls) where T : Control => parent.Children.OfType<T>().Single(c => c.Classes.Contains(cls));

    private static string Name(Control control) => string.Join('.', control.Classes);

    private static double Right(Visual child, Visual ancestor) => child.TranslatePoint(new Point(child.Bounds.Width, 0), ancestor)!.Value.X;

    private static Rect Box(Visual child, Visual ancestor) => new(child.TranslatePoint(default, ancestor)!.Value, child.Bounds.Size);

    private static CardDb Cards()
    {
        const string json = """
            [
              {"id": "HERO_10", "dbfId": 56550, "name": "Illidan Stormrage", "type": "HERO", "cardClass": "DEMONHUNTER", "set": "HERO_SKINS"},
              {"id": "TB_BaconShop_HERO_56", "dbfId": 59201, "name": "Nozdormu on vacation at the Gilded Cove", "type": "HERO", "set": "BATTLEGROUNDS"},
              {"id": "T_LONG", "dbfId": 1001, "name": "Nightmare Lord Xavius", "cost": 4, "type": "MINION", "cardClass": "NEUTRAL", "set": "EMERALD_DREAM", "collectible": true},
              {"id": "T_FILL", "dbfId": 1002, "name": "Filler", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true}
            ]
            """;
        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// Three Ranked games with one deck under a long name, two Battlegrounds games with a rating, an Arena run and a
    /// Tavern Brawl, the classes with the longest names (Demon Hunter, Death Knight) on both sides.
    /// </summary>
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
                DeckName = "Охотник на демонов на К'Туне с порождениями Бездны", DeckCode = code, Rank = i == 0 ? "Platinum 5" : null,
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

        var older = at.AddDays(-3);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(history.Add(new GameRecord
            {
                Key = "main-compact-arena-" + i, StartedAt = older.AddMinutes(i * 12), EndedAt = older.AddMinutes((i * 12) + 10), Mode = "Arena",
                GameType = "GT_ARENA", Format = "FT_WILD", Result = i == 2 ? "Loss" : "Win", Turns = 9, PlayerClass = "DEATHKNIGHT",
                OpponentClass = "DEMONHUNTER", DeckId = 770_001,
            }));
            Assert.True(history.Add(new GameRecord
            {
                Key = "main-compact-brawl-" + i, StartedAt = older.AddHours(2).AddMinutes(i * 12), EndedAt = older.AddHours(2).AddMinutes((i * 12) + 10),
                Mode = "Brawl", GameType = "GT_TAVERNBRAWL", Format = "FT_WILD", Result = i == 0 ? "Loss" : "Win", Turns = 7, PlayerClass = "DEATHKNIGHT",
                OpponentClass = "DEMONHUNTER", ScenarioId = 4242,
            }));
        }

        return history;
    }
}

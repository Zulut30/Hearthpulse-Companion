using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Tests;

/// <summary>
/// The Achievements page in Avalonia, laid out headless (AC24, AC26): the star is a named button in the Tab order that
/// tracks a chain, the journal builds the rows of one subcategory and a secret chain has no star, and the compact
/// layout (420) swaps the table of contents for chips and puts a chain's progress under its name; the status line is
/// read out when it changes. The page is built on the caller (<see cref="BackgroundWork.Inline"/>).
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class AchievementsLayoutTests
{
    public AchievementsLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    private static (Window Window, MainView View, MainViewModel Model, Avalonia.Styling.Styles Theme) Open(double width, AchievementBook? book)
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App)
        {
            Background = BackgroundWork.Inline,
            AchievementsReadable = true,
            SaveSettings = _ => { },
        };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1600 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        view.ShowPage("Achievements");
        model.SetAchievements(book);
        PageRowsPanelAvaloniaTests.Settle(window);
        return (window, view, model, theme);
    }

    private static AchievementsPage Page(MainView view) => view.GetVisualDescendants().OfType<AchievementsPage>().Single();

    [Theory]
    [InlineData(1280, "en")]
    [InlineData(420, "ru")]
    public void A_star_is_a_named_button_in_the_tab_order_that_tracks_its_chain(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var (window, view, model, theme) = Open(width, AchievementsScene.Build(DateTime.UtcNow, confirmed: true));
        try
        {
            Assert.Equal(width < 900, view.Classes.Contains("compact"));
            var stars = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("star") && b.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(stars);
            var star = stars.First(b => b.DataContext is AchievementRow { Tracked: false });
            var row = (AchievementRow)star.DataContext!;
            Assert.Equal(row.StarName, AutomationProperties.GetName(star));
            Assert.True(star.Focusable && star.IsTabStop && star.IsEffectivelyEnabled);

            star.Command!.Execute(star.CommandParameter);
            PageRowsPanelAvaloniaTests.Settle(window);

            Assert.True(row.Tracked);
            Assert.Equal(Loc.F("Achievements.Star.Remove", row.Title), AutomationProperties.GetName(star));
            var tracked = Page(view).TrackedList;
            Assert.Equal(1, tracked.ItemCount);
            Assert.Contains(tracked.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("star") && b.DataContext is AchievementRow { Tracked: true });

            // The bars are the theme's accent (LibraryLook's shareBar.fill over the stats' red bar).
            var bar = view.GetVisualDescendants().OfType<ProgressBar>().First(b => b.Classes.Contains("fill") && b.IsEffectivelyVisible);
            Assert.Equal(((ISolidColorBrush)bar.FindResource("Accent")!).Color, ((ISolidColorBrush)bar.Foreground!).Color);

            // Compact: the categories go under the points, a tracked row's bar and gain under its text.
            var compact = width < 900;
            var categories = Page(view).SummaryCard.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Classes.Contains("statsRight"));
            Assert.Equal((compact ? 1 : 0, compact ? 0 : 2), (Grid.GetRow(categories), Grid.GetColumn(categories)));
            var right = tracked.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("trackedRight"));
            Assert.Equal((compact ? 1 : 0, compact ? 2 : 4), (Grid.GetRow(right), Grid.GetColumn(right)));
            var name = tracked.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("chainName"));
            Assert.True(name.Bounds.Width > 100, $"name {name.Bounds.Width}");
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });

    [Theory]
    [InlineData(1280)]
    [InlineData(420)]
    public void The_journal_holds_one_subcategory_and_compact_swaps_its_contents_for_chips(double width) => LocBindingTests.Headless.Run(() =>
    {
        var book = AchievementsScene.Build(DateTime.UtcNow, confirmed: true);
        var (window, view, model, theme) = Open(width, book);
        try
        {
            var page = Page(view);
            // The journal's rows are built near the page's viewport only: scroll to it.
            page.ShowBrowse();
            PageRowsPanelAvaloniaTests.Settle(window);
            var rows = page.BrowseRows;
            Assert.Equal(model.AchievementBrowseRows.Count, rows.ItemCount);
            Assert.True(rows.ItemCount < book.Chains.Count);
            Assert.All(model.AchievementBrowseRows.OfType<AchievementRow>(), r => Assert.Equal(model.ShownAchievementSubcategory, book.Chain(r.HeadId)!.SubcategoryId));

            // A secret chain shows no star.
            var secret = rows.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("star") && b.DataContext is AchievementRow { Secret: true }).ToList();
            Assert.All(secret, b => Assert.False(b.IsEffectivelyVisible));

            var compact = width < 900;
            Assert.Equal(!compact, page.Subcategories.IsEffectivelyVisible);
            Assert.Equal(compact, page.SubcategoryChips.IsEffectivelyVisible);

            // The progress lines up from row to row (review m3): one column of 176 on the right, in compact the line under
            // the name.
            var progress = rows.GetVisualDescendants().OfType<Grid>().Where(p => p.Classes.Contains("browseProgress") && p.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(progress);
            Assert.Single(progress.Select(p => Math.Round(p.TranslatePoint(default, rows)!.Value.X)).Distinct());
            Assert.All(progress, p => Assert.Equal(compact ? 1 : 0, Grid.GetRow(p)));
            if (!compact)
            {
                Assert.All(progress, p => Assert.Equal(176, p.Bounds.Width));
            }

            // A category's name wraps instead of being cut to a few letters in compact (review m4).
            var names = page.SummaryCard.GetVisualDescendants().OfType<TextBlock>().Where(t => model.AchievementSummary.Categories.Any(c => c.Title == t.Text)).ToList();
            Assert.Equal(model.AchievementSummary.Categories.Count, names.Count);
            Assert.All(names, t => Assert.Equal(TextWrapping.Wrap, t.TextWrapping));

            // Another subcategory replaces the rows.
            model.ShowAchievementSubcategory.Execute(model.AchievementSubcategories[1].Key);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Equal(model.AchievementBrowseRows.Count, rows.ItemCount);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });

    /// <summary>As in <see cref="CollectionLayoutTests"/>: the headless platform gives every character its font size as width.</summary>
    private const double GlyphScale = 0.6;

    /// <summary>
    /// One subcategory: a chain done to its end ("All levels completed"), a long count in progress under a long name, and
    /// a tracked chain whose current tier turned secret (tier 1 seen and done, tier 2 secret).
    /// </summary>
    private static AchievementBook Edges(DateTime now) => new(
        [new AchievementCategory(1, "Общие", "general", 1)],
        [new AchievementSubcategory(11, 1, "Прогресс", 1)],
        [new AchievementSection(111, 11, "Путь героя", 1)],
        new Dictionary<int, AchievementDef>
        {
            [1] = new(1, 111, 1, "Мастер I", "Сыграйте $q игр.", false, 10, 5, 2),
            [2] = new(2, 111, 2, "Мастер II", "Сыграйте $q игр.", false, 20, 10, 0),
            [3] = new(3, 111, 3, "Еженедельное мастерство в испытаниях", "Победите $q раз.", false, 10_000, 10, 0),
            [10] = new(10, 111, 4, "Видимый", "Пройдите путь.", false, 1, 5, 11),
            [11] = new(11, 111, 5, string.Empty, string.Empty, true, 1, 5, 0),
        },
        new Dictionary<int, AchievementState>
        {
            [1] = new(1, AchievementStatus.Completed, 10, now.AddDays(-9)),
            [2] = new(2, AchievementStatus.Completed, 20, now.AddDays(-3)),
            [3] = new(3, AchievementStatus.InProgress, 8_601, null),
            [10] = new(10, AchievementStatus.Completed, 1, now.AddDays(-2)),
        },
        new Dictionary<int, AchievementGain>(),
        now)
    {
        Confirmed = true,
    };

    [Theory]
    [InlineData(420)]
    [InlineData(1280)]
    public void A_journal_rows_progress_fits_its_column_and_a_secret_chains_star_hands_the_focus_on(double width) => LocBindingTests.Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var (window, view, model, theme) = Open(width, Edges(DateTime.UtcNow));
        try
        {
            model.Settings.TrackedAchievements = [10];
            model.SetAchievements(null);
            model.SetAchievements(Edges(DateTime.UtcNow));
            var page = Page(view);
            page.ShowBrowse();
            PageRowsPanelAvaloniaTests.Settle(window);
            var compact = width < 900;

            // Each row's count - the check, "All levels completed" (compact: "Completed") or "8,601 of 10,000" - fits its
            // column of 176 (review m7); in compact it starts the line under the name and the level follows it, never over it.
            var columns = page.BrowseRows.GetVisualDescendants().OfType<Grid>().Where(p => p.Classes.Contains("browseProgress") && p.IsEffectivelyVisible).ToList();
            Assert.Equal(2, columns.Count); // the secret chain has no progress column
            foreach (var column in columns)
            {
                var count = column.Children.OfType<StackPanel>().Single(p => p.Classes.Contains("browseCount"));
                var texts = count.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
                var needed = texts.Sum(t => Natural(t) + t.Margin.Left + t.Margin.Right);
                Assert.True(needed <= column.Bounds.Width + 0.5, $"{string.Join(" ", texts.Select(t => t.Text))}: {needed} > {column.Bounds.Width}");
                var level = column.Children.OfType<TextBlock>().Single(t => t.Classes.Contains("browseLevel"));
                Assert.Equal(compact ? 0 : 1, Grid.GetRow(level));
                if (compact)
                {
                    Assert.True(level.Bounds.X >= count.Bounds.Right, $"the level starts at {level.Bounds.X}, the count ends at {count.Bounds.Right}");
                    Assert.True(level.Bounds.Right <= column.Bounds.Width + 0.5, $"the level ends at {level.Bounds.Right} of {column.Bounds.Width}");
                }
            }

            // A chain's name is one line, trimmed (whole in the row's tip), so no word of it is broken; in compact it has
            // the row's width - as much as the line under it - and its description goes to the tip.
            var names = page.BrowseRows.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("chainName") && t.IsEffectivelyVisible).ToList();
            Assert.Equal(3, names.Count);
            Assert.All(names, n => Assert.Equal((TextWrapping.NoWrap, TextTrimming.CharacterEllipsis), (n.TextWrapping, n.TextTrimming)));
            var longName = names.Single(n => n.DataContext is AchievementRow { HeadId: 3 });
            var longColumn = columns.Single(c => c.DataContext is AchievementRow { HeadId: 3 });
            if (compact)
            {
                Assert.Equal(longColumn.Bounds.Width, longName.Bounds.Width, 1);
                Assert.Equal(longColumn.TranslatePoint(default, page.BrowseRows)!.Value.X, longName.TranslatePoint(default, page.BrowseRows)!.Value.X, 1);
            }

            var done = model.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == 1);
            Assert.Contains(page.BrowseRows.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == (compact ? done.ProgressShort : done.ProgressText));

            // The tracked secret chain's star in the journal: pressed from the keyboard it goes with its tracking, and the
            // focus moves on instead of falling out of the page (review n9).
            var star = page.BrowseRows.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("star") && b.DataContext is AchievementRow { HeadId: 10 });
            Assert.True(star.IsEffectivelyVisible);
            Assert.True(star.Focus(NavigationMethod.Tab));
            star.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            star.Command!.Execute(star.CommandParameter);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Empty(model.Settings.TrackedAchievements);
            Assert.False(star.IsEffectivelyVisible);
            var focused = TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement();
            Assert.NotNull(focused);
            Assert.NotSame(star, focused);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });

    private static double Natural(TextBlock text)
    {
        var probe = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width * GlyphScale;
    }

    [Fact]
    public void The_status_line_is_read_out_and_without_a_book_the_empty_state_says_why() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open(1280, AchievementsScene.Build(DateTime.UtcNow, confirmed: false));
        try
        {
            var page = Page(view);
            Assert.True(page.StatusLine.IsEffectivelyVisible);
            var line = page.StatusLine.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == model.AchievementsStatusLine!.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(line));
            Assert.Contains(page.StatusLine.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.T("Achievements.Mark.Saved") && t.IsEffectivelyVisible);

            model.SetAchievements(null);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(page.StatusLine.IsEffectivelyVisible);
            Assert.True(page.AchievementsEmptyState.IsEffectivelyVisible);
            Assert.Equal(Loc.T("Achievements.Empty.None"), page.AchievementsEmptyState.Content);
            Assert.False(page.AchievementsContent.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });
}

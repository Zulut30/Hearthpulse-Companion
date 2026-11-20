using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Tests;

/// <summary>
/// The Achievements page in Avalonia, laid out headless (AC24, AC26): the star is a named button in the Tab order that
/// tracks a chain, the journal builds the rows of one subcategory and a secret chain has no star, and the compact
/// layout (420) swaps the table of contents for chips; the status line is read out when it changes.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class AchievementsLayoutTests
{
    public AchievementsLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    private static (Window Window, MainView View, MainViewModel Model, Avalonia.Styling.Styles Theme) Open(double width, AchievementBook? book)
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App) { AchievementsReadable = true };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1600 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        view.ShowPage("Achievements");
        model.SetAchievements(book);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return model.AchievementsWork.IsCompleted;
        }, TimeSpan.FromSeconds(30)), "the page was not built");
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

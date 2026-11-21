using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Tests;

/// <summary>
/// The Recap page in Avalonia, laid out headless (AC28): the period's arrows are named buttons whose tip shows on the
/// disabled one too, the hero number is whole at the compact window's narrowest and at the usual width, and the slides
/// follow the recap; an empty period shows its empty state and no slides; a failed recap says so and its button works it
/// out again. The recap is worked out on the caller (<see cref="BackgroundWork.Inline"/>).
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class WrappedLayoutTests
{
    private static readonly DateTime Today = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);

    public WrappedLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    private static GameRecord Game(int day, string mode = "Ranked", int minutes = 12) => new()
    {
        Key = "wl" + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + mode,
        StartedAt = new DateTime(2026, 11, day, 21, 0, 0, DateTimeKind.Local),
        EndedAt = new DateTime(2026, 11, day, 21, minutes, 0, DateTimeKind.Local),
        Mode = mode,
        Result = "Win",
        PlayerClass = "MAGE",
        DeckName = "Sample Mage",
        Placement = mode == "Battlegrounds" ? 2 : 0,
        HeroCardId = mode == "Battlegrounds" ? "TB_BaconShop_HERO_01" : "HERO_08",
    };

    private static (Window Window, MainView View, MainViewModel Model, Avalonia.Styling.Styles Theme) Open(double width, IEnumerable<GameRecord> games,
                                                                                                         BackgroundWork? background = null)
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var history = HistoryStore.InMemory();
        foreach (var game in games)
        {
            history.Add(game);
        }

        var model = new MainViewModel(history, new AppSettings(), TestDataFolder.App) { Background = background ?? BackgroundWork.Inline, WrappedToday = () => Today };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 2400 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        view.ShowPage("Wrapped");
        PageRowsPanelAvaloniaTests.Settle(window);
        return (window, view, model, theme);
    }

    /// <summary>As in <see cref="CollectionLayoutTests"/>: the headless platform gives every character its font size as width.</summary>
    private const double GlyphScale = 0.6;

    /// <summary>The width a text takes on one line with nothing to stop it, in a real font's proportions.</summary>
    private static double Natural(TextBlock text)
    {
        var probe = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight };
        probe.Measure(Avalonia.Size.Infinity);
        return probe.DesiredSize.Width * GlyphScale;
    }

    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Theory]
    [InlineData(1280, "en")]
    [InlineData(420, "ru")]
    public void The_arrows_are_named_and_the_hero_number_is_whole(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var (window, view, model, theme) = Open(width, [Game(2), Game(3), Game(5, "Battlegrounds", 30), Game(6, "Casual")]);
        try
        {
            var page = view.GetVisualDescendants().OfType<WrappedPage>().Single();
            var back = Named<Button>(page, "BackButton");
            var forward = Named<Button>(page, "ForwardButton");
            Assert.Equal(model.WrappedBackName, AutomationProperties.GetName(back));
            Assert.Equal(model.WrappedForwardName, AutomationProperties.GetName(forward));
            Assert.False(forward.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(forward));
            Assert.Equal(Loc.T("Wrapped.Nav.Current"), ToolTip.GetTip(forward));

            var number = Named<TextBlock>(page, "HeroNumber");
            Assert.True(number.IsEffectivelyVisible);
            Assert.Equal("4", number.Text);
            var card = Named<Border>(page, "HeroCard");
            Assert.True(number.Bounds.Width <= card.Bounds.Width, $"{number.Bounds.Width} > {card.Bounds.Width}");
            Assert.Equal(model.WrappedTitle, Named<TextBlock>(page, "PeriodTitle").Text);
            Assert.True(Named<StackPanel>(page, "Slides").IsEffectivelyVisible);

            // Three numbers in one row and each chart in one row, compact too (the window's two-column rule skips "fixed").
            foreach (var grid in page.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Where(g => g.Rows == 1 && g.IsEffectivelyVisible && !g.Classes.Contains("kpis")))
            {
                Assert.Contains("fixed", grid.Classes);
                Assert.Single(grid.Children.Where(c => c.IsVisible).Select(c => c.Bounds.Y).Distinct());
            }

            // The three numbers fit their thirds, and an hour's label ("21") is whole under its narrow bar.
            foreach (var value in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("trioValue") && t.IsEffectivelyVisible))
            {
                Assert.True(Natural(value) <= ((Control)value.Parent!).Bounds.Width + 0.5, $"{value.Text}: {Natural(value)} > {((Control)value.Parent!).Bounds.Width}");
            }

            foreach (var label in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "12" or "21" && t.IsEffectivelyVisible))
            {
                Assert.True(Natural(label) <= label.Bounds.Width + 0.5, $"{label.Text}: {Natural(label)} > {label.Bounds.Width}");
            }

            var favourites = page.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single(g => g.Classes.Contains("favTiles"));
            Assert.Equal(width < 900 ? 1 : 3, favourites.Columns);

            // Days without games (Wednesday, the weekend) keep the count's line: every bar stands on one base (review m2).
            var days = Named<ItemsControl>(page, "DaysChart");
            var bases = days.GetVisualDescendants().OfType<Border>().Where(b => b.Height == 1).ToList();
            Assert.Equal(7, bases.Count);
            Assert.Single(bases.Select(b => Math.Round(b.TranslatePoint(default, days)!.Value.Y)).Distinct());

            // The period's achievements: in compact the date and points go under the name (review m4).
            model.SetAchievements(Core.Progression.AchievementsScene.Build(Today.ToUniversalTime(), confirmed: true));
            PageRowsPanelAvaloniaTests.Settle(window);
            var meta = page.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("recapMeta") && p.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(meta);
            Assert.All(meta, p => Assert.Equal(width < 900 ? 1 : 0, Grid.GetRow(p)));
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });

    [Fact]
    public void An_empty_period_shows_its_empty_state_and_no_slides() => LocBindingTests.Headless.Run(() =>
    {
        var (window, view, model, theme) = Open(1280, [Game(2)]);
        try
        {
            model.ShowWrappedPeriod(new Core.Stats.WrappedPeriod(Core.Stats.WrappedSpan.Month, 2026, 3), clamp: false);
            PageRowsPanelAvaloniaTests.Settle(window);
            var page = view.GetVisualDescendants().OfType<WrappedPage>().Single();
            var empty = Named<ContentControl>(page, "RecapEmpty");
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Equal(Loc.T("Wrapped.Empty.Period"), empty.Content);
            Assert.False(Named<StackPanel>(page, "Slides").IsEffectivelyVisible);
            Assert.False(Named<StackPanel>(page, "HeroNumbers").IsEffectivelyVisible);
            Assert.False(Named<StackPanel>(page, "RecapError").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
        }
    });

    [Fact]
    public void A_failed_recap_says_so_and_its_button_works_it_out_again() => LocBindingTests.Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var work = new FailingWork("WrappedData") { Fail = true };
        var (window, view, model, theme) = Open(1280, [Game(2), Game(3)], work);
        try
        {
            // The history read failed: the hero card says so with "Try again" (no numbers, no empty state, no "working out").
            var page = view.GetVisualDescendants().OfType<WrappedPage>().Single();
            var error = Named<StackPanel>(page, "RecapError");
            Assert.True(error.IsEffectivelyVisible);
            Assert.Contains(error.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Не удалось посчитать итоги." && t.IsEffectivelyVisible);
            var retry = Named<Button>(page, "RetryButton");
            Assert.Equal("Посчитать снова", retry.Content);
            Assert.True(retry.IsEffectivelyEnabled);
            Assert.Same(model.RetryWrapped, retry.Command);
            Assert.False(Named<StackPanel>(page, "HeroNumbers").IsEffectivelyVisible);
            Assert.False(Named<ContentControl>(page, "RecapEmpty").IsEffectivelyVisible);
            Assert.False(model.WrappedFirstComputing);

            work.Fail = false;
            retry.Command!.Execute(retry.CommandParameter);
            PageRowsPanelAvaloniaTests.Settle(window);

            Assert.False(error.IsEffectivelyVisible);
            Assert.True(Named<StackPanel>(page, "HeroNumbers").IsEffectivelyVisible);
            Assert.Equal("2", Named<TextBlock>(page, "HeroNumber").Text);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
            Loc.Instance.SetLanguage(Loc.English);
        }
    });
}

using Avalonia.Controls;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia sidebar: a radio raises IsCheckedChanged for the newly checked button while the old one is still
/// checked, so the page shown is the sender's, not the first checked one (WPF unchecks the group first).
/// </summary>
public sealed partial class LocBindingTests
{
    /// <summary>A game opened from the Overview's recent games moves the nav to History and its page stays open.</summary>
    [Fact]
    public void A_game_opened_off_the_history_page_stays_open() => Headless.Run(() =>
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, $"hp-nav-match-{Guid.NewGuid():N}.db"));
        var game = MatchTimelineTests.Game(null);
        Assert.True(history.Add(game));
        WithMainView((view, model) =>
        {
            view.ShowPage("Overview");
            Dispatcher.UIThread.RunJobs();

            var open = model.OpenMatchPageAsync(game.Key);
            Assert.True(model.MatchOpen);
            Assert.Equal("History", view.CurrentPage);
            // The page is read on this thread (BackgroundWork.Inline): it has landed by now, with no pool thread or
            // clock in between.
            Dispatcher.UIThread.RunJobs();

            Assert.True(open.IsCompletedSuccessfully && open.Result);
            Assert.True(model.MatchOpen);
            Assert.True(view.FindControl<ScrollViewer>("PageMatch")!.IsVisible);

            // Leaving History lets the match go.
            view.ShowPage("Overview");
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.MatchOpen);
        }, model: new MainViewModel(history, new AppSettings(), TestDataFolder.App) { Background = Hearthpulse.UI.Infrastructure.BackgroundWork.Inline });
    });

    /// <summary>Standard → Wild → Standard: each step shows only the format it goes to.</summary>
    [Fact]
    public void Standard_to_wild_shows_only_the_format_it_goes_to() => Headless.Run(() => WithMainView((view, model) =>
    {
        view.ShowPage(ConstructedPage.Standard);
        Dispatcher.UIThread.RunJobs();
        var formats = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ConstructedFormat))
            {
                formats.Add(model.ConstructedFormat);
            }
        };

        view.ShowPage(ConstructedPage.Wild);
        Assert.Equal(ConstructedPage.Wild, view.CurrentPage);
        view.ShowPage(ConstructedPage.Standard);
        Assert.Equal(ConstructedPage.Standard, view.CurrentPage);
        Assert.Equal([ConstructedPage.FormatOf(ConstructedPage.Wild), ConstructedPage.FormatOf(ConstructedPage.Standard)], formats);
    }));

    /// <summary>
    /// The library pages after History (Collection, Achievements, Recap) open by name, each its own page, from their
    /// sidebar items; an unknown name still goes home. The WPF twin is MainNavigationContractTests.
    /// </summary>
    [Fact]
    public void Library_pages_open_by_name_each_on_its_own_page() => Headless.Run(() => WithMainView((view, _) =>
    {
        foreach (var (page, nav, control) in new[] { ("Collection", "NavCollection", "PageCollection"), ("Achievements", "NavAchievements", "PageAchievements"), ("Wrapped", "NavWrapped", "PageWrapped") })
        {
            view.ShowPage(page);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(page, view.CurrentPage);
            Assert.True(view.FindControl<RadioButton>(nav)!.IsChecked);
            Assert.True(view.FindControl<UserControl>(control)!.IsVisible);
        }

        view.ShowPage("Nowhere");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Overview", view.CurrentPage);
    }));
}

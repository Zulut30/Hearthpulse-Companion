using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Tracking;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfRow = Hearthpulse.App.Main.AchievementRow;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The Achievements page in both hosts (docs/achievements-page.md, AC23-AC27): the WPF and the Avalonia view model show
/// the same blocks, rows and status line for the same book in every state, a star press changes both alike, and the
/// WPF page builds every block with the model's texts and named stars. The page is built on the caller
/// (<c>BackgroundWork.Inline</c>). The Avalonia page alone: <c>Hearthpulse.Tests/AchievementsLayoutTests</c>.
/// </summary>
public sealed class AchievementsContractTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(12).ToUniversalTime();

    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    /// <summary>The page's public state as text: every block, its rows and their texts, in order.</summary>
    private static List<string> State(object model)
    {
        string[] names =
        [
            "AchievementsStatusLine", "AchievementsEmpty", "AchievementsEmptyShown", "AchievementsShown", "AchievementsGraphicsOpacity",
            "AchievementSummary", "TrackedAchievementRows", "TrackedAchievementsCount", "TrackedAchievementsFull", "TrackedAchievementsMissing",
            "TrackedAchievementsEmpty", "NearlyDoneRows", "RecentAchievementRows", "AchievementCategories", "AchievementSubcategories",
            "AchievementBrowseRows",
        ];
        var lines = new List<string>();
        foreach (var name in names)
        {
            Describe(name, model.GetType().GetProperty(name)!.GetValue(model), lines, 0);
        }

        return lines;
    }

    private static void Describe(string name, object? value, List<string> lines, int depth)
    {
        switch (value)
        {
            case null:
                lines.Add(name + " = null");
                break;
            case string or bool or int or double or long:
                lines.Add(name + " = " + Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case IEnumerable list:
                var i = 0;
                foreach (var item in list)
                {
                    Describe($"{name}[{i++}]", item, lines, depth + 1);
                }

                lines.Add($"{name}.Count = {i}");
                break;
            default:
                if (depth > 3)
                {
                    break;
                }

                foreach (var property in value.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract").OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    Describe(name + "." + property.Name, property.GetValue(value), lines, depth + 1);
                }

                break;
        }
    }

    [Fact]
    public void Both_view_models_show_the_same_page_for_the_same_book_in_every_state()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline, AchievementsReadable = true };
            var ava = new AvaMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.UI.Infrastructure.BackgroundWork.Inline, AchievementsReadable = true };
            wpf.PageShown("Achievements");
            ava.PageShown("Achievements");

            void Same(string what)
            {
                var (w, a) = (State(wpf), State(ava));
                Assert.True(w.SequenceEqual(a), what + ": " + string.Join(" | ", w.Except(a).Take(5)));
            }

            Same("no book");
            ava.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" });
            wpf.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" });
            Same("no book, the game running");
            wpf.AchievementsReadable = ava.AchievementsReadable = false;
            Same("no book, another system");

            var saved = AchievementsScene.Build(Now, confirmed: false);
            wpf.SetAchievements(saved);
            ava.SetAchievements(saved);
            Same("saved, another system");
            Assert.True(ava.AchievementsStatusLine!.Saved);
            wpf.AchievementsReadable = ava.AchievementsReadable = true;
            Same("saved, the game running");

            var confirmed = AchievementsScene.Build(Now, confirmed: true);
            wpf.SetAchievements(confirmed);
            ava.SetAchievements(confirmed);
            Same("confirmed");
            Assert.False(ava.AchievementsStatusLine!.Saved);

            foreach (var head in new[] { AchievementsScene.TieredHead, AchievementsScene.TierGainHead, AchievementsScene.ProgressGainHead })
            {
                wpf.ToggleAchievementStar.Execute(head.ToString(CultureInfo.InvariantCulture));
                ava.ToggleAchievementStar.Execute(head.ToString(CultureInfo.InvariantCulture));
            }

            Same("three tracked");
            Assert.Equal(3, ava.TrackedAchievementRows.Count);
            wpf.ShowAchievementCategory.Execute("3");
            ava.ShowAchievementCategory.Execute("3");
            wpf.ShowAchievementSubcategory.Execute("32");
            ava.ShowAchievementSubcategory.Execute("32");
            Same("category 3, its second subcategory");

            Loc.Instance.SetLanguage(Loc.English);
            Same("English");
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    [Fact]
    public void The_WPF_page_builds_every_block_with_the_models_texts_and_named_stars() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline, AchievementsReadable = true };
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump();
        try
        {
            window.ShowPage("Achievements");
            model.SetAchievements(AchievementsScene.Build(Now, confirmed: false));
            model.ToggleAchievementStar.Execute(AchievementsScene.TieredHead.ToString(CultureInfo.InvariantCulture));
            Pump();
            var page = window.PageAchievements;
            page.UpdateLayout();

            Assert.True(page.AchievementsContent.IsVisible);
            Assert.False(page.AchievementsEmptyState.IsVisible);
            Assert.True(page.StatusLine.IsVisible);
            var texts = VisualChildren(page).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(model.AchievementsStatusLine!.Text, texts);
            Assert.Contains(Loc.T("Achievements.Mark.Saved"), texts);
            Assert.Contains(model.AchievementSummary.Points, texts);
            Assert.Contains(model.TrackedAchievementRows[0].Detail, texts);
            Assert.Contains(model.NearlyDoneRows[0].NearDetail, texts);
            Assert.Contains(model.RecentAchievementRows[0].Title, texts);

            // The stars: buttons named for their chain, in the Tab order; one per chain row that can be starred.
            var stars = VisualChildren(page).OfType<Button>().Where(b => b.DataContext is WpfRow && b.IsVisible).ToList();
            Assert.NotEmpty(stars);
            Assert.All(stars, b => Assert.Equal(((WpfRow)b.DataContext).StarName, AutomationProperties.GetName(b)));
            Assert.All(stars, b => Assert.True(b.Focusable && KeyboardNavigation.GetIsTabStop(b)));
            Assert.Contains(stars, b => ((WpfRow)b.DataContext).Tracked);

            // The journal: one subcategory, the heading of its named section first.
            Assert.Equal(model.AchievementBrowseRows.Count, page.BrowseRows.Items.Count);
            IEnumerable<string> MenuKeys() => VisualChildren(page.Subcategories).OfType<Button>()
                .Select(b => Assert.IsType<string>(b.CommandParameter));
            Assert.Equal(["11", "12"], MenuKeys());
            model.ShowAchievementCategory.Execute("3");
            Pump();
            Assert.Equal(["31", "32"], MenuKeys());

            // Without a book: the empty state alone.
            model.SetAchievements(null);
            Pump();
            Assert.False(page.AchievementsContent.IsVisible);
            Assert.True(page.AchievementsEmptyState.IsVisible);
            Assert.False(page.StatusLine.IsVisible);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in VisualChildren(child))
            {
                yield return nested;
            }
        }
    }
}

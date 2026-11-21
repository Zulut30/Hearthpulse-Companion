using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;
using WpfWork = Hearthpulse.App.Infrastructure.BackgroundWork;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The Recap page in both hosts (docs/wrapped-page.md, AC28): the WPF and the Avalonia view model show the same recap,
/// period and arrows for the same history and book, month and year alike, and the WPF page shows the model's numbers
/// with named arrows, and a failure with its "Try again" button. The recap is worked out on the caller
/// (<c>BackgroundWork.Inline</c>). The Avalonia page alone: <c>Hearthpulse.Tests/WrappedLayoutTests</c>.
/// </summary>
public sealed class WrappedContractTests
{
    private static readonly DateTime Today = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);

    private static HistoryStore History()
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        var games = new (int Month, int Day, int Hour, string Mode, string Result, int Place)[]
        {
            (10, 1, 20, "Ranked", "Win", 0), (11, 2, 21, "Ranked", "Win", 0), (11, 3, 21, "Ranked", "Loss", 0), (11, 4, 9, "Arena", "Win", 0),
            (11, 8, 22, "Battlegrounds", "Win", 2), (11, 9, 22, "Battlegrounds", "Loss", 7), (11, 10, 21, "Duos", "Win", 1),
        };
        foreach (var (month, day, hour, mode, result, place) in games)
        {
            var start = new DateTime(2026, month, day, hour, 0, 0, DateTimeKind.Local);
            Assert.True(history.Add(new GameRecord
            {
                Key = string.Create(CultureInfo.InvariantCulture, $"wrapped-contract-{month}-{day}-{mode}"),
                StartedAt = start,
                EndedAt = start.AddMinutes(17),
                Mode = mode,
                Result = result,
                Placement = place,
                PlayerClass = mode is "Battlegrounds" or "Duos" ? string.Empty : "PRIEST",
                HeroCardId = mode is "Battlegrounds" or "Duos" ? "TB_BaconShop_HERO_01" : "HERO_09",
                DeckName = mode is "Battlegrounds" or "Duos" ? null : "Sample Priest",
                OpponentName = "Opponent#1234",
            }));
        }

        return history;
    }

    private static List<string> State(object model)
    {
        string[] names =
        [
            "WrappedTitle", "WrappedSpanKey", "WrappedCanGoBack", "WrappedCanGoForward", "WrappedBackTip", "WrappedForwardTip",
            "WrappedBackName", "WrappedForwardName", "WrappedEmpty", "WrappedFailed", "HasRecap", "Recap",
        ];
        var lines = new List<string>();
        foreach (var name in names)
        {
            Describe(name, model.GetType().GetProperty(name)!.GetValue(model), lines);
        }

        return lines;
    }

    private static void Describe(string name, object? value, List<string> lines)
    {
        switch (value)
        {
            case null:
                lines.Add(name + " = null");
                break;
            case string or bool or int or double:
                lines.Add(name + " = " + Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case IEnumerable list:
                var i = 0;
                foreach (var item in list)
                {
                    Describe($"{name}[{i++}]", item, lines);
                }

                lines.Add($"{name}.Count = {i}");
                break;
            default:
                foreach (var property in value.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract").OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    Describe(name + "." + property.Name, property.GetValue(value), lines);
                }

                break;
        }
    }

    [Fact]
    public void Both_view_models_show_the_same_recap_for_the_same_history_and_book()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            var book = AchievementsScene.Build(Today.ToUniversalTime(), confirmed: true);
            var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = WpfWork.Inline, WrappedToday = () => Today };
            var ava = new AvaMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.UI.Infrastructure.BackgroundWork.Inline, WrappedToday = () => Today };
            wpf.SetAchievements(book);
            ava.SetAchievements(book);

            void Same(string what)
            {
                var (w, a) = (State(wpf), State(ava));
                Assert.True(w.SequenceEqual(a), what + ": " + string.Join(" | ", w.Except(a).Take(5)));
            }

            wpf.PageShown("Wrapped");
            ava.PageShown("Wrapped");
            Same("this month");
            Assert.Equal("Ноябрь 2026", ava.WrappedTitle);
            Assert.True(ava.Recap!.HasBattlegrounds);

            wpf.WrappedBack.Execute(null);
            ava.WrappedBack.Execute(null);
            Same("last month");
            wpf.SetWrappedSpan.Execute("Year");
            ava.SetWrappedSpan.Execute("Year");
            Same("the year");
            Loc.Instance.SetLanguage(Loc.English);
            Same("the year in English");
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    private static WpfWindow OffScreen(WpfMain model) => new(model)
    {
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
        ShowActivated = false, ShowInTaskbar = false,
    };

    [Fact]
    public void The_WPF_page_shows_the_models_numbers_with_named_arrows() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = WpfWork.Inline, WrappedToday = () => Today };
        var window = OffScreen(model);
        window.Show();
        Pump();
        try
        {
            window.ShowPage("Wrapped");
            Pump();
            var page = window.PageWrapped;
            page.UpdateLayout();

            Assert.Equal(model.Recap!.Number, page.HeroNumber.Text);
            // The window's Display text formatting draws no 64 px glyph with a gradient brush: the hero number stays Ideal (review m6).
            Assert.Equal(System.Windows.Media.TextFormattingMode.Ideal, System.Windows.Media.TextOptions.GetTextFormattingMode(page.HeroNumber));
            Assert.True(page.HeroNumber.ActualHeight > 40);

            // Days without games keep the count's line: every day's bar stands on one base (review m2).
            var bases = VisualChildren(page.DaysChart).OfType<Border>().Where(b => b.Height == 1).ToList();
            Assert.Equal(7, bases.Count);
            Assert.Single(bases.Select(b => Math.Round(b.TranslatePoint(new Point(0, 0), page.DaysChart).Y)).Distinct());
            Assert.True(page.HeroNumbers.IsVisible);
            Assert.True(page.Slides.IsVisible);
            Assert.Equal(model.WrappedTitle, page.PeriodTitle.Text);
            Assert.Equal(model.WrappedBackName, AutomationProperties.GetName(page.BackButton));
            Assert.Equal(model.WrappedForwardName, AutomationProperties.GetName(page.ForwardButton));
            Assert.False(page.ForwardButton.IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(page.ForwardButton));
            var texts = VisualChildren(page).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(model.Recap.Caption, texts);
            Assert.Contains(model.Recap.Day, texts);
            Assert.DoesNotContain(texts, t => t.Contains("Opponent", StringComparison.Ordinal));

            // An empty period: its empty state, no slides.
            model.ShowWrappedPeriod(new Core.Stats.WrappedPeriod(Core.Stats.WrappedSpan.Month, 2026, 3), clamp: false);
            Pump();
            Assert.True(page.RecapEmpty.IsVisible);
            Assert.False(page.Slides.IsVisible);
            Assert.False(page.RecapError.IsVisible);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    [Fact]
    public void The_WPF_page_says_a_recap_failed_and_its_button_works_it_out_again() => WpfThread.RunFresh(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var work = new FailingWork { Fail = true };
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = work, WrappedToday = () => Today };
        var window = OffScreen(model);
        window.Show();
        Pump();
        try
        {
            window.ShowPage("Wrapped");
            Pump();
            var page = window.PageWrapped;
            page.UpdateLayout();

            Assert.True(page.RecapError.IsVisible);
            Assert.Contains("Не удалось посчитать итоги.", VisualChildren(page.RecapError).OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal("Посчитать снова", page.RetryButton.Content);
            Assert.Same(model.RetryWrapped, page.RetryButton.Command);
            Assert.True(page.RetryButton.IsEnabled);
            Assert.False(page.HeroNumbers.IsVisible);
            Assert.False(page.RecapEmpty.IsVisible);

            work.Fail = false;
            page.RetryButton.Command.Execute(page.RetryButton.CommandParameter);
            Pump();
            Assert.False(page.RecapError.IsVisible);
            Assert.True(page.HeroNumbers.IsVisible);
            Assert.Equal(model.Recap!.Number, page.HeroNumber.Text);
        }
        finally
        {
            window.CloseForReal();
            Pump();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>The recap's work throws while <see cref="Fail"/> is set, as a failed history read would; other work runs at once.</summary>
    private sealed class FailingWork : WpfWork
    {
        public bool Fail { get; set; }

        public override Task<T> RunAsync<T>(Func<T> work) =>
            Fail && typeof(T).Name == "WrappedData" ? Task.FromException<T>(new InvalidOperationException("made-up failure")) : Inline.RunAsync(work);
    }

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

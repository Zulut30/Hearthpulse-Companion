using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.App;

/// <summary>Render-check scenes of the Recap page (docs/wrapped-page.md).</summary>
public partial class App
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=wrapped</c>: the page on the data copy's history and achievement book (the session's
    /// book, else <c>achievements.json</c>): this month (<c>main-wrapped-month</c>, the whole page <c>-month-page</c>),
    /// this year (<c>-year</c>, <c>-year-page</c>), English (<c>-en</c>), a period with nothing in it (<c>-empty</c>) and the
    /// month without an achievement book (<c>-noach</c>). The span, the period and the session's book are put back.
    /// </summary>
    private async Task RenderWrappedAsync(string folder)
    {
        var language = Loc.Instance.Language;
        var book = _session.Achievements ?? new AchievementsStore(AppPaths.Achievements).Load();
        _mainViewModel.SetAchievements(book);
        _main.ShowPage("Wrapped");
        var page = _main.PageWrapped;

        async Task Settle()
        {
            await _mainViewModel.WrappedWork;
            await Task.Delay(1500);
            _main.UpdateLayout();
            (page.Content as ScrollViewer)?.ScrollToTop();
            _main.UpdateLayout();
        }

        async Task Shot(string name, bool whole = false)
        {
            await Settle();
            Save(_main, Path.Combine(folder, $"main-wrapped-{name}.png"));
            if (whole && (page.Content as ScrollViewer)?.Content is FrameworkElement content)
            {
                SaveFull(content, Path.Combine(folder, $"main-wrapped-{name}-page.png"));
            }
        }

        _mainViewModel.ShowWrappedPeriod(Core.Stats.WrappedPeriod.MonthOf(DateTime.Now));
        await Shot("month", whole: true);
        _mainViewModel.SetWrappedSpan.Execute("Year");
        await Shot("year", whole: true);
        _mainViewModel.SetWrappedSpan.Execute("Month");
        _mainViewModel.ShowWrappedPeriod(Core.Stats.WrappedPeriod.MonthOf(DateTime.Now));
        Loc.Instance.SetLanguage(Loc.English);
        await Shot("en");
        Loc.Instance.SetLanguage(language);

        // A month long before the first game: the arrows stop at the range, so the request names it outright.
        _mainViewModel.ShowWrappedPeriod(new Core.Stats.WrappedPeriod(Core.Stats.WrappedSpan.Month, 2001, 1), clamp: false);
        await Shot("empty");

        _mainViewModel.SetAchievements(null);
        _mainViewModel.ShowWrappedPeriod(Core.Stats.WrappedPeriod.MonthOf(DateTime.Now));
        await Shot("noach");

        _mainViewModel.SetAchievements(_session.Achievements);
    }
}

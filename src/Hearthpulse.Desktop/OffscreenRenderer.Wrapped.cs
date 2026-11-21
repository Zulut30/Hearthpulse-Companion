using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the Recap page (docs/wrapped-page.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=wrapped</c>: the page on the data copy's history and achievement book (the session's
    /// book, else <c>achievements.json</c>): this month (<c>main-wrapped-month</c>, the whole page <c>-month-page</c>),
    /// this year (<c>-year</c>, <c>-year-page</c>), English (<c>-en</c>), the compact layout at 420 (<c>-compact</c>), a
    /// period with nothing in it (<c>-empty</c>) and the month without an achievement book (<c>-noach</c>, the whole
    /// page <c>-noach-page</c>). The window's size, the language and the session's book are put back.
    /// </summary>
    private static async Task RenderWrappedAsync(Window main, MainView view, AppHost host, string folder)
    {
        var (width, height) = (main.Width, main.Height);
        var language = Loc.Instance.Language;
        host.Main.SetAchievements(host.Session.Achievements ?? new AchievementsStore(AppPaths.Achievements).Load());
        view.ShowPage("Wrapped");

        async Task Shot(string name, double? tall = null)
        {
            while (!host.Main.WrappedWork.IsCompleted)
            {
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
            }

            if (tall is { } h)
            {
                main.Height = h;
            }

            view.GetVisualDescendants().OfType<WrappedPage>().First().Scroller.ScrollToHome();
            await Save(main, Path.Combine(folder, $"main-wrapped-{name}.png"));
            main.Height = height;
        }

        host.Main.ShowWrappedPeriod(WrappedPeriod.MonthOf(DateTime.Now));
        await Shot("month");
        await Shot("month-page", 3200);
        host.Main.SetWrappedSpan.Execute("Year");
        await Shot("year");
        await Shot("year-page", 3200);
        host.Main.SetWrappedSpan.Execute("Month");
        host.Main.ShowWrappedPeriod(WrappedPeriod.MonthOf(DateTime.Now));
        Loc.Instance.SetLanguage(Loc.English);
        await Shot("en");
        Loc.Instance.SetLanguage(language);
        main.Width = 420;
        await Shot("compact", 3600);
        main.Width = width;

        // A month long before the first game: the arrows stop at the range, so the request names it outright.
        host.Main.ShowWrappedPeriod(new WrappedPeriod(WrappedSpan.Month, 2001, 1), clamp: false);
        await Shot("empty");
        host.Main.SetAchievements(null);
        host.Main.ShowWrappedPeriod(WrappedPeriod.MonthOf(DateTime.Now));
        await Shot("noach");
        await Shot("noach-page", 3200);

        host.Main.SetAchievements(host.Session.Achievements);
    }
}

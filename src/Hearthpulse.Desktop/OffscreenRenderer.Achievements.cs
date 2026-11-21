using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the Achievements page (docs/achievements-page.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=achievements</c>: the page on the data copy's <c>achievements.json</c> (the session's book,
    /// else the file itself) with three chains tracked (<c>main-achievements</c>, the whole page <c>-page</c>), one
    /// subcategory with named sections and a secret chain in the journal (<c>-browse</c>), English (<c>-en</c>), the
    /// compact layout at 420 (<c>-compact</c>) and its journal on a chain done to its end (<c>-browse-compact</c>); then the states on the made-up book of <see cref="AchievementsScene"/>:
    /// none (<c>-none</c>), a system without the read (<c>-unsupported</c>), a saved book (<c>-saved</c>) and ten chains
    /// tracked with the other stars off (<c>-full</c>). Without a book in the copy the first frames show the made-up one.
    /// The tracked chains, the language, the window's size and the session's book are put back afterwards.
    /// </summary>
    private static async Task RenderAchievementsAsync(Window main, MainView view, AppHost host, string folder)
    {
        var (width, height) = (main.Width, main.Height);
        var tracked = host.Settings.TrackedAchievements;
        var language = Loc.Instance.Language;
        var now = DateTime.UtcNow;
        var own = host.Session.Achievements ?? new AchievementsStore(AppPaths.Achievements).Load() ?? AchievementsScene.Build(now, confirmed: false);

        view.ShowPage("Achievements");
        async Task Show(AchievementBook? book)
        {
            host.Main.SetAchievements(book);
            while (!host.Main.AchievementsWork.IsCompleted)
            {
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
            }

            Dispatcher.UIThread.RunJobs();
        }

        void Track(AchievementBook book, int count)
        {
            host.Settings.TrackedAchievements = [];
            foreach (var chain in book.NearlyDone(count).Concat(book.Chains.Where(c => !c.Hidden)).Select(c => c.HeadId).Distinct().Take(count))
            {
                host.Main.ToggleAchievement(chain);
            }
        }

        AchievementsPage Page() => view.GetVisualDescendants().OfType<AchievementsPage>().First();

        void Top() => Page().Scroller.ScrollToHome();

        Task Shot(string name) => Save(main, Path.Combine(folder, $"main-achievements{name}.png"));

        await Show(own);
        Track(own, 3);
        Top();
        await Shot(string.Empty);
        main.Height = 3600;
        await Shot("-page");
        main.Height = height;

        if (AchievementBrowseSample(own) is { } sample)
        {
            host.Main.ShowAchievementCategory.Execute(sample.Category.ToString(System.Globalization.CultureInfo.InvariantCulture));
            host.Main.ShowAchievementSubcategory.Execute(sample.Subcategory.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Dispatcher.UIThread.RunJobs();
        Page().ShowBrowse();
        await Shot("-browse");

        Loc.Instance.SetLanguage(Loc.English);
        Top();
        await Shot("-en");
        Loc.Instance.SetLanguage(language);

        main.Width = 420;
        main.Height = 1500;
        Top();
        await Shot("-compact");

        // The journal in compact, on a subcategory with a chain done to its end ("Completed" in the narrow column).
        if (AchievementDoneSample(own) is { } done)
        {
            host.Main.ShowAchievementCategory.Execute(done.Category.ToString(System.Globalization.CultureInfo.InvariantCulture));
            host.Main.ShowAchievementSubcategory.Execute(done.Subcategory.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Dispatcher.UIThread.RunJobs();
        Page().ShowBrowse();
        await Shot("-browse-compact");
        main.Width = width;
        main.Height = height;

        // The states, on the made-up book.
        host.Settings.TrackedAchievements = [];
        await Show(null);
        Top();
        await Shot("-none");
        host.Main.AchievementsReadable = false;
        await Shot("-unsupported");
        host.Main.AchievementsReadable = true;
        await Show(AchievementsScene.Build(now, confirmed: false));
        Track(host.Main.AchievementBookShown!, 3);
        Top();
        await Shot("-saved");
        await Show(AchievementsScene.Build(now, confirmed: true));
        Track(host.Main.AchievementBookShown!, AppSettings.MaxTrackedAchievements);
        main.Height = 3600;
        Top();
        await Shot("-full");
        main.Height = height;

        host.Settings.TrackedAchievements = tracked;
        host.Main.SetAchievements(host.Session.Achievements);
    }

    /// <summary>The subcategory of the first chain of more than one tier done to its end, else none.</summary>
    private static (int Category, int Subcategory)? AchievementDoneSample(AchievementBook book) =>
        book.Chains.FirstOrDefault(c => c.Completed && c.Tiers.Count > 1) is { } chain ? (chain.CategoryId, chain.SubcategoryId) : null;

    /// <summary>The first subcategory with a named section and a secret chain, else one with a named section, else none.</summary>
    private static (int Category, int Subcategory)? AchievementBrowseSample(AchievementBook book)
    {
        var named = book.Sections.Where(s => s.Name.Length > 0).Select(s => s.Id).ToHashSet();
        var bySub = book.Chains.GroupBy(c => c.SubcategoryId).ToList();
        var pick = bySub.FirstOrDefault(g => g.Any(c => named.Contains(c.SectionId)) && g.Any(c => c.Hidden))
                   ?? bySub.FirstOrDefault(g => g.Any(c => named.Contains(c.SectionId)));
        return pick is null ? null : (pick.First().CategoryId, pick.Key);
    }
}

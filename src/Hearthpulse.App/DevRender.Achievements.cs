using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.App;

/// <summary>Render-check scenes of the Achievements page (docs/achievements-page.md).</summary>
public partial class App
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=achievements</c>: the page on the data copy's <c>achievements.json</c> (the session's
    /// book, else the file itself) with three chains tracked (<c>main-achievements</c>, the whole page <c>-page</c>), one
    /// subcategory with named sections and a secret chain in the journal (<c>-browse</c>), and English (<c>-en</c>);
    /// then the states on the made-up book of <see cref="AchievementsScene"/>: none (<c>-none</c>), a saved book
    /// (<c>-saved</c>), its working out failed (<c>-error</c>), a system without the read (<c>-unsupported</c>) and ten chains tracked with the other stars off
    /// (<c>-full</c>, the whole page <c>-full-page</c>). Without a book in the copy the first frames show the made-up
    /// one. The tracked chains, the language and the session's book are put back afterwards.
    /// </summary>
    private async Task RenderAchievementsAsync(string folder)
    {
        var tracked = _settings.TrackedAchievements;
        var language = Loc.Instance.Language;
        var now = DateTime.UtcNow;
        var own = _session.Achievements ?? new AchievementsStore(AppPaths.Achievements).Load() ?? AchievementsScene.Build(now, confirmed: false);

        _main.ShowPage("Achievements");
        var page = _main.PageAchievements;

        async Task Show(AchievementBook? book)
        {
            _mainViewModel.SetAchievements(book);
            await _mainViewModel.AchievementsWork;
            await Task.Delay(1200);
            _main.UpdateLayout();
        }

        void Track(AchievementBook book, int count)
        {
            _settings.TrackedAchievements = [];
            foreach (var chain in book.NearlyDone(count).Concat(book.Chains.Where(c => !c.Hidden)).Select(c => c.HeadId).Distinct().Take(count))
            {
                _mainViewModel.ToggleAchievement(chain);
            }
        }

        void Top() => (page.Content as ScrollViewer)?.ScrollToTop();

        async Task Shot(string name)
        {
            await Task.Delay(600);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, $"main-achievements{name}.png"));
        }

        await Show(own);
        Track(own, 3);
        Top();
        await Shot(string.Empty);
        if ((page.Content as ScrollViewer)?.Content is FrameworkElement content)
        {
            SaveFull(content, Path.Combine(folder, "main-achievements-page.png"));
        }

        // A subcategory with named sections and, where the book has one, a secret chain.
        if (BrowseSample(own) is { } sample)
        {
            _mainViewModel.ShowAchievementCategory.Execute(sample.Category.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _mainViewModel.ShowAchievementSubcategory.Execute(sample.Subcategory.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        await Task.Delay(600);
        page.ShowBrowse();
        await Shot("-browse");

        Loc.Instance.SetLanguage(Loc.English);
        Top();
        await Shot("-en");
        Loc.Instance.SetLanguage(language);

        // The states, on the made-up book.
        _settings.TrackedAchievements = [];
        await Show(null);
        Top();
        await Shot("-none");
        _mainViewModel.AchievementsReadable = false;
        await Shot("-unsupported");
        _mainViewModel.AchievementsReadable = true;
        await Show(AchievementsScene.Build(now, confirmed: false));
        Track(_mainViewModel.AchievementBookShown!, 3);
        Top();
        await Shot("-saved");
        _mainViewModel.ShowAchievementsFailed();
        await Shot("-error");
        await Show(AchievementsScene.Build(now, confirmed: true));
        Track(_mainViewModel.AchievementBookShown!, AppSettings.MaxTrackedAchievements);
        Top();
        await Shot("-full");
        if ((page.Content as ScrollViewer)?.Content is FrameworkElement full)
        {
            SaveFull(full, Path.Combine(folder, "main-achievements-full-page.png"));
        }

        _settings.TrackedAchievements = tracked;
        _mainViewModel.SetAchievements(_session.Achievements);
    }

    /// <summary>The first subcategory with a named section and a secret chain, else one with a named section, else none.</summary>
    private static (int Category, int Subcategory)? BrowseSample(AchievementBook book)
    {
        var named = book.Sections.Where(s => s.Name.Length > 0).Select(s => s.Id).ToHashSet();
        var bySub = book.Chains.GroupBy(c => c.SubcategoryId).ToList();
        var pick = bySub.FirstOrDefault(g => g.Any(c => named.Contains(c.SectionId)) && g.Any(c => c.Hidden))
                   ?? bySub.FirstOrDefault(g => g.Any(c => named.Contains(c.SectionId)));
        return pick is null ? null : (pick.First().CategoryId, pick.Key);
    }
}

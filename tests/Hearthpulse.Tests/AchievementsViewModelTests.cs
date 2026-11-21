using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Achievements page's view model (docs/achievements-page.md, AC23-AC27) on the made-up book of
/// <see cref="AchievementsScene"/>: the summary, the star and the tracked block, the nearly done and recent lists, the
/// browse view one subcategory at a time, every state's line, an older book worked out last, and a language switch.
/// The page's work runs on the caller (<see cref="BackgroundWork.Inline"/>) unless a test holds it.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class AchievementsViewModelTests : IDisposable
{
    /// <summary>Local noon today: the scene's gains (two hours and a day before) are "today" and "yesterday".</summary>
    private static readonly DateTime Now = DateTime.Today.AddHours(12).ToUniversalTime();

    public AchievementsViewModelTests() => Loc.Instance.SetLanguage(Loc.Russian);

    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static AchievementBook Book(bool confirmed = true) => AchievementsScene.Build(Now, confirmed);

    private static MainViewModel Model(AppSettings? settings = null, BackgroundWork? background = null) =>
        new(HistoryStore.InMemory(), settings ?? new AppSettings(), TestDataFolder.App)
        {
            Background = background ?? BackgroundWork.Inline,
            AchievementsReadable = true,
            SaveSettings = _ => { },
        };

    private static MainViewModel Shown(AchievementBook? book, AppSettings? settings = null)
    {
        var vm = Model(settings);
        vm.SetAchievements(book);
        vm.PageShown("Achievements");
        return vm;
    }

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------ AC23 the summary

    [Fact]
    public void The_summary_shows_the_books_points_and_completed_tiers_in_all_and_by_category()
    {
        var book = Book();
        var vm = Shown(book);
        var summary = book.Summary;

        Assert.True(vm.AchievementsShown);
        Assert.False(vm.AchievementsEmptyShown);
        Assert.Equal(summary.PointsEarned.ToString("N0", CultureInfo.CurrentCulture), vm.AchievementSummary.Points);
        Assert.Equal(Loc.F("Achievements.Completed", summary.Completed, summary.Total), vm.AchievementSummary.Completed);
        Assert.StartsWith(Loc.F("Achievements.PointsOf", summary.PointsTotal, string.Empty).TrimEnd(), vm.AchievementSummary.Of, StringComparison.Ordinal);
        Assert.Equal((double)summary.PointsEarned / summary.PointsTotal, vm.AchievementSummary.Share, 6);

        Assert.Equal(5, vm.AchievementSummary.Categories.Count);
        Assert.Equal(summary.Categories.Select(c => c.Category.Name), vm.AchievementSummary.Categories.Select(c => c.Title));
        Assert.Equal(summary.Categories.Select(c => Loc.F("Achievements.Fraction", c.PointsEarned, c.PointsTotal)), vm.AchievementSummary.Categories.Select(c => c.Fraction));
        Assert.Equal(summary.Categories.Select(c => Loc.F("Achievements.Category.Line", c.Completed, c.Total)), vm.AchievementSummary.Categories.Select(c => c.Line));
        Assert.Equal(["\U000F01A5", "\U000F0787", "\U000F0639", "\U000F034D", "\U000F0297"], vm.AchievementSummary.Categories.Select(c => c.Glyph));
    }

    [Fact]
    public void A_category_of_the_summary_opens_that_category_in_the_journal()
    {
        var vm = Shown(Book());

        vm.ShowAchievementCategory.Execute(vm.AchievementSummary.Categories[2].Key);

        Assert.Equal(3, vm.ShownAchievementCategory);
        Assert.Equal(31, vm.ShownAchievementSubcategory);
        Assert.Equal([false, false, true, false, false], vm.AchievementCategories.Select(c => c.Selected));
    }

    // ------------------------------------------------------------------------------------------ AC24 the star

    [Fact]
    public void A_star_tracks_the_chain_by_its_first_tier_saves_it_and_a_second_press_stops_tracking()
    {
        // The settings go to a file of this test's own: the run's settings.json is written by other tests too.
        var file = TestDataFolder.Scratch("hp-achievements-star") + ".json";
        var settings = new AppSettings();
        var vm = new MainViewModel(HistoryStore.InMemory(), settings, TestDataFolder.App)
        {
            Background = BackgroundWork.Inline,
            AchievementsReadable = true,
            SaveSettings = s => s.Save(file),
        };
        vm.SetAchievements(Book());
        vm.PageShown("Achievements");
        Assert.True(vm.TrackedAchievementsEmpty);
        Assert.False(File.Exists(file));

        vm.ToggleAchievementStar.Execute(Key(AchievementsScene.TieredHead));

        Assert.Equal([AchievementsScene.TieredHead], settings.TrackedAchievements);
        // Written at once, as a restart reads it.
        Assert.Equal([AchievementsScene.TieredHead], AppSettings.Load(file).TrackedAchievements);
        var row = Assert.Single(vm.TrackedAchievementRows);
        Assert.Equal("Коллекционер II", row.Title);
        Assert.Equal("Уровень 2 из 3", row.Level);
        Assert.Equal(Loc.F("Achievements.Progress", 34, 50), row.ProgressText);
        Assert.Equal(0.68, row.Share, 6);
        Assert.Equal("+7", row.GainValue);
        var gainAt = Now.AddHours(-2).ToLocalTime();
        Assert.Equal(" · сегодня в " + gainAt.ToString("t", Loc.DateCulture), row.GainRest);
        Assert.Equal("Снять с отслеживания «Коллекционер II»", row.StarName);
        Assert.True(row.Tracked);
        Assert.Equal(Loc.F("Achievements.Tracked.Count", 1, 10), vm.TrackedAchievementsCount);
        Assert.False(vm.TrackedAchievementsEmpty);

        // The same chain elsewhere on the page lights up in place.
        vm.ShowAchievementSubcategory.Execute("11");
        Assert.True(vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == AchievementsScene.TieredHead).Tracked);

        // A chain whose last gain is a new tier.
        vm.ToggleAchievementStar.Execute(Key(AchievementsScene.TierGainHead));
        Assert.Equal("+1 уровень", vm.TrackedAchievementRows[1].GainValue);
        Assert.Equal(" · вчера", vm.TrackedAchievementRows[1].GainRest);

        vm.ToggleAchievementStar.Execute(Key(AchievementsScene.TieredHead));
        Assert.Equal([AchievementsScene.TierGainHead], settings.TrackedAchievements);
        Assert.Equal([AchievementsScene.TierGainHead], AppSettings.Load(file).TrackedAchievements);
        Assert.Equal([AchievementsScene.TierGainHead], vm.TrackedAchievementRows.Select(r => r.HeadId));
        Assert.False(vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == AchievementsScene.TieredHead).Tracked);
        Assert.Equal("Отслеживать «Коллекционер II»", vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == AchievementsScene.TieredHead).StarName);
        TestDataFolder.TryDelete(file);
    }

    [Fact]
    public void The_tracked_chains_survive_a_restart_and_a_chain_missing_from_the_book_stays_in_the_settings()
    {
        var settings = new AppSettings { TrackedAchievements = [999_999, AchievementsScene.ProgressGainHead] };
        var vm = Shown(Book(), settings);

        var row = Assert.Single(vm.TrackedAchievementRows);
        Assert.Equal(AchievementsScene.ProgressGainHead, row.HeadId);
        Assert.Equal("+3", row.GainValue);
        Assert.True(vm.HasTrackedAchievementsMissing);
        Assert.Equal(Loc.P("Achievements.Tracked.Missing", 1), vm.TrackedAchievementsMissing);
        Assert.Equal([999_999, AchievementsScene.ProgressGainHead], settings.TrackedAchievements);
        Assert.Equal(Loc.F("Achievements.Tracked.Count", 2, 10), vm.TrackedAchievementsCount);
    }

    [Fact]
    public void At_ten_tracked_chains_the_other_stars_are_off_with_the_reason_and_an_eleventh_is_not_added()
    {
        var settings = new AppSettings();
        var book = Book();
        var vm = Shown(book, settings);
        var heads = book.Chains.Where(c => !c.Hidden).Select(c => c.HeadId).Take(11).ToList();

        foreach (var head in heads.Take(10))
        {
            vm.ToggleAchievementStar.Execute(Key(head));
        }

        Assert.True(vm.TrackedAchievementsFull);
        Assert.Equal(Loc.F("Achievements.Tracked.Full", 10), vm.TrackedAchievementsFullText);
        var other = vm.NearlyDoneRows.First(r => !r.Tracked);
        Assert.False(other.StarEnabled);
        Assert.Equal(Loc.P("Achievements.Star.Limit", 10), other.StarTip);
        Assert.StartsWith("Отслеживать «", other.StarName, StringComparison.Ordinal);
        Assert.All(vm.TrackedAchievementRows, r => Assert.True(r.StarEnabled));

        vm.ToggleAchievementStar.Execute(Key(heads[10]));
        Assert.Equal(heads.Take(10), settings.TrackedAchievements);

        // One off: every star is on again.
        vm.ToggleAchievementStar.Execute(Key(heads[0]));
        Assert.True(other.StarEnabled);
        Assert.False(vm.TrackedAchievementsFull);
    }

    // ------------------------------------------------------------------------------------------ AC25 the two lists

    [Fact]
    public void Nearly_done_and_recently_completed_follow_the_book_and_a_secret_completion_is_named_hidden()
    {
        var book = Book();
        var vm = Shown(book);

        Assert.Equal(book.NearlyDone(12).Select(c => c.HeadId), vm.NearlyDoneRows.Select(r => r.HeadId));
        Assert.Equal(12, vm.NearlyDoneRows.Count);
        Assert.All(vm.NearlyDoneRows, r => Assert.True(r.CanStar));
        var first = book.NearlyDone(1)[0];
        Assert.Equal(((double)first.Progress / first.Quota).ToString("P0", CultureInfo.CurrentCulture), vm.NearlyDoneRows[0].Percent);

        var recent = book.RecentlyCompleted(12);
        Assert.Equal(12, vm.RecentAchievementRows.Count);
        var secret = vm.RecentAchievementRows[recent.ToList().FindIndex(c => c.HeadId == 1005)];
        Assert.Equal("Скрытое достижение", secret.Title);
        Assert.True(secret.Muted);
        Assert.Equal(Loc.P("Wrapped.Ach.Points", 15), secret.Points);
    }

    /// <summary>One category, subcategory and section with the given tiers and states (made up, for the edge cases).</summary>
    private static AchievementBook Small(AchievementDef[] tiers, AchievementState[] states) => new(
        [new AchievementCategory(1, "Общие", "general", 1)],
        [new AchievementSubcategory(11, 1, "Прогресс", 1)],
        [new AchievementSection(111, 11, "Путь героя", 1)],
        tiers.ToDictionary(t => t.Id),
        states.ToDictionary(t => t.Id),
        new Dictionary<int, AchievementGain>(),
        Now)
    {
        Confirmed = true,
    };

    [Fact]
    public void Nearly_done_takes_a_quota_of_two_or_more_and_some_progress()
    {
        // Four chains of one tier each, at the edges of the rule: a quota of 1 is done or not (even "in progress" with
        // its 1 of 1), a quota of 2 with 1 counts; no progress at all does not count, the first point does.
        var vm = Shown(Small(
            [
                new AchievementDef(1, 111, 1, "Один шаг", "Сыграйте игру.", false, 1, 5, 0),
                new AchievementDef(2, 111, 2, "Два шага", "Сыграйте $q игры.", false, 2, 5, 0),
                new AchievementDef(3, 111, 3, "Не начато", "Сыграйте $q игр.", false, 50, 5, 0),
                new AchievementDef(4, 111, 4, "Первый шаг", "Сыграйте $q игр.", false, 50, 5, 0),
            ],
            [
                new AchievementState(1, AchievementStatus.InProgress, 1, null),
                new AchievementState(2, AchievementStatus.InProgress, 1, null),
                new AchievementState(3, AchievementStatus.InProgress, 0, null),
                new AchievementState(4, AchievementStatus.InProgress, 1, null),
            ]));

        Assert.Equal([2, 4], vm.NearlyDoneRows.Select(r => r.HeadId));
        Assert.Equal([Loc.F("Achievements.Progress", 1, 2), Loc.F("Achievements.Progress", 1, 50)], vm.NearlyDoneRows.Select(r => r.ProgressText));
    }

    [Fact]
    public void A_chain_not_done_never_shows_a_hundred_percent()
    {
        var done = Now.AddDays(-3);
        var vm = Shown(Small(
            [new AchievementDef(1, 111, 1, "Почти", "Сыграйте $q игр.", false, 500, 10, 0), new AchievementDef(2, 111, 2, "Готово", "Победите.", false, 3, 5, 0)],
            [new AchievementState(1, AchievementStatus.InProgress, 499, null), new AchievementState(2, AchievementStatus.Completed, 3, done)]));

        var near = Assert.Single(vm.NearlyDoneRows);
        Assert.Equal(0.99.ToString("P0", CultureInfo.CurrentCulture), near.Percent);
        Assert.Equal(Loc.F("Achievements.Progress", 499, 500), near.ProgressText);
        var completed = vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == 2);
        Assert.Equal(1.0.ToString("P0", CultureInfo.CurrentCulture), completed.Percent);
        Assert.Equal(Loc.F("Achievements.CompletedOn", done.ToLocalTime()), completed.Date);
        Assert.Contains(completed.Date, completed.Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tracked_chain_whose_current_tier_turned_secret_keeps_its_star_and_a_secret_chain_cannot_be_added()
    {
        // Tier 1 is seen and done, tier 2 is secret and not done: the chain's current tier is secret.
        var book = Small(
            [new AchievementDef(10, 111, 1, "Видимый", "Пройдите путь.", false, 1, 5, 11), new AchievementDef(11, 111, 2, string.Empty, string.Empty, true, 1, 5, 0)],
            [new AchievementState(10, AchievementStatus.Completed, 1, Now.AddDays(-2))]);
        var settings = new AppSettings { TrackedAchievements = [10] };
        var vm = Shown(book, settings);

        var row = Assert.Single(vm.TrackedAchievementRows);
        Assert.True(row.Secret);
        Assert.True(row.CanStar);
        Assert.True(row.StarEnabled);
        Assert.True(vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == 10).CanStar);

        vm.ToggleAchievementStar.Execute("10");
        Assert.Empty(settings.TrackedAchievements);
        Assert.Empty(vm.TrackedAchievementRows);
        var browse = vm.AchievementBrowseRows.OfType<AchievementRow>().Single(r => r.HeadId == 10);
        Assert.False(browse.CanStar);

        // Neither this chain nor the scene's secret one is added by the command.
        vm.ToggleAchievementStar.Execute("10");
        Assert.Empty(settings.TrackedAchievements);
        var scene = Shown(Book(), settings);
        scene.ToggleAchievementStar.Execute("1004");
        Assert.Empty(settings.TrackedAchievements);
        Assert.Empty(scene.TrackedAchievementRows);
    }

    // ------------------------------------------------------------------------------------------ AC26 the journal

    [Fact]
    public void The_journal_shows_one_subcategory_with_its_named_sections_and_keeps_a_secret_chain_secret()
    {
        var vm = Shown(Book());

        Assert.Equal(1, vm.ShownAchievementCategory);
        Assert.Equal(11, vm.ShownAchievementSubcategory);
        Assert.Equal([true, false], vm.AchievementSubcategories.Select(s => s.IsSelected));
        var rows = vm.AchievementBrowseRows;
        Assert.Equal(new AchievementSectionRow("Путь героя"), rows[0]);
        // The unnamed section has no heading: one heading in all.
        Assert.Single(rows.OfType<AchievementSectionRow>());
        var chains = rows.OfType<AchievementRow>().ToList();
        Assert.Equal([1001, 1004, 1005, 1006, 1007], chains.Take(5).Select(r => r.HeadId));
        Assert.All(chains, r => Assert.True(r.HeadId is >= 1000 and < 1300));

        var tiered = chains[0];
        Assert.Equal("Уровень 2 из 3", tiered.Level);
        Assert.Equal(Loc.P("Achievements.PointsCount", 20), tiered.Points);
        Assert.Equal("Соберите 50 разных карт.", tiered.Description);

        // The row shows tier 2, not done: tier 1's date is not its date, in the row or in the tip (review M1).
        var tierOneDone = vm.AchievementBookShown!.States[AchievementsScene.TieredHead].CompletedAt!.Value.ToLocalTime();
        Assert.Equal("—", tiered.Date);
        Assert.DoesNotContain(Loc.F("Achievements.CompletedOn", tierOneDone), tiered.Tip, StringComparison.Ordinal);

        var secret = chains[1];
        Assert.True(secret.Secret);
        Assert.False(secret.CanStar);
        Assert.Equal("Скрытое достижение", secret.Title);
        Assert.Equal(Loc.T("Achievements.HiddenNote"), secret.Description);
        Assert.Equal((string.Empty, string.Empty, false), (secret.ProgressText, secret.Points, secret.ShowsBar));

        var binary = chains[3];
        Assert.Equal((Loc.T("Achievements.NotDone"), false), (binary.ProgressText, binary.ShowsBar));
        var done = chains[4];
        Assert.Equal((Loc.T("Achievements.Done"), true, false), (done.ProgressText, done.Done, done.ShowsBar));
        Assert.NotEqual("—", done.Date);

        vm.ShowAchievementSubcategory.Execute("12");
        Assert.Equal(12, vm.ShownAchievementSubcategory);
        var other = vm.AchievementBrowseRows.OfType<AchievementRow>().Select(r => r.HeadId).ToList();
        Assert.Contains(AchievementsScene.TierGainHead, other);
        Assert.Contains(1310, other);
        Assert.DoesNotContain(AchievementsScene.TieredHead, other);
        Assert.Equal(new AchievementSectionRow("Испытания"), vm.AchievementBrowseRows[0]);
    }

    // ------------------------------------------------------------------------------------------ AC27 the states

    [Theory]
    [InlineData("none", true, false, "Achievements.Empty.None")]
    [InlineData("none", true, true, "Achievements.Empty.NotRead")]
    [InlineData("none", false, false, "Achievements.Empty.Unsupported")]
    public void Without_a_book_the_empty_state_says_why_and_there_is_no_status_line(string state, bool readable, bool running, string key)
    {
        Assert.Equal("none", state);
        var vm = Shown(null);
        vm.AchievementsReadable = readable;
        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = running, Scene = "HUB" });

        Assert.Null(vm.AchievementsStatusLine);
        Assert.True(vm.AchievementsEmptyShown);
        Assert.False(vm.AchievementsShown);
        Assert.Equal(Loc.T(key), vm.AchievementsEmpty);
    }

    [Theory]
    [InlineData(true, false, "Achievements.Status.Saved")]
    [InlineData(true, true, "Achievements.Status.SavedNotRead")]
    [InlineData(false, false, "Achievements.Status.SavedUnsupported")]
    public void A_saved_book_is_marked_with_its_date_and_the_next_step(bool readable, bool running, string key)
    {
        var book = Book(confirmed: false);
        var vm = Shown(book);
        vm.AchievementsReadable = readable;
        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = running, Scene = "HUB" });

        var line = vm.AchievementsStatusLine!;
        Assert.Equal(Loc.F(key, Loc.F("Collection.When.Date", book.ReadAt.ToLocalTime())), line.Text);
        Assert.True(line.Saved);
        Assert.Equal(Loc.T("Achievements.Mark.Saved"), line.Mark);
        Assert.Equal(DeckBuildView.SavedOpacity, vm.AchievementsGraphicsOpacity);
        Assert.All(vm.NearlyDoneRows, r => Assert.Equal(DeckBuildView.SavedOpacity, r.GraphicsOpacity));
    }

    [Fact]
    public void A_book_read_now_says_when_and_carries_no_mark()
    {
        var vm = Shown(Book());

        var line = vm.AchievementsStatusLine!;
        Assert.False(line.Saved);
        Assert.Equal(Loc.F("Achievements.Status.Confirmed", Loc.F("Collection.When.Today", Now.ToLocalTime())), line.Text);
        Assert.Equal(1, vm.AchievementsGraphicsOpacity);
    }

    [Fact]
    public void The_page_is_built_only_once_shown_and_a_newer_book_replaces_the_rows() => LocBindingTests.Headless.Run(() =>
    {
        var held = new HeldWork("AchievementsData");
        var vm = Model(background: held);
        vm.SetAchievements(Book());
        Assert.Equal(0, held.Count);
        Assert.False(vm.AchievementsShown);
        Assert.Equal(Loc.T("Achievements.Loading"), vm.AchievementsEmpty);

        vm.PageShown("Achievements");
        Assert.Equal(1, held.Count);
        held.Release(0);
        Assert.True(vm.AchievementsShown);

        vm.SetAchievements(Book(confirmed: false));
        held.Release(1);
        Assert.True(vm.AchievementsStatusLine!.Saved);
        Assert.All(vm.NearlyDoneRows, r => Assert.Equal(DeckBuildView.SavedOpacity, r.GraphicsOpacity));

        vm.SetAchievements(null);
        Assert.Equal(2, held.Count);
        Assert.False(vm.AchievementsShown);
        Assert.Equal(Loc.T("Achievements.Empty.None"), vm.AchievementsEmpty);
    });

    [Fact]
    public void A_book_worked_out_after_a_newer_one_is_dropped_and_the_old_rows_stay_meanwhile() => LocBindingTests.Headless.Run(() =>
    {
        var held = new HeldWork("AchievementsData");
        var vm = Model(background: held);
        var scene = Book();
        vm.SetAchievements(scene);
        vm.PageShown("Achievements");
        held.Release(0);
        var sceneRows = scene.NearlyDone(MainViewModel.AchievementListLength).Select(c => c.HeadId).ToList();
        Assert.Equal(sceneRows, vm.NearlyDoneRows.Select(r => r.HeadId));

        // Two newer books: the scene's rows stay until the newest is worked out; the one between, done last, is dropped.
        var between = Small([new AchievementDef(1, 111, 1, "Между", "Сыграйте $q игр.", false, 10, 5, 0)], [new AchievementState(1, AchievementStatus.InProgress, 4, null)]);
        var newest = Small([new AchievementDef(2, 111, 1, "Новейшая", "Сыграйте $q игр.", false, 10, 5, 0)], [new AchievementState(2, AchievementStatus.InProgress, 7, null)]);
        vm.SetAchievements(between);
        vm.SetAchievements(newest);
        Assert.Equal(3, held.Count);
        Assert.Equal(sceneRows, vm.NearlyDoneRows.Select(r => r.HeadId));

        held.Release(2);
        Assert.Equal([2], vm.NearlyDoneRows.Select(r => r.HeadId));
        Assert.Equal([2], vm.AchievementBrowseRows.OfType<AchievementRow>().Select(r => r.HeadId));
        held.Release(1);
        Assert.Equal([2], vm.NearlyDoneRows.Select(r => r.HeadId));
        Assert.Equal([2], vm.AchievementBrowseRows.OfType<AchievementRow>().Select(r => r.HeadId));
    });

    // ------------------------------------------------------------------------------------------ language

    [Fact]
    public void A_language_switch_rebuilds_the_rows_without_a_new_book()
    {
        var vm = Shown(Book());
        vm.ToggleAchievementStar.Execute(Key(AchievementsScene.TieredHead));
        Assert.Equal("Уровень 2 из 3", vm.TrackedAchievementRows[0].Level);

        Loc.Instance.SetLanguage(Loc.English);

        Assert.Equal("Level 2 of 3", vm.TrackedAchievementRows[0].Level);
        Assert.Equal("Stop tracking “Коллекционер II”", vm.TrackedAchievementRows[0].StarName);
        Assert.StartsWith("Achievements read from the game today at ", vm.AchievementsStatusLine!.Text, StringComparison.Ordinal);
        Assert.Equal(Loc.F("Achievements.Completed", vm.AchievementBookShown!.Summary.Completed, vm.AchievementBookShown.Summary.Total), vm.AchievementSummary.Completed);
    }
}

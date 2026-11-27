using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC7, the pages' side of "Read data from the game client": with reading off the collection, the
/// achievements and the Battlegrounds rating say so and where to turn it back on (Settings → Data), never "not read
/// yet" or an empty frame; a system without the read keeps its own text first. Made-up data only.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class GameMemoryPagesTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(12).ToUniversalTime();

    public GameMemoryPagesTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.Russian));

    private static MainViewModel Model(AppSettings? settings = null, HistoryStore? history = null)
    {
        var vm = new MainViewModel(history ?? HistoryStore.InMemory(), settings ?? new AppSettings(), TestDataFolder.App)
        {
            Background = BackgroundWork.Inline,
            CollectionReadable = true,
            AchievementsReadable = true,
            SaveSettings = _ => { },
        };
        vm.SetCards(CollectionPageFixture.Cards);
        return vm;
    }

    [Fact]
    public void The_switch_is_on_by_default_and_turning_it_off_tells_the_host_once() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        var changes = 0;
        vm.GameMemoryChanged += () => changes++;
        Assert.True(vm.ReadGameMemory);
        Assert.False(vm.GameMemoryOff);

        vm.ReadGameMemory = false;
        vm.ReadGameMemory = false;

        Assert.False(vm.Settings.ReadGameMemory);
        Assert.True(vm.GameMemoryOff);
        // Off, the switch shows on any system, so the pages' "Settings → Data" always finds it.
        Assert.True(vm.GameMemorySwitchShown);
        Assert.Equal(1, changes);
    });

    [Fact]
    public void With_reading_off_the_collection_says_so_and_where_to_turn_it_on() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model(new AppSettings { ReadGameMemory = false });
        vm.PageShown("Collection");

        // No read will come, client running or not: never "not read yet".
        Assert.Equal(Loc.T("CollectionPage.Empty.Off"), vm.CollectionPageEmpty);
        Assert.Contains("Настройки → Данные", vm.CollectionPageEmpty, StringComparison.Ordinal);
        Assert.Equal(CollectionGlyphs.ReadingOff, vm.CollectionPageEmptyGlyph);
        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true });
        Assert.Equal(Loc.T("CollectionPage.Empty.Off"), vm.CollectionPageEmpty);
        vm.CollectionTabCoins = true;
        Assert.Equal(Loc.T("CollectionPage.Empty.CosmeticsOff"), vm.CollectionPageEmpty);
        vm.CollectionTabSets = true;

        // The top decks' status line and the deck page: no "can build" labels, and no note that waits for a read.
        Assert.Equal(Loc.T("Collection.Status.Off"), vm.CollectionStatus);
        Assert.False(vm.HasCanBuildNote);

        // A saved collection: its date, the mark and the way back.
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        var date = Loc.F("Collection.When.Date", CollectionPageFixture.ReadAt.ToLocalTime());
        Assert.Equal(Loc.F("CollectionPage.Status.SavedOff", date), vm.CollectionPageStatus!.Text);
        Assert.True(vm.CollectionPageStatus.Saved);
        Assert.Equal(Loc.F("Collection.Status.SavedOff", date), vm.CollectionStatus);

        // A system without the read says that first.
        vm.SetCollection(null);
        vm.CollectionReadable = false;
        Assert.Equal(Loc.T("CollectionPage.Empty.Unsupported"), vm.CollectionPageEmpty);
        vm.CollectionReadable = true;

        // Turned back on, at once: the usual next step.
        vm.ReadGameMemory = true;
        Assert.Equal(Loc.T("CollectionPage.Empty.NotRead"), vm.CollectionPageEmpty);
        Assert.Equal(Loc.T("Collection.Status.NotRead"), vm.CollectionStatus);
    });

    [Fact]
    public void With_reading_off_the_achievements_page_says_so_and_a_saved_book_names_the_way_back() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model(new AppSettings { ReadGameMemory = false });
        vm.PageShown("Achievements");
        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" });

        Assert.Equal(Loc.T("Achievements.Empty.Off"), vm.AchievementsEmpty);
        Assert.Equal(CollectionGlyphs.ReadingOff, vm.AchievementsEmptyGlyph);
        Assert.True(vm.AchievementsEmptyShown);
        Assert.Null(vm.AchievementsStatusLine);

        var book = AchievementsScene.Build(Now, confirmed: false);
        vm.SetAchievements(book);
        Assert.Equal(Loc.F("Achievements.Status.SavedOff", Loc.F("Collection.When.Date", book.ReadAt.ToLocalTime())), vm.AchievementsStatusLine!.Text);
        Assert.True(vm.AchievementsStatusLine.Saved);

        vm.SetAchievements(null);
        vm.AchievementsReadable = false;
        Assert.Equal(Loc.T("Achievements.Empty.Unsupported"), vm.AchievementsEmpty);
        vm.AchievementsReadable = true;
        vm.ReadGameMemory = true;
        Assert.Equal(Loc.T("Achievements.Empty.NotRead"), vm.AchievementsEmpty);
    });

    [Fact]
    public void With_reading_off_the_rating_says_why_none_comes() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model(new AppSettings { ReadGameMemory = false });

        // Home's rating tile and My stats: the reason instead of a session change or "no rating yet".
        Assert.Equal(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.OverviewTiles, t => t.Label == Loc.T("Main.Tile.SoloRating")).Detail);
        Assert.Equal(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.BgStatTiles, t => t.Label == Loc.T("Stats.Bg.HighestMmr")).Detail);
        Assert.Equal(Loc.T("Stats.Bg.RatingOff"), vm.BgRatingEmpty);
        // No earlier rated game: the empty state says it all, no second line; the rating card stays for it.
        Assert.False(vm.BgRatingOffShown);
        Assert.True(vm.BgRatingShown);

        // Reading on and still no rating: the rating tiles and card go (release cleanup G-3, G-4).
        vm.ReadGameMemory = true;
        Assert.Equal(Loc.T("Stats.Bg.RatingEmpty"), vm.BgRatingEmpty);
        Assert.DoesNotContain(vm.BgStatTiles, t => t.Label == Loc.T("Stats.Bg.HighestMmr"));
        Assert.DoesNotContain(vm.OverviewTiles, t => t.Label == Loc.T("Main.Tile.SoloRating"));
        Assert.False(vm.BgRatingShown);

        // Off again: both come back in place, saying why.
        vm.ReadGameMemory = false;
        Assert.Equal(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.OverviewTiles, t => t.Label == Loc.T("Main.Tile.SoloRating")).Detail);
        Assert.Equal(Loc.T("Main.Tile.SoloRating"), vm.OverviewTiles[0].Label);
        Assert.Equal(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.BgStatTiles, t => t.Label == Loc.T("Stats.Bg.HighestMmr")).Detail);
        Assert.True(vm.BgRatingShown);
    });

    /// <summary>Review M1: the Constructed rank is read from the client only; with reading off its card and tile say so.</summary>
    [Fact]
    public void With_reading_off_the_constructed_rank_says_why_none_comes() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model(new AppSettings { ReadGameMemory = false });

        Assert.Equal(Loc.T("Stats.Ct.RankOff"), vm.CtRankEmpty);
        Assert.Equal(Loc.T("Stats.Ct.RankOff"), vm.RankOffReason);
        Assert.Contains("Настройки → Данные", vm.CtRankEmpty, StringComparison.Ordinal);
        Assert.Equal(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.CtStatTiles, t => t.Label == Loc.T("Stats.Ct.Rank")).Detail);
        Assert.False(vm.CtRankOffShown);

        vm.ReadGameMemory = true;
        Assert.Equal(Loc.T("Stats.Ct.RankEmpty"), vm.CtRankEmpty);
        Assert.Equal(string.Empty, vm.RankOffReason);
        Assert.Equal(Loc.T("Stats.Ct.RankHidden"), Assert.Single(vm.CtStatTiles, t => t.Label == Loc.T("Stats.Ct.Rank")).Detail);
    });

    /// <summary>
    /// QA Q2, Q6c and the coordinator's rule for P2: earlier games' rating (home and My stats) and rank stay on screen with
    /// one reason line over them while reading is off; on again, the line goes.
    /// </summary>
    [Fact]
    public void With_reading_off_earlier_ratings_and_ranks_stay_under_one_reason_line() => LocBindingTests.Headless.Run(() =>
    {
        var history = HistoryStore.InMemory();
        var start = DateTime.Now.AddDays(-2);
        history.Add(Bg("bg1", start, 6000, 6050));
        history.Add(Bg("bg2", start.AddHours(1), 6050, 6100));
        history.Add(Ranked("ct1", start, 20));
        history.Add(Ranked("ct2", start.AddHours(1), 21));
        var vm = Model(new AppSettings { ReadGameMemory = false }, history);

        Assert.NotEmpty(vm.RatingPoints);
        Assert.NotEmpty(vm.BgRatingChart);
        Assert.NotEmpty(vm.CtRankChart);
        Assert.Equal(Loc.T("Stats.Bg.RatingOff"), vm.RatingOffReason);
        Assert.Equal(Loc.T("Stats.Ct.RankOff"), vm.RankOffReason);
        Assert.True(vm.HomeRatingOffShown);
        Assert.True(vm.BgRatingOffShown);
        Assert.True(vm.CtRankOffShown);

        vm.ReadGameMemory = true;
        Assert.False(vm.HomeRatingOffShown);
        Assert.False(vm.BgRatingOffShown);
        Assert.False(vm.CtRankOffShown);
        Assert.Equal((string.Empty, string.Empty), (vm.RatingOffReason, vm.RankOffReason));
    });

    private static GameRecord Bg(string key, DateTime start, int before, int after) => new()
    {
        Key = key,
        StartedAt = start,
        EndedAt = start.AddMinutes(20),
        Mode = "Battlegrounds",
        HeroCardId = "TB_BaconShop_HERO_72",
        Placement = 3,
        RatingBefore = before,
        RatingAfter = after,
    };

    private static GameRecord Ranked(string key, DateTime start, int stars) => new()
    {
        Key = key,
        StartedAt = start,
        EndedAt = start.AddMinutes(10),
        Mode = "Ranked",
        Format = "FT_STANDARD",
        Result = "Win",
        Turns = 8,
        RankStarLevel = stars,
        PlayerClass = "MAGE",
        OpponentClass = "PRIEST",
    };
}

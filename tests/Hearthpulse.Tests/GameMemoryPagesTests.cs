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

    private static MainViewModel Model(AppSettings? settings = null)
    {
        var vm = new MainViewModel(HistoryStore.InMemory(), settings ?? new AppSettings(), TestDataFolder.App)
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
        // No earlier rated game: the empty state says it all, no second line.
        Assert.False(vm.BgRatingOffShown);

        vm.ReadGameMemory = true;
        Assert.Equal(Loc.T("Stats.Bg.RatingEmpty"), vm.BgRatingEmpty);
        Assert.Equal(Loc.T("Stats.Bg.RatingNotRead"), Assert.Single(vm.BgStatTiles, t => t.Label == Loc.T("Stats.Bg.HighestMmr")).Detail);
        Assert.NotEqual(Loc.T("Main.Tile.ReadingOff"), Assert.Single(vm.OverviewTiles, t => t.Label == Loc.T("Main.Tile.SoloRating")).Detail);
    });
}

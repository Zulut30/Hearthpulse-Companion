using System.Globalization;
using System.Text;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Collection page's view model (docs/collection-page.md) on made-up data only: the sets by filter, the hero skins
/// by class, the coins and the card backs, the status line and the empty state of every state, the count off the UI
/// thread, and the texts after a language switch.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class CollectionPageViewModelTests
{
    public CollectionPageViewModelTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Fact]
    public void Sets_sum_the_filter_and_come_newest_first_with_their_counts() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.PageShown("Collection");

        // Standard: only the Standard set; no group caption on the tiles.
        Assert.True(vm.CollectionTabSets);
        Assert.True(vm.CollectionSetsShown);
        Assert.True(vm.CollectionFilterShown);
        var standard = Assert.Single(vm.CollectionSets);
        Assert.Equal("CATACLYSM", standard.Set);
        Assert.Equal(CardSets.DisplayName("CATACLYSM"), standard.Name);
        Assert.False(standard.HasGroup);
        // 5 of 7 playable copies (2 commons, a rare, 1 of 2 epics; no legendary): 71 %, rounded down.
        Assert.Equal(0.71.ToString("P0", CultureInfo.CurrentCulture), standard.Percent);
        Assert.Equal("5/7", standard.Playable);
        Assert.Equal("3/4", standard.Unique);
        // Missing: one epic (400) and the legendary (1,600) = 2,000 dust; the golden rare fills its slot.
        Assert.Equal(Loc.F("CollectionPage.Set.Dust", 2000L), standard.Dust);
        Assert.False(standard.Complete);
        Assert.Equal(["COMMON", "RARE", "EPIC", "LEGENDARY"], standard.Rarities.Select(r => r.Rarity));
        Assert.Equal("2/2", standard.Rarities[0].Count);
        Assert.Equal("Common", standard.Rarities[0].Name);
        Assert.Contains("Common", standard.Rarities[0].Tip, StringComparison.Ordinal);
        // Golden copies wait for the owner's check.
        Assert.False(standard.HasGolden);
        Assert.Equal(3, vm.CollectionSum.Count);
        Assert.Equal(standard.Percent, vm.CollectionSum[0].Value);
        Assert.Equal(5 / 7.0, vm.CollectionSumFraction, 3);

        // Wild: Standard and Wild sets, newest first, each with its group named; the complete Wild set says so.
        vm.CollectionFilterWild = true;
        Assert.Equal(["CATACLYSM", "TITANS"], vm.CollectionSets.Select(s => s.Set));
        Assert.All(vm.CollectionSets, s => Assert.True(s.HasGroup));
        var titans = vm.CollectionSets[1];
        Assert.True(titans.Complete);
        Assert.Equal(Loc.T("CollectionPage.Set.Done"), titans.Dust);
        Assert.Equal(1.0.ToString("P0", CultureInfo.CurrentCulture), titans.Percent);

        // All: Classic too; its missing cards cannot be crafted, which the tile says in words.
        vm.CollectionFilterAll = true;
        var classic = vm.CollectionSets.Single(s => s.Set == "VANILLA");
        Assert.Equal(Loc.T("CollectionPage.Group.Classic"), classic.Group);
        Assert.True(classic.HasUncraftable);
        Assert.Equal(Loc.T("CollectionPage.Set.UncraftableOnly"), classic.Dust);

        // The render switch shows the golden copies (never a setting).
        vm.CollectionGoldenShown = true;
        Assert.True(vm.CollectionSets.All(s => s.HasGolden));
        Assert.Equal(4, vm.CollectionSum.Count);
    });

    [Fact]
    public void An_empty_filter_says_so_and_keeps_the_filter_to_pick_all() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model(CollectionPageFixture.WildOnlyCards);
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.PageShown("Collection");

        Assert.Empty(vm.CollectionSets);
        Assert.Equal(Loc.T("CollectionPage.Empty.Filter"), vm.CollectionPageEmpty);
        Assert.False(vm.CollectionSetsShown);
        Assert.True(vm.CollectionFilterShown);
        vm.CollectionFilterAll = true;
        Assert.False(vm.HasCollectionPageEmpty);
        Assert.NotEmpty(vm.CollectionSets);
    });

    [Fact]
    public void Hero_skins_show_one_class_at_a_time_owned_first_and_name_the_missing_in_words() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.PageShown("Collection");
        vm.CollectionTabSkins = true;

        // Classes in the game's order (Death Knight before Mage), with "owned/all" on each chip; the first is chosen.
        Assert.Equal(["DEATHKNIGHT", "MAGE"], vm.CollectionSkinClasses.Select(c => c.Key));
        Assert.True(vm.CollectionSkinClasses[0].Selected);
        Assert.Equal(Loc.F("CollectionPage.Chip", Loc.T("Core.Class.MAGE"), 1, 2), vm.CollectionSkinClasses[1].Label);
        Assert.Equal(Loc.F("CollectionPage.Owned", 1, 3), vm.CollectionSkinsOwned);

        vm.SetCollectionSkinClass.Execute("MAGE");
        Assert.Equal(["SKIN_MAGE_B", "SKIN_MAGE_A"], vm.CollectionSkins.Select(s => s.CardId));
        var owned = vm.CollectionSkins[0];
        var missing = vm.CollectionSkins[1];
        Assert.True(owned.Owned);
        Assert.False(owned.HasBadge);
        Assert.Equal(1, owned.ArtOpacity);
        Assert.False(missing.Owned);
        Assert.Equal(Loc.T("CollectionPage.Item.Out"), missing.State);
        Assert.Equal(CollectionGlyphs.Lock, missing.Badge);
        Assert.Equal(MainViewModel.NotOwnedOpacity, missing.ArtOpacity);
        Assert.Contains(Loc.T("CollectionPage.Item.Out"), missing.A11y, StringComparison.Ordinal);
        Assert.Equal("Mage", vm.CollectionSkinGroup!.Name);
        Assert.Equal("HERO_08", vm.CollectionSkinGroup.HeroCardId);
        Assert.Equal(Loc.F("CollectionPage.OfTotal", 1, 2), vm.CollectionSkinGroup.Count);
    });

    [Fact]
    public void Coins_take_the_card_name_and_art_else_the_client_name_with_favourites_first() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.SetCosmetics(CollectionPageFixture.Cosmetics());
        vm.PageShown("Collection");
        vm.CollectionTabCoins = true;

        Assert.True(vm.CollectionCoinsShown);
        Assert.Equal(Loc.F("CollectionPage.Owned", 2, 5), vm.CollectionCoinsOwned);
        // The favourite, the other owned, then the ones not owned.
        Assert.Equal(["2", "1", "3", "4", "5"], vm.CollectionCoins.Select(c => c.Key));
        var favourite = vm.CollectionCoins[0];
        Assert.True(favourite.Favourite);
        Assert.Equal(CollectionGlyphs.Heart, favourite.Badge);
        Assert.Contains(Loc.T("CollectionPage.Item.Favourite"), favourite.Tip, StringComparison.Ordinal);
        // A coin whose card the card data has: the card's name and art; one it lacks: the client's name, no art.
        Assert.Equal("Coin Card", vm.CollectionCoins[1].Name);
        Assert.Equal("COIN_CARD", vm.CollectionCoins[1].CardId);
        Assert.Equal("Client coin", vm.CollectionCoins[2].Name);
        Assert.False(vm.CollectionCoins[2].HasArt);
        Assert.Equal(Loc.T("CollectionPage.Item.Out"), vm.CollectionCoins[2].State);
        // Cards that all read "The Coin" do not tell their coins apart: the client's names do, the cards' art stays.
        Assert.Equal(["Stormwind Coin", "Lordaeron Coin"], vm.CollectionCoins.Skip(3).Select(c => c.Name));
        Assert.Equal(["COIN_A", "COIN_B"], vm.CollectionCoins.Skip(3).Select(c => c.CardId));
    });

    [Fact]
    public void Card_backs_group_by_category_with_other_last_and_draw_their_medallion_by_id() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetCosmetics(CollectionPageFixture.Cosmetics());
        vm.PageShown("Collection");
        vm.CollectionTabBacks = true;

        Assert.True(vm.CollectionBacksShown);
        // Base, Seasonal, then None and Unknown together as "Other"; empty groups are left out.
        Assert.Equal(["Base", "Seasonal", "Unknown"], vm.CollectionBackGroups.Select(g => g.Key));
        Assert.Equal(Loc.F("CollectionPage.Chip", Loc.T("CollectionPage.Backs.Group.Other"), 0, 2), vm.CollectionBackGroups[2].Label);
        Assert.Equal(Loc.F("CollectionPage.Owned", 2, 5), vm.CollectionBacksOwned);

        // Base: the first group, in the game's order; the id 0 back is the classic one.
        Assert.Equal([0, 4], vm.CollectionBacks.Select(b => b.Id));
        var classic = vm.CollectionBacks[0];
        Assert.Equal((9, 0.0), (classic.Lobes, classic.Angle));
        Assert.True(classic.Favourite);
        Assert.Contains(classic.Description, classic.A11y, StringComparison.Ordinal);
        var other = vm.CollectionBacks[1];
        Assert.Equal((4, 45.0), (other.Lobes, other.Angle));
        Assert.Equal(Loc.T("CollectionPage.Item.Out"), other.State);

        vm.SetCollectionBackGroup.Execute("Unknown");
        Assert.Equal([5, 9], vm.CollectionBacks.Select(b => b.Id));
        Assert.Equal(Loc.T("CollectionPage.Backs.Group.Other"), vm.CollectionBackGroup!.Name);
    });

    [Fact]
    public void Every_state_has_its_line_and_no_made_up_zero() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.PageShown("Collection");

        // No collection: no line, the empty state that names the next step (by the client and the system).
        Assert.Null(vm.CollectionPageStatus);
        Assert.Equal(Loc.T("CollectionPage.Empty.None"), vm.CollectionPageEmpty);
        Assert.Empty(vm.CollectionSum);
        vm.CollectionReadable = false;
        Assert.Equal(Loc.T("CollectionPage.Empty.Unsupported"), vm.CollectionPageEmpty);
        vm.CollectionReadable = true;

        // A saved collection: its date, the mark, dimmed pictures (never the words).
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        var saved = vm.CollectionPageStatus!;
        Assert.True(saved.Saved);
        Assert.Equal(Loc.T("CollectionPage.Mark.Saved"), saved.Mark);
        Assert.Equal(Loc.F("CollectionPage.Status.Saved", Loc.F("Collection.When.Date", CollectionPageFixture.ReadAt.ToLocalTime())), saved.Text);
        Assert.Equal(DeckBuildView.SavedOpacity, vm.CollectionArtOpacity);
        Assert.All(vm.CollectionSets, s => Assert.Equal(DeckBuildView.SavedOpacity, s.ArtOpacity));
        vm.CollectionReadable = false;
        Assert.Equal(Loc.F("CollectionPage.Status.SavedUnsupported", Loc.F("Collection.When.Date", CollectionPageFixture.ReadAt.ToLocalTime())), vm.CollectionPageStatus!.Text);
        vm.CollectionReadable = true;

        // Confirmed: read from the game, no mark, full strength.
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: true));
        Assert.False(vm.CollectionPageStatus!.Saved);
        Assert.Equal(CollectionGlyphs.Check, vm.CollectionPageStatus.Glyph);
        Assert.Equal(1, vm.CollectionArtOpacity);

        // The coins and backs follow the cosmetics: none yet while the collection is known.
        vm.CollectionTabCoins = true;
        Assert.Null(vm.CollectionPageStatus);
        Assert.Equal(Loc.T("CollectionPage.Empty.NoCosmetics"), vm.CollectionPageEmpty);
        vm.CollectionReadable = false;
        Assert.Equal(Loc.T("CollectionPage.Empty.CosmeticsUnsupported"), vm.CollectionPageEmpty);
    });

    [Fact]
    public void Card_data_not_loaded_says_so_and_the_count_fills_in_when_it_comes() => LocBindingTests.Headless.Run(() =>
    {
        var vm = new MainViewModel(History(), new AppSettings(), TestDataFolder.App) { Background = BackgroundWork.Inline };
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.PageShown("Collection");

        Assert.Equal(Loc.T("CollectionPage.Empty.NoCards"), vm.CollectionPageEmpty);
        Assert.NotNull(vm.CollectionPageStatus);
        Assert.False(vm.CollectionFilterShown);

        vm.SetCards(CollectionPageFixture.Cards);
        Assert.False(vm.HasCollectionPageEmpty);
        Assert.Single(vm.CollectionSets);
    });

    [Fact]
    public void The_count_runs_off_the_ui_thread_keeps_the_old_one_meanwhile_and_drops_a_stale_one() => LocBindingTests.Headless.Run(() =>
    {
        var held = new HeldWork();
        var vm = new MainViewModel(History(), new AppSettings(), TestDataFolder.App) { Background = held };
        vm.SetCards(CollectionPageFixture.Cards);
        vm.SetCollection(CollectionPageFixture.Collection());
        vm.PageShown("Collection");

        // The first count: "Counting…" in place of the tab.
        Assert.Equal(Loc.T("CollectionPage.Loading"), vm.CollectionPageEmpty);
        held.Release(0);
        Assert.False(vm.HasCollectionPageEmpty);
        Assert.False(vm.CollectionSets[0].Complete);

        // Two newer collections: the first of them finishes last and is dropped; the old sets stay until the newest is in.
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: true, full: true));
        Assert.False(vm.HasCollectionPageEmpty);
        Assert.False(vm.CollectionSets[0].Complete);
        held.Release(2);
        Assert.True(vm.CollectionSets[0].Complete);
        held.Release(1);
        Assert.True(vm.CollectionSets[0].Complete);
        Assert.Equal(2, vm.CollectionPageCounts);
    });

    [Fact]
    public void A_language_switch_rebuilds_names_groups_and_states() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        vm.SetCosmetics(CollectionPageFixture.Cosmetics());
        vm.PageShown("Collection");
        vm.CollectionFilterWild = true;
        var english = vm.CollectionSets[0].Name;

        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            Assert.Equal(CardSets.DisplayName("CATACLYSM"), vm.CollectionSets[0].Name);
            Assert.NotEqual(english, vm.CollectionSets[0].Name);
            Assert.Equal("Стандартный", vm.CollectionSets[0].Group);
            Assert.Equal("по сохранённым данным", vm.CollectionPageStatus!.Mark);
            Assert.Equal("Для игры", vm.CollectionSum[0].Label);
            vm.CollectionTabBacks = true;
            Assert.Equal("Базовые · 1/2", vm.CollectionBackGroups[0].Label);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }

        Assert.Equal("Basic · 1/2", vm.CollectionBackGroups[0].Label);
    });

    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, $"hp-collection-page-{Guid.NewGuid():N}.db"));

    private static MainViewModel Model(CardDb? cards = null)
    {
        var vm = new MainViewModel(History(), new AppSettings(), TestDataFolder.App) { Background = BackgroundWork.Inline, CollectionReadable = true };
        vm.SetCards(cards ?? CollectionPageFixture.Cards);
        return vm;
    }

    /// <summary>
    /// Holds each count of the page until the test releases it by its number (in any order); the view model's other
    /// work (the history read on new card data) runs at once.
    /// </summary>
    private sealed class HeldWork : BackgroundWork
    {
        private readonly List<Action> _work = [];

        public override Task<T> RunAsync<T>(Func<T> work)
        {
            if (typeof(T).Name != "CollectionPageData")
            {
                return Inline.RunAsync(work);
            }

            // The count's continuation runs when the test releases it: inline, or on the dispatcher run after it.
            var done = new TaskCompletionSource<T>();
            _work.Add(() => done.SetResult(work()));
            return done.Task;
        }

        public void Release(int index)
        {
            _work[index]();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Main;
using static Hearthpulse.Tests.CollectionFixture;

namespace Hearthpulse.Tests;

/// <summary>
/// The collection views, second round (reviews of 3bac37b4): a saved collection's order never decides which lists are
/// shown; a reason per gap; no label from a missing number; the state of a pricing under way and of a failed one;
/// a long list's prices applied on the caller's context and a superseded pricing dropped; one build per change; the
/// render check's made-up deck stays open through a rebuild of the deck list.
/// </summary>
public sealed partial class CollectionViewTests
{
    // ------------------------------------------------------------------------------------------ the order and the sixty

    [Fact]
    public void By_a_saved_collection_the_order_by_dust_shows_the_same_lists_as_the_order_by_win_rate()
    {
        // 61 unreadable lists with a better win rate than every real one: the win rate's sixty are all of those.
        var vm = Shown(Collection(confirmed: false), more: MainViewModel.MetaDeckLimit + 1, moreWinRate: 0.90);
        Assert.Equal(MainViewModel.MetaDeckLimit, vm.MetaDecks.Count);
        var byWinRate = vm.MetaDecks.Select(d => d.Code).ToList();
        Assert.DoesNotContain(Owned, byWinRate);

        vm.SetMetaDeckSort.Execute("Dust");

        // The saved numbers put no list on the page that the win rate left off it, and take none away.
        Assert.Equal(byWinRate.Order(StringComparer.Ordinal), vm.MetaDecks.Select(d => d.Code).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Owned, vm.MetaDecks.Select(d => d.Code));
        Assert.Equal(new[] { false, true }, vm.MetaDeckSorts.Select(s => s.Selected));

        // By the confirmed collection the order picks from every list: the lists with an answer come first.
        vm.SetCollection(Collection(confirmed: true));
        Assert.Equal(MainViewModel.MetaDeckLimit, vm.MetaDecks.Count);
        Assert.Equal(new[] { Owned, OtherPrinting, Craftable, Reward, NeedsDust, NotCraftable }, vm.MetaDecks.Take(6).Select(d => d.Code));

        // And saved again: back to the win rate's sixty.
        vm.SetCollection(Collection(confirmed: false));
        Assert.Equal(byWinRate.Order(StringComparer.Ordinal), vm.MetaDecks.Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------------------------------ the dust numbers

    [Fact]
    public void The_reason_tells_the_three_dust_numbers_apart()
    {
        var vm = Shown(Collection());

        // Needed for the missing cards, at hand, and the difference the label names: 2400, 1000, 1400.
        var needs = Row(vm, NeedsDust).Build!;
        Assert.Equal($"{N(1400)} dust short", needs.Label);
        Assert.Equal($"The missing cards cost {N(2400)} dust and you have {N(1000)}: {N(1400)} short.", needs.Tip);
        Assert.Equal($"Missing cards: {N(2400)} dust", needs.Total);

        var craftable = Row(vm, Craftable).Build!;
        Assert.Equal($"The missing cards cost {N(800)} dust and you have {N(1000)}: enough.", craftable.Tip);

        // The column's number is another one (the whole deck from nothing), and its head says so in both languages.
        Assert.NotEqual(N(2400), Row(vm, NeedsDust).Dust);
        Assert.Contains("whole deck", Loc.T("Collection.Dust.ColumnTip"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ why a deck has no label

    [Fact]
    public void Each_gap_of_the_deck_cost_has_a_reason_of_its_own()
    {
        var cards = CardDb.Parse("enUS", new MemoryStream("""
            [
              {"id":"G_COMMON","dbfId":1,"name":"Common","rarity":"COMMON","set":"TITANS","type":"MINION","cost":1,"collectible":true},
              {"id":"G_NO_RARITY","dbfId":2,"name":"No rarity","set":"TITANS","type":"MINION","cost":1,"collectible":true}
            ]
            """u8.ToArray()));
        var collection = Collection();
        Dictionary<string, int> Deck(string id) => new(StringComparer.Ordinal) { [id] = 1 };
        var sideboard = new DeckDefinition("Deck", 0, "code", "Standard", [637], new Dictionary<int, int> { [1] = 1 }) { SideboardUnread = true };

        var gaps = new (DeckCost Cost, DeckCostGap Gap, string Key)[]
        {
            (DeckCost.For((IReadOnlyDictionary<string, int>?)null, collection, cards), DeckCostGap.EmptyDeck, "Collection.Deck.NoCode"),
            (DeckCost.For(Deck("G_COMMON"), collection, null), DeckCostGap.NoCardData, "Collection.Deck.NoCardData"),
            (DeckCost.For(Deck("G_NOT_IN_DATA"), collection, cards), DeckCostGap.UnknownCard, "Collection.Deck.UnknownCards"),
            (DeckCost.For(Deck("G_NO_RARITY"), collection, cards), DeckCostGap.UnknownRarity, "Collection.Deck.UnknownRarity"),
            (DeckCost.For(sideboard, collection, cards), DeckCostGap.UnreadableSideboard, "Collection.Deck.UnreadableSideboard"),
        };

        Assert.All(gaps, g => Assert.Equal((DeckBuild.Unknown, g.Gap), (g.Cost.State, g.Cost.Gap)));
        Assert.All(gaps, g => Assert.Equal(g.Key, MainViewModel.DeckGapText(g.Cost)));
        Assert.Equal(gaps.Length, gaps.Select(g => g.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var language in new[] { Loc.English, Loc.Russian })
        {
            Loc.Instance.SetLanguage(language);
            Assert.Equal(gaps.Length, gaps.Select(g => Loc.T(g.Key)).Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("sideboard", Loc.T("Collection.Deck.UnreadableSideboard").Replace("дополнительный набор", "sideboard", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        Loc.Instance.SetLanguage(Loc.English);
        Assert.Contains("rarity", Loc.T("Collection.Deck.UnknownRarity"), StringComparison.Ordinal);

        // A priced deck names no gap; a gap nobody named gets the neutral text, not a wrong reason.
        Assert.Equal("Collection.Deck.Unpriced", MainViewModel.DeckGapText(DeckCost.For(Deck("G_COMMON"), collection, cards)));
    }

    // ------------------------------------------------------------------------------------------ never a made-up zero

    [Fact]
    public void A_state_without_the_dust_numbers_its_label_states_gets_no_label()
    {
        MissingCard[] missing = [new("C_EPIC", 2, 400)];
        DeckBuildView? Of(DeckBuild state, long? needed, long? lacking, long? available) =>
            DeckBuildViews.Of(state, needed, lacking, available, needed, estimated: false, confirmed: true, missing, Cards);

        // With the numbers: the labels as ever.
        Assert.Equal($"Craftable: {N(800)} dust", Of(DeckBuild.CraftableNow, 800, 0, 1000)!.Label);
        Assert.Equal($"{N(300)} dust short", Of(DeckBuild.NeedsDust, 800, 300, 500)!.Label);

        // Without one of them: no label, rather than "Craftable: 0 dust" or "0 dust short".
        Assert.Null(Of(DeckBuild.CraftableNow, null, 0, 1000));
        Assert.Null(Of(DeckBuild.CraftableNow, 800, 0, null));
        Assert.Null(Of(DeckBuild.NeedsDust, null, 300, 500));
        Assert.Null(Of(DeckBuild.NeedsDust, 800, null, 500));
        Assert.Null(Of(DeckBuild.NeedsDust, 800, 300, null));
        Assert.Null(Of(DeckBuild.Unknown, 800, 300, 500));

        // "Can't be crafted" states no number of the deck; without a craftable part it says only that.
        var none = DeckBuildViews.Of(DeckBuild.NotCraftable, null, null, null, null, estimated: false, confirmed: true, [new("C_EVENT", 1, null)], Cards)!;
        Assert.Equal(("Can't be crafted", "Dust can't complete this deck."), (none.Label, none.Total));
        Assert.DoesNotContain("0", none.Label + none.Total + none.Tip, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ pricing a long list

    [Fact]
    public void A_long_list_s_prices_are_applied_on_the_caller_s_context_and_a_superseded_pricing_is_dropped()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(Meta(more: MainViewModel.InlineDeckCosts + 20));
        var real = vm.PriceDecksWith;
        var first = Collection();
        var second = Collection(dust: 5000);
        using var firstGate = new ManualResetEventSlim();
        using var secondGate = new ManualResetEventSlim();
        var pricingThreads = new ConcurrentBag<int>();
        vm.PriceDecksWith = (codes, collection, cards) =>
        {
            pricingThreads.Add(Environment.CurrentManagedThreadId);
            Assert.True((ReferenceEquals(collection, first) ? firstGate : secondGate).Wait(TimeSpan.FromSeconds(30)), "the gate opened");
            return real(codes, collection, cards);
        };

        var caller = Environment.CurrentManagedThreadId;
        var appliedOn = new ConcurrentQueue<int>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.MetaDecks))
            {
                appliedOn.Enqueue(Environment.CurrentManagedThreadId);
            }
        };

        // Only now the context: every post from here on is a pricing's way back.
        var context = new QueueContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            vm.CanBuildOnly = true;
            vm.SetCollection(first);

            // Under way: the filter's rows are not in, and the page says it is working, not that nothing can be built.
            Assert.True(vm.DeckCostsPending);
            Assert.True(vm.MetaPricingShown);
            Assert.Empty(vm.MetaDecks);
            Assert.False(vm.MetaCanBuildEmpty);
            Assert.False(vm.MetaDecksEmpty);

            // A newer collection while the first is still being priced.
            vm.SetCollection(second);
            Assert.True(vm.DeckCostsPending);
            var builds = vm.MetaDeckBuilds;
            appliedOn.Clear();

            // The second pricing finishes on its pool thread and waits at the context: nothing is applied from there.
            secondGate.Set();
            context.WaitForPost();
            Assert.True(vm.DeckCostsPending);
            Assert.Empty(vm.MetaDecks);
            Assert.Empty(appliedOn);

            context.RunOne();
            Assert.False(vm.DeckCostsPending);
            Assert.False(vm.MetaPricingShown);
            Assert.Equal(new[] { NeedsDust, Reward, Craftable, OtherPrinting, Owned }, vm.MetaDecks.Select(d => d.Code));
            Assert.Equal($"Craftable: {N(2400)} dust", Row(vm, NeedsDust).Build!.Label);
            Assert.NotEmpty(appliedOn);
            Assert.All(appliedOn, thread => Assert.Equal(caller, thread));
            Assert.All(pricingThreads, thread => Assert.NotEqual(caller, thread));
            var afterSecond = vm.MetaDeckBuilds;
            Assert.Equal(builds + 1, afterSecond);

            // The first pricing comes back late: its numbers are of a collection that is no longer the one shown.
            firstGate.Set();
            context.WaitForPost();
            context.RunOne();
            Assert.Equal(afterSecond, vm.MetaDeckBuilds);
            Assert.Equal($"Craftable: {N(2400)} dust", Row(vm, NeedsDust).Build!.Label);
            Assert.Equal(5, vm.MetaDecks.Count);
            Assert.Equal(2, pricingThreads.Count);
        }
        finally
        {
            firstGate.Set();
            secondGate.Set();
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    [Fact]
    public void A_failed_pricing_is_shown_with_a_way_to_try_again()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(Meta(more: MainViewModel.InlineDeckCosts + 20));
        var real = vm.PriceDecksWith;
        var fail = true;
        var calls = 0;
        vm.PriceDecksWith = (codes, collection, cards) =>
        {
            Interlocked.Increment(ref calls);
            return fail ? throw new InvalidOperationException("synthetic pricing failure") : real(codes, collection, cards);
        };

        var context = new QueueContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            vm.CanBuildOnly = true;
            vm.SetCollection(Collection());
            Assert.True(vm.DeckCostsPending);
            context.WaitForPost();
            context.RunOne();

            // Failed: the wait is over, the page says so, and neither empty state claims anything about the lists.
            Assert.False(vm.DeckCostsPending);
            Assert.True(vm.DeckCostsFailed);
            Assert.False(vm.MetaPricingShown);
            Assert.False(vm.MetaCanBuildEmpty);
            Assert.Empty(vm.MetaDecks);
            Assert.Equal(1, calls);

            // The same inputs are not priced again by themselves...
            vm.SetMetaDeckSort.Execute("Dust");
            Assert.Equal(1, calls);

            // ...the player's "check again" is what does it.
            fail = false;
            vm.RetryDeckCosts.Execute(null);
            Assert.False(vm.DeckCostsFailed);
            Assert.True(vm.DeckCostsPending);
            context.WaitForPost();
            context.RunOne();
            Assert.Equal(2, calls);
            Assert.False(vm.DeckCostsFailed);
            Assert.Equal(new[] { Owned, OtherPrinting, Craftable }, vm.MetaDecks.Select(d => d.Code));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }

        // A short list is priced where it is asked for: its failure is shown the same way and never thrown at the caller.
        var small = Shown(null);
        small.PriceDecksWith = (_, _, _) => throw new InvalidOperationException("synthetic pricing failure");
        small.SetCollection(Collection());
        Assert.True(small.DeckCostsFailed);
        Assert.False(small.DeckCostsPending);
        Assert.All(small.MetaDecks, d => Assert.Null(d.Build));
        small.PriceDecksWith = real;
        small.RetryDeckCosts.Execute(null);
        Assert.False(small.DeckCostsFailed);
        Assert.Equal("All cards owned", Row(small, Owned).Build!.Label);
    }

    // ------------------------------------------------------------------------------------------ one build per change

    [Fact]
    public void A_change_builds_the_lists_and_reads_the_deck_page_s_block_once()
    {
        // What the view model starts in the background (history reads, the deck page's build) waits at this context and
        // is never run: only what a change does at once is counted.
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new QueueContext());
        try
        {
            CountBuilds();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    private void CountBuilds()
    {
        var vm = Shown(Collection());
        vm.OpenSampleDeck(SampleDeck());
        Assert.True(vm.DeckOpen);

        var (builds, reads) = (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes);
        vm.SetCollection(Collection(dust: 5000));
        Assert.Equal((builds + 1, reads + 1), (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes));

        // The same collection again is no change at all.
        var same = Collection();
        vm.SetCollection(same);
        (builds, reads) = (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes);
        vm.SetCollection(same);
        Assert.Equal((builds, reads), (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes));

        // New card data: one build of the lists, one read of the block.
        vm.SetCards(CardDb.Parse("enUS", new MemoryStream("[]"u8.ToArray())));
        Assert.Equal((builds + 1, reads + 1), (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes));

        // The filter and the order: one build each, and the deck page is not read for them.
        (builds, reads) = (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes);
        vm.CanBuildOnly = true;
        vm.SetMetaDeckSort.Execute("Dust");
        Assert.Equal((builds + 2, reads), (vm.MetaDeckBuilds, vm.DeckPage.CollectionRefreshes));
    }

    // ------------------------------------------------------------------------------------------ the render's deck

    [Fact]
    public void A_sample_deck_stays_open_when_new_stats_rebuild_the_deck_list()
    {
        // The render check's scene: a deck no history has, opened, and then the stats are set (which reads the history
        // and builds the deck list again). The scene's deck frames showed the tier list because that closed the page.
        var context = new QueueContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var vm = Shown(Collection());
            var deck = SampleDeck();
            vm.OpenSampleDeck(deck);
            Assert.True(vm.DeckOpen);
            Assert.Equal(deck.Key, vm.DeckPage.Key);

            // Only the newest build of the deck list is applied: the one the new stats start.
            var built = vm.DeckListBuilds;
            vm.SetConstructedMeta(Meta());
            context.RunUntil(() => vm.DeckListBuilds > built);

            // The deck list was built from a history without this deck, and the page is still on it, with its block.
            Assert.DoesNotContain(vm.DeckFamiliesShown, f => f.Key == deck.Key);
            Assert.True(vm.DeckOpen);
            Assert.Equal(deck.Key, vm.DeckPage.Key);
            Assert.Equal($"{N(1400)} dust short", vm.DeckPage.Collection.Build!.Label);

            // Closed by the player, it is gone: the next rebuild does not bring it back, and its key opens nothing.
            vm.DeckPage.Back.Execute(null);
            Assert.False(vm.DeckOpen);
            Assert.False(vm.OpenDeckPage(deck.Key));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    private static DeckFamily SampleDeck()
    {
        var game = new GameRecord
        {
            Key = "collection-view-sample",
            StartedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local),
            EndedAt = new DateTime(2026, 11, 14, 10, 8, 0, DateTimeKind.Local),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            Result = "Win",
            PlayerClass = "MAGE",
            OpponentClass = "WARRIOR",
            DeckName = "Sample Mage",
            DeckCode = NeedsDust,
        };
        return DeckFamilies.Build(DeckFamilies.Versions([game]), []).Single();
    }

    private MainViewModel Shown(CardCollection? collection, int more, double moreWinRate)
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetCollection(collection);
        vm.SetConstructedMeta(Meta(more, moreWinRate));
        return vm;
    }

    /// <summary>
    /// A synchronization context the test runs by hand, as a UI thread runs its queue: what is posted waits until the
    /// test takes it, on the test's own thread. A wait is bounded only so that a hang ends.
    /// </summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _posted = [];
        private (SendOrPostCallback Callback, object? State)? _next;

        public override void Post(SendOrPostCallback d, object? state) => _posted.Add((d, state));

        /// <summary>Blocks until something was posted, without running it.</summary>
        public void WaitForPost()
        {
            if (_next is null)
            {
                Assert.True(_posted.TryTake(out var work, Hang), "nothing was posted to the context");
                _next = work;
            }
        }

        /// <summary>Runs the next posted piece of work on this thread.</summary>
        public void RunOne()
        {
            WaitForPost();
            var (callback, state) = _next!.Value;
            _next = null;
            callback(state);
        }

        public void RunUntil(Func<bool> done)
        {
            while (!done())
            {
                RunOne();
            }
        }
    }
}

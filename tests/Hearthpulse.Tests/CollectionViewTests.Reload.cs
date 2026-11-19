using Hearthpulse.Core;
using Hearthpulse.UI.Main;
using static Hearthpulse.Tests.CollectionFixture;

namespace Hearthpulse.Tests;

/// <summary>The top decks while new lists are priced: the "can build" rows stay, dimmed, and change in one step.</summary>
public sealed partial class CollectionViewTests
{
    [Fact]
    public void Can_build_rows_stay_in_place_dimmed_while_new_lists_are_priced_and_then_change_in_one_step()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(Meta(more: MainViewModel.InlineDeckCosts + 20));
        var context = new QueueContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            // The filter turned on with the first collection: nothing to keep yet, so the card says it is working.
            vm.CanBuildOnly = true;
            vm.SetCollection(Collection());
            Assert.True(vm.MetaPricingShown);
            context.RunUntil(() => !vm.DeckCostsPending);
            var shown = vm.MetaDecks.ToList();
            Assert.NotEmpty(shown);
            Assert.False(vm.MetaDecksRefreshing);

            // A newer collection is priced: the rows stay where they are, dimmed, and no "working out" box replaces them.
            vm.SetCollection(Collection(dust: 5000));
            Assert.True(vm.DeckCostsPending);
            Assert.True(vm.MetaDecksRefreshing);
            Assert.False(vm.MetaPricingShown);
            Assert.False(vm.MetaCanBuildEmpty);
            Assert.Equal(shown.Count, vm.MetaDecks.Count);
            Assert.All(shown.Zip(vm.MetaDecks), pair => Assert.Same(pair.First, pair.Second));

            // Priced: the new rows in one step, back to full.
            context.RunUntil(() => !vm.DeckCostsPending);
            Assert.False(vm.MetaDecksRefreshing);
            Assert.False(vm.MetaPricingShown);
            Assert.Equal(new[] { NeedsDust, Reward, Craftable, OtherPrinting, Owned }, vm.MetaDecks.Select(d => d.Code));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }
}

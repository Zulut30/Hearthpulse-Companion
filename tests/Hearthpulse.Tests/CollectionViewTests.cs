using System.ComponentModel;
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Main;
using static Hearthpulse.Tests.CollectionFixture;

namespace Hearthpulse.Tests;

/// <summary>
/// The player's collection in the meta deck lists and on the own deck page (docs/collection.md, "In the interface"):
/// one label per answer of the deck cost and none for an unknown one, the estimate and saved-collection marks, the
/// "can build" filter that works only by a confirmed collection, the order by missing dust, the status line of every
/// state, and that a new collection prices the lists again.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed partial class CollectionViewTests : IDisposable
{
    private readonly string _db = TestDataFolder.Scratch("hp-collection-view") + ".db";
    private readonly HistoryStore _history;

    public CollectionViewTests()
    {
        _history = new HistoryStore(_db);
        Loc.Instance.SetLanguage(Loc.English);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_db);
    }

    private MainViewModel Shown(CardCollection? collection, int more = 0)
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetCollection(collection);
        vm.SetConstructedMeta(Meta(more));
        return vm;
    }

    private static MetaDeckRow Row(MainViewModel vm, string code) => vm.MetaDecks.Single(d => d.Code == code);

    private static string N(long dust) => dust.ToString("N0", CultureInfo.CurrentCulture);

    // ------------------------------------------------------------------------------------------ the five states

    [Fact]
    public void Each_answer_has_its_own_label_with_a_glyph_and_an_unknown_one_has_none()
    {
        var vm = Shown(Collection());

        Assert.Equal(("Owned", "All cards owned"), (Row(vm, Owned).Build!.Kind, Row(vm, Owned).Build!.Label));
        Assert.Equal(Row(vm, Owned).Build!.Name, Row(vm, Owned).Build!.Details);
        Assert.Equal(("CraftableNow", $"Craftable: {N(800)} dust"), (Row(vm, Craftable).Build!.Kind, Row(vm, Craftable).Build!.Label));
        Assert.Equal(("NeedsDust", $"{N(1400)} dust short"), (Row(vm, NeedsDust).Build!.Kind, Row(vm, NeedsDust).Build!.Label));
        Assert.Equal(("NotCraftable", "Can't be crafted"), (Row(vm, NotCraftable).Build!.Kind, Row(vm, NotCraftable).Build!.Label));

        // Unknown: no label at all, never "owned" and never "0 dust".
        Assert.Null(Row(vm, Unreadable).Build);
        Assert.False(Row(vm, Unreadable).HasBuild);

        // Colour is never the only carrier: four different glyphs and four different texts, and a plain label is not dimmed.
        var builds = vm.MetaDecks.Select(d => d.Build).OfType<DeckBuildView>().ToList();
        Assert.Equal(4, builds.Select(b => b.Glyph).Distinct(StringComparer.Ordinal).Count());
        Assert.All(builds, b => Assert.True(b.Glyph.Length > 0 && b.Label.Length > 0));
        Assert.All(builds, b => Assert.Equal((false, 1.0), (b.Saved, b.Opacity)));
        Assert.Equal(string.Empty, Row(vm, Owned).Build!.Marks);
    }

    [Fact]
    public void The_reason_names_the_dust_needed_and_the_dust_at_hand()
    {
        var vm = Shown(Collection());

        Assert.Contains(N(800), Row(vm, Craftable).Build!.Tip, StringComparison.Ordinal);
        Assert.Contains(N(Dust), Row(vm, Craftable).Build!.Tip, StringComparison.Ordinal);
        Assert.Contains(N(2400), Row(vm, NeedsDust).Build!.Tip, StringComparison.Ordinal);
        Assert.Contains("can't be crafted", Row(vm, NotCraftable).Build!.Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_missing_cards_have_a_price_each_and_a_sum()
    {
        var build = Row(Shown(Collection()), NeedsDust).Build!;

        // Two epics at 400 and one legendary at 1600: 2400.
        Assert.Equal(2, build.Missing.Count);
        var epic = build.Missing.Single(m => m.CardId == "C_EPIC");
        Assert.Equal(("2 × Epic", $"2 × {N(400)} dust"), (epic.Name, epic.Price));
        var legend = build.Missing.Single(m => m.CardId == "C_LEGEND");
        Assert.Equal((LongName, $"{N(1600)} dust"), (legend.Name, legend.Price));
        Assert.Equal($"Missing cards: {N(2400)} dust", build.Total);

        // The accessible text of the label says all of it.
        Assert.StartsWith(build.Label, build.Name, StringComparison.Ordinal);
        Assert.Contains("2 × Epic", build.Details, StringComparison.Ordinal);
        Assert.Contains(build.Total, build.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_no_dust_buys_is_named_so_and_the_sum_is_never_a_price_of_the_deck()
    {
        var build = Row(Shown(Collection()), NotCraftable).Build!;

        var reward = Assert.Single(build.Missing);
        Assert.Equal(("Event Reward", "can't be crafted"), (reward.Name, reward.Price));
        Assert.Equal("Dust can't complete this deck.", build.Total);
        Assert.DoesNotContain("Missing cards:", build.Total, StringComparison.Ordinal);
    }

    [Fact]
    public void An_owned_deck_has_no_missing_cards_and_no_sum()
    {
        var build = Row(Shown(Collection()), Owned).Build!;

        Assert.False(build.HasMissing);
        Assert.Equal(string.Empty, build.Total);
    }

    // ------------------------------------------------------------------------------------------ estimates

    [Fact]
    public void Another_printing_in_the_collection_marks_the_label_as_an_estimate_and_says_why()
    {
        var build = Row(Shown(Collection()), OtherPrinting).Build!;

        Assert.Equal("CraftableNow", build.Kind);
        Assert.Equal("estimate", build.Marks);
        Assert.Equal(build.Label + " · estimate", build.Name);
        Assert.Contains("another printing", build.Tip, StringComparison.Ordinal);
        Assert.Equal("may own another printing", Assert.Single(build.Missing).Note);
    }

    [Fact]
    public void A_card_the_game_hands_out_marks_the_label_as_an_estimate_and_says_why()
    {
        var build = Row(Shown(Collection()), Reward).Build!;

        Assert.Equal(("NeedsDust", "estimate"), (build.Kind, build.Marks));
        Assert.Contains("may not be craftable right now", build.Tip, StringComparison.Ordinal);
        Assert.Equal("may not be craftable now", Assert.Single(build.Missing).Note);
    }

    // ------------------------------------------------------------------------------------------ a saved collection

    [Fact]
    public void By_a_saved_collection_every_label_is_dimmed_and_says_it_is_by_a_saved_collection()
    {
        var vm = Shown(Collection(confirmed: false));

        var builds = vm.MetaDecks.Select(d => d.Build).OfType<DeckBuildView>().ToList();
        Assert.Equal(6, builds.Count);
        Assert.All(builds, b =>
        {
            Assert.True(b.Saved);
            Assert.True(b.Opacity < 1);
            Assert.EndsWith("by a saved collection", b.Marks, StringComparison.Ordinal);
            Assert.Contains("by a saved collection", b.Name, StringComparison.Ordinal);
            Assert.Contains("saved collection", b.Tip, StringComparison.Ordinal);
        });
        Assert.Equal("estimate · by a saved collection", Row(vm, OtherPrinting).Build!.Marks);
        Assert.True(vm.CollectionSaved);
    }

    [Fact]
    public void The_can_build_filter_is_off_limits_by_a_saved_collection_and_says_why()
    {
        var vm = Shown(Collection(confirmed: false));

        Assert.False(vm.CanBuildAvailable);
        Assert.Contains("read from the running game", vm.CanBuildNote, StringComparison.Ordinal);

        // Switching it on changes nothing shown: no deck is hidden by numbers nobody vouches for.
        vm.CanBuildOnly = true;
        Assert.False(vm.CanBuildOnly);
        Assert.Equal(Decks.Count, vm.MetaDecks.Count);
        Assert.False(vm.MetaCanBuildEmpty);

        // The choice is kept: it applies once the collection is confirmed, and stops when it no longer is.
        vm.SetCollection(Collection(confirmed: true));
        Assert.True(vm.CanBuildOnly);
        Assert.Equal(new[] { Craftable, OtherPrinting, Owned }, vm.MetaDecks.Select(d => d.Code));
        vm.SetCollection(Collection(confirmed: false));
        Assert.False(vm.CanBuildOnly);
        Assert.Equal(Decks.Count, vm.MetaDecks.Count);
    }

    // ------------------------------------------------------------------------------------------ the filter and the order

    [Fact]
    public void Can_build_keeps_the_owned_and_the_craftable_now_lists_only()
    {
        var vm = Shown(Collection());
        Assert.True(vm.CanBuildAvailable);
        Assert.Equal(string.Empty, vm.CanBuildNote);

        vm.CanBuildOnly = true;

        Assert.Equal(new[] { Craftable, OtherPrinting, Owned }, vm.MetaDecks.Select(d => d.Code));
        Assert.All(vm.MetaDecks, d => Assert.Contains(d.Build!.Kind, new[] { "Owned", "CraftableNow" }));

        vm.CanBuildOnly = false;
        Assert.Equal(Decks, vm.MetaDecks.Select(d => d.Code));
    }

    [Fact]
    public void Can_build_with_nothing_to_build_shows_its_own_empty_state()
    {
        // No dust and no rares: nothing of the meta is owned or craftable.
        var poor = new CardCollection(new Dictionary<string, OwnedCopies>(StringComparer.Ordinal) { ["C_COMMON"] = new(1, 0, 0) }, 0, ReadAt) { Confirmed = true };
        var vm = Shown(poor);

        vm.CanBuildOnly = true;

        Assert.Empty(vm.MetaDecks);
        Assert.True(vm.MetaCanBuildEmpty);
        Assert.False(vm.MetaDecksEmpty);
    }

    [Fact]
    public void The_order_by_missing_dust_puts_what_needs_no_more_dust_first_and_the_unknown_last()
    {
        var vm = Shown(Collection());
        Assert.Equal(Decks, vm.MetaDecks.Select(d => d.Code));
        Assert.Equal(new[] { true, false }, vm.MetaDeckSorts.Select(s => s.Selected));

        vm.SetMetaDeckSort.Execute("Dust");

        // Nothing short: owned (0), the look-alike list (400), the two epics (800). Then 600 short, 1400 short, the
        // one no dust completes, the unknown.
        Assert.Equal(new[] { Owned, OtherPrinting, Craftable, Reward, NeedsDust, NotCraftable, Unreadable }, vm.MetaDecks.Select(d => d.Code));
        Assert.Equal(new[] { false, true }, vm.MetaDeckSorts.Select(s => s.Selected));

        vm.SetMetaDeckSort.Execute("WinRate");
        Assert.Equal(Decks, vm.MetaDecks.Select(d => d.Code));
    }

    [Fact]
    public void The_side_card_s_best_lists_carry_the_same_labels()
    {
        var vm = Shown(Collection());

        Assert.NotEmpty(vm.ArchetypeDecks);
        Assert.All(vm.ArchetypeDecks, d => Assert.Equal((Row(vm, d.Code).Build?.Name, Row(vm, d.Code).Build?.Details), (d.Build?.Name, d.Build?.Details)));
        Assert.Contains(vm.ArchetypeDecks, d => d.Build is not null);
    }

    // ------------------------------------------------------------------------------------------ no collection

    [Fact]
    public void Without_a_collection_there_are_no_labels_and_the_status_says_to_start_the_game()
    {
        var vm = Shown(null);

        Assert.False(vm.HasCollection);
        Assert.All(vm.MetaDecks, d => Assert.Null(d.Build));
        Assert.Equal(Decks, vm.MetaDecks.Select(d => d.Code));
        Assert.Equal(Loc.T("Collection.Status.None"), vm.CollectionStatus);
        Assert.False(vm.CanBuildAvailable);
        Assert.Equal(Loc.T("Collection.Filter.NeedsCollection"), vm.CanBuildNote);

        // The order by dust has nothing to order by: the lists stay by win rate.
        vm.SetMetaDeckSort.Execute("Dust");
        Assert.Equal(Decks, vm.MetaDecks.Select(d => d.Code));
        Assert.Equal(new[] { true, false }, vm.MetaDeckSorts.Select(s => s.Selected));
    }

    [Fact]
    public void With_the_game_running_and_no_collection_the_status_says_the_read_did_not_work_yet()
    {
        var vm = Shown(null);
        // The view model's background work raises changes from pool threads meanwhile: a queue that takes that.
        var raised = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        vm.PropertyChanged += (_, e) => raised.Enqueue(e.PropertyName);

        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" });

        Assert.Equal(Loc.T("Collection.Status.NotRead"), vm.CollectionStatus);
        Assert.Contains(nameof(MainViewModel.CollectionStatus), raised);
    }

    [Fact]
    public void On_a_system_that_cannot_read_the_game_the_status_says_the_collection_is_read_on_windows()
    {
        var vm = Shown(null);

        vm.CollectionReadable = false;

        Assert.Contains("Windows", vm.CollectionStatus, StringComparison.Ordinal);
        Assert.Equal(Loc.T("Collection.Status.Unsupported"), vm.CollectionStatus);
        // The status line says it all: no promise that the filter will work later.
        Assert.Equal(string.Empty, vm.CanBuildNote);
        Assert.False(vm.CanBuildAvailable);
        Assert.All(vm.MetaDecks, d => Assert.Null(d.Build));
    }

    // ------------------------------------------------------------------------------------------ the status line

    [Fact]
    public void A_confirmed_collection_says_when_it_was_read_from_the_game()
    {
        var now = DateTime.Now;
        var today = new DateTime(now.Year, now.Month, now.Day, 12, 0, 0, DateTimeKind.Local);
        var vm = Shown(Collection(readAt: today.ToUniversalTime()));

        Assert.Equal($"Collection read from the game today at {today.ToString("t", Loc.DateCulture)}.", vm.CollectionStatus);

        vm.SetCollection(Collection(readAt: ReadAt));
        Assert.Equal($"Collection read from the game {ReadAt.ToLocalTime().ToString("MMMM d, yyyy", Loc.DateCulture)} at {ReadAt.ToLocalTime().ToString("t", Loc.DateCulture)}.", vm.CollectionStatus);
    }

    [Fact]
    public void A_saved_collection_is_called_saved_with_its_date_and_never_a_file()
    {
        var vm = Shown(Collection(confirmed: false));
        var date = ReadAt.ToLocalTime().ToString("MMMM d, yyyy", Loc.DateCulture);

        Assert.StartsWith("Saved collection from " + date, vm.CollectionStatus, StringComparison.Ordinal);
        Assert.EndsWith("start Hearthstone to update it.", vm.CollectionStatus, StringComparison.Ordinal);

        // With the game running the next step is another one, and the date stays.
        vm.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" });
        Assert.StartsWith("Saved collection from " + date, vm.CollectionStatus, StringComparison.Ordinal);
        Assert.Contains("couldn't read it from the game yet", vm.CollectionStatus, StringComparison.Ordinal);

        foreach (var language in new[] { Loc.English, Loc.Russian })
        {
            Loc.Instance.SetLanguage(language);
            Assert.DoesNotContain("file", vm.CollectionStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("файл", vm.CollectionStatus, StringComparison.OrdinalIgnoreCase);
        }

        Assert.StartsWith("Сохранённая коллекция от ", vm.CollectionStatus, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------ a new collection

    [Fact]
    public void A_new_collection_prices_the_lists_again()
    {
        var vm = Shown(Collection());
        // The view model's background work raises changes from pool threads meanwhile: a queue that takes that.
        var raised = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        vm.PropertyChanged += (_, e) => raised.Enqueue(e.PropertyName);
        Assert.Equal("NeedsDust", Row(vm, NeedsDust).Build!.Kind);

        // The same cards with dust for everything: what lacked dust is craftable now.
        vm.SetCollection(Collection(dust: 5000));

        Assert.Equal("CraftableNow", Row(vm, NeedsDust).Build!.Kind);
        Assert.Equal($"Craftable: {N(2400)} dust", Row(vm, NeedsDust).Build!.Label);
        Assert.Contains(nameof(MainViewModel.MetaDecks), raised);
        Assert.Contains(nameof(MainViewModel.CollectionStatus), raised);
        Assert.Equal(Row(vm, NeedsDust).Build!.Details, vm.ArchetypeDecks.Single(d => d.Code == NeedsDust).Build!.Details);

        // And none: the labels go.
        vm.SetCollection(null);
        Assert.All(vm.MetaDecks, d => Assert.Null(d.Build));
        Assert.All(vm.ArchetypeDecks, d => Assert.Null(d.Build));
    }

    [Fact]
    public void The_session_s_event_reaches_the_view_model_through_the_host_s_dispatch()
    {
        // The hosts' wiring in small: the event carries no value and comes from another thread; the handler posts to
        // the UI thread (here a queue run by the test) and reads the session's collection there.
        using var memory = new Hearthpulse.Core.Memory.HearthstoneMemory();
        var file = TestDataFolder.Scratch("hp-collection-view-file") + ".json";
        try
        {
            new CollectionStore(file).Save(Collection());
            var session = new GameSession(HistoryStore.InMemory(), memory)
            {
                ReadsMemory = false,
                IsHearthstoneRunning = () => false,
                CollectionWatch = new CollectionWatch(new CollectionStore(file)),
            };
            var vm = Shown(null);
            var posted = new System.Collections.Concurrent.BlockingCollection<Action>();
            session.CollectionChanged += () => posted.Add(() => vm.SetCollection(session.Collection));

            session.CollectionWatch.Start();

            Assert.True(posted.TryTake(out var run, TimeSpan.FromSeconds(30)), "the saved collection was announced");
            Assert.All(vm.MetaDecks, d => Assert.Null(d.Build));
            run!();

            // Loaded from the file: a saved collection, never a confirmed one.
            Assert.True(vm.CollectionSaved);
            Assert.Equal("CraftableNow", Row(vm, Craftable).Build!.Kind);
            Assert.True(Row(vm, Craftable).Build!.Saved);
        }
        finally
        {
            TestDataFolder.TryDelete(file);
        }
    }

    [Fact]
    public void A_long_list_is_priced_off_the_calling_thread_and_shows_its_labels_when_done()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        vm.SetCards(Cards);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(Meta(more: MainViewModel.InlineDeckCosts + 20));

        // The prices come back on the caller's context, as on the UI thread of both hosts. Without one they came back
        // on a pool thread in the middle of the asserts below (the rows cleared, not yet filled: CI 45876834441).
        var context = new QueueContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            vm.CanBuildOnly = true;
            vm.SetCollection(Collection());

            // While the prices are being made (and after), the filter does not claim that nothing can be built.
            Assert.True(vm.DeckCostsPending);
            Assert.False(vm.MetaCanBuildEmpty);
            context.RunUntil(() => vm.MetaDecks.Any(d => d.Build is not null));

            Assert.Equal(new[] { Craftable, OtherPrinting, Owned }, vm.MetaDecks.Select(d => d.Code));
            Assert.Equal("All cards owned", Row(vm, Owned).Build!.Label);
            Assert.False(vm.MetaCanBuildEmpty);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    // ------------------------------------------------------------------------------------------ the own deck page

    [Fact]
    public void The_own_deck_page_lists_the_missing_cards_of_its_deck_with_the_status_line()
    {
        var vm = Shown(Collection());

        var block = vm.DeckCollectionFor(NeedsDust);

        Assert.Equal(vm.CollectionStatus, block.Status);
        Assert.True(block.HasBuild);
        Assert.Equal($"{N(1400)} dust short", block.Build!.Label);
        Assert.Equal(new[] { "C_EPIC", "C_LEGEND" }, block.Build.Missing.Select(m => m.CardId).Order(StringComparer.Ordinal));
        Assert.Equal($"Missing cards: {N(2400)} dust", block.Build.Total);
        Assert.Equal(string.Empty, block.Note);
    }

    [Fact]
    public void The_own_deck_page_says_why_a_deck_cannot_be_checked_and_shows_no_label_for_it()
    {
        var vm = Shown(Collection(confirmed: false));

        var noCode = vm.DeckCollectionFor(null);
        Assert.Null(noCode.Build);
        Assert.Equal(Loc.T("Collection.Deck.NoCode"), noCode.Note);
        Assert.StartsWith("Saved collection from ", noCode.Status, StringComparison.Ordinal);

        // By a saved collection the page's label is dimmed and marked too.
        var saved = vm.DeckCollectionFor(Owned);
        Assert.True(saved.Build!.Saved);
        Assert.Equal("by a saved collection", saved.Build.Marks);

        // Without a collection: the status line alone, no label and no second explanation.
        vm.SetCollection(null);
        var none = vm.DeckCollectionFor(Owned);
        Assert.Equal((null, string.Empty, Loc.T("Collection.Status.None")), (none.Build, none.Note, none.Status));
    }

    [Fact]
    public void Nothing_shown_splits_the_copies_by_finish()
    {
        // The collection holds a plain and a golden rare; the views count copies and never name a finish.
        var vm = Shown(Collection());
        foreach (var language in new[] { Loc.English, Loc.Russian })
        {
            Loc.Instance.SetLanguage(language);
            vm.SetCollection(Collection(dust: Dust + 1));
            var text = string.Join('\n', vm.MetaDecks.Select(d => d.Build?.Details + d.Build?.Name)) + vm.CollectionStatus + vm.CanBuildNote;
            Assert.DoesNotContain("golden", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("золот", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}

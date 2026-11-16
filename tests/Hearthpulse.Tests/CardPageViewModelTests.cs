using System.Globalization;
using System.Text;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The card page's view model (docs/card-page.md): what a card row opens, the texts of each section with their source,
/// date and sample, the empty states, Back and the navigation around it, the other format, and a slow load that must
/// not replace a newer one. The numbers come from a fake loader; the history is a scratch database.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class CardPageViewModelTests : IDisposable
{
    private static readonly DateTime Collected = new(2026, 11, 16, 6, 4, 0, DateTimeKind.Utc);
    private static readonly DateTime MetaUpdated = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _db = TestDataFolder.Scratch("hp-card-page") + ".db";
    private readonly HistoryStore _history;

    public CardPageViewModelTests()
    {
        _history = new HistoryStore(_db);
        Loc.Instance.SetLanguage(Loc.English);
    }

    public void Dispose()
    {
        Loc.Instance.SetLanguage(Loc.English);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_db);
    }

    [Fact]
    public async Task A_card_row_opens_the_page_with_the_card_its_numbers_their_slice_and_date()
    {
        var vm = Model(out var asked);
        vm.SetConstructedMeta(Meta());

        Assert.True(await vm.OpenCardPageAsync("T_BOLT"));

        Assert.True(vm.CardOpen);
        Assert.Equal(["standard"], asked);
        var page = vm.CardPage;
        Assert.False(page.Loading);
        Assert.Equal(("T_BOLT", "Test Bolt", "Discover a spell. Deal 3 damage."), (page.CardId, page.Name, page.Text));
        Assert.Equal([Loc.F("Card.Cost", 2), "Mage", "Emerald Dream", Loc.T("Main.Nav.Standard")], page.Facts);

        // 22.4 % of decks at 1.99 copies; 55.1 % deck win rate; drawn 54.1 %; played 53.6 % over 143,721 plays; kept 50 %
        // (56.1 % in the opening hand); turn 6.4.
        Assert.Equal(
            [
                (Loc.T("Card.Stats.DeckShare"), P(0.2236, "P1"), Loc.F("Card.Stats.Copies", 1.99), 0),
                (Loc.T("Card.Stats.DeckWinRate"), P(0.5513, "P1"), Loc.T("Card.Stats.DeckWinRateDetail"), 1),
                (Loc.T("Card.Stats.Drawn"), P(0.5407, "P1"), Loc.T("Card.Stats.DrawnDetail"), 1),
                (Loc.T("Card.Stats.Played"), P(0.5361, "P1"), Loc.P("Card.Stats.PlayedDetail", 143721), 1),
                (Loc.T("Card.Stats.Kept"), P(0.5020, "P0"), Loc.F("Card.Stats.KeptDetail", P(0.5610, "P1")), 0),
                (Loc.T("Card.Stats.Turn"), 6.41.ToString("0.0", CultureInfo.CurrentCulture), Loc.T("Card.Stats.TurnDetail"), 0),
            ],
            page.Tiles.Select(t => (t.Label, t.Value, t.Detail, t.Sign)));
        // The source is named "Hearthpulse data", with the slice the answer names and the date it was collected.
        Assert.Equal($"{Loc.F("Source.Health.Updated", Collected.ToLocalTime())} · Gold · last 14 days", page.StatsSource);
        Assert.StartsWith(HearthpulseData.Attribution, page.StatsSource, StringComparison.Ordinal);
        Assert.Equal(string.Empty, page.StatsEmpty);
    }

    [Fact]
    public async Task Archetype_and_mulligan_rows_name_their_sample_and_the_meta_they_come_from()
    {
        var vm = Model(out _);
        vm.SetConstructedMeta(Meta());

        await vm.OpenCardPageAsync("T_BOLT");

        var page = vm.CardPage;
        // Tempo Mage: both of its two lists run the card, one with 2 copies and one with 1 -> 100 %, 1.5 copies.
        var row = Assert.Single(page.Archetypes);
        Assert.Equal(("Tempo Mage", "Mage", "HERO_08"), (row.Name, row.ClassName, row.HeroId));
        Assert.Equal((Loc.P("Card.Archetypes.Lists", 2, 2), P(1, "P0"), 1.5.ToString("0.#", CultureInfo.CurrentCulture)), (row.Lists, row.Share, row.Copies));
        Assert.Equal((P(0.55, "P1"), 1000.ToString("N0", CultureInfo.CurrentCulture)), (row.WinRate, row.Games));
        Assert.Equal(0.05, row.Edge, 6);

        // In hand 60 % against the same games' 52 %: +8.0 points over 200 hands, kept 80 %.
        var mulligan = Assert.Single(page.Mulligan);
        Assert.Equal((P(0.80, "P0"), P(0.60, "P1"), 200.ToString("N0", CultureInfo.CurrentCulture)), (mulligan.KeepRate, mulligan.WinRate, mulligan.Hands));
        Assert.Equal(Loc.F("Card.Mulligan.Points", "+" + 8.0.ToString("0.0", CultureInfo.CurrentCulture)), mulligan.Impact);
        Assert.Equal(0.08, mulligan.Edge, 6);

        Assert.Equal(
            $"{Loc.F("Card.Meta.Source", HearthpulseData.Attribution, ConstructedMeta.Label(ConstructedMeta.Ranks, "legend"), ConstructedMeta.Label(ConstructedMeta.Periods, "past_week"))}"
            + $" · {Loc.F("Card.Updated", MetaUpdated.ToLocalTime())}",
            page.MetaSource);
        Assert.Equal((string.Empty, string.Empty), (page.ArchetypesEmpty, page.MulliganEmpty));
    }

    [Fact]
    public async Task My_games_come_from_the_local_history_of_the_format_only()
    {
        // Standard: drawn and played in a win, drawn in a loss, one game without the card. Wild: played in a win.
        Add("a", "FT_STANDARD", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);
        Add("b", "FT_STANDARD", "Loss", ["T_BOLT"], ["T_BOLT"], ["T_FILL", "T_BOLT"], ["T_FILL"]);
        Add("c", "FT_STANDARD", "Win", ["T_FILL"], [], ["T_FILL"], ["T_FILL"]);
        Add("d", "FT_WILD", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);
        var vm = Model(out _);

        await vm.OpenCardPageAsync("T_BOLT");

        var page = vm.CardPage;
        Assert.Equal(
            [
                // Two games (1-1) are under the five a verdict needs: no colour.
                (Loc.T("Card.Mine.Drawn"), P(0.5, "P0"), Loc.P("Card.Mine.Record", 2, 1, 1), 0),
                (Loc.T("Card.Mine.Played"), P(1, "P0"), Loc.P("Card.Mine.Record", 1, 1, 0), 0),
                (Loc.T("Card.Mine.Kept"), Loc.F("Card.Mine.KeptOf", 1, 2), Loc.T("Card.Mine.KeptDetail"), 0),
            ],
            page.MineTiles.Select(t => (t.Label, t.Value, t.Detail, t.Sign)));
        Assert.Equal(Loc.P("Card.Mine.Note", 3, 3), page.MineNote);
        Assert.Equal(string.Empty, page.MineEmpty);
    }

    [Fact]
    public async Task Empty_states_say_what_is_missing_and_show_no_number()
    {
        // The list is there but has no such card; no meta; no games at all.
        var vm = Model(out _, format => Slice(format, new CardStat("T_FILL", 1002, 0.1, 0.5, 2, 10, 0.5, 0.5, 0.5, 0.5, 1)));
        await vm.OpenCardPageAsync("T_BOLT");
        var page = vm.CardPage;
        Assert.Empty(page.Tiles);
        Assert.Equal((Loc.T("Card.Stats.None"), string.Empty), (page.StatsEmpty, page.StatsSource));
        Assert.Empty(page.Archetypes);
        Assert.Equal((Loc.T("Card.Archetypes.NoMeta"), Loc.T("Card.Archetypes.NoMeta"), string.Empty), (page.ArchetypesEmpty, page.MulliganEmpty, page.MetaSource));
        Assert.Empty(page.MineTiles);
        Assert.Equal((Loc.T("Card.Mine.NoGames"), string.Empty), (page.MineEmpty, page.MineNote));

        // The list could not be loaded (offline with no copy): a way forward, not "no data for this card".
        var offline = Model(out _, _ => null);
        offline.SetConstructedMeta(Meta());
        Add("c", "FT_STANDARD", "Win", ["T_FILL"], [], ["T_FILL"], ["T_FILL"]);
        await offline.OpenCardPageAsync("T_FILL");
        Assert.Equal(Loc.T("Card.Stats.Unavailable"), offline.CardPage.StatsEmpty);
        // The meta is loaded and both sections have this card: neither says "not loaded".
        Assert.Equal(string.Empty, offline.CardPage.ArchetypesEmpty);
        Assert.Single(offline.CardPage.Mulligan);

        // Games of the format, none with the card.
        await offline.OpenCardPageAsync("T_BOLT_WILD");
        Assert.Equal(Loc.T("Card.Mine.None"), offline.CardPage.MineEmpty);
        Assert.Equal((Loc.T("Card.Archetypes.None"), Loc.T("Card.Mulligan.None")), (offline.CardPage.ArchetypesEmpty, offline.CardPage.MulliganEmpty));
    }

    [Fact]
    public async Task A_card_the_card_data_does_not_have_opens_nothing()
    {
        var vm = Model(out var asked);

        Assert.False(await vm.OpenCardPageAsync("NOPE_001"));
        vm.OpenCard.Execute(null);
        vm.OpenCard.Execute(string.Empty);

        Assert.False(vm.CardOpen);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Back_closes_the_card_before_the_deck_under_it_and_leaving_the_format_pages_lets_go_of_it()
    {
        var vm = Model(out _);
        vm.PageShown(ConstructedPage.Standard);
        await vm.OpenCardPageAsync("T_BOLT");

        Assert.True(vm.GoBack(ConstructedPage.Standard));
        Assert.False(vm.CardOpen);
        Assert.Equal(string.Empty, vm.CardPage.CardId);
        Assert.False(vm.GoBack(ConstructedPage.Standard));

        await vm.OpenCardPageAsync("T_BOLT");
        vm.CardPage.Back.Execute(null);
        Assert.False(vm.CardOpen);

        await vm.OpenCardPageAsync("T_BOLT");
        vm.PageShown("History");
        Assert.False(vm.CardOpen);
    }

    [Fact]
    public async Task The_other_format_shows_its_own_numbers_for_the_open_card()
    {
        var vm = Model(out var asked);
        vm.PageShown(ConstructedPage.Standard);
        await vm.OpenCardPageAsync("T_BOLT");
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.StatsSource) && vm.CardPage.StatsSource.Contains("Legend", StringComparison.Ordinal))
            {
                changed.TrySetResult();
            }
        };

        vm.PageShown(ConstructedPage.Wild);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(vm.CardOpen);
        Assert.Equal(["standard", "wild"], asked);
        Assert.Contains(Loc.T("Main.Nav.Wild"), vm.CardPage.Facts);
        Assert.EndsWith("Legend · last 14 days", vm.CardPage.StatsSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Five_quick_clicks_on_cards_make_one_request_and_only_the_last_card_is_shown()
    {
        // The real loader over a server that holds its answer: the list is 1.3 MB and the player does not wait for it.
        var server = new HeldServer();
        var cache = TestDataFolder.Scratch("hp-card-page-cache");
        var data = new HearthpulseData(new HearthpulseApi(new HttpClient(server), "https://api.test/"), cache) { MinCardStatRows = 5 };
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = data.LoadCardStatsAsync;
        try
        {
            string[] clicks = ["T_BOLT", "T_FILL", "T_BOLT_WILD", "T_BOLT", "BE_014"];
            var opens = clicks.Select(vm.OpenCardPageAsync).ToList();
            await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // The card itself is on screen before its numbers; nothing has been cancelled or asked twice.
            Assert.True(vm.CardOpen);
            Assert.True(vm.CardPage.Loading);
            Assert.Equal("BE_014", vm.CardPage.CardId);
            Assert.All(opens, o => Assert.False(o.IsCompleted));
            server.Release.TrySetResult();
            var shown = await Task.WhenAll(opens).WaitAsync(TimeSpan.FromSeconds(30));

            // One request for five clicks; the four cards the player left never reach the page.
            Assert.Single(server.Requests);
            Assert.Equal([false, false, false, false, true], shown);
            Assert.Equal("BE_014", vm.CardPage.CardId);
            Assert.False(vm.CardPage.Loading);
            // "22.36%" in the fixture row of BE_014.
            Assert.Equal(P(0.2236, "P1"), vm.CardPage.Tiles[0].Value);

            // Back and another card: the page asks again and the cache answers, still one request.
            vm.CardPage.Back.Execute(null);
            Assert.True(await vm.OpenCardPageAsync("T_BOLT"));
            Assert.Single(server.Requests);
            Assert.Equal(Loc.T("Card.Stats.None"), vm.CardPage.StatsEmpty);
        }
        finally
        {
            TestDataFolder.TryDelete(cache);
        }
    }

    [Fact]
    public async Task Leaving_the_page_while_the_list_loads_does_not_end_the_request_the_next_card_shares()
    {
        var server = new HeldServer();
        var cache = TestDataFolder.Scratch("hp-card-page-cache");
        var data = new HearthpulseData(new HearthpulseApi(new HttpClient(server), "https://api.test/"), cache) { MinCardStatRows = 5 };
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = data.LoadCardStatsAsync;
        try
        {
            var first = vm.OpenCardPageAsync("T_BOLT");
            await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            vm.CardPage.Back.Execute(null);
            Assert.False(vm.CardOpen);

            var second = vm.OpenCardPageAsync("BE_014");
            Assert.True(vm.CardPage.Loading);
            server.Release.TrySetResult();

            Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Single(server.Requests);
            Assert.False(server.Cancelled);
            Assert.Equal("BE_014", vm.CardPage.CardId);
            Assert.NotEmpty(vm.CardPage.Tiles);
        }
        finally
        {
            TestDataFolder.TryDelete(cache);
        }
    }

    [Fact]
    public async Task A_meta_a_language_or_a_data_summary_that_arrives_while_the_list_loads_asks_for_nothing_more_and_loses_nothing()
    {
        var held = new TaskCompletionSource<CardStatsSlice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken token = default;
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = (_, cancel) =>
        {
            Interlocked.Increment(ref calls);
            token = cancel;
            return held.Task;
        };
        var built = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.Loading) && !vm.CardPage.Loading && vm.CardPage.Archetypes.Count > 0
                && vm.CardPage.MineNote.Contains("игра", StringComparison.Ordinal))
            {
                built.TrySetResult();
            }
        };
        Add("a", "FT_STANDARD", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);

        var open = vm.OpenCardPageAsync("T_BOLT");
        vm.SetConstructedMeta(Meta());
        vm.SetDataHealth(new DataHealthReport([], DateTime.UtcNow));
        Loc.Instance.SetLanguage(Loc.Russian);
        vm.SetCards(Cards());

        // Four rebuilds later the list has been asked for once, and the page's token was never cancelled.
        Assert.Equal(1, calls);
        Assert.False(token.CanBeCanceled);
        Assert.True(vm.CardPage.Loading);
        held.SetResult(Slice("standard", Stat("T_BOLT", 1001)));
        await built.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await open.WaitAsync(TimeSpan.FromSeconds(30));

        // The answer is on the page with everything that arrived meanwhile: the meta's rows and the new language.
        Assert.Equal(1, calls);
        Assert.Equal(6, vm.CardPage.Tiles.Count);
        Assert.Equal("Tempo Mage", vm.CardPage.Archetypes[0].Name);
        Assert.EndsWith("· Золото · за 14 дней", vm.CardPage.StatsSource, StringComparison.Ordinal);

        // A later rebuild of the open page reuses the list it has.
        vm.SetConstructedMeta(Meta());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_result_for_a_card_or_format_the_page_has_left_is_never_shown()
    {
        var answers = new Dictionary<string, TaskCompletionSource<CardStatsSlice?>>(StringComparer.Ordinal)
        {
            ["standard"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["wild"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = (format, _) => answers[format].Task;
        vm.PageShown(ConstructedPage.Standard);

        // Standard is asked first and answers last: by then the page shows the card in Wild.
        var standard = vm.OpenCardPageAsync("T_BOLT");
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.Loading) && !vm.CardPage.Loading)
            {
                shown.TrySetResult();
            }
        };
        vm.PageShown(ConstructedPage.Wild);
        answers["wild"].SetResult(Slice("wild", Stat("T_BOLT", 1001)));
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var wildSource = vm.CardPage.StatsSource;
        Assert.EndsWith("Legend · last 14 days", wildSource, StringComparison.Ordinal);

        answers["standard"].SetResult(Slice("standard", Stat("T_BOLT", 1001) with { DeckShare = 0.99 }));
        Assert.False(await standard.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal(wildSource, vm.CardPage.StatsSource);
        Assert.Contains(Loc.T("Main.Nav.Wild"), vm.CardPage.Facts);
        Assert.DoesNotContain(vm.CardPage.Tiles, t => t.Value == P(0.99, "P1"));

        // A page that was closed takes no late result either.
        var late = new TaskCompletionSource<CardStatsSlice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LoadCardStats = (_, _) => late.Task;
        vm.CardPage.Back.Execute(null);
        var closed = vm.OpenCardPageAsync("T_FILL");
        vm.CardPage.Back.Execute(null);
        late.SetResult(Slice("wild", Stat("T_FILL", 1002)));
        Assert.False(await closed.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(vm.CardOpen);
        Assert.Equal(string.Empty, vm.CardPage.CardId);
        Assert.Empty(vm.CardPage.Tiles);
    }

    [Fact]
    public async Task My_games_are_read_once_for_the_open_page_and_again_after_the_history_changes()
    {
        Add("a", "FT_STANDARD", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);
        var vm = Model(out _);
        await vm.OpenCardPageAsync("T_BOLT");
        Assert.Equal(Loc.P("Card.Mine.Note", 1, 1), vm.CardPage.MineNote);

        // A rebuild with the same history shows the same games (the read is kept for the page).
        var same = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.MetaSource) && vm.CardPage.MetaSource.Length > 0)
            {
                same.TrySetResult();
            }
        };
        vm.SetConstructedMeta(Meta());
        await same.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(Loc.P("Card.Mine.Note", 1, 1), vm.CardPage.MineNote);

        // A game saved while the page is open is counted by the next build, and by another card of the page.
        var counted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.MineNote) && vm.CardPage.MineNote == Loc.P("Card.Mine.Note", 2, 2))
            {
                counted.TrySetResult();
            }
        };
        Add("b", "FT_STANDARD", "Loss", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);
        vm.SetConstructedMeta(Meta());
        await counted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(Loc.P("Card.Mine.Record", 2, 1, 1), vm.CardPage.MineTiles[0].Detail);
    }

    [Fact]
    public async Task A_load_that_fails_leaves_loading_for_an_error_with_a_retry_that_asks_again()
    {
        // The first answer is held (a slow line), then comes back empty (the 20-second limit); the second has the list.
        var held = new TaskCompletionSource<CardStatsSlice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = (format, _) =>
            Interlocked.Increment(ref calls) == 1 ? held.Task : Task.FromResult<CardStatsSlice?>(Slice(format, Stat("T_BOLT", 1001)));

        var open = vm.OpenCardPageAsync("T_BOLT");
        var page = vm.CardPage;
        Assert.True(page.Loading);
        Assert.False(page.StatsFailed);
        Assert.Equal(string.Empty, page.StatsEmpty);

        held.SetResult(null);
        Assert.True(await open.WaitAsync(TimeSpan.FromSeconds(30)));

        // Not "loading" for ever: the section says what failed and offers the way forward; the card itself stays.
        Assert.False(page.Loading);
        Assert.True(page.StatsFailed);
        Assert.Equal(Loc.T("Card.Stats.Unavailable"), page.StatsEmpty);
        Assert.Empty(page.Tiles);
        Assert.True(vm.CardOpen);
        Assert.Equal("Test Bolt", page.Name);

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(page.Tiles) && page.Tiles.Count > 0)
            {
                loaded.TrySetResult();
            }
        };
        page.Retry.Execute(null);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, calls);
        Assert.False(page.StatsFailed);
        Assert.Equal(string.Empty, page.StatsEmpty);

        // A card the list simply does not have is not a failure: no retry is offered.
        Assert.True(await vm.OpenCardPageAsync("T_FILL"));
        Assert.False(page.StatsFailed);
        Assert.Equal(Loc.T("Card.Stats.None"), page.StatsEmpty);
    }

    [Fact]
    public async Task A_late_source_adds_the_delay_line_under_the_numbers()
    {
        var vm = Model(out _);
        await vm.OpenCardPageAsync("T_BOLT");
        Assert.DoesNotContain('\n', vm.CardPage.StatsSource);
        var late = new DateTime(2026, 11, 12, 3, 35, 0, DateTimeKind.Utc);
        var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.StatsSource) && vm.CardPage.StatsSource.Contains('\n', StringComparison.Ordinal))
            {
                delayed.TrySetResult();
            }
        };

        vm.SetDataHealth(new DataHealthReport(
            [new DataSourceHealth(HearthpulseData.CardStatsSource("standard")!, DataFreshness.Stale, 90, 30, late, null)], DateTime.UtcNow));
        await delayed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.EndsWith("\n" + Loc.F("Source.Health.Delay", late.ToLocalTime()), vm.CardPage.StatsSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Russian_texts_are_built_whole_and_follow_a_language_switch()
    {
        Add("a", "FT_STANDARD", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]);
        var vm = Model(out _);
        vm.SetConstructedMeta(Meta());
        await vm.OpenCardPageAsync("T_BOLT");
        var rebuilt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CardPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CardPage.MineNote) && vm.CardPage.MineNote.Contains("игра", StringComparison.Ordinal))
            {
                rebuilt.TrySetResult();
            }
        };

        Loc.Instance.SetLanguage(Loc.Russian);
        await rebuilt.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var page = vm.CardPage;
        Assert.EndsWith("· Золото · за 14 дней", page.StatsSource, StringComparison.Ordinal);
        Assert.StartsWith("Данные Hearthpulse · обновлено ", page.StatsSource, StringComparison.Ordinal);
        Assert.Equal("2 из 2 списков", page.Archetypes[0].Lists);
        Assert.Equal("Только ваши игры этого формата, считаем по играм: 1 игра с данными о картах из 1.", page.MineNote);
        Assert.Equal("1 игра · 1–0", page.MineTiles[0].Detail);
        Assert.Contains("Стоимость: 2", page.Facts);
        Assert.DoesNotContain(page.Tiles.Concat(page.MineTiles), t => t.Label.Length == 0 || t.Label.StartsWith("Card.", StringComparison.Ordinal));
    }

    private static string P(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>A model with the test cards and a loader answering every format with BE-like numbers for T_BOLT.</summary>
    private MainViewModel Model(out List<string> asked, Func<string, CardStatsSlice?>? answer = null)
    {
        var formats = asked = [];
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.SetCards(Cards());
        vm.LoadCardStats = (format, _) =>
        {
            lock (formats)
            {
                formats.Add(format);
            }

            return Task.FromResult(answer is null ? Slice(format, Stat("T_BOLT", 1001)) : answer(format));
        };
        return vm;
    }

    private static CardStat Stat(string id, int dbf) => new(id, dbf, 0.2236, 0.5513, 1.99, 143721, 0.5361, 0.5407, 0.5020, 0.5610, 6.41);

    private static CardStatsSlice Slice(string format, CardStat stat) => new(format, format == "wild" ? "LEGEND" : "GOLD", "LAST_14_DAYS", Collected,
        new Dictionary<string, CardStat>(StringComparer.Ordinal) { [stat.CardId] = stat });

    private static CardDb Cards() => CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id": "HERO_08", "dbfId": 637, "name": "Jaina Proudmoore", "type": "HERO", "cardClass": "MAGE", "set": "HERO_SKINS"},
          {"id": "T_BOLT", "dbfId": 1001, "name": "Test Bolt", "cost": 2, "type": "SPELL", "cardClass": "MAGE", "set": "EMERALD_DREAM",
           "text": "[x]<b>Discover</b> a spell.\nDeal $3 damage.", "collectible": true},
          {"id": "T_FILL", "dbfId": 1002, "name": "Test Filler", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true},
          {"id": "BE_014", "dbfId": 132347, "name": "Spawn of C'Thun", "cost": 4, "type": "MINION", "cardClass": "NEUTRAL", "set": "BLACK_EMPIRE", "collectible": true},
          {"id": "T_BOLT_WILD", "dbfId": 1004, "name": "Old Bolt", "cost": 3, "type": "SPELL", "cardClass": "MAGE", "set": "LEGACY", "collectible": true}
        ]
        """)));

    /// <summary>Standard, Legend, 7 days: Tempo Mage (1000 games, 55 %, mulligan baseline 52 %) with two lists, both running T_BOLT.</summary>
    private static ConstructedMeta Meta()
    {
        var cards = Cards();
        MetaDeck Deck(int bolts) => new(
            DeckString.Encode(new Dictionary<string, int>(StringComparer.Ordinal) { ["T_BOLT"] = bolts, ["T_FILL"] = 2 }, "MAGE", "FT_STANDARD", cards)!,
            1, "Tempo Mage", "MAGE", 100, 50, 0.5, default);
        return new ConstructedMeta("standard", "legend", "past_week", MetaUpdated, 1000,
            [
                new MetaArchetype(1, "Tempo Mage", "Tempo Mage", "MAGE", 1000, 550, 0.55, 1, 0, 0, 0, 0, [],
                    [new MetaMulliganCard("T_BOLT", 100, 0.80, 200, 0.60), new MetaMulliganCard("T_FILL", 100, 0.5, 300, 0.5)]) { MulliganBaseline = 0.52 },
            ],
            [Deck(2), Deck(1)]);
    }

    /// <summary>Answers the Standard card list from the fixture once <see cref="Release"/> is set; counts requests and cancellations.</summary>
    private sealed class HeldServer : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.PathAndQuery.TrimStart('/'));
            }

            Waiting.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            var body = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cards", "cards-standard.json"), cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private void Add(string key, string format, string result, string[] offered, string[] replaced, string[] drawn, string[] played)
    {
        var at = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local);
        Assert.True(_history.Add(new GameRecord
        {
            Key = key,
            StartedAt = at,
            EndedAt = at.AddMinutes(8),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = format,
            Result = result,
            PlayerClass = "MAGE",
            OpponentClass = "PRIEST",
            Mulligan = GameCards.MulliganJson(new MulliganFacts(offered, replaced, [])),
            CardsDrawn = GameCards.TurnsJson(drawn.Select((id, i) => new CardTurn(id, i)).ToList()),
            CardsPlayed = GameCards.TurnsJson(played.Select((id, i) => new CardTurn(id, i + 1)).ToList()),
        }));
    }
}

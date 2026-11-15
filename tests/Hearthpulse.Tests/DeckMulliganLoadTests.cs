using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The per-deck mulligan (docs/mulligan-v2.md 2.2) over a fake server answering with trimmed live answers of
/// 2026-11-15 (Fixtures/Mulligan/hsreplay-*; produced by the requests named in docs/mulligan-v2.md): the player's exact
/// list, a close list, no match (not one slice asked: the list of decks carries the cards), an old cache without card
/// lists, Wild, the slices of a game, the disk cache with remembered 404s, a server that is down and a cancelled caller.
/// </summary>
public sealed class DeckMulliganLoadTests : IDisposable
{
    private const string Warlock = "7X8Zqlb1wBPwkkaQVGT8ee";
    private const string RogueA = "vqugUxEMufzPN9UNvcADrg";
    private const string RogueB = "H643Fy7N2Lp5ebtbXp6Msf";
    private const string WildRogue = "wZZ3Ak2JvYAzAHlXHZIcwh";

    private readonly string _cache = TestDataFolder.Scratch("deckmulligan");

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Fact]
    public async Task The_players_exact_list_gets_that_decks_numbers()
    {
        var server = Server.Standard();
        var deck = ListOf(Warlock);

        var guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty);

        Assert.NotNull(guide);
        Assert.Equal((MulliganScope.Deck, Warlock, 0), (guide.Scope, guide.DeckId, guide.DeckDifference));
        Assert.Equal("C'Thun Warlock", guide.Name);
        Assert.Contains(HearthpulseData.Attribution, guide.Source, StringComparison.Ordinal);
        var all = Assert.Single(guide.DeckSlices);
        Assert.Equal(("legend", "all", "any", 456569), (all.Rank, all.Initiative, all.OpponentClass, all.Games));

        var view = MulliganGuide.Compute(guide, Hand("BE_085", "CORE_CS2_062", "CAP_405"), null, null, null, guide.Name)!;
        Assert.Equal(MulliganScope.Deck, view.Scope);
        Assert.Equal(456569, view.DeckGames);
        Assert.Equal(new DateTime(2026, 11, 14, 1, 5, 27, DateTimeKind.Utc), view.DataAsOf);
        Assert.Equal(0.5409, view.BaseWinRate, 4);
        var ritual = view.Cards[0];
        Assert.Equal(0.971, ritual.KeepRate!.Value, 4);
        Assert.Equal(0.5672, ritual.WinRate!.Value, 4);
        Assert.Equal(0.0263, ritual.Delta!.Value, 4); // the answer's own opening_hand_impact, 2.63 points
        Assert.Equal(MulliganCall.Keep, ritual.Call);
        Assert.Equal(MulliganCall.Mulligan, view.Cards[1].Call); // Hellfire: 51.3 % against the deck's 54.09 %

        // The deck's own numbers are the whole guide: the archetype sources are never asked.
        Assert.Equal(2, server.Requests.Count); // the list of decks (it names the deck's cards) and the deck's slice
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("api/db/", StringComparison.Ordinal) || r.Contains("hsguru", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_different_cards_make_it_a_close_deck_and_three_do_not_match()
    {
        var server = Server.Standard();
        var near = ListOf(Warlock);
        near.Remove(69519); // both copies of Hellfire
        near[1] = 1;
        near[2] = 1;

        var guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", near, CardDb.Empty);

        Assert.NotNull(guide);
        Assert.Equal((MulliganScope.NearDeck, Warlock, 2), (guide.Scope, guide.DeckId, guide.DeckDifference));
        var view = MulliganGuide.Compute(guide, Hand("BE_085", "CAP_405"), null, null, null, guide.Name)!;
        Assert.Equal((MulliganScope.NearDeck, 2, 456569), (view.Scope, view.DeckDifference, view.DeckGames!.Value));

        near[130281] = 1; // one Ritual of C'Thun out as well: three cards away
        near[3] = 1;
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "WARLOCK", near, CardDb.Empty));
    }

    [Fact]
    public async Task The_same_list_wins_over_a_close_one_with_more_games()
    {
        // The two collected Coin Rogue lists are two cards apart; the other one has more Legend games.
        var server = Server.Standard();
        Assert.Equal(2, MulliganGuide.DeckDifference(ListOf(RogueA), ListOf(RogueB)));

        var guide = await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "ROGUE", ListOf(RogueA), CardDb.Empty);

        Assert.Equal((MulliganScope.Deck, RogueA), (guide!.Scope, guide.DeckId));
    }

    [Fact]
    public async Task A_deck_that_matches_none_asks_for_the_list_of_decks_and_not_one_slice()
    {
        var server = Server.Standard();
        var homebrew = Enumerable.Range(1000, 15).ToDictionary(dbf => dbf, _ => 2);

        // A class the server collects no deck for, then a class with two collected decks, the game's coin and opponent
        // known: the list of decks names every deck's cards, so nothing else is asked.
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "DRUID", homebrew, CardDb.Empty, opponentClass: "MAGE", goingFirst: false));
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "ROGUE", homebrew, CardDb.Empty, opponentClass: "MAGE", goingFirst: false));
        // Three cards away from a collected list is no match either.
        var far = ListOf(Warlock);
        far.Remove(69519);
        far[130281] = 1;
        far[1] = 3;
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "WARLOCK", far, CardDb.Empty, opponentClass: "MAGE", goingFirst: false));

        Assert.Equal(["v1/hsreplay/mulligan/decks?format=standard&limit=500"], server.Requests);
        Assert.Equal(["hsreplay-mulligan-decks-standard.json"], Directory.GetFiles(_cache).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_list_of_decks_cached_without_card_lists_reads_them_from_the_decks_slices()
    {
        // The answer as the server gave it before 2026-11-15: the same rows without deck_list.
        var server = Server.Standard();
        var rows = System.Text.Json.Nodes.JsonNode.Parse(Server.Text("hsreplay-mulligan-decks-standard.json"))!;
        foreach (var row in rows["data"]!.AsArray())
        {
            row!.AsObject().Remove("deck_list");
        }

        server.Route("v1/hsreplay/mulligan/decks?format=standard", rows.ToJsonString());
        var homebrew = Enumerable.Range(1000, 15).ToDictionary(dbf => dbf, _ => 2);

        // No match: one all-games slice per collected deck of the class names its cards, nothing of this game.
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "ROGUE", homebrew, CardDb.Empty, opponentClass: "MAGE", goingFirst: false));
        var slices = server.Requests.Where(r => r.StartsWith("v1/hsreplay/mulligan?", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, slices.Count);
        Assert.All(slices, r => Assert.Contains("initiative=all&opponent_class=any", r, StringComparison.Ordinal));

        // Those answers are cached like any other: the next lookup asks nothing.
        var asked = server.Requests.Count;
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "ROGUE", homebrew, CardDb.Empty));
        Assert.Equal(asked, server.Requests.Count);

        // And a deck with the same list is still found through its slice.
        var guide = await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "ROGUE", ListOf(RogueA), CardDb.Empty);
        Assert.Equal((MulliganScope.Deck, RogueA), (guide!.Scope, guide.DeckId));
        Assert.Equal(asked, server.Requests.Count);
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
    }

    [Fact]
    public async Task A_wild_deck_is_matched_among_the_wild_decks_and_reads_its_wild_slices()
    {
        // GET v1/hsreplay/mulligan/decks?format=wild&limit=500 (Rogue and Druid rows) and
        // v1/hsreplay/mulligan?deck=wZZ3Ak2JvYAzAHlXHZIcwh&format=wild&rank=legend&initiative=all|coin&opponent_class=any, 2026-11-15
        var server = Server.Wild();
        var listed = HearthpulseData.ParseMulliganDecks(System.Text.Json.JsonDocument.Parse(Server.Text("hsreplay-mulligan-decks-wild.json")).RootElement.GetProperty("data"));
        var deck = listed.First(d => d.DeckId == WildRogue).DeckList!.ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal(30, deck.Values.Sum());

        var guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "wild", "ROGUE", deck, CardDb.Empty, goingFirst: false);

        Assert.NotNull(guide);
        Assert.Equal((MulliganScope.Deck, WildRogue), (guide.Scope, guide.DeckId));
        Assert.Contains(guide.DeckSlices, s => s is { Initiative: "all", Rank: "legend", Games: 42642 });
        Assert.Contains(guide.DeckSlices, s => s is { Initiative: "coin", Games: 21312 });
        var view = MulliganGuide.Compute(guide, Hand("TLC_515"), null, null, false, guide.Name)!;
        Assert.Equal((MulliganScope.Deck, 21312, false), (view.Scope, view.DeckGames!.Value, view.GoingFirst!.Value));
        Assert.Equal(0.6233, view.BaseWinRate, 4);
        Assert.Equal(0.5571, view.Cards[0].WinRate!.Value, 4);
        Assert.Equal(new DateTime(2026, 11, 8, 3, 49, 29, DateTimeKind.Utc), view.DataAsOf);
        Assert.All(server.Requests, r => Assert.Contains("format=wild", r, StringComparison.Ordinal));

        // The other collected Wild Rogue lists are four and six cards away; a list two cards from this one is a close deck.
        var near = new Dictionary<int, int>(deck);
        var swapped = near.First(c => c.Value == 2).Key;
        near.Remove(swapped);
        near[1] = 2;
        var close = await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "wild", "ROGUE", near, CardDb.Empty);
        Assert.Equal((MulliganScope.NearDeck, WildRogue, 2), (close!.Scope, close.DeckId, close.DeckDifference));

        // A Wild homebrew, and the source by the game's format: no slice, and Standard is never asked.
        var asked = server.Requests.Count;
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "wild", "ROGUE", Enumerable.Range(1000, 15).ToDictionary(dbf => dbf, _ => 2), CardDb.Empty));
        Assert.Equal(asked, server.Requests.Count);
        var source = new MulliganGuideSource(Data(server));
        var changed = new SemaphoreSlim(0);
        source.Changed += () => changed.Release();
        var definition = new DeckDefinition("Wild Rogue", 0, string.Empty, "FT_WILD", [], deck);
        Assert.Null(source.For("ROGUE", definition, "FT_WILD", CardDb.Empty));
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(MulliganScope.Deck, source.For("ROGUE", definition, "FT_WILD", CardDb.Empty)!.Scope);
        Assert.All(server.Requests, r => Assert.Contains("format=wild", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancelled_caller_gets_nothing_and_no_exception()
    {
        var deck = ListOf(Warlock);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Cancelled before the request.
        var server = Server.Standard();
        var data = Data(server, "cancelled");
        Assert.Empty(await data.LoadHsreplayMulliganDecksAsync("standard", cancelled.Token));
        Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend", cancellationToken: cancelled.Token));
        Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(data, "standard", "WARLOCK", deck, CardDb.Empty, cancellationToken: cancelled.Token));

        // Cancelled while the server is answering: the list, then the slice of a matched deck.
        foreach (var held in new[] { "mulligan/decks", "mulligan?deck=" })
        {
            var slow = Server.Standard();
            slow.HoldOn = held;
            using var cancel = new CancellationTokenSource();
            var lookup = MulliganGuideSource.LoadDeckGuideAsync(Data(slow, "slow-" + (held.Length % 7)), "standard", "WARLOCK", deck, CardDb.Empty, cancellationToken: cancel.Token);
            await slow.Waiting.Task.WaitAsync(TimeSpan.FromMinutes(1));
            await cancel.CancelAsync();

            Assert.Null(await lookup);
        }

        // Nothing half-written, and a cancelled answer is not remembered as "the server has nothing".
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp", SearchOption.AllDirectories));
        Assert.NotNull(await MulliganGuideSource.LoadDeckGuideAsync(Data(server, "cancelled"), "standard", "WARLOCK", deck, CardDb.Empty));
    }

    [Fact]
    public async Task The_games_initiative_and_opponent_slices_are_read_and_a_missing_one_is_remembered()
    {
        var server = Server.Standard();
        var deck = ListOf(Warlock);

        // On the coin against a Mage: all four slices exist, the most specific one speaks.
        var guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty, "MAGE", false);
        Assert.Equal(4, guide!.DeckSlices.Count);
        var view = MulliganGuide.Compute(guide, Hand("BE_085", "CAP_405"), null, "MAGE", false, guide.Name)!;
        Assert.Equal((10268, false, "MAGE"), (view.DeckGames!.Value, view.GoingFirst!.Value, view.OpponentClass));
        Assert.Equal(0.5513, view.BaseWinRate, 4);

        // Against a Priest the server has nothing (404): the coin slice speaks and names no class.
        guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty, "PRIEST", false);
        view = MulliganGuide.Compute(guide!, Hand("BE_085", "CAP_405"), null, "PRIEST", false, guide!.Name)!;
        Assert.Equal((218620, false, (string?)null), (view.DeckGames!.Value, view.GoingFirst!.Value, view.OpponentClass));
        Assert.Equal(2, server.Requests.Count(r => r.Contains("opponent_class=PRIEST", StringComparison.Ordinal)));

        // The two 404s are on disk: a new session asks for nothing again.
        var asked = server.Requests.Count;
        guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty, "PRIEST", false);
        Assert.Equal(2, guide!.DeckSlices.Count);
        Assert.Equal(asked, server.Requests.Count);
        Assert.Null(await Data(server).LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend", "all", "PRIEST"));
        Assert.Equal(asked, server.Requests.Count);
    }

    [Fact]
    public async Task The_rank_filter_reads_the_closest_rank_the_deck_is_collected_at()
    {
        Assert.Equal("legend", MulliganGuideSource.DeckRankFor("legend", ["bronze_gold", "legend", "diamond"]));
        Assert.Equal("legend", MulliganGuideSource.DeckRankFor("top_legend", ["bronze_gold", "legend"]));
        Assert.Equal("diamond_4to1", MulliganGuideSource.DeckRankFor("diamond", ["legend", "diamond", "diamond_4to1"]));
        Assert.Equal("bronze_gold", MulliganGuideSource.DeckRankFor("legend", ["bronze_gold"]));
        Assert.Equal("bronze_gold", MulliganGuideSource.DeckRankFor("all", ["legend", "bronze_gold"]));

        var server = Server.Standard();
        var guide = await MulliganGuideSource.LoadGuideAsync(Data(server), "standard", "WARLOCK", ListOf(Warlock), CardDb.Empty, rank: "all");

        var slice = Assert.Single(guide!.DeckSlices);
        Assert.Equal("bronze_gold", slice.Rank);
        Assert.Equal(MulliganScope.Deck, guide.Scope);
        Assert.DoesNotContain(server.Requests, r => r.Contains("rank=legend", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_source_loads_the_deck_guide_in_the_background_and_its_slices_once_the_game_is_known()
    {
        var server = Server.Standard();
        var deck = new DeckDefinition("C'Thun Warlock", 0, string.Empty, "FT_STANDARD", [], ListOf(Warlock));
        var source = new MulliganGuideSource(Data(server));
        var changed = new SemaphoreSlim(0);
        source.Changed += () => changed.Release();

        Assert.Null(source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty));
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        var guide = source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty)!;
        Assert.Equal(MulliganScope.Deck, guide.Scope);
        Assert.Single(guide.DeckSlices);

        Assert.Same(guide, source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty, "MAGE", true));
        Assert.True(await changed.WaitAsync(TimeSpan.FromMinutes(1)));
        guide = source.For("WARLOCK", deck, "FT_STANDARD", CardDb.Empty, "MAGE", true)!;
        Assert.Contains(guide.DeckSlices, s => s is { Initiative: "first", OpponentClass: "any", Games: 237099 });
        Assert.Contains(guide.DeckSlices, s => s is { Initiative: "all", OpponentClass: "MAGE", Games: 21080 });
        // Going first against a Mage is not collected in the fixtures: the class slice speaks.
        var view = MulliganGuide.Compute(guide, Hand("BE_085"), null, "MAGE", true, guide.Name)!;
        Assert.Equal((21080, "MAGE", (bool?)null), (view.DeckGames!.Value, view.OpponentClass, view.GoingFirst));
    }

    [Fact]
    public async Task A_server_that_is_down_or_times_out_gives_no_deck_guide_and_never_throws()
    {
        var deck = ListOf(Warlock);
        foreach (var failure in new Exception[] { new HttpRequestException("down"), new TaskCanceledException("timeout") })
        {
            var down = new Server { Failure = failure };
            var data = Data(down, "down-" + failure.GetType().Name);

            Assert.Empty(await data.LoadHsreplayMulliganDecksAsync("standard"));
            Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend"));
            Assert.Null(await MulliganGuideSource.LoadDeckGuideAsync(data, "standard", "WARLOCK", deck, CardDb.Empty));
        }

        // With answers on disk a server that went down still gives the deck's numbers (a stale cache beats nothing).
        var server = Server.Standard();
        Assert.NotNull(await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty));
        foreach (var file in Directory.GetFiles(_cache, "hsreplay-mulligan-*.json"))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow - TimeSpan.FromHours(7));
        }

        server.Failure = new HttpRequestException("down");
        var stale = await MulliganGuideSource.LoadDeckGuideAsync(Data(server), "standard", "WARLOCK", deck, CardDb.Empty);
        Assert.Equal(MulliganScope.Deck, stale!.Scope);
    }

    [Fact]
    public async Task Values_that_are_not_the_apis_keys_never_reach_the_server()
    {
        var server = Server.Standard();
        var data = Data(server);

        Assert.Null(await data.LoadHsreplayDeckMulliganAsync("../decks", "standard", "legend"));
        Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard&x=1", "legend"));
        Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard", "top_legend"));
        Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend", "second"));
        Assert.Null(await data.LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend", "all", "MAGE&deck=x"));
        Assert.Empty(await data.LoadHsreplayMulliganDecksAsync("standard/../x"));

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task The_opponents_class_goes_to_the_server_in_upper_case()
    {
        var server = Server.Standard();

        var slice = await Data(server).LoadHsreplayDeckMulliganAsync(Warlock, "standard", "legend", "coin", "mage");

        Assert.Equal(("coin", "MAGE", 10268), (slice!.Initiative, slice.OpponentClass, slice.Games));
        Assert.Equal([$"v1/hsreplay/mulligan?deck={Warlock}&format=standard&rank=legend&initiative=coin&opponent_class=MAGE"], server.Requests);
    }

    private static List<OfferedCard> Hand(params string[] ids) => ids.Select((id, i) => new OfferedCard(i + 1, id, id, 0)).ToList();

    /// <summary>The card list of a collected deck, from its slice fixture.</summary>
    private static Dictionary<int, int> ListOf(string deckId)
    {
        using var doc = JsonDocument.Parse(Server.Text($"hsreplay-deck-mulligan-{deckId}-legend-all-any.json"));
        return HearthpulseData.ParseDeckMulligan(doc.RootElement.GetProperty("data"), default, deckId)!.DeckList.ToDictionary(c => c.Key, c => c.Value);
    }

    private HearthpulseData Data(Server server, string? cache = null) =>
        new(new HearthpulseApi(new HttpClient(server), "https://api.test/"), cache is null ? _cache : Path.Combine(_cache, cache));

    /// <summary>Answers with the fixture routed for a request, 404 for anything else, or fails every request.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body)> _routes = [];

        public List<string> Requests { get; } = [];

        /// <summary>When set, every request fails with it (the server is down, or the request timed out).</summary>
        public Exception? Failure { get; set; }

        /// <summary>A request holding this text waits until its caller cancels (<see cref="Waiting"/> completes once it does).</summary>
        public string? HoldOn { get; set; }

        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Later routes win, so a test can override a fixture.</summary>
        public void Route(string fragment, string body) => _routes.Insert(0, (fragment, body));

        public static Server Wild()
        {
            var server = new Server();
            server._routes.Add(("v1/hsreplay/mulligan/decks?format=wild", Text("hsreplay-mulligan-decks-wild.json")));
            foreach (var initiative in new[] { "all", "coin" })
            {
                server._routes.Add(($"v1/hsreplay/mulligan?deck={WildRogue}&format=wild&rank=legend&initiative={initiative}&opponent_class=any",
                    Text($"hsreplay-deck-mulligan-wild-{WildRogue}-legend-{initiative}-any.json")));
            }

            return server;
        }

        public static Server Standard()
        {
            var server = new Server();
            server._routes.Add(("v1/hsreplay/mulligan/decks?format=standard", Text("hsreplay-mulligan-decks-standard.json")));
            foreach (var (deck, rank, initiative, opponent) in new[]
                     {
                         (Warlock, "legend", "all", "any"), (Warlock, "legend", "coin", "any"), (Warlock, "legend", "first", "any"),
                         (Warlock, "legend", "all", "MAGE"), (Warlock, "legend", "coin", "MAGE"), (Warlock, "bronze_gold", "all", "any"),
                         (RogueA, "legend", "all", "any"), (RogueB, "legend", "all", "any"),
                     })
            {
                server._routes.Add(($"v1/hsreplay/mulligan?deck={deck}&format=standard&rank={rank}&initiative={initiative}&opponent_class={opponent}",
                    Text($"hsreplay-deck-mulligan-{deck}-{rank}-{initiative}-{opponent}.json")));
            }

            return server;
        }

        public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mulligan", name));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            lock (Requests)
            {
                Requests.Add(path);
            }

            if (Failure is { } failure)
            {
                throw failure;
            }

            if (HoldOn is { } held && path.Contains(held, StringComparison.Ordinal))
            {
                Waiting.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var route = _routes.FirstOrDefault(r => path.StartsWith(r.Fragment, StringComparison.Ordinal));
            return route.Body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\": \"This deck slice is not collected\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }
}

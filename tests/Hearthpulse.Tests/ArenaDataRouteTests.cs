using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The compact Arena card route and the legendary packages (docs/arena-draft.md, "Data"). Fixtures are live answers of
/// 2026-11-15 cut down to a few rows: <c>arena-cards-route.json</c> is
/// <c>GET v1/arena/cards?fields=id,dbfId,score,tier,cardClass,win_rate</c> (seven of 1218 rows, <c>meta.count</c> set
/// to seven so the fixture is one whole page), <c>arena-legendaries.json</c> is
/// <c>GET datasets/hsreplay_arena_legendaries</c> (two of 133 groups, without <c>by_class</c>). No network here: a fake
/// handler answers.
/// </summary>
public sealed class ArenaDataRouteTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "hp-arena-route-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    [Fact]
    public void The_route_parses_scores_the_slice_and_the_date()
    {
        var slice = HearthpulseData.ParseArenaRoute(Root("arena-cards-route.json"));

        Assert.Equal(7, slice.Cards.Count);
        Assert.Equal((7, 7), (slice.Rows, slice.Total));
        Assert.True(slice.Underground);
        Assert.Equal("LAST_4_DAYS", slice.TimeRange);
        Assert.Equal(new DateTime(2026, 11, 15, 17, 0, 8, DateTimeKind.Utc), slice.FetchedAt!.Value, TimeSpan.FromSeconds(1));
        var card = slice.Cards.Single(c => c.CardId == "BE_287");
        Assert.Equal((130341, 54.0, "S", "DEATHKNIGHT"), (card.DbfId, card.Score, card.Tier, card.CardClass));
        Assert.Equal(0.611, card.WinRate!.Value, 3);
    }

    /// <summary>
    /// A card the server has no score for stays without one: no number and no letter, although the row carries the
    /// source's own letter (which follows the win rate of a handful of games).
    /// </summary>
    [Fact]
    public void A_card_without_a_score_is_not_scored_as_zero()
    {
        var slice = HearthpulseData.ParseArenaRoute(Root("arena-cards-route.json"));
        var unscored = slice.Cards.Single(c => c.CardId == "TLC_231");
        var low = slice.Cards.Single(c => c.CardId == "CORE_EX1_507");

        Assert.Null(unscored.Score);
        Assert.Null(unscored.Tier);
        Assert.Equal("S", unscored.SourceTier);
        // The letter shown is the score's, not the one sent with the row.
        Assert.Equal((1.8, "E", "S"), (low.Score, low.Tier, low.SourceTier));

        var meta = new ArenaMeta(slice.Cards, []);
        var view = ArenaDraft.Build(new ArenaDraftState("HERO_07", 0, ["TLC_231", "BE_283"], "DRAFTING"), ["TLC_231", "BE_283", "RLK_066"], meta, Cards);
        var option = view.Options[0];
        Assert.Null(option.Score);
        Assert.Null(option.Tier);
        Assert.False(option.IsBest);
        Assert.Null(view.Deck.Single(c => c.CardId == "TLC_231").Score);
        // The deck's average counts the scored card only: an unscored one is no zero in it.
        Assert.Equal((54.0, 1, 2), (view.Score.Average, view.Score.Scored, view.Score.Cards));
    }

    [Fact]
    public void The_answer_of_another_shape_gives_no_cards()
    {
        Assert.Empty(HearthpulseData.ParseArenaRoute(JsonSerializer.Deserialize<JsonElement>("""{"data":null}""")).Cards);
        Assert.Empty(HearthpulseData.ParseArenaRoute(JsonSerializer.Deserialize<JsonElement>("""{"data":{"structured":{}}}""")).Cards);
        var odd = HearthpulseData.ParseArenaRoute(JsonSerializer.Deserialize<JsonElement>(
            """{"data":[{"score":10},7,{"id":"A","score":"high","win_rate":null}],"meta":{"game_type":"brawl","count":"3"}}"""));

        var card = Assert.Single(odd.Cards);
        Assert.Equal(("A", null, null), (card.CardId, card.Score, card.WinRate));
        // Rows without an id are skipped but counted: the next page starts after them.
        Assert.Equal(3, odd.Rows);
        Assert.Null(odd.Total);
        Assert.Null(odd.Underground);
    }

    [Fact]
    public async Task Meta_loads_from_the_route_and_offline_from_its_cache()
    {
        var server = new FakeApi { Route = true, Legendaries = true };
        var meta = await Data(server).LoadArenaMetaAsync();

        Assert.Equal(7, meta.CardCount);
        Assert.Equal(54.0, meta.Score("BE_283"));
        Assert.Null(meta.Score("RLK_051"));
        Assert.Equal(130395, meta.Card(130395)!.DbfId);
        Assert.True(meta.Underground);
        Assert.Equal("LAST_4_DAYS", meta.TimeRange);
        Assert.Equal(new DateTime(2026, 11, 15, 17, 0, 8, DateTimeKind.Utc), meta.FetchedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(11, meta.Classes.Count);
        Assert.Equal(2, meta.PackageCount);
        // The 1.7 MB dataset is not asked for while the route answers.
        Assert.Single(server.Requests, r => r.EndsWith(HearthpulseData.ArenaCardsRoute, StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, r => r.Contains(HearthpulseData.ArenaCardsPath, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_cache, "arena-cards-route.json")));
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));

        // A fresh cache answers without the server.
        var again = new FakeApi { Route = true, Legendaries = true };
        Assert.Equal(7, (await Data(again).LoadArenaMetaAsync()).CardCount);
        Assert.Empty(again.Requests);

        // The server goes down and the cache is old: the stale copy is served, with its slice and date.
        Age(TimeSpan.FromDays(2));
        var offline = await Data(new FakeApi { Offline = true }).LoadArenaMetaAsync();
        Assert.Equal(7, offline.CardCount);
        Assert.True(offline.Underground);
        Assert.Equal(meta.FetchedAt, offline.FetchedAt);
        Assert.Equal(["BE_283", "RLK_066", "RLK_051"], offline.PackageOf("BE_287"));
    }

    [Fact]
    public async Task Without_the_route_the_dataset_is_read_and_the_missing_route_is_remembered()
    {
        var server = new FakeApi { Route = false };
        var meta = await Data(server).LoadArenaMetaAsync();

        Assert.Equal(5, meta.CardCount);
        Assert.Equal(54.0, meta.Score("BE_085"));
        Assert.True(meta.Underground);
        Assert.Null(meta.TimeRange);
        Assert.Equal(0, meta.PackageCount);
        Assert.Contains(server.Requests, r => r.EndsWith(HearthpulseData.ArenaCardsRoute, StringComparison.Ordinal));
        Assert.Contains(server.Requests, r => r.EndsWith(HearthpulseData.ArenaCardsPath, StringComparison.Ordinal));
        // The 404 is cached as "nothing" for the cache's age: the next load asks for neither the route nor the packages.
        Assert.Equal("{\"data\":null}", File.ReadAllText(Path.Combine(_cache, "arena-cards-route.json")));

        var second = new FakeApi { Route = false };
        Assert.Equal(5, (await Data(second).LoadArenaMetaAsync()).CardCount);
        Assert.Empty(second.Requests);
    }

    [Fact]
    public async Task The_route_coming_back_replaces_the_dataset()
    {
        Assert.Equal(5, (await Data(new FakeApi { Route = false }).LoadArenaMetaAsync()).CardCount);
        Age(TimeSpan.FromHours(7));

        var meta = await Data(new FakeApi { Route = true }).LoadArenaMetaAsync();

        Assert.Equal(7, meta.CardCount);
        Assert.Equal("LAST_4_DAYS", meta.TimeRange);
    }

    [Fact]
    public async Task Offline_after_an_update_the_dataset_cache_of_the_old_version_still_serves()
    {
        // What the previous version left behind: the trimmed dataset under its old name, no route file.
        using (var doc = JsonDocument.Parse(File.ReadAllText(Fixture("arena-cards.json"))))
        {
            Directory.CreateDirectory(_cache);
            var small = HearthpulseData.ShrinkArenaCards(doc.RootElement.GetProperty("data"));
            File.WriteAllText(Path.Combine(_cache, "arena-cards.json"), JsonSerializer.Serialize(new { data = small }));
        }

        Age(TimeSpan.FromDays(1));
        var meta = await Data(new FakeApi { Offline = true }).LoadArenaMetaAsync();

        Assert.Equal(5, meta.CardCount);
        Assert.True(meta.Underground);
    }

    /// <summary>A timeout is a <see cref="TaskCanceledException"/>, not an <see cref="HttpRequestException"/>: the load still does not throw.</summary>
    [Fact]
    public async Task A_timeout_gives_an_empty_meta_instead_of_an_exception()
    {
        var meta = await Data(new FakeApi { TimesOut = true }).LoadArenaMetaAsync();

        Assert.False(meta.HasCards);
        Assert.Empty(meta.Classes);
        Assert.Equal(0, meta.PackageCount);
    }

    [Fact]
    public async Task A_cancelled_load_is_cancelled_not_swallowed()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Data(new FakeApi { Route = true }).LoadArenaMetaAsync(cancel.Token));
    }

    [Fact]
    public async Task A_list_longer_than_a_page_is_read_page_by_page()
    {
        var server = new FakeApi { Pages = [["A", "B", "C"], ["D", "E"]] };
        var meta = await Data(server).LoadArenaMetaAsync();

        Assert.Equal(["A", "B", "C", "D", "E"], meta.Cards.Select(c => c.CardId).Order(StringComparer.Ordinal));
        Assert.True(meta.Underground);
        Assert.Contains(server.Requests, r => r.EndsWith(HearthpulseData.ArenaCardsRoute + "&offset=3", StringComparison.Ordinal));
        Assert.Equal(2, server.Requests.Count(r => r.Contains("v1/arena/cards", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_later_page_that_fails_keeps_the_cards_read_so_far()
    {
        var server = new FakeApi { Pages = [["A", "B", "C"]], PagesTotal = 5 };
        var meta = await Data(server).LoadArenaMetaAsync();

        Assert.Equal(3, meta.CardCount);
        Assert.Null(meta.Score("D"));
    }

    [Fact]
    public void Legendary_packages_parse_from_the_real_answer_and_from_the_cache()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("arena-legendaries.json")));
        var data = doc.RootElement.GetProperty("data");

        var whole = HearthpulseData.ParseArenaLegendaries(data);
        var small = JsonSerializer.SerializeToElement(HearthpulseData.ShrinkArenaLegendaries(data));
        var cached = HearthpulseData.ParseArenaLegendaries(small);

        Assert.Equal(["BE_004", "BE_287"], whole.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["BE_283", "RLK_066", "RLK_051"], whole["BE_287"]);
        Assert.Equal(["TOY_811", "YOG_509", "JAM_011"], whole["BE_004"]);
        Assert.Equal(whole["BE_287"], cached["BE_287"]);
        Assert.Equal(whole["BE_004"], cached["BE_004"]);
        // The cache keeps ids only: no names, rates or scores of the dataset.
        Assert.True(small.GetRawText().Length < 200);
    }

    [Fact]
    public void A_package_counts_copies_and_skips_broken_groups()
    {
        var data = JsonSerializer.Deserialize<JsonElement>("""
            {"structured":{"groups":[
              {"key_card":{"id":"L1"},"cards":[{"id":"A","count":2},{"card_id":"B"},{"count":1},"C"]},
              {"key_card":null,"cards":[{"id":"X"}]},
              {"key_card":{"id":"L2"},"cards":[]},
              {"key_card":{"id":"L1"},"cards":[{"id":"Z"}]},
              7
            ]}}
            """);

        var packages = HearthpulseData.ParseArenaLegendaries(data);

        Assert.Equal(["A", "A", "B", "C"], Assert.Single(packages).Value);
        Assert.Empty(HearthpulseData.ParseArenaLegendaries(JsonSerializer.Deserialize<JsonElement>("[]")));
    }

    /// <summary>
    /// L-M1 (docs/arena-draft.md): before the pick a badge names only what the client itself showed. The server knows the
    /// package of the offered legendary; without a package in the offer read from the screen the badge has neither its
    /// cards nor its average.
    /// </summary>
    [Fact]
    public void The_servers_package_list_is_never_shown_on_an_offer()
    {
        var meta = Meta();
        Assert.Equal(3, meta.PackageOf("BE_287").Count);

        var view = ArenaDraft.Build(new ArenaDraftState("HERO_07", 0, [], "DRAFTING"), new ArenaDraftOffer(["BE_287", "RLK_066", "BE_283"], true), meta, Cards);

        var legendary = view.Options[0];
        Assert.Empty(legendary.Package!);
        Assert.Equal(meta.Score("BE_287"), legendary.Score);
        Assert.Equal(0, view.Picks);
    }

    [Fact]
    public void A_first_pick_whose_offer_was_not_read_gets_the_servers_package_after_the_pick()
    {
        using var draft = Session(Meta());
        draft.HeroPowerOffer(underground: true);
        draft.Session.SetDraftOffer((ArenaDraftOffer?)null);

        draft.Choose("BE_287");

        var view = draft.Session.Current.ArenaDraft!;
        Assert.Equal(4, view.Picks);
        Assert.Equal(["BE_283", "BE_287", "RLK_051", "RLK_066"], view.Deck.Select(c => c.CardId).Order(StringComparer.Ordinal));

        // The client logs a legendary taken with its package twice: still one pick and one package.
        draft.Choose("BE_287");
        Assert.Equal(4, draft.Session.Current.ArenaDraft!.Picks);
    }

    [Fact]
    public void An_offer_that_was_read_is_the_truth_also_without_a_package()
    {
        using var draft = Session(Meta());
        draft.HeroPowerOffer(underground: true);
        draft.Session.SetDraftOffer(new ArenaDraftOffer(["BE_287", "RLK_066", "BE_283"], true));
        draft.Tick();

        draft.Choose("BE_287");

        Assert.Equal(1, draft.Session.Current.ArenaDraft!.Picks);
    }

    [Fact]
    public void The_servers_package_is_not_added_to_a_normal_run_or_after_the_first_card()
    {
        using (var normal = Session(Meta()))
        {
            normal.HeroPowerOffer(underground: false);
            normal.Session.SetDraftOffer((ArenaDraftOffer?)null);
            normal.Choose("BE_287");
            Assert.Equal(1, normal.Session.Current.ArenaDraft!.Picks);
        }

        using (var unknown = Session(Meta()))
        {
            unknown.Session.SetDraftOffer((ArenaDraftOffer?)null);
            unknown.Choose("BE_287");
            Assert.Equal(1, unknown.Session.Current.ArenaDraft!.Picks);
        }

        using var later = Session(Meta());
        later.HeroPowerOffer(underground: true);
        later.Session.SetDraftOffer((ArenaDraftOffer?)null);
        later.Choose("RLK_066");
        later.Choose("BE_287");
        Assert.Equal(2, later.Session.Current.ArenaDraft!.Picks);
    }

    [Fact]
    public void Lines_read_while_catching_up_add_no_package_from_the_server()
    {
        using var draft = Session(Meta());
        draft.HeroPowerOffer(underground: true);
        draft.Session.SetDraftOffer((ArenaDraftOffer?)null);

        draft.Session.OnLines("Arena.log", ["D 21:01:00.0000000 Client chooses: Legendary (BE_287)"], catchingUp: true);
        draft.Tick();

        Assert.Equal(1, draft.Session.Current.ArenaDraft!.Picks);
    }

    private static ArenaMeta Meta()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("arena-legendaries.json")));
        var slice = HearthpulseData.ParseArenaRoute(Root("arena-cards-route.json"));
        return new ArenaMeta(slice.Cards, [], slice.FetchedAt, slice.Underground, slice.TimeRange, HearthpulseData.ParseArenaLegendaries(doc.RootElement.GetProperty("data")));
    }

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"BE_287","dbfId":130341,"name":"Legendary","cost":8,"type":"MINION","rarity":"LEGENDARY","cardClass":"DEATHKNIGHT","collectible":true},
          {"id":"BE_283","dbfId":130395,"name":"Epic","cost":4,"type":"MINION","rarity":"EPIC","cardClass":"DEATHKNIGHT","collectible":true},
          {"id":"RLK_066","dbfId":78356,"name":"Rare Minion","cost":2,"type":"MINION","rarity":"RARE","cardClass":"DEATHKNIGHT","collectible":true},
          {"id":"RLK_051","dbfId":78345,"name":"Rare Spell","cost":2,"type":"SPELL","rarity":"RARE","cardClass":"DEATHKNIGHT","collectible":true},
          {"id":"TLC_231","dbfId":117648,"name":"Unscored","cost":3,"type":"MINION","rarity":"COMMON","cardClass":"DRUID","collectible":true},
          {"id":"HERO_07","dbfId":893,"name":"Gul'dan","type":"HERO","cardClass":"WARLOCK","collectible":true},
          {"id":"HERO_07bp","dbfId":1,"name":"Life Tap","type":"HERO_POWER","cardClass":"WARLOCK"},
          {"id":"HERO_08bp","dbfId":2,"name":"Fireblast","type":"HERO_POWER","cardClass":"MAGE"}
        ]
        """)));

    /// <summary>A run on the draft screen after the hero pick, with the helper on and no game memory.</summary>
    private static DraftSession Session(ArenaMeta meta)
    {
        var memory = new HearthstoneMemory();
        var draft = new DraftSession(memory, new DateTime(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc));
        draft.Session = new GameSession(HistoryStore.InMemory(), memory)
        {
            Clock = () => draft.Now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Cards = Cards,
        };
        draft.Session.SetArenaMeta(meta);
        draft.Session.SetArenaDraftEnabled(true);
        draft.Session.OnLines("LoadingScreen.log", ["D 21:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=HUB currMode=DRAFT"], catchingUp: false);
        draft.Session.OnLines("Arena.log",
        [
            "D 21:00:01.0000000 DraftManager.OnBegin - Got new draft deck with ID: 900000002",
            "D 21:00:01.0000000 SetDraftMode - DRAFTING",
            "D 21:00:02.0000000 Client chooses: Gul'dan (HERO_07)",
            "D 21:00:02.0000000 DraftManager.OnChosen(): hero=HERO_07 premium=NORMAL",
        ], catchingUp: false);
        return draft;
    }

    private sealed class DraftSession(HearthstoneMemory memory, DateTime now) : IDisposable
    {
        public GameSession Session { get; set; } = null!;

        public DateTime Now { get; private set; } = now;

        public void Tick()
        {
            Now = Now.AddSeconds(1);
            Session.OnTick();
        }

        /// <summary>The hero power offer is read from the screen (it names the run's Arena) and one is taken.</summary>
        public void HeroPowerOffer(bool underground)
        {
            Session.SetDraftOffer(["HERO_07bp", "HERO_08bp"], underground);
            Tick();
            Session.OnLines("Arena.log", ["D 21:00:10.0000000 Client chooses: Life Tap (HERO_07bp)"], catchingUp: false);
            Tick();
        }

        public void Choose(string cardId)
        {
            Session.OnLines("Arena.log", [$"D 21:01:00.0000000 Client chooses: Card ({cardId})"], catchingUp: false);
            Tick();
        }

        public void Dispose() => memory.Dispose();
    }

    private void Age(TimeSpan age)
    {
        foreach (var file in Directory.GetFiles(_cache))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow - age);
        }
    }

    private HearthpulseData Data(FakeApi server) => new(new HearthpulseApi(new HttpClient(server), HearthpulseApi.DefaultBaseUrl), _cache);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Arena", name);

    /// <summary>The whole answer, as the cache keeps a page of the route (<c>data</c> and <c>meta</c>).</summary>
    private static JsonElement Root(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture(name)));
        return doc.RootElement.Clone();
    }

    /// <summary>The data API with or without the compact route; classes and the old dataset are always there.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        public bool Route { get; init; }

        public bool Legendaries { get; init; }

        public bool Offline { get; init; }

        public bool TimesOut { get; init; }

        /// <summary>Card ids per page of the route, served by <c>offset</c> instead of the fixture.</summary>
        public IReadOnlyList<string[]>? Pages { get; init; }

        /// <summary>The count the paged answers claim; the cards of <see cref="Pages"/> when not set.</summary>
        public int? PagesTotal { get; init; }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (Requests)
            {
                Requests.Add(path);
            }

            if (Offline)
            {
                throw new HttpRequestException("offline");
            }

            if (TimesOut)
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
            }

            if (Pages is not null && path.Contains("v1/arena/cards", StringComparison.Ordinal))
            {
                return Task.FromResult(Page(path));
            }

            var file = path.Contains("v1/arena/cards", StringComparison.Ordinal) ? (Route ? "arena-cards-route.json" : null)
                : path.Contains("datasets/hsreplay_arena_cards_advanced", StringComparison.Ordinal) ? "arena-cards.json"
                : path.Contains("datasets/hsreplay_arena_legendaries", StringComparison.Ordinal) ? (Legendaries ? "arena-legendaries.json" : null)
                : path.Contains("v1/arena/classes", StringComparison.Ordinal) ? "arena-classes.json"
                : null;
            return Task.FromResult(file is null ? NotFound() : Json(File.ReadAllText(Fixture(file))));
        }

        private HttpResponseMessage Page(string path)
        {
            var at = path.IndexOf("&offset=", StringComparison.Ordinal);
            var offset = at < 0 ? 0 : int.Parse(path[(at + 8)..], CultureInfo.InvariantCulture);
            var total = PagesTotal ?? Pages!.Sum(p => p.Length);
            var start = 0;
            foreach (var page in Pages!)
            {
                if (start == offset)
                {
                    var rows = string.Join(',', page.Select(id => $$"""{"id":"{{id}}","score":40.0}"""));
                    var meta = $$"""{"count":{{total}},"offset":{{offset}},"game_type":"underground","time_range":"LAST_4_DAYS"}""";
                    return Json("{\"data\":[" + rows + "],\"meta\":" + meta + "}");
                }

                start += page.Length;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"detail\":\"boom\"}") };
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"Not Found\"}") };
    }
}

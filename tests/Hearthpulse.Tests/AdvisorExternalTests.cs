using System.Net;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core;
using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Site;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The in-game advisor on the permitted external data with an empty stats pool (docs/bg-meta-sources.md, section 4):
/// comps and trinkets from the site service's lists, the lobby adjustment of the hero pick from each hero's
/// compositions. Real answers cut down to fixtures (Fixtures/BgSite, Fixtures/BgMeta); the network is a fake handler.
/// </summary>
public sealed class AdvisorExternalTests : IDisposable
{
    private const string Jaraxxus = "TB_BaconShop_HERO_37";
    private const string Millificent = "TB_BaconShop_HERO_49";
    private const string Quilboars = "Свинобразы - APM-усиление таверны";
    private const string Elementals = "Элементали - Живой азерит";
    private const string Undead = "Нежить - APM-нежить";

    // The fixture's compositions of Jaraxxus (hero-compositions-top_50_percent.json): tribe, games, average place.
    private static readonly (string Tribe, int Games, double Place)[] JaraxxusComps =
    [
        ("MURLOC", 35, 3.59), ("DEMON", 6371, 3.911), ("MECHANICAL", 59, 4.2), ("UNDEAD", 37, 4.224), ("BEAST", 104, 4.32),
        ("ABERRATION", 42, 4.345), ("QUILBOAR", 155, 4.371), ("DRAGON", 73, 4.588), ("PIRATE", 49, 4.602), ("ELEMENTAL", 43, 5.011),
    ];

    private readonly string _dir = TestDataFolder.Scratch("hp-advisor-external");

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    // ---- comps ---------------------------------------------------------------------------------

    [Fact]
    public async Task With_an_empty_pool_the_hero_pick_shows_the_best_comps_the_lobby_allows()
    {
        var meta = await ExternalMetaAsync();
        var advisor = new BattlegroundsAdvisor { Meta = meta };

        // Neither Undead nor Pirates in the lobby: the Undead comp's core cannot appear there.
        var advice = advisor.Enrich(HeroPick(["QUILBOAR", "ELEMENTAL", "MURLOC", "DEMON", "BEAST"]), 7000, Cards());

        // Ordered by the hero's own average place in the comp's tribe family: Quilboar 4.371 before Elemental 5.011
        // for Jaraxxus, Elemental 4.557 before Quilboar 4.685 for Millificent.
        var jaraxxus = advice.HeroOptions.Single(h => h.CardId == Jaraxxus);
        Assert.Equal([Quilboars, Elementals], jaraxxus.TopComps!.Select(c => c.Name));
        Assert.Equal([Elementals, Quilboars], advice.HeroOptions.Single(h => h.CardId == Millificent).TopComps!.Select(c => c.Name));

        // The tooltip's facts: the source's tier letter, difficulty and advice.
        var comp = jaraxxus.TopComps![0];
        Assert.Equal(("S", "easy"), (comp.Tier, comp.Difficulty));
        Assert.False(string.IsNullOrWhiteSpace(comp.Tip));
        // Each hero's own games ordered these: the badge may name them (BgHeroComp.ByHero).
        Assert.All(advice.HeroOptions, h => Assert.All(h.TopComps!, c => Assert.True(c.ByHero, $"{h.CardId}: {c.Name}")));

        // Nothing here is the pool's: the source line does not name it, and nothing is limited.
        Assert.False(meta.FromPool);
        Assert.StartsWith("Hearthpulse data · ", advice.MetaSource, StringComparison.Ordinal);
        Assert.DoesNotContain(BattlegroundsMeta.PoolName, advice.MetaSource, StringComparison.Ordinal);
        Assert.Null(advice.UsageNote);
    }

    [Fact]
    public async Task A_hero_without_games_of_its_own_gets_the_order_every_hero_shares()
    {
        var meta = await ExternalMetaAsync();
        var advisor = new BattlegroundsAdvisor { Meta = meta };

        // A hero the source has no compositions for: the comps come in the source's tier order, the same for any such
        // hero - a lobby fact the hero badge leaves to its tooltip (O-21).
        var advice = advisor.Enrich(HeroPick(["QUILBOAR", "ELEMENTAL", "MURLOC", "DEMON", "BEAST"], "TB_BaconShop_HERO_NONE_1", "TB_BaconShop_HERO_NONE_2"), 7000, Cards());

        Assert.All(advice.HeroOptions, h => Assert.NotEmpty(h.TopComps!));
        Assert.All(advice.HeroOptions, h => Assert.All(h.TopComps!, c => Assert.False(c.ByHero, $"{h.CardId}: {c.Name}")));
        Assert.Equal(advice.HeroOptions[0].TopComps!.Select(c => c.Name), advice.HeroOptions[1].TopComps!.Select(c => c.Name));
    }

    [Fact]
    public async Task A_lobby_with_the_comps_tribes_offers_it_and_an_unknown_lobby_offers_all()
    {
        var meta = await ExternalMetaAsync();
        var advisor = new BattlegroundsAdvisor { Meta = meta };
        var cards = Cards();

        var withUndead = advisor.Enrich(HeroPick(["UNDEAD", "PIRATE", "MURLOC", "DEMON", "BEAST"]), 7000, cards);
        // Elementals and Quilboars are out: of the three comps only the Undead one can be played.
        Assert.All(withUndead.HeroOptions, h => Assert.Equal([Undead], h.TopComps!.Select(c => c.Name)));

        // Undead without Pirates: two of its core cards are Pirates, and a comp does not work without its core.
        var noPirates = advisor.Enrich(HeroPick(["UNDEAD", "NAGA", "MURLOC", "DEMON", "BEAST"]), 7000, cards);
        Assert.All(noPirates.HeroOptions, h => Assert.Empty(h.TopComps!));

        // Tribes not read yet (macOS, Linux, before the memory answers): nothing is ruled out, one comp per tribe.
        var unknown = advisor.Enrich(HeroPick([]), 7000, cards);
        Assert.All(unknown.HeroOptions, h => Assert.Equal(2, h.TopComps!.Count));
    }

    [Fact]
    public async Task External_comps_have_no_placement_and_only_the_allowed_source()
    {
        var meta = await ExternalMetaAsync();

        // The fixture's two Firestone comps never reach the advisor.
        Assert.Equal(["hsreplay-76", "hsreplay-95", "hsreplay-3"], meta.Comps.Select(c => c.Id));
        Assert.All(meta.Comps, c => Assert.True(c.External));
        Assert.All(meta.Comps, c => Assert.Equal(0, c.AveragePlacement));
        Assert.Equal([["QUILBOAR"], ["ELEMENTAL"], ["UNDEAD"]], meta.Comps.Select(c => c.Tribes.ToArray()));
        Assert.All(meta.Comps, c => Assert.All(c.Cards, card => Assert.Equal("CORE", card.Status)));

        // In a game the comps are listed and, in Auto mode, one becomes the target; the order key is never a number shown.
        var advisor = new BattlegroundsAdvisor { Meta = meta, CompMode = CompMode.Auto };
        var advice = advisor.Enrich(InGame(["QUILBOAR", "ELEMENTAL", "MURLOC", "DEMON", "BEAST"]), 7000, Cards());
        Assert.Equal(2, advice.Comps.Count);
        Assert.Single(advice.Comps, c => c.IsTarget);
        Assert.All(advice.Comps, c => Assert.Equal(0, c.AveragePlacement));
        Assert.All(advice.Comps, c => Assert.Null(c.HeroPlacement));
    }

    [Fact]
    public void A_comp_without_a_tribe_in_its_title_takes_the_tribe_most_of_its_core_shares()
    {
        var cards = Cards();

        Assert.Equal(["UNDEAD"], BattlegroundsMeta.DominantTribes("Нежить - APM-нежить", ["BG33_825", "BG33_823"], cards));
        Assert.Equal(["UNDEAD"], BattlegroundsMeta.DominantTribes("Без названия", ["BG25_010", "BG36_515", "BG33_825"], cards));
        Assert.Equal(["PIRATE", "UNDEAD"], BattlegroundsMeta.DominantTribes("Без названия", ["BG25_010", "BG36_515", "BG33_825", "BG33_823"], cards));
        // One card of a tribe, tribeless cards, cards the database does not know: no family.
        Assert.Empty(BattlegroundsMeta.DominantTribes("Зверинец", ["BG25_010", "BG33_825", "BG28_633"], cards));
        Assert.Empty(BattlegroundsMeta.DominantTribes("Без названия", ["BG35_883", "BG_LOE_077", "NOT_A_CARD"], cards));
    }

    // ---- lobby adjustment ----------------------------------------------------------------------

    [Fact]
    public async Task Changing_the_lobby_tribes_changes_the_hero_adjustment()
    {
        var meta = await ExternalMetaAsync();
        var advisor = new BattlegroundsAdvisor { Meta = meta };
        var cards = Cards();
        string[] withDemons = ["DEMON", "MURLOC", "MECHANICAL", "BEAST", "QUILBOAR"];
        string[] withoutDemons = ["DRAGON", "MURLOC", "MECHANICAL", "BEAST", "QUILBOAR"];

        var first = advisor.Enrich(HeroPick(withDemons), 7000, cards).HeroOptions.Single(h => h.CardId == Jaraxxus).Meta!;
        var second = advisor.Enrich(HeroPick(withoutDemons), 7000, cards).HeroOptions.Single(h => h.CardId == Jaraxxus).Meta!;

        // Worked out here from the fixture's rows, apart from the code: (place in the lobby's comps - place in all) * n / (n + 150).
        Assert.Equal(3.702, first.AveragePosition, 3);
        Assert.Equal(first.AveragePosition + Expected(withDemons), first.AdjustedPosition, 9);
        Assert.Equal(second.AveragePosition + Expected(withoutDemons), second.AdjustedPosition, 9);
        // Demons are 6371 of the hero's 6968 games at 3.911: with them the lobby is a little better, without them clearly worse.
        Assert.InRange(first.AdjustedPosition - first.AveragePosition, -0.05, -0.001);
        Assert.InRange(second.AdjustedPosition - second.AveragePosition, 0.25, 0.4);
        Assert.Equal(("S", "A"), (first.Tier, second.Tier));

        static double Expected(string[] lobby)
        {
            var allowed = JaraxxusComps.Where(c => lobby.Contains(c.Tribe)).ToList();
            double games = allowed.Sum(c => c.Games);
            var inLobby = allowed.Sum(c => c.Place * c.Games) / games;
            var overall = JaraxxusComps.Sum(c => c.Place * c.Games) / JaraxxusComps.Sum(c => c.Games);
            return (inLobby - overall) * games / (games + 150);
        }
    }

    [Fact]
    public void Changing_the_lobby_tribes_changes_which_hero_is_best()
    {
        // Two heroes a tenth of a place apart; each is strong in another tribe.
        var advisor = new BattlegroundsAdvisor { Meta = HandMeta() };
        var cards = Cards();

        var demons = advisor.Enrich(HeroPick(["DEMON", "MURLOC", "MECHANICAL", "QUILBOAR", "NAGA"], "HERO_A", "HERO_B"), null, cards);
        var beasts = advisor.Enrich(HeroPick(["BEAST", "MURLOC", "MECHANICAL", "QUILBOAR", "NAGA"], "HERO_A", "HERO_B"), null, cards);

        Assert.Equal("HERO_A", demons.HeroOptions.Single(h => h.Recommended).CardId);
        Assert.Equal("HERO_B", beasts.HeroOptions.Single(h => h.Recommended).CardId);
        // HERO_A: 1000 games at 3.5 with Demons, 1000 at 4.5 with Beasts, 4.0 over both; n / (n + 150) = 1000 / 1150.
        Assert.Equal(4.0 - (0.5 * 1000 / 1150), demons.HeroOptions[0].Meta!.AdjustedPosition, 9);
        Assert.Equal(4.0 + (0.5 * 1000 / 1150), beasts.HeroOptions[0].Meta!.AdjustedPosition, 9);
    }

    [Fact]
    public void The_lobby_adjustment_needs_a_sample_a_solo_game_and_known_tribes_and_the_pools_row_comes_first()
    {
        var cards = Cards();
        string[] lobby = ["DEMON", "MURLOC", "MECHANICAL", "QUILBOAR", "NAGA"];
        double Adjustment(BattlegroundsMeta meta, BattlegroundsView view)
        {
            var hero = new BattlegroundsAdvisor { Meta = meta }.Enrich(view, null, cards).HeroOptions[0].Meta!;
            return hero.AdjustedPosition - hero.AveragePosition;
        }

        Assert.NotEqual(0, Adjustment(HandMeta(), HeroPick(lobby, "HERO_A", "HERO_B")));

        // 99 games in the compositions the lobby allows: under the 100 the rule asks for.
        Assert.Equal(0, Adjustment(HandMeta(demonGames: 99), HeroPick(lobby, "HERO_A", "HERO_B")));
        // A single known composition has nothing to be compared with.
        Assert.Null(HandMeta(onlyDemons: true).LobbyDelta("HERO_A", lobby));
        // Every composition allowed: the lobby says nothing about this hero.
        Assert.Equal(0.0, HandMeta().LobbyDelta("HERO_A", ["DEMON", "BEAST", "MURLOC", "NAGA", "PIRATE"]));
        // A hero without compositions, tribes not read yet, Duos (the numbers are solo).
        Assert.Null(HandMeta().LobbyDelta("HERO_NONE", lobby));
        Assert.Equal(0, Adjustment(HandMeta(), HeroPick(["DEMON", "MURLOC"], "HERO_A", "HERO_B")));
        Assert.Equal(0, Adjustment(HandMeta(), HeroPick(lobby, "HERO_A", "HERO_B") with { IsDuos = true }));

        // The stats pool's own row for a tribe of this lobby (id 15 = Demon) is used instead, as it was.
        Assert.Equal(0.3, Adjustment(HandMeta(poolImpact: 0.3), HeroPick(lobby, "HERO_A", "HERO_B")), 9);
    }

    // ---- trinkets ------------------------------------------------------------------------------

    [Fact]
    public async Task An_offered_trinket_gets_its_numbers_and_the_best_one_is_recommended()
    {
        var meta = await ExternalMetaAsync();
        var advisor = new BattlegroundsAdvisor { Meta = meta };
        var view = InGame(["QUILBOAR", "ELEMENTAL", "MURLOC", "DEMON", "BEAST"]) with
        {
            TrinketOptions =
            [
                new BgTrinketOption("BG30_MagicItem_403", "Ironforge Anvil"), new BgTrinketOption("BG36_MagicItem_216", "Eternal Portrait"),
                new BgTrinketOption("BG35_MagicItem_754", "Thin sample"), new BgTrinketOption("NOT_IN_THE_LIST", "Unknown"),
            ],
        };

        var offer = advisor.Enrich(view, 7000, Cards()).TrinketOptions;

        // Top 50% (the advisor's bracket without MMR floors), as the list has them.
        var best = offer.Single(t => t.Recommended);
        Assert.Equal("BG36_MagicItem_216", best.CardId);
        Assert.Equal((3.47, 0.277, 2528, "S"), (best.Meta!.AveragePlacement, Math.Round(best.Meta.PickRate, 3), best.Meta.DataPoints, best.Meta.Tier));
        // Hearthpulse data, dated, and its sample is a lower bound.
        Assert.True(best.Meta.External);
        Assert.True(best.Meta.GamesAreMinimum);
        Assert.Equal(new DateTime(2026, 11, 9, 17, 1, 21, DateTimeKind.Utc), best.Meta.Updated!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(3.91, offer[0].Meta!.AveragePlacement);

        // Listed only in the top 1% with at least 10 games: under the 50 a trinket needs. Not in any list: nothing invented.
        Assert.Null(offer[2].Meta);
        Assert.Null(offer[3].Meta);

        // Duos keeps to what it had: the lists are solo.
        Assert.All(advisor.Enrich(view with { IsDuos = true }, 7000, Cards()).TrinketOptions, t => Assert.Null(t.Meta));
    }

    [Fact]
    public async Task A_trinket_listed_once_per_tribe_is_one_row_with_the_smallest_sample()
    {
        var meta = await ExternalMetaAsync();

        var compass = meta.Trinkets["BG30_MagicItem_426t"];
        // Top 50%: one row (Elemental). Top 1%: Quilboar 127 games at 3.65 and Mech 30 at 3.73, no row without a tribe.
        Assert.Equal((3989, 4.25), (compass.DataPoints, compass.AveragePlacement));
        Assert.Equal(30, compass.PlacementAtMmr[1].DataPoints);
        Assert.Equal(((127 * 3.65) + (30 * 3.73)) / 157, compass.PlacementAtMmr[1].Placement, 9);
        // A row with no games behind it (BG30_MagicItem_545: 0) is not a number.
        Assert.DoesNotContain("BG30_MagicItem_545", meta.Trinkets.Keys);
    }

    // ---- the pool and its daily limit ----------------------------------------------------------

    [Fact]
    public async Task The_daily_limit_closes_the_pools_rows_and_never_the_external_ones()
    {
        var external = await ExternalMetaAsync();
        var gate = await SpentGateAsync();
        var cards = Cards();
        var view = HeroPick(["QUILBOAR", "ELEMENTAL", "MURLOC", "DEMON", "BEAST"]) with
        {
            TrinketOptions = [new BgTrinketOption("BG36_MagicItem_216", "Eternal Portrait"), new BgTrinketOption("TRK_POOL", "Pool only")],
        };

        // External data alone: the spent limit is not even asked, and there is no note about one.
        var free = new BattlegroundsAdvisor { Meta = external, Usage = gate, Game = "game-4" }.Enrich(view, 7000, cards);
        Assert.Equal(2, free.HeroOptions[0].TopComps!.Count);
        Assert.True(free.TrinketOptions[0].Recommended);
        Assert.Null(free.UsageNote);

        // The pool adds a comp and two trinkets of its own, one of them the same card as an external row.
        var merged = PoolClient.Merge(external, PoolMeta());
        Assert.True(merged.FromPool);
        Assert.Contains(BattlegroundsMeta.PoolName, merged.StoredSource, StringComparison.Ordinal);

        var closed = new BattlegroundsAdvisor { Meta = merged, Usage = gate, Game = "game-4" }.Enrich(view, 7000, cards);
        // The pool's comp is hidden, the external ones stay.
        Assert.Equal([Quilboars, Elementals], closed.HeroOptions[0].TopComps!.Select(c => c.Name));
        // The external row answers for the shared card; the pool-only trinket has no number today.
        Assert.Equal((3.47, true), (closed.TrinketOptions[0].Meta!.AveragePlacement, closed.TrinketOptions[0].Meta!.External));
        Assert.Null(closed.TrinketOptions[1].Meta);
        Assert.False(string.IsNullOrEmpty(closed.UsageNote));

        // Without a limit the pool's own rows are in front, and they are not called Hearthpulse data.
        var open = new BattlegroundsAdvisor { Meta = merged }.Enrich(view, 7000, cards);
        Assert.Contains("Pool comp", open.HeroOptions[0].TopComps!.Select(c => c.Name));
        Assert.Equal((3.0, false, false), (open.TrinketOptions[0].Meta!.AveragePlacement, open.TrinketOptions[0].Meta!.External, open.TrinketOptions[0].Meta!.GamesAreMinimum));
        Assert.Equal(3.2, open.TrinketOptions[1].Meta!.AveragePlacement);
        Assert.Equal(200, open.TrinketOptions[0].Meta!.DataPoints);
    }

    [Fact]
    public async Task An_empty_pool_changes_nothing_and_is_not_named_as_a_source()
    {
        var external = await ExternalMetaAsync();
        var empty = new BattlegroundsMeta(new Dictionary<int, IReadOnlyDictionary<string, MetaHero>>(), [], [], new Dictionary<string, MetaTrinket>(),
            new Dictionary<int, IReadOnlyDictionary<string, MetaCard>>(), DateTime.UtcNow) { Source = BattlegroundsMeta.PoolAttribution };

        var merged = PoolClient.Merge(external, empty);

        Assert.Same(external, merged);
        Assert.Equal("Hearthpulse data", merged.Source);
        Assert.False(merged.FromPool);
    }

    [Fact]
    public async Task A_thin_pool_trinket_does_not_stand_in_front_of_the_external_row()
    {
        var external = await ExternalMetaAsync();
        var pool = PoolMeta(sharedTrinketGames: 49);

        var merged = PoolClient.Merge(external, pool);

        Assert.True(merged.Trinkets["BG36_MagicItem_216"].External);
        Assert.Equal(3.47, merged.TrinketFor("BG36_MagicItem_216", pool: true, external: true)!.AveragePlacement);
    }

    // ---- loading -------------------------------------------------------------------------------

    [Fact]
    public async Task The_community_load_lays_the_external_lists_over_the_api_meta()
    {
        var server = ApiServer.Full();
        using var http = new HttpClient(server);

        var data = new HearthpulseData(new HearthpulseApi(http, HearthpulseApi.DefaultBaseUrl), _dir);

        var meta = await BattlegroundsMeta.LoadCommunityAsync(data, http, new AppSettings { PoolEndpoint = "off" }, () => Task.FromResult<BgExternalLists?>(Lists()), Cards());

        Assert.NotNull(meta);
        Assert.Equal(3, meta.Comps.Count);
        Assert.Contains("BG36_MagicItem_216", meta.Trinkets.Keys);
        Assert.Equal("Hearthpulse data", meta.Source);
        // Every hero's compositions come with the API step, in one request; no hero is asked for by itself.
        Assert.Equal(4, meta.HeroComps.Count);
        Assert.Single(server.Requests, r => r.Contains("heroes/compositions", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, r => r.Contains("api/bg/heroes/", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, r => r.Contains("zerotoheroes", StringComparison.OrdinalIgnoreCase) || r.Contains("firestone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hero_compositions_parse_from_the_real_answer()
    {
        using var doc = JsonDocument.Parse(MetaFixture("hero-compositions-top_50_percent.json"));

        var slice = HearthpulseData.ParseHeroCompositions(doc.RootElement);

        Assert.Equal(4, slice.Heroes.Count);
        Assert.Equal(new DateTime(2026, 11, 12, 3, 35, 3, DateTimeKind.Utc), slice.FetchedAt!.Value, TimeSpan.FromSeconds(1));
        // The answer names the tribes itself; it has none for "Aberrations", which the family's id is known by.
        Assert.Equal(JaraxxusComps.OrderBy(c => c.Tribe, StringComparer.Ordinal),
            slice.Heroes[Jaraxxus].Select(c => (c.Tribes.Single(), c.Games, c.Placement)).OrderBy(c => c.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public void Hero_compositions_of_another_slice_or_source_are_not_read_and_broken_rows_are_dropped()
    {
        static BgHeroCompsSlice Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return HearthpulseData.ParseHeroCompositions(doc.RootElement);
        }

        const string data = """
            "data": [
              {"id": "HERO_X", "compositions": [
                {"composition_id": 9, "name": "Демоны", "tribes": ["DEMON"], "num_games": 500, "avg_placement": 3.9},
                {"composition_id": 999, "name": "Наги", "tribes": [], "num_games": 120, "avg_placement": 4.4},
                {"composition_id": 998, "name": "Что-то новое", "num_games": 80, "avg_placement": 4.1},
                {"composition_id": 997, "name": "Два племени", "tribes": ["BEAST", "MURLOC", 5], "num_games": 70, "avg_placement": 4.2},
                {"composition_id": 3, "name": "Звери", "tribes": ["BEAST"], "num_games": 0, "avg_placement": 4.0},
                {"composition_id": 2, "name": "Мурлоки", "tribes": ["MURLOC"], "num_games": 50, "avg_placement": 9.5},
                {"composition_id": "x", "num_games": 50, "avg_placement": 4.0}, "junk"]},
              {"id": "HERO_EMPTY", "compositions": []}, {"compositions": []}, "junk"]
            """;

        var hero = Assert.Single(Parse($$"""{ {{data}}, "meta": {"source_id": "hsreplay_battlegrounds_hero_details", "mmr": "TOP_50_PERCENT"} }""").Heroes);
        Assert.Equal("HERO_X", hero.Key);
        // The answer's tribes; without them the family known by id or name; an unknown one keeps its numbers and has no tribe.
        Assert.Equal(["DEMON", "NAGA", "", "BEAST+MURLOC"], hero.Value.Select(c => string.Join('+', c.Tribes)));
        Assert.Equal([500, 120, 80, 70], hero.Value.Select(c => c.Games));

        Assert.Empty(Parse($$"""{ {{data}}, "meta": {"mmr": "TOP_20_PERCENT"} }""").Heroes);
        Assert.Empty(Parse($$"""{ {{data}}, "meta": {"source_id": "firestone_battlegrounds_comps"} }""").Heroes);
        Assert.Empty(Parse($$"""{ {{data}}, "meta": {"source_id": "hsreplay", "tribes_source_id": "zerotoheroes"} }""").Heroes);
        Assert.Empty(Parse("""{"data": null}""").Heroes);
        Assert.Empty(Parse("[]").Heroes);
        Assert.Null(Parse("[]").FetchedAt);

        Assert.Empty(BattlegroundsMeta.CompositionTribes(998, "Что-то новое"));
        Assert.Equal(["MECHANICAL"], BattlegroundsMeta.CompositionTribes(8, "whatever"));
    }

    [Fact]
    public async Task Hero_compositions_are_one_request_kept_small_on_disk_and_a_missing_slice_is_remembered()
    {
        var server = ApiServer.Full();
        var data = new HearthpulseData(new HearthpulseApi(new HttpClient(server), HearthpulseApi.DefaultBaseUrl), _dir);

        var first = await BattlegroundsMeta.LoadAsync(data);
        var requests = server.Requests.Count;
        var second = await BattlegroundsMeta.LoadAsync(data);

        Assert.Equal(10, first!.HeroComps[Jaraxxus].Count);
        Assert.Equal(first.HeroComps[Millificent].Select(c => (c.Id, c.Games, c.Placement)), second!.HeroComps[Millificent].Select(c => (c.Id, c.Games, c.Placement)));
        // One request for all heroes, cached for hours like the other answers; none per hero.
        Assert.Equal(requests, server.Requests.Count);
        Assert.Single(server.Requests, r => r.EndsWith("v1/battlegrounds/heroes/compositions?mmr=TOP_50_PERCENT", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, r => r.Contains("api/bg/heroes/", StringComparison.Ordinal));

        // Only what is read is kept on disk: no placement distributions.
        var cached = File.ReadAllText(Path.Combine(_dir, "bg-hero-comps-top_50_percent.json"));
        Assert.DoesNotContain("placement_distribution", cached, StringComparison.Ordinal);
        Assert.Contains("\"fetched_at\"", cached, StringComparison.Ordinal);

        // A server without the slice (422, or 404 before the route existed): remembered, and the meta has no compositions.
        var bare = ApiServer.Full();
        bare.Remove("heroes/compositions");
        var other = Path.Combine(_dir, "bare");
        var without = await BattlegroundsMeta.LoadAsync(new HearthpulseData(new HearthpulseApi(new HttpClient(bare), HearthpulseApi.DefaultBaseUrl), other));
        Assert.Empty(without!.HeroComps);
        Assert.Equal("{\"data\":null}", File.ReadAllText(Path.Combine(other, "bg-hero-comps-top_50_percent.json")));
        Assert.Equal(without.Hero(Jaraxxus, 50)!.AveragePosition, new BattlegroundsAdvisor { Meta = without }
            .Enrich(HeroPick(["DRAGON", "MURLOC", "MECHANICAL", "BEAST", "QUILBOAR"]), 7000, Cards()).HeroOptions[0].Meta!.AdjustedPosition);
    }

    // ---- data ----------------------------------------------------------------------------------

    /// <summary>The API meta with the heroes' compositions (fixtures) and the external lists (fixtures); no pool.</summary>
    private async Task<BattlegroundsMeta> ExternalMetaAsync()
    {
        var data = new HearthpulseData(new HearthpulseApi(new HttpClient(ApiServer.Full()), HearthpulseApi.DefaultBaseUrl), Path.Combine(_dir, "meta"));
        var meta = await BattlegroundsMeta.LoadAsync(data);
        return meta!.WithExternal(Lists(), Cards());
    }

    /// <summary>The comps and the trinket lists of two brackets as the site client parses them, Firestone rows refused.</summary>
    private static BgExternalLists Lists()
    {
        using var comps = JsonDocument.Parse(SiteFixture("comps.json"));
        using var top50 = JsonDocument.Parse(SiteFixture("trinkets-top50.json"));
        using var all = JsonDocument.Parse(SiteFixture("tier-lists.json"));
        return new BgExternalLists(BgSiteParser.ParseComps(comps.RootElement).Comps, new Dictionary<int, BgSiteTierList>
        {
            [50] = BgSiteParser.ParseList(top50.RootElement, BgSiteList.Trinkets, heroes: null, "trinkets")!,
            [1] = BgSiteParser.ParseTierList(all.RootElement, BgSiteList.Trinkets, heroes: null)!,
        });
    }

    /// <summary>
    /// Two heroes in the top 50% a tenth of a place apart, with compositions by hand. HERO_A: Demons 3.5, Beasts 4.5;
    /// HERO_B: Demons 4.2, Beasts 4.0; 1000 games each unless said.
    /// </summary>
    private static BattlegroundsMeta HandMeta(int demonGames = 1000, bool onlyDemons = false, double? poolImpact = null)
    {
        var impact = poolImpact is { } value ? new Dictionary<int, double> { [15] = value } : [];
        var heroes = new Dictionary<string, MetaHero>(StringComparer.Ordinal)
        {
            ["HERO_A"] = new("HERO_A", 0, 4.0, 0.5, impact),
            ["HERO_B"] = new("HERO_B", 0, 4.1, 0.5, new Dictionary<int, double>()),
        };
        IReadOnlyList<MetaHeroComp> Comps(double demons, double beasts) => onlyDemons
            ? [new MetaHeroComp(9, "Demons", ["DEMON"], demonGames, demons)]
            : [new MetaHeroComp(9, "Demons", ["DEMON"], demonGames, demons), new MetaHeroComp(3, "Beasts", ["BEAST"], 1000, beasts)];
        return new BattlegroundsMeta(new Dictionary<int, IReadOnlyDictionary<string, MetaHero>> { [50] = heroes }, [], [],
            new Dictionary<string, MetaTrinket>(), new Dictionary<int, IReadOnlyDictionary<string, MetaCard>>(), new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc))
        {
            HeroComps = new Dictionary<string, IReadOnlyList<MetaHeroComp>>(StringComparer.Ordinal) { ["HERO_A"] = Comps(3.5, 4.5), ["HERO_B"] = Comps(4.2, 4.0) },
        };
    }

    /// <summary>The stats pool's own rows: a comp, a trinket the external list has too and one it has not.</summary>
    private static BattlegroundsMeta PoolMeta(int sharedTrinketGames = 200)
    {
        var comp = new MetaComp("pool_comp", "Pool comp", "S", "Easy", ["DEMON"], [new MetaCompCard("BG28_633", "CORE")], null, null, 3.2,
            new Dictionary<int, double>(), new Dictionary<string, (int, double)>());
        var trinkets = new Dictionary<string, MetaTrinket>(StringComparer.Ordinal)
        {
            ["BG36_MagicItem_216"] = new("BG36_MagicItem_216", sharedTrinketGames, 3.0, 0.4, new Dictionary<int, (double, int)>()),
            ["TRK_POOL"] = new("TRK_POOL", 300, 3.2, 0.3, new Dictionary<int, (double, int)>()),
        };
        return new BattlegroundsMeta(new Dictionary<int, IReadOnlyDictionary<string, MetaHero>>(), [], [comp], trinkets,
            new Dictionary<int, IReadOnlyDictionary<string, MetaCard>>(), new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc))
        {
            Source = BattlegroundsMeta.PoolAttribution,
            SharedGames = 500,
        };
    }

    /// <summary>A gate whose three free games of the day are used up (signed out: counted on this machine).</summary>
    private async Task<UsageGate> SpentGateAsync()
    {
        var gate = new UsageGate(new AccountClient(new HttpClient(new LimitsSite()), LimitsSite.Base), account: null, Path.Combine(_dir, "usage.json"))
        {
            UtcNow = () => new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
        };
        await gate.RefreshLimitsAsync();
        for (var game = 1; game <= 3; game++)
        {
            Assert.True(gate.Allow(UsageGate.BgPoolStats, $"game-{game}"));
            gate.GameFinished();
        }

        Assert.False(gate.Allow(UsageGate.BgPoolStats, "game-4"));
        return gate;
    }

    private static BattlegroundsView HeroPick(string[] races, string first = Jaraxxus, string second = Millificent) => InGame(races) with
    {
        Turn = 0,
        HeroCardId = null,
        IsHeroSelection = true,
        HeroOptions = [new BgHeroOption(first, first, 0, null), new BgHeroOption(second, second, 0, null)],
        Board = [],
        Shop = [],
    };

    private static BattlegroundsView InGame(string[] races) => new(
        Turn: 5,
        TechLevel: 3,
        MaxTechLevel: 6,
        HeroCardId: Jaraxxus,
        Health: 30,
        Armor: 0,
        Place: 3,
        IsDuos: false,
        InCombat: false,
        IsHeroSelection: false,
        HeroOptions: [],
        Races: races,
        RacesConfirmed: true,
        Opponents: [],
        Combats: [],
        TierHistory: [],
        Board: [],
        AnomalyCardId: null,
        Triples: 0,
        Shop: [],
        Hand: []);

    /// <summary>The heroes and the core cards of the fixture's three comps, with their tribes as the game has them.</summary>
    private static CardDb Cards()
    {
        const string json = """
            [
              { "id": "TB_BaconShop_HERO_37", "dbfId": 59807, "name": "Lord Jaraxxus", "type": "HERO", "battlegroundsHero": true },
              { "id": "TB_BaconShop_HERO_49", "dbfId": 60366, "name": "Millificent Manastorm", "type": "HERO", "battlegroundsHero": true },
              { "id": "HERO_A", "dbfId": 11, "name": "Hero A", "type": "HERO", "battlegroundsHero": true },
              { "id": "HERO_B", "dbfId": 12, "name": "Hero B", "type": "HERO", "battlegroundsHero": true },
              { "id": "BG28_633", "dbfId": 101, "name": "Felboar", "type": "MINION", "races": ["QUILBOAR"], "techLevel": 3, "isBattlegroundsPoolMinion": true },
              { "id": "BG35_883", "dbfId": 102, "name": "Balinda Stonehearth", "type": "MINION", "techLevel": 6, "isBattlegroundsPoolMinion": true },
              { "id": "BG_LOE_077", "dbfId": 103, "name": "Brann Bronzebeard", "type": "MINION", "techLevel": 5, "isBattlegroundsPoolMinion": true },
              { "id": "BG35_881", "dbfId": 104, "name": "Leyline Surfacer", "type": "MINION", "races": ["ELEMENTAL"], "techLevel": 4, "isBattlegroundsPoolMinion": true },
              { "id": "BG28_707", "dbfId": 105, "name": "Living Azerite", "type": "MINION", "races": ["ELEMENTAL"], "techLevel": 5, "isBattlegroundsPoolMinion": true },
              { "id": "BG36_701", "dbfId": 106, "name": "Kelp Keeper", "type": "MINION", "races": ["MURLOC"], "techLevel": 2, "isBattlegroundsPoolMinion": true },
              { "id": "BG25_010", "dbfId": 107, "name": "Handless Forsaken", "type": "MINION", "races": ["UNDEAD"], "techLevel": 2, "isBattlegroundsPoolMinion": true },
              { "id": "BG36_515", "dbfId": 108, "name": "Snazzy Phantom", "type": "MINION", "races": ["UNDEAD"], "techLevel": 3, "isBattlegroundsPoolMinion": true },
              { "id": "BG34_692", "dbfId": 109, "name": "Forsaken Weaver", "type": "MINION", "races": ["UNDEAD"], "techLevel": 4, "isBattlegroundsPoolMinion": true },
              { "id": "BG33_825", "dbfId": 110, "name": "Proud Privateer", "type": "MINION", "races": ["PIRATE"], "techLevel": 3, "isBattlegroundsPoolMinion": true },
              { "id": "BG33_823", "dbfId": 111, "name": "Sky Admiral Rogers", "type": "MINION", "races": ["PIRATE"], "techLevel": 5, "isBattlegroundsPoolMinion": true }
            ]
            """;
        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static string MetaFixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BgMeta", name));

    private static string SiteFixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BgSite", name));

    /// <summary>
    /// The Hearthpulse API in miniature: the hero and minion slices and the heroes' compositions from Fixtures/BgMeta,
    /// 404 for the rest.
    /// </summary>
    private sealed class ApiServer : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body)> _routes = [];

        public List<string> Requests { get; } = [];

        public static ApiServer Full()
        {
            var server = new ApiServer();
            foreach (var mmr in new[] { "TOP_50_PERCENT", "TOP_20_PERCENT", "TOP_5_PERCENT", "TOP_1_PERCENT" })
            {
                server._routes.Add(($"api/bg/heroes?mode=solo&mmr={mmr}", MetaFixture($"heroes-{mmr.ToLowerInvariant()}.json")));
            }

            foreach (var mmr in new[] { "ALL", "TOP_50_PERCENT", "TOP_20_PERCENT" })
            {
                server._routes.Add(($"minions/stats?mmr={mmr}", MetaFixture($"minions-{mmr.ToLowerInvariant()}.json")));
            }

            server._routes.Add(("heroes/compositions?mmr=TOP_50_PERCENT", MetaFixture("hero-compositions-top_50_percent.json")));
            return server;
        }

        public void Remove(string fragment) => _routes.RemoveAll(r => r.Fragment.Contains(fragment, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(url);
            }

            var route = _routes.FirstOrDefault(r => url.Contains(r.Fragment, StringComparison.Ordinal));
            return Task.FromResult(route.Body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\": \"Not Found\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>The site's usage limits alone: three free games a day, on.</summary>
    private sealed class LimitsSite : HttpMessageHandler
    {
        public const string Base = "https://site.test/api/v1/";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("usage/limits", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"enabled":true,"day":"2026-11-02","resetsAt":"2026-11-03T00:00:00.000Z","features":{"bg.poolStats":{"section":"battlegrounds","freePerDay":3}}}""", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

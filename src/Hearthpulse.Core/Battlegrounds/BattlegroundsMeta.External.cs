using Hearthpulse.Core.Battlegrounds.Site;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// One of a hero's compositions as the source groups final boards (a tribe family: "Mechs", "Demons"), with the
/// hero's games and average place in it (top 50%, current patch).
/// </summary>
/// <param name="Tribes">The family's tribe as a race key; empty when the family is not one the app knows.</param>
public sealed record MetaHeroComp(int Id, string Name, IReadOnlyList<string> Tribes, int Games, double Placement);

/// <summary>
/// The external lists the in-game advisor reads besides the API meta (docs/bg-meta-sources.md): the comps of the
/// site's service and its trinket tier list per MMR bracket (50, 20, 5, 1). Both arrive already cut to the allowed
/// source by <see cref="BgSiteClient"/>.
/// </summary>
public sealed record BgExternalLists(IReadOnlyList<BgSiteComp> Comps, IReadOnlyDictionary<int, BgSiteTierList> TrinketsByBracket);

/// <summary>
/// What the advisor takes from the permitted external data when the stats pool has nothing: comps, trinkets and
/// the lobby adjustment of the hero pick. Definitions and small-sample rules: docs/bg-meta-sources.md, section 4.
/// </summary>
public sealed partial class BattlegroundsMeta
{
    /// <summary>Games the hero needs in the compositions the lobby allows before the lobby adjustment is used.</summary>
    public const int LobbyMinGames = 100;

    /// <summary>The prior of the lobby adjustment: it is shrunk toward zero by <c>n / (n + 150)</c>.</summary>
    public const int LobbyPrior = 150;

    /// <summary>
    /// The source's composition ids (HSReplay tribe families, checked 2026-11-15) as race keys: for a family the API
    /// sends without tribes (it does for "Aberrations"), and for the families the external comps' titles lead with.
    /// </summary>
    private static readonly Dictionary<int, string> CompositionRaces = new()
    {
        [2] = "MURLOC", [3] = "BEAST", [8] = "MECHANICAL", [9] = "DEMON", [20] = "DRAGON",
        [36] = "PIRATE", [39] = "ELEMENTAL", [41] = "QUILBOAR", [47] = "UNDEAD", [56] = "ABERRATION",
    };

    /// <summary>The families' names as the API sends them (Russian, some English), for an id the table above lacks.</summary>
    private static readonly Dictionary<string, string> CompositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Мурлоки"] = "MURLOC", ["Murlocs"] = "MURLOC", ["Звери"] = "BEAST", ["Beasts"] = "BEAST",
        ["Механизмы"] = "MECHANICAL", ["Mechs"] = "MECHANICAL", ["Демоны"] = "DEMON", ["Demons"] = "DEMON",
        ["Драконы"] = "DRAGON", ["Dragons"] = "DRAGON", ["Пираты"] = "PIRATE", ["Pirates"] = "PIRATE",
        ["Элементали"] = "ELEMENTAL", ["Elementals"] = "ELEMENTAL", ["Свинобраз"] = "QUILBOAR", ["Свинобразы"] = "QUILBOAR",
        ["Quilboar"] = "QUILBOAR", ["Quilboars"] = "QUILBOAR", ["Нежить"] = "UNDEAD", ["Undead"] = "UNDEAD",
        ["Наги"] = "NAGA", ["Naga"] = "NAGA", ["Nagas"] = "NAGA", ["Аберрации"] = "ABERRATION", ["Aberrations"] = "ABERRATION",
    };

    /// <summary>
    /// Each hero's compositions (top 50%, current patch), by hero card id: the lobby adjustment of the hero pick and
    /// the order of a hero's comps. Empty when the API has no such slice or the pool's meta stands alone.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MetaHeroComp>> HeroComps { get; init; } =
        new Dictionary<string, IReadOnlyList<MetaHeroComp>>(StringComparer.Ordinal);

    /// <summary>
    /// External trinket rows that a row of the stats pool stands in front of in <see cref="Trinkets"/>: they answer when
    /// the pool's row may not be used (the daily limit).
    /// </summary>
    internal IReadOnlyDictionary<string, MetaTrinket> ShadowedTrinkets { get; init; } = new Dictionary<string, MetaTrinket>(StringComparer.Ordinal);

    /// <summary>The tribe of a composition family as a one-item list; empty when neither its id nor its name is known.</summary>
    internal static IReadOnlyList<string> CompositionTribes(int id, string name) =>
        CompositionRaces.TryGetValue(id, out var race) || CompositionNames.TryGetValue(name.Trim(), out race) ? [race] : [];

    /// <summary>
    /// The trinket row the advisor may use: the pool's own when <paramref name="pool"/> allows it, else the external one
    /// when <paramref name="external"/> does. A row under <see cref="MinTrinketGames"/> is not used. Null without one.
    /// </summary>
    public MetaTrinket? TrinketFor(string cardId, bool pool, bool external)
    {
        foreach (var row in new[] { Trinkets.GetValueOrDefault(cardId), ShadowedTrinkets.GetValueOrDefault(cardId) })
        {
            if (row is not null && (row.External ? external : pool) && row.DataPoints >= MinTrinketGames)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// The hero's composition of exactly these tribes (a single-tribe family), when it has at least
    /// <paramref name="minGames"/> games; null otherwise.
    /// </summary>
    public MetaHeroComp? HeroFamily(string baseHeroId, IReadOnlyList<string> tribes, int minGames) =>
        tribes.Count == 1 && HeroComps.TryGetValue(baseHeroId, out var comps)
            ? comps.FirstOrDefault(c => c.Tribes.Count == 1 && c.Tribes[0] == tribes[0] && c.Games >= minGames)
            : null;

    /// <summary>
    /// How the hero's average place moves in a lobby of these tribes (docs/bg-meta-sources.md, section 4): the
    /// games-weighted average place over the hero's compositions the lobby allows, minus the same over all of them,
    /// shrunk by <c>n / (n + <see cref="LobbyPrior"/>)</c> where n is the games in the allowed ones. Negative = better.
    /// Compositions whose tribe is unknown leave both averages. Null (no adjustment) when the hero has fewer than two
    /// known compositions or under <see cref="LobbyMinGames"/> games in the allowed ones.
    /// </summary>
    public double? LobbyDelta(string baseHeroId, IReadOnlyCollection<string> lobbyRaces)
    {
        if (!HeroComps.TryGetValue(baseHeroId, out var all))
        {
            return null;
        }

        var known = all.Where(c => c.Tribes.Count > 0 && c.Games > 0).ToList();
        var allowed = known.Where(c => c.Tribes.All(lobbyRaces.Contains)).ToList();
        long games = known.Sum(c => (long)c.Games);
        long allowedGames = allowed.Sum(c => (long)c.Games);
        if (known.Count < 2 || allowedGames < LobbyMinGames)
        {
            return null;
        }

        var overall = known.Sum(c => c.Placement * c.Games) / games;
        var inLobby = allowed.Sum(c => c.Placement * c.Games) / allowedGames;
        return (inLobby - overall) * allowedGames / (allowedGames + LobbyPrior);
    }

    /// <summary>
    /// The external lists for <see cref="WithExternal"/>: the comps of the refresh and the trinket tier list of each
    /// bracket (cached per slice by the client). Null when neither could be had. Never throws.
    /// </summary>
    public static async Task<BgExternalLists?> LoadExternalAsync(BgSiteClient site, Task<BgSiteStats?> refresh, CancellationToken cancellationToken = default)
    {
        var lists = HeroSlices.Select(s => site.LoadTrinketsAsync(s.Mmr, cancellationToken)).ToList();
        var stats = await refresh.ConfigureAwait(false);
        var trinkets = new Dictionary<int, BgSiteTierList>();
        for (var i = 0; i < lists.Count; i++)
        {
            if (await lists[i].ConfigureAwait(false) is { Entries.Count: > 0 } list)
            {
                trinkets[HeroSlices[i].Bracket] = list;
            }
        }

        var comps = stats?.Comps ?? [];
        return comps.Count == 0 && trinkets.Count == 0 ? null : new BgExternalLists(comps, trinkets);
    }

    /// <summary>
    /// This meta with the external comps and trinkets: comps become <see cref="MetaComp"/>s without a placement (the
    /// source publishes none), trinkets <see cref="MetaTrinket"/>s whose sample is a lower bound. Rows this meta already
    /// has stay in front. Only lists of an allowed source are read. The source line does not change: these are
    /// "Hearthpulse data" like the API's heroes.
    /// </summary>
    public BattlegroundsMeta WithExternal(BgExternalLists lists, CardDb cards)
    {
        var comps = lists.Comps
            .Where(c => Comps.All(own => own.Id != c.Id))
            .Select(c => new MetaComp(c.Id, c.NameRu, c.Tier ?? string.Empty, c.Difficulty ?? string.Empty, DominantTribes(c.NameRu, c.CoreCardIds, cards),
                c.CoreCardIds.Select(id => new MetaCompCard(id, "CORE")).ToList(), null, c.Tip, 0,
                new Dictionary<int, double>(), new Dictionary<string, (int, double)>(StringComparer.Ordinal))
            {
                External = true,
            })
            .ToList();

        var trinkets = ExternalTrinkets(lists.TrinketsByBracket);
        var shadowed = new Dictionary<string, MetaTrinket>(ShadowedTrinkets, StringComparer.Ordinal);
        foreach (var (id, own) in Trinkets)
        {
            if (trinkets.TryGetValue(id, out var external))
            {
                shadowed[id] = external;
            }

            trinkets[id] = own;
        }

        Diag.Info($"Battlegrounds meta: {comps.Count} external comps, {trinkets.Values.Count(t => t.External) + shadowed.Count} external trinkets in brackets {string.Join('/', lists.TrinketsByBracket.Keys.OrderDescending())}");
        return Copy([.. Comps, .. comps], trinkets, shadowed);
    }

    /// <summary>A copy with other comps and trinkets; everything else as it is.</summary>
    private BattlegroundsMeta Copy(IReadOnlyList<MetaComp> comps, IReadOnlyDictionary<string, MetaTrinket> trinkets,
        IReadOnlyDictionary<string, MetaTrinket> shadowed) =>
        new(HeroesByBracket, Percentiles, comps, trinkets, CardsByBracket, Updated)
        {
            Tribes = Tribes,
            SharedGames = SharedGames,
            SharedHeroRows = SharedHeroRows,
            Source = _source,
            ShadowedTrinkets = shadowed,
            HeroComps = HeroComps,
        };

    /// <summary>
    /// The tribe family of an external comp (the source has no field for it). Its title leads with the family
    /// ("Нежить - APM-нежить"): a known name there decides. Without one, the tribe most core cards belong to, when at
    /// least two do (two tribes on a tie); a comp of tribeless cards or of single cards of many tribes has none. This
    /// orders and groups comps; whether a lobby can play one is decided card by card (<see cref="CoreInLobby"/>).
    /// </summary>
    internal static IReadOnlyList<string> DominantTribes(string title, IReadOnlyList<string> coreCardIds, CardDb cards)
    {
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && CompositionNames.TryGetValue(title[..dash].Trim(), out var family))
        {
            return [family];
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var race in coreCardIds.SelectMany(id => cards.Get(id)?.Races ?? []).Where(r => r != "ALL"))
        {
            counts[race] = counts.GetValueOrDefault(race) + 1;
        }

        var most = counts.Count == 0 ? 0 : counts.Values.Max();
        var top = counts.Where(kv => kv.Value == most).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
        return most >= 2 && top.Count <= 2 ? top : [];
    }

    /// <summary>
    /// True when every core card of the comp can appear in a lobby of these tribes: a minion of tribes that are all
    /// out of the lobby cannot, and a comp does not work without its core. Tribeless cards, cards of every tribe and
    /// cards the database does not know do not decide.
    /// </summary>
    internal static bool CoreInLobby(MetaComp comp, CardDb cards, IReadOnlyCollection<string> lobbyRaces) =>
        comp.Cards.Where(c => c.Status == "CORE").All(c =>
            cards.Get(c.CardId)?.Races is not { Count: > 0 } races || races.Contains("ALL") || races.Any(lobbyRaces.Contains));

    /// <summary>
    /// Trinket rows from the tier lists of each bracket. The overall numbers are those of the widest bracket that has
    /// the trinket. A trinket the service lists once per tribe (one card id, several rows) uses its row without a
    /// tribe; when every row has one, the rows' average weighted by their samples, with the smallest of them as the
    /// sample: the offer does not say which tribe it is for.
    /// </summary>
    private static Dictionary<string, MetaTrinket> ExternalTrinkets(IReadOnlyDictionary<int, BgSiteTierList> byBracket)
    {
        var result = new Dictionary<string, MetaTrinket>(StringComparer.Ordinal);
        var atMmr = new Dictionary<string, Dictionary<int, (double, int)>>(StringComparer.Ordinal);
        var sourceAtMmr = new Dictionary<string, Dictionary<int, (double, DateTime?)>>(StringComparer.Ordinal);
        foreach (var (bracket, list) in byBracket.OrderByDescending(kv => kv.Key))
        {
            if (list.Kind != BgSiteList.Trinkets || !BgSiteParser.Allowed(list.Source))
            {
                continue;
            }

            foreach (var rows in list.Entries.Where(e => e.AveragePlacement is not null && e.Games is > 0).GroupBy(e => e.CardId, StringComparer.Ordinal))
            {
                var plain = rows.FirstOrDefault(e => e.Race is null);
                var games = plain?.Games ?? rows.Min(e => e.Games!.Value);
                double weight = rows.Sum(e => (double)e.Games!.Value);
                var placement = plain?.AveragePlacement ?? rows.Sum(e => e.AveragePlacement!.Value * e.Games!.Value) / weight;
                var pickRate = plain is not null ? plain.PickRate ?? 0 : rows.Sum(e => (e.PickRate ?? 0) * e.Games!.Value) / weight;

                (atMmr.TryGetValue(rows.Key, out var at) ? at : atMmr[rows.Key] = [])[bracket] = (placement, games);
                (sourceAtMmr.TryGetValue(rows.Key, out var context) ? context : sourceAtMmr[rows.Key] = [])[bracket] =
                    (pickRate, list.FetchedAt == default ? null : list.FetchedAt);
                // Widest bracket first: the first row seen is the overall one.
                result.TryAdd(rows.Key, new MetaTrinket(rows.Key, games, placement, pickRate, atMmr[rows.Key])
                {
                    External = true,
                    GamesAreMinimum = true,
                    Updated = list.FetchedAt == default ? null : list.FetchedAt,
                    SourceAtMmr = sourceAtMmr[rows.Key],
                });
            }
        }

        return result;
    }
}

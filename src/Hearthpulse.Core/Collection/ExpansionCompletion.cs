using Hearthpulse.Core.Cards;
using Hearthpulse.Core.CardPool;

namespace Hearthpulse.Core.Collection;

/// <summary>The format group a set is filtered by.</summary>
public enum SetGroup
{
    /// <summary>In Standard (<c>CardPoolTables.StandardSets</c>).</summary>
    Standard,

    /// <summary>Wild only.</summary>
    Wild,

    /// <summary>The Classic format's set (VANILLA).</summary>
    Classic,
}

/// <summary>One rarity of a set: the same counts as <see cref="SetCompletion"/>.</summary>
public sealed record RarityCompletion(string Rarity, int UniqueTotal, int UniqueOwned, int PlayableTotal, int PlayableOwned, long DustToComplete);

/// <summary>
/// How full one set of the collection is (docs/collection-cosmetics.md, "Expansions").
/// </summary>
/// <param name="Set">The card data's set code ("" for a sum).</param>
/// <param name="UniqueTotal">Collectible cards of the set.</param>
/// <param name="UniqueOwned">Of them, cards with at least one copy of any finish.</param>
/// <param name="PlayableTotal">Copies a deck can hold: two of each card, one of a legendary.</param>
/// <param name="PlayableOwned">Owned copies up to that limit per card.</param>
/// <param name="GoldenOwned">Golden copies up to the limit per card; shown only once <see cref="CollectionFinish.GoldenVerified"/>.</param>
/// <param name="DustToComplete">Dust to craft every missing playable copy of the cards that can be crafted.</param>
/// <param name="DustEstimated">A missing card that can be crafted is also handed out by the game (<c>howToEarn</c>): it may not be craftable now.</param>
/// <param name="UncraftableMissing">Missing playable copies no dust buys (free cards, Core, events, Classic).</param>
public sealed record SetCompletion(
    string Set, SetGroup Group, int ReleaseOrder,
    int UniqueTotal, int UniqueOwned, int PlayableTotal, int PlayableOwned, int GoldenOwned,
    long DustToComplete, bool DustEstimated, int UncraftableMissing,
    IReadOnlyList<RarityCompletion> ByRarity);

/// <summary>
/// The completion of every set of the card data by a collection: pure, never throws on missing cards. A card counts only
/// under its own card id (a printing in another set fills no slot here, as the game's collection screen counts it);
/// dust follows <see cref="DeckCost.CraftCost"/>.
/// </summary>
public static class ExpansionCompletion
{
    // Sets one does not complete: hero skins have their own tab; CORE_HIDDEN holds old Core printings the game keeps out of sight.
    private static readonly HashSet<string> NotExpansions = new(StringComparer.Ordinal) { "HERO_SKINS", "CORE_HIDDEN" };

    private static readonly string[] Rarities = ["FREE", "COMMON", "RARE", "EPIC", "LEGENDARY"];

    /// <summary>Every set with collectible cards, newest first (<see cref="CardSets.ReleaseOrder"/>). <paramref name="standardSets"/> null: <c>CardPoolTables.StandardSets</c>.</summary>
    public static IReadOnlyList<SetCompletion> Build(CardCollection collection, CardDb cards, IReadOnlySet<string>? standardSets = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(cards);
        var standard = standardSets ?? CardPoolTables.StandardSets;
        return cards.All
            .Where(c => c.Collectible && c.Set.Length > 0 && !NotExpansions.Contains(c.Set))
            .GroupBy(c => c.Set, StringComparer.Ordinal)
            .Select(set => Of(set.Key, set, collection, standard))
            .OrderByDescending(s => s.ReleaseOrder)
            .ThenBy(s => s.Set, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The sum of <paramref name="sets"/> for the line above a filtered list: Set "", the first set's group, rarities summed.</summary>
    public static SetCompletion Sum(IEnumerable<SetCompletion> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var list = sets.ToList();
        var rarities = list.SelectMany(s => s.ByRarity)
            .GroupBy(r => r.Rarity, StringComparer.Ordinal)
            .OrderBy(g => RarityRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new RarityCompletion(g.Key, g.Sum(r => r.UniqueTotal), g.Sum(r => r.UniqueOwned), g.Sum(r => r.PlayableTotal),
                g.Sum(r => r.PlayableOwned), g.Sum(r => r.DustToComplete)))
            .ToList();
        return new SetCompletion(
            string.Empty, list.Count > 0 ? list[0].Group : SetGroup.Standard, 0,
            list.Sum(s => s.UniqueTotal), list.Sum(s => s.UniqueOwned), list.Sum(s => s.PlayableTotal), list.Sum(s => s.PlayableOwned),
            list.Sum(s => s.GoldenOwned), list.Sum(s => s.DustToComplete), list.Exists(s => s.DustEstimated), list.Sum(s => s.UncraftableMissing),
            rarities);
    }

    /// <summary>Copies of a card a deck can hold.</summary>
    internal static int Limit(Card card) => card.IsLegendary ? 1 : 2;

    private static SetCompletion Of(string set, IEnumerable<Card> cards, CardCollection collection, IReadOnlySet<string> standard)
    {
        var rows = cards.Select(card =>
        {
            var owned = collection.Owned(card.Id);
            var limit = Limit(card);
            var have = Math.Min(Math.Max(owned.Total, 0), limit);
            var missing = limit - have;
            var dust = DeckCost.CraftCost(card);
            return (Card: card, Limit: limit, Have: have, Golden: Math.Min(Math.Max(owned.Golden, 0), limit),
                Dust: (long)missing * (dust ?? 0), Uncraftable: dust is null ? missing : 0,
                Estimated: missing > 0 && dust is not null && card.HasHowToEarn);
        }).ToList();

        var byRarity = rows.GroupBy(r => r.Card.Rarity, StringComparer.Ordinal)
            .OrderBy(g => RarityRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new RarityCompletion(g.Key, g.Count(), g.Count(r => r.Have > 0), g.Sum(r => r.Limit), g.Sum(r => r.Have), g.Sum(r => r.Dust)))
            .ToList();
        var group = set == "VANILLA" ? SetGroup.Classic : standard.Contains(set) ? SetGroup.Standard : SetGroup.Wild;
        return new SetCompletion(
            set, group, CardSets.ReleaseOrder(set),
            rows.Count, rows.Count(r => r.Have > 0), rows.Sum(r => r.Limit), rows.Sum(r => r.Have), rows.Sum(r => r.Golden),
            rows.Sum(r => r.Dust), rows.Exists(r => r.Estimated), rows.Sum(r => r.Uncraftable),
            byRarity);
    }

    private static int RarityRank(string rarity)
    {
        var index = Array.IndexOf(Rarities, rarity);
        return index < 0 ? Rarities.Length : index;
    }
}

namespace Hearthpulse.Core.Cards;

/// <summary>Printings of one card: what the card data says outright, and what only looks alike (docs/collection.md).</summary>
public sealed partial class CardDb
{
    private Dictionary<int, IReadOnlyList<Card>>? _printings;
    private ILookup<(string Name, string Type, string CardClass, int Cost), Card>? _lookAlikes;

    /// <summary>
    /// The printings the game counts as copies of one card, <paramref name="card"/> included: cards joined by
    /// HearthstoneJSON <c>countAsCopyOfDbfId</c> (a Core printing and its original). Only collectible cards of the same
    /// type, class and cost are joined; a card with no such link is alone. Never by name.
    /// </summary>
    public IReadOnlyList<Card> Printings(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var printings = LazyInitializer.EnsureInitialized(ref _printings, BuildPrintings);
        return printings.TryGetValue(card.DbfId, out var group) ? group : [card];
    }

    /// <summary>
    /// Other collectible cards with the same name, type, class and cost that the card data does <b>not</b> link to
    /// <paramref name="card"/> (an older Core printing, a reprint). The game may or may not let one stand in for the
    /// other: a hint for "you may own this as another printing", never counted as owned.
    /// </summary>
    public IReadOnlyList<Card> LookAlikes(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.Collectible || card.Name.Length == 0)
        {
            return [];
        }

        var lookAlikes = LazyInitializer.EnsureInitialized(ref _lookAlikes,
            () => _byDbf.Values.Where(c => c.Collectible && c.Name.Length > 0).ToLookup(Look));
        var linked = Printings(card);
        return [.. lookAlikes[Look(card)].Where(other => !linked.Contains(other)).OrderBy(c => c.Id, StringComparer.Ordinal)];
    }

    private static (string, string, string, int) Look(Card card) => (card.Name, card.Type, card.CardClass, card.Cost);

    private Dictionary<int, IReadOnlyList<Card>> BuildPrintings()
    {
        // Each link joins a printing to the card it counts as a copy of; printings of one original share its group.
        var groups = new Dictionary<int, List<Card>>();
        foreach (var card in _byDbf.Values)
        {
            if (card.CountAsCopyOfDbfId <= 0 || !card.Collectible || !_byDbf.TryGetValue(card.CountAsCopyOfDbfId, out var original)
                || !original.Collectible || original.CountAsCopyOfDbfId > 0 || ReferenceEquals(original, card)
                || original.Type != card.Type || original.CardClass != card.CardClass || original.Cost != card.Cost)
            {
                continue;
            }

            if (!groups.TryGetValue(original.DbfId, out var group))
            {
                groups[original.DbfId] = group = [original];
            }

            group.Add(card);
        }

        var result = new Dictionary<int, IReadOnlyList<Card>>();
        foreach (var group in groups.Values)
        {
            group.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var card in group)
            {
                result[card.DbfId] = group;
            }
        }

        return result;
    }
}

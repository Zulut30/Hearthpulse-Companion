using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Collection;

/// <summary>The one answer to "can I build this deck" a view shows; exactly one holds.</summary>
public enum DeckBuild
{
    /// <summary>Not known: see <see cref="DeckCost.Gap"/> for what is missing to tell. Never to be shown as owned or as "0 dust".</summary>
    Unknown,

    /// <summary>Every card is in the collection.</summary>
    Owned,

    /// <summary>Cards are missing, all of them can be crafted, and the dust at hand pays for them.</summary>
    CraftableNow,

    /// <summary>Cards are missing, all of them can be crafted, and the dust at hand is not enough.</summary>
    NeedsDust,

    /// <summary>A missing card cannot be crafted at all: no dust completes the deck.</summary>
    NotCraftable,
}

/// <summary>Why a deck's cost is <see cref="DeckBuild.Unknown"/>.</summary>
public enum DeckCostGap
{
    None,

    /// <summary>No collection was read yet (always so on macOS and Linux).</summary>
    NoCollection,

    /// <summary>The card data is not loaded.</summary>
    NoCardData,

    /// <summary>No deck, a deck without cards, or a card with no or a negative number of copies (a code that did not decode).</summary>
    EmptyDeck,

    /// <summary>The deck names a card the card data does not know (a newer patch than the card data).</summary>
    UnknownCard,

    /// <summary>A missing collectible card has no known rarity, so its crafting price is unknown.</summary>
    UnknownRarity,

    /// <summary>The deck code has sideboards that could not be read.</summary>
    UnreadableSideboard,
}

/// <summary>A card of a deck the collection has too few copies of.</summary>
/// <param name="CardId">The card as the deck names it.</param>
/// <param name="Copies">How many copies are missing.</param>
/// <param name="DustEach">Dust to craft one plain copy (of the cheapest printing that can be crafted); null when no printing can be crafted.</param>
public sealed record MissingCard(string CardId, int Copies, int? DustEach)
{
    /// <summary>False for a card no dust buys (a free set, a reward).</summary>
    public bool Craftable => DustEach is not null;

    /// <summary>Dust for all missing copies; zero for a card that cannot be crafted (see <see cref="Craftable"/>: it is not free).</summary>
    public int Dust => Copies * (DustEach ?? 0);

    /// <summary>
    /// The card has a price, but the game hands it out in some way (card data <c>howToEarn</c>): it may be one the game
    /// does not let be crafted right now. An estimate, not a ban.
    /// </summary>
    public bool CraftUncertain { get; init; }

    /// <summary>
    /// The collection holds another card with the same name, type, class and cost that the card data does not link to
    /// this one. The game may accept it in the deck: the card may not be missing at all. Never counted as owned.
    /// </summary>
    public bool MayOwnOtherPrinting { get; init; }
}

/// <summary>
/// What a deck costs the owner of a collection (docs/collection.md). Made by <see cref="For(IReadOnlyDictionary{string, int}, CardCollection, CardDb)"/>,
/// a pure function that never throws on missing input: what it cannot tell is <see cref="DeckBuild.Unknown"/>.
/// </summary>
public sealed record DeckCost
{
    private DeckCost()
    {
    }

    /// <summary>The answer; everything else here is detail of it.</summary>
    public DeckBuild State { get; private init; }

    /// <summary>Why <see cref="State"/> is unknown; <see cref="DeckCostGap.None"/> otherwise.</summary>
    public DeckCostGap Gap { get; private init; }

    /// <summary>The missing cards, craftable or not, by card id. Empty when unknown.</summary>
    public IReadOnlyList<MissingCard> Missing { get; private init; } = [];

    /// <summary>
    /// Dust that completes the deck: 0 when owned, the sum when every missing card can be crafted. Null when unknown,
    /// and when a missing card cannot be crafted (no amount completes the deck; see <see cref="DustForCraftable"/>).
    /// </summary>
    public long? DustNeeded { get; private init; }

    /// <summary>
    /// Dust for the missing cards that can be crafted. Equal to <see cref="DustNeeded"/> when that is known; with
    /// <see cref="DeckBuild.NotCraftable"/> it is the cost of the rest, a part of nothing. Null when unknown.
    /// </summary>
    public long? DustForCraftable { get; private init; }

    /// <summary>The collection's dust balance; null without a collection.</summary>
    public long? DustAvailable { get; private init; }

    /// <summary>Dust still to be found: 0 when owned or craftable now, the difference when dust is needed, null otherwise.</summary>
    public long? DustShort { get; private init; }

    /// <summary>
    /// The answer leans on a guess: a missing card may be owned as another printing, or may not be craftable right now
    /// (<see cref="MissingCard.MayOwnOtherPrinting"/>, <see cref="MissingCard.CraftUncertain"/>).
    /// </summary>
    public bool Estimated { get; private init; }

    /// <summary>The collection was read from the client that is running now (<see cref="CardCollection.Confirmed"/>); false also without one.</summary>
    public bool Confirmed { get; private init; }

    /// <summary>The missing cards no dust buys.</summary>
    public IEnumerable<MissingCard> Uncraftable => Missing.Where(card => !card.Craftable);

    /// <summary>
    /// The cost of a deck given as copies by card id. Copies of any finish count, and so do copies of the printings the
    /// card data joins to the card (<see cref="CardDb.Printings"/>): together they fill the slots. No deck needs more
    /// than two copies of a card or more than one of a legendary: a list asking for more is counted up to that limit.
    /// </summary>
    public static DeckCost For(IReadOnlyDictionary<string, int>? deck, CardCollection? collection, CardDb? cards)
    {
        if (cards is null || cards.Count == 0)
        {
            return Unknown(DeckCostGap.NoCardData, collection);
        }

        if (collection is null)
        {
            return Unknown(DeckCostGap.NoCollection, collection);
        }

        if (deck is null || deck.Count == 0 || deck.Values.Any(copies => copies <= 0))
        {
            return Unknown(DeckCostGap.EmptyDeck, collection);
        }

        // One slot group per card the game tells apart: the printings the card data joins share their slots.
        var groups = new SortedDictionary<string, (Card Named, IReadOnlyList<Card> Printings, long Wanted)>(StringComparer.Ordinal);
        var groupOf = new Dictionary<Card, string>();
        foreach (var (cardId, copies) in deck.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (cards.Get(cardId) is not { } card)
            {
                return Unknown(DeckCostGap.UnknownCard, collection);
            }

            var printings = cards.Printings(card);
            if (!groupOf.TryGetValue(printings[0], out var key))
            {
                groupOf[printings[0]] = key = card.Id;
                groups[key] = (card, printings, 0);
            }

            groups[key] = groups[key] with { Wanted = groups[key].Wanted + copies };
        }

        var missing = new List<MissingCard>();
        foreach (var (named, printings, wanted) in groups.Values)
        {
            var needed = Math.Min(wanted, printings.Any(p => p.IsLegendary) ? 1 : 2);
            var owned = printings.Sum(p => (long)collection.Owned(p.Id).Total);
            if (owned >= needed)
            {
                continue;
            }

            if (printings.Any(p => p.Collectible && !UncraftableSets.Contains(p.Set)
                && p.Rarity is not ("COMMON" or "RARE" or "EPIC" or "LEGENDARY" or "FREE")))
            {
                return Unknown(DeckCostGap.UnknownRarity, collection);
            }

            // The cheapest printing that can be crafted; among equals, one the game is not known to hand out.
            var craft = printings
                .Select(p => (Card: p, Dust: CraftCost(p)))
                .Where(p => p.Dust is not null)
                .OrderBy(p => p.Dust).ThenBy(p => p.Card.HasHowToEarn)
                .FirstOrDefault();
            missing.Add(new MissingCard(named.Id, (int)(needed - owned), craft.Dust)
            {
                CraftUncertain = craft.Dust is not null && craft.Card.HasHowToEarn,
                MayOwnOtherPrinting = printings.Any(p => cards.LookAlikes(p).Any(other => collection.Owned(other.Id).Total > 0)),
            });
        }

        var craftable = missing.Sum(card => (long)card.Dust);
        var state = missing.Count == 0 ? DeckBuild.Owned
            : missing.Exists(card => !card.Craftable) ? DeckBuild.NotCraftable
            : craftable <= collection.Dust ? DeckBuild.CraftableNow
            : DeckBuild.NeedsDust;
        return new DeckCost
        {
            State = state,
            Missing = missing,
            DustNeeded = state == DeckBuild.NotCraftable ? null : craftable,
            DustForCraftable = craftable,
            DustAvailable = collection.Dust,
            DustShort = state == DeckBuild.NotCraftable ? null : Math.Max(0, craftable - collection.Dust),
            Estimated = missing.Exists(card => card.CraftUncertain || card.MayOwnOtherPrinting),
            Confirmed = collection.Confirmed,
        };
    }

    /// <summary>
    /// The cost of a decoded deck code (copies by DBF id, each naming one printing), its sideboards included: a
    /// sideboard card that is collectible is a card to own like any other; one that is not (a module of a card that
    /// is built from parts) comes with its owner and is not counted.
    /// </summary>
    public static DeckCost For(DeckDefinition? deck, CardCollection? collection, CardDb? cards)
    {
        if (cards is null || cards.Count == 0)
        {
            return Unknown(DeckCostGap.NoCardData, collection);
        }

        if (deck is null || deck.Cards.Count == 0)
        {
            return Unknown(collection is null ? DeckCostGap.NoCollection : DeckCostGap.EmptyDeck, collection);
        }

        if (deck.SideboardUnread)
        {
            return Unknown(collection is null ? DeckCostGap.NoCollection : DeckCostGap.UnreadableSideboard, collection);
        }

        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (dbfId, copies, sideboard) in deck.Cards.Select(c => (c.Key, c.Value, false)).Concat(deck.Sideboard.Select(c => (c.Key, c.Value, true))))
        {
            if (cards.Get(dbfId) is not { } card)
            {
                return Unknown(collection is null ? DeckCostGap.NoCollection : DeckCostGap.UnknownCard, collection);
            }

            if (sideboard && !card.Collectible)
            {
                continue;
            }

            // A count no deck has: the code is damaged.
            if (copies is <= 0 or > CollectionLimits.MaxCopies)
            {
                return Unknown(collection is null ? DeckCostGap.NoCollection : DeckCostGap.EmptyDeck, collection);
            }

            byId[card.Id] = byId.GetValueOrDefault(card.Id) + copies;
        }

        return For(byId, collection, cards);
    }

    /// <summary>
    /// Known dust to craft one plain copy, by rarity: 40, 100, 400, 1600. Null for uncraftable cards or unknown rarity.
    /// The deck result distinguishes unknown rarity from a confirmed restriction (not collectible, FREE rarity,
    /// or a set the game gives instead of crafting, <see cref="UncraftableSets"/>).
    /// </summary>
    internal static int? CraftCost(Card? card)
    {
        if (card is not { Collectible: true } || UncraftableSets.Contains(card.Set))
        {
            return null;
        }

        return card.Rarity switch
        {
            "COMMON" => 40,
            "RARE" => 100,
            "EPIC" => 400,
            "LEGENDARY" => 1600,
            _ => null,
        };
    }

    private static DeckCost Unknown(DeckCostGap gap, CardCollection? collection) => new()
    {
        State = DeckBuild.Unknown,
        Gap = gap,
        DustAvailable = collection?.Dust,
        Confirmed = collection?.Confirmed == true,
    };

    /// <summary>
    /// HearthstoneJSON sets whose cards are never crafted: the Core set of the year and its past rotations (given for
    /// free, taken back on rotation), hero skins, event rewards, and the Classic-format mirror of the old basic cards.
    /// </summary>
    private static readonly HashSet<string> UncraftableSets = new(StringComparer.Ordinal)
    {
        "CORE", "CORE_HIDDEN", "HERO_SKINS", "EVENT", "VANILLA",
    };
}

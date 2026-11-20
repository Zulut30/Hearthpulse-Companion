namespace Hearthpulse.Core.Collection;

/// <summary>
/// The group a card back is shown in: the client's <c>m_sortCategory</c> (enum <c>Assets.CardBack+SortCategory</c>) in
/// the order the enum declares it. The numbers 0..13 are that order, an assumption the distribution of the read values
/// agrees with (docs/collection-cosmetics.md); any other number is <see cref="Unknown"/>.
/// </summary>
public enum CardBackCategory
{
    None,
    Base,
    Fireside,
    Expansions,
    HeroSkins,
    Seasonal,
    Legend,
    Esports,
    GameLicenses,
    Promotions,
    PrePurchase,
    Blizzcon,
    GoldenCelebration,
    Events,
    Unknown,
}

/// <summary>A card back of the client's catalog. Name and description are in the client's language, as read.</summary>
/// <param name="Id">The client's card back id (<c>m_ID</c>).</param>
/// <param name="SortOrder">The client's order within its group.</param>
public sealed record CosmeticCardBack(int Id, string Name, string Description, CardBackCategory Category, int SortOrder);

/// <summary>A coin of the client's catalog.</summary>
/// <param name="Id">The client's coin id (<c>m_ID</c>).</param>
/// <param name="CardDbfId">The dbfId of the coin's card in the card data (its name and art come from there).</param>
public sealed record CosmeticCoin(int Id, int CardDbfId, string Name, string Description);

/// <summary>
/// The client's card backs and coins with what the player owns and marked as favourite, read at <see cref="ReadAt"/>
/// (UTC; docs/collection-cosmetics.md). The catalog holds only the enabled entries and no "random card back". Kept on
/// this machine only.
/// </summary>
/// <param name="OwnedCardBacks">Card back ids the player owns, as the client holds them (an id may be missing from the catalog).</param>
/// <param name="OwnedCoins">Coin ids the player owns, as read.</param>
public sealed record CosmeticsCollection(
    IReadOnlyList<CosmeticCardBack> CardBacks,
    IReadOnlySet<int> OwnedCardBacks,
    IReadOnlySet<int> FavoriteCardBacks,
    IReadOnlyList<CosmeticCoin> Coins,
    IReadOnlySet<int> OwnedCoins,
    IReadOnlySet<int> FavoriteCoins,
    DateTime ReadAt)
{
    /// <summary>
    /// True only for cosmetics read from the client that is running now; false for the saved file and once that client
    /// was closed, restarted or sent to its login screen (as <see cref="CardCollection.Confirmed"/>).
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>
    /// The cosmetics from the client's rows, by the rules every reader shares: only enabled entries, no random card
    /// back, the sort category mapped by <see cref="CategoryOf"/>. Null when anything is out of
    /// <see cref="CosmeticsLimits"/> or an id repeats: the reader then has no cosmetics rather than a part of them.
    /// </summary>
    public static CosmeticsCollection? FromClient(
        IEnumerable<ClientCardBack> cardBacks,
        IEnumerable<int> ownedCardBacks,
        IEnumerable<int> favoriteCardBacks,
        IEnumerable<ClientCoin> coins,
        IEnumerable<int> ownedCoins,
        IEnumerable<int> favoriteCoins,
        DateTime readAt)
    {
        ArgumentNullException.ThrowIfNull(cardBacks);
        ArgumentNullException.ThrowIfNull(coins);
        var backs = cardBacks.Where(b => b.Enabled && !b.Random)
            .Select(b => new CosmeticCardBack(b.Id, b.Name, b.Description, CategoryOf(b.SortCategory), b.SortOrder))
            .ToList();
        var coinList = coins.Where(c => c.Enabled).Select(c => new CosmeticCoin(c.Id, c.CardDbfId, c.Name, c.Description)).ToList();
        var result = new CosmeticsCollection(
            backs, ownedCardBacks.ToHashSet(), favoriteCardBacks.ToHashSet(),
            coinList, ownedCoins.ToHashSet(), favoriteCoins.ToHashSet(),
            readAt.ToUniversalTime());
        return CosmeticsLimits.Fits(result) ? result : null;
    }

    /// <summary>The group of a client sort category number; a number the enum does not have is <see cref="CardBackCategory.Unknown"/>.</summary>
    public static CardBackCategory CategoryOf(int sortCategory) =>
        sortCategory is >= 0 and < (int)CardBackCategory.Unknown ? (CardBackCategory)sortCategory : CardBackCategory.Unknown;
}

/// <summary>A row of the client's card back table (<c>GameDbf.CardBack</c>) as read, before the catalog's rules.</summary>
public sealed record ClientCardBack(int Id, string Name, string Description, int SortCategory, int SortOrder, bool Enabled, bool Random);

/// <summary>A row of the client's coin table (<c>GameDbf.CosmeticCoin</c>) as read, before the catalog's rules.</summary>
public sealed record ClientCoin(int Id, int CardDbfId, string Name, string Description, bool Enabled);

/// <summary>The bounds cosmetics are held to, the same for a read of the client and the saved file.</summary>
internal static class CosmeticsLimits
{
    /// <summary>Far more card backs or coins than the game has (some hundreds).</summary>
    public const int MaxEntries = 10_000;

    /// <summary>A name or description longer than any the client has.</summary>
    public const int MaxText = 2_000;

    /// <summary>
    /// True when every list is within the bounds, no id is negative (the client's first card back, "Classic", is 0),
    /// catalog ids do not repeat and every text is short enough.
    /// </summary>
    public static bool Fits(CosmeticsCollection cosmetics)
    {
        ArgumentNullException.ThrowIfNull(cosmetics);
        return cosmetics.CardBacks.Count <= MaxEntries && cosmetics.Coins.Count <= MaxEntries
            && Ids(cosmetics.OwnedCardBacks) && Ids(cosmetics.FavoriteCardBacks) && Ids(cosmetics.OwnedCoins) && Ids(cosmetics.FavoriteCoins)
            && cosmetics.CardBacks.All(b => b.Id >= 0 && Text(b.Name) && Text(b.Description) && Enum.IsDefined(b.Category))
            && cosmetics.Coins.All(c => c.Id >= 0 && c.CardDbfId >= 0 && Text(c.Name) && Text(c.Description))
            && cosmetics.CardBacks.Select(b => b.Id).Distinct().Count() == cosmetics.CardBacks.Count
            && cosmetics.Coins.Select(c => c.Id).Distinct().Count() == cosmetics.Coins.Count;
    }

    private static bool Ids(IReadOnlySet<int> ids) => ids.Count <= MaxEntries && ids.All(id => id >= 0);

    private static bool Text(string? text) => text is not null && text.Length <= MaxText;
}

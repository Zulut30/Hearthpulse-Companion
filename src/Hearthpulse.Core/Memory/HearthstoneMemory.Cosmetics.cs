using Hearthpulse.Core.Collection;

namespace Hearthpulse.Core.Memory;

public sealed partial class HearthstoneMemory
{
    // Some hundred card backs and coins, read a record and two localized texts each, plus four sets: far less than this.
    private const int CosmeticsReadBudget = 200_000;
    private static readonly TimeSpan CosmeticsTimeBudget = TimeSpan.FromSeconds(2);

    private static readonly string[] CardBackFields =
        ["m_ID", "m_name", "m_description", "m_sortCategory", "m_sortOrder", "m_enabled", "m_isRandomCardBack"];

    private static readonly string[] CoinFields = ["m_ID", "m_cardId", "m_name", "m_description", "m_enabled"];

    private string? _cosmeticsMissLogged;

    /// <summary>Why the last <see cref="GetCosmetics(Func{bool}?)"/> gave nothing; null after a read that gave cosmetics.</summary>
    internal string? LastCosmeticsMiss { get; private set; }

    /// <summary>
    /// The card backs and coins of the client's catalog (<c>GameDbf.CardBack</c>, <c>GameDbf.CosmeticCoin</c>) with the
    /// ones the player owns and marked as favourite (<c>NetCacheCardBacks</c>, <c>NetCacheCoins</c>: sets of ids), by the
    /// rules of <see cref="CosmeticsCollection.FromClient"/> (docs/collection-cosmetics.md). Names and descriptions are
    /// the client's language. Null on another OS than Windows, before the client loaded them, when
    /// <paramref name="cancelled"/> says the read is no longer wanted, or when anything read does not fit together: never
    /// a part. Read only. The reason for a null goes to the log once per change of reason.
    /// </summary>
    public CosmeticsCollection? GetCosmetics(Func<bool>? cancelled = null) => GetCosmetics(cancelled, CosmeticsReadBudget, CosmeticsTimeBudget);

    internal CosmeticsCollection? GetCosmetics(Func<bool>? cancelled, int maxReads, TimeSpan maxTime)
    {
        string? miss = null;
        var cosmetics = Safe(rt =>
        {
            var read = new ClientRead(this, rt, cancelled);
            var result = ReadCosmetics(read);
            miss = read.Miss;
            return result;
        }, quiet: true, maxReads, maxTime);
        LastCosmeticsMiss = cosmetics is not null ? null : miss ?? (IsAttached ? BudgetMiss : "not attached to the client");
        if (LastCosmeticsMiss != _cosmeticsMissLogged)
        {
            _cosmeticsMissLogged = LastCosmeticsMiss;
            if (LastCosmeticsMiss is { } reason)
            {
                Diag.Info("Cosmetics not read: " + reason);
            }
        }

        return cosmetics;
    }

    private CosmeticsCollection? ReadCosmetics(ClientRead read)
    {
        var rt = read.Runtime;
        if (!read.Wanted())
        {
            return null;
        }

        var gameDbf = rt.FindClass("GameDbf");
        var backsCache = FindNetCacheObject(rt, "NetCacheCardBacks");
        var coinsCache = FindNetCacheObject(rt, "NetCacheCoins");
        if (gameDbf is null || backsCache.IsNull || coinsCache.IsNull)
        {
            return read.Fail<CosmeticsCollection>("the client has not loaded its card backs or coins (or their classes were renamed)");
        }

        var ownedBacksField = Field(backsCache.Class, "Cosmetics.cardBacks", ["<CardBacks>k__BackingField"]);
        var favoriteBacksField = Field(backsCache.Class, "Cosmetics.favoriteCardBacks", ["<FavoriteCardBacks>k__BackingField"]);
        var ownedCoinsField = Field(coinsCache.Class, "Cosmetics.coins", ["<Coins>k__BackingField"]);
        var favoriteCoinsField = Field(coinsCache.Class, "Cosmetics.favoriteCoins", ["<FavoriteCoins>k__BackingField"]);
        if (ownedBacksField is null || favoriteBacksField is null || ownedCoinsField is null || favoriteCoinsField is null)
        {
            return read.Fail<CosmeticsCollection>("a field of the owned or favourite card backs or coins was not found");
        }

        (HashSet<int> Values, int Version)?[] Sets() =>
        [
            read.IntSet(backsCache[ownedBacksField], "the owned card backs", CosmeticsLimits.MaxEntries),
            read.IntSet(backsCache[favoriteBacksField], "the favourite card backs", CosmeticsLimits.MaxEntries),
            read.IntSet(coinsCache[ownedCoinsField], "the owned coins", CosmeticsLimits.MaxEntries),
            read.IntSet(coinsCache[favoriteCoinsField], "the favourite coins", CosmeticsLimits.MaxEntries),
        ];

        var sets = Sets();
        if (Array.Exists(sets, s => s is null))
        {
            return null;
        }

        var backsTable = read.Table(gameDbf, "CardBack", CardBackFields, CosmeticsLimits.MaxEntries);
        var coinsTable = backsTable is null ? null : read.Table(gameDbf, "CosmeticCoin", CoinFields, CosmeticsLimits.MaxEntries);
        if (backsTable is null || coinsTable is null)
        {
            return null;
        }

        var backs = new List<ClientCardBack>(backsTable.Count);
        for (var i = 0; i < backsTable.Count; i++)
        {
            if (!read.Wanted()
                || read.Localized(backsTable.Ptr(i, "m_name")) is not { } name
                || read.Localized(backsTable.Ptr(i, "m_description")) is not { } description)
            {
                return null;
            }

            backs.Add(new ClientCardBack(backsTable.Int(i, "m_ID"), name, description, backsTable.Int(i, "m_sortCategory"),
                backsTable.Int(i, "m_sortOrder"), backsTable.Bool(i, "m_enabled"), backsTable.Bool(i, "m_isRandomCardBack")));
        }

        var coins = new List<ClientCoin>(coinsTable.Count);
        for (var i = 0; i < coinsTable.Count; i++)
        {
            if (!read.Wanted()
                || read.Localized(coinsTable.Ptr(i, "m_name")) is not { } name
                || read.Localized(coinsTable.Ptr(i, "m_description")) is not { } description)
            {
                return null;
            }

            coins.Add(new ClientCoin(coinsTable.Int(i, "m_ID"), coinsTable.Int(i, "m_cardId"), name, description, coinsTable.Bool(i, "m_enabled")));
        }

        // A card back or coin bought, earned or marked while the sets were read: the next read gets them whole.
        var again = Sets();
        for (var i = 0; i < sets.Length; i++)
        {
            if (again[i] is not { } now || now.Version != sets[i]!.Value.Version || !now.Values.SetEquals(sets[i]!.Value.Values))
            {
                return read.Fail<CosmeticsCollection>("the owned or favourite card backs or coins changed while they were read");
            }
        }

        return CosmeticsCollection.FromClient(backs, sets[0]!.Value.Values, sets[1]!.Value.Values, coins, sets[2]!.Value.Values, sets[3]!.Value.Values, DateTime.UtcNow)
            ?? read.Fail<CosmeticsCollection>("the card backs or coins are out of bounds or an id repeats");
    }
}

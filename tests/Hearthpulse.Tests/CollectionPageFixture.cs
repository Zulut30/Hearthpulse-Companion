using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Tests;

/// <summary>A made-up card data, collection and cosmetics for the Collection page's tests (never a player's own).</summary>
internal static class CollectionPageFixture
{
    public static readonly DateTime ReadAt = new(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);

    private const string CardJson = """
        {"id":"CA_C","dbfId":101,"name":"Cataclysm Common","rarity":"COMMON","set":"CATACLYSM","type":"MINION","cost":1,"collectible":true},
        {"id":"CA_R","dbfId":102,"name":"Cataclysm Rare","rarity":"RARE","set":"CATACLYSM","type":"SPELL","cost":2,"collectible":true},
        {"id":"CA_E","dbfId":103,"name":"Cataclysm Epic","rarity":"EPIC","set":"CATACLYSM","type":"MINION","cost":3,"collectible":true},
        {"id":"CA_L","dbfId":104,"name":"Cataclysm Legend","rarity":"LEGENDARY","set":"CATACLYSM","type":"MINION","cost":4,"collectible":true},
        {"id":"TI_C","dbfId":201,"name":"Titans Common","rarity":"COMMON","set":"TITANS","type":"MINION","cost":1,"collectible":true},
        {"id":"VA_C","dbfId":301,"name":"Classic Common","rarity":"COMMON","set":"VANILLA","type":"MINION","cost":1,"collectible":true},
        {"id":"HERO_08","dbfId":637,"name":"Jaina","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE"},
        {"id":"SKIN_MAGE_A","dbfId":401,"name":"Arcane Archmage","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE","collectible":true},
        {"id":"SKIN_MAGE_B","dbfId":402,"name":"Blazing Mage","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE","collectible":true},
        {"id":"SKIN_DK_A","dbfId":403,"name":"Frozen Knight","set":"HERO_SKINS","type":"HERO","cardClass":"DEATHKNIGHT","collectible":true},
        {"id":"COIN_CARD","dbfId":501,"name":"Coin Card","set":"CORE","type":"SPELL","cost":0},
        {"id":"COIN_FAV","dbfId":502,"name":"Favourite Coin","set":"CORE","type":"SPELL","cost":0},
        {"id":"COIN_A","dbfId":503,"name":"The Coin","set":"CORE","type":"SPELL","cost":0},
        {"id":"COIN_B","dbfId":504,"name":"The Coin","set":"CORE","type":"SPELL","cost":0}
        """;

    /// <summary>A Standard set (CATACLYSM), a Wild one (TITANS), Classic (VANILLA), three hero skins of two classes, four coin cards.</summary>
    public static readonly CardDb Cards = Parse(CardJson);

    /// <summary>Wild and Classic sets only: the Standard filter is empty.</summary>
    public static readonly CardDb WildOnlyCards = Parse(string.Join(",\n", CardJson.Split('\n').Where(l => !l.Contains("CATACLYSM", StringComparison.Ordinal)).Select(l => l.TrimEnd().TrimEnd(','))));

    private static CardDb Parse(string cards) => CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("[" + cards + "]")));

    /// <summary>
    /// CATACLYSM: both commons, a rare, one epic, no legendary; TITANS complete; no VANILLA card; one mage skin.
    /// <paramref name="full"/>: every set complete.
    /// </summary>
    public static CardCollection Collection(bool confirmed = true, bool full = false) =>
        new(new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            ["CA_C"] = new(2, 0, 0),
            ["CA_R"] = new(1, 1, 0),
            ["CA_E"] = new(full ? 2 : 1, 0, 0),
            ["CA_L"] = new(full ? 1 : 0, 0, 0),
            ["TI_C"] = new(2, 0, 0),
            ["VA_C"] = new(full ? 2 : 0, 0, 0),
            ["SKIN_MAGE_B"] = new(1, 0, 0),
        }, 500, ReadAt)
        {
            Confirmed = confirmed,
        };

    /// <summary>
    /// Five card backs in Base, Seasonal, None and Unknown; five coins: two with cards of their own names, one not in the
    /// card data, two whose cards share the name "The Coin".
    /// </summary>
    public static CosmeticsCollection Cosmetics(bool confirmed = true) =>
        new(
            [
                new CosmeticCardBack(0, "Classic", "The first back.", CardBackCategory.Base, 0),
                new CosmeticCardBack(4, "Second", string.Empty, CardBackCategory.Base, 1),
                new CosmeticCardBack(7, "Season", string.Empty, CardBackCategory.Seasonal, 0),
                new CosmeticCardBack(5, "Nameless group", string.Empty, CardBackCategory.None, 0),
                new CosmeticCardBack(9, "Unknown group", string.Empty, CardBackCategory.Unknown, 0),
            ],
            new HashSet<int> { 0, 7 }, new HashSet<int> { 0 },
            [
                new CosmeticCoin(1, 501, "Client name of the coin card", string.Empty), new CosmeticCoin(2, 502, "Client favourite", string.Empty),
                new CosmeticCoin(3, 999, "Client coin", string.Empty), new CosmeticCoin(4, 503, "Stormwind Coin", string.Empty),
                new CosmeticCoin(5, 504, "Lordaeron Coin", string.Empty),
            ],
            new HashSet<int> { 1, 2 }, new HashSet<int> { 2 },
            ReadAt)
        {
            Confirmed = confirmed,
        };
}

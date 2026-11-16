using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Constructed;

namespace Hearthpulse.Tests;

/// <summary>
/// A made-up card set, collection and meta for the collection views (linked into the Windows suite as well): one deck
/// list per answer of <see cref="DeckCost"/>, each priced by hand in <see cref="Decks"/>. Nothing here is a real
/// player's collection or dust.
/// </summary>
internal static class CollectionFixture
{
    /// <summary>Dust of the made-up collection.</summary>
    public const long Dust = 1000;

    /// <summary>A long card name, as Russian card names run, for the layout tests.</summary>
    public const string LongName = "Архимаг Тестовых Данных, Хранитель Очень Длинных Названий";

    public static readonly DateTime ReadAt = new(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);

    // CORE_ALIKE shares the name, type, class and cost of C_ALIKE without a link in the card data (a look-alike).
    public static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"HERO_08","dbfId":637,"name":"Jaina","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE"},
          {"id":"C_COMMON","dbfId":1,"name":"Common","rarity":"COMMON","set":"TITANS","type":"MINION","cost":1,"collectible":true},
          {"id":"C_RARE","dbfId":2,"name":"Rare","rarity":"RARE","set":"TITANS","type":"SPELL","cost":2,"collectible":true},
          {"id":"C_EPIC","dbfId":3,"name":"Epic","rarity":"EPIC","set":"TITANS","type":"MINION","cost":3,"collectible":true},
          {"id":"C_LEGEND","dbfId":4,"name":"Архимаг Тестовых Данных, Хранитель Очень Длинных Названий","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":4,"collectible":true},
          {"id":"C_EVENT","dbfId":5,"name":"Event Reward","rarity":"LEGENDARY","set":"EVENT","type":"MINION","cost":6,"collectible":true},
          {"id":"C_REWARD","dbfId":6,"name":"Track Reward","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":8,"collectible":true,"howToEarn":"Given at the start of the expansion."},
          {"id":"C_ALIKE","dbfId":7,"name":"Same Name","rarity":"EPIC","set":"TITANS","type":"SPELL","cost":4,"collectible":true},
          {"id":"CORE_ALIKE","dbfId":8,"name":"Same Name","rarity":"EPIC","set":"CORE_HIDDEN","type":"SPELL","cost":4,"collectible":true}
        ]
        """)));

    /// <summary>Owns the commons and rares, a look-alike of C_ALIKE, and <see cref="Dust"/> dust.</summary>
    public static CardCollection Collection(bool confirmed = true, long dust = Dust, DateTime? readAt = null) =>
        new(new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            ["C_COMMON"] = new(2, 0, 0),
            ["C_RARE"] = new(1, 1, 0),
            ["CORE_ALIKE"] = new(1, 0, 0),
        }, dust, readAt ?? ReadAt)
        {
            Confirmed = confirmed,
        };

    public static string Code(params (string Id, int Copies)[] cards) =>
        DeckString.Encode(cards.ToDictionary(c => c.Id, c => c.Copies, StringComparer.Ordinal), "MAGE", "Standard", Cards)
        ?? throw new InvalidOperationException("deck code");

    /// <summary>Every card owned.</summary>
    public static readonly string Owned = Code(("C_COMMON", 2), ("C_RARE", 2));

    /// <summary>Two epics missing: 2 x 400 = 800 dust, and 1000 at hand pays for them.</summary>
    public static readonly string Craftable = Code(("C_COMMON", 2), ("C_EPIC", 2));

    /// <summary>Two epics and a legendary missing: 800 + 1600 = 2400 dust, 1400 short of the 1000 at hand.</summary>
    public static readonly string NeedsDust = Code(("C_COMMON", 2), ("C_EPIC", 2), ("C_LEGEND", 1));

    /// <summary>An event reward missing: no dust buys it. The rare beside it is owned.</summary>
    public static readonly string NotCraftable = Code(("C_RARE", 2), ("C_EVENT", 1));

    /// <summary>C_ALIKE missing (400) while the collection has its look-alike: craftable, an estimate.</summary>
    public static readonly string OtherPrinting = Code(("C_COMMON", 2), ("C_ALIKE", 1));

    /// <summary>A card the game hands out missing (1600, 600 short): an estimate, it may not be craftable now.</summary>
    public static readonly string Reward = Code(("C_COMMON", 2), ("C_REWARD", 1));

    /// <summary>Not a deck code: nothing can be told.</summary>
    public const string Unreadable = "not-a-deck-code";

    /// <summary>The lists in the order of their win rates, best first.</summary>
    public static IReadOnlyList<string> Decks { get; } = [NeedsDust, NotCraftable, Reward, Craftable, OtherPrinting, Owned, Unreadable];

    /// <summary>
    /// A Standard meta of one archetype with <see cref="Decks"/>, then <paramref name="more"/> lists of another class
    /// whose codes do not decode (they only make the list long enough to be priced on a pool thread).
    /// </summary>
    public static ConstructedMeta Meta(int more = 0)
    {
        var seen = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var archetype = new MetaArchetype(1, "Test Mage", "Test Mage", "MAGE", 5000, 2800, 0.56, 0.2, 0, 0, 0, 0, [], []);
        var decks = Decks.Select((code, i) => new MetaDeck(code, 1, "Test Mage", "MAGE", 900, 500, 0.60 - (i * 0.01), seen)).ToList();
        for (var i = 0; i < more; i++)
        {
            decks.Add(new MetaDeck("extra-list-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 2, "Other", "ROGUE", 900, 300, 0.30, seen));
        }

        // Core builds these from the API (an internal constructor); the Windows suite, which links this file, is no friend of Core.
        return (ConstructedMeta)Activator.CreateInstance(
            typeof(ConstructedMeta), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            ["standard", "legend", "past_week", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc), 12100, new[] { archetype }, decks.ToArray()],
            null)!;
    }
}

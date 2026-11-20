using System.Collections.Concurrent;
using System.Globalization;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.Collection;

/// <summary>
/// Names and release order of the card data's collectible sets (docs/collection-cosmetics.md). Neither the card data
/// nor the client's set table has a set's name, so they are Loc keys <c>CardSet.&lt;CODE&gt;</c> (<c>cardsets.json</c>);
/// the order is the sets' order of release, told apart where needed by the dbfIds the card data gives them.
/// </summary>
public static class CardSets
{
    private const string Prefix = "CardSet.";

    // Oldest first. Every collectible set of the card data except HERO_SKINS and CORE_HIDDEN (not sets one completes);
    // CORE is the current year's Core set, placed where the year begins; VANILLA where the Classic format came out.
    // Written as keys: each is then named in the code where the localization check looks for it.
    private static readonly string[] Keys =
    [
        "CardSet.LEGACY", "CardSet.EXPERT1", "CardSet.NAXX", "CardSet.GVG", "CardSet.BRM", "CardSet.TGT", "CardSet.LOE",
        "CardSet.OG", "CardSet.KARA", "CardSet.GANGS", "CardSet.UNGORO", "CardSet.ICECROWN", "CardSet.LOOTAPALOOZA",
        "CardSet.GILNEAS", "CardSet.BOOMSDAY", "CardSet.TROLL", "CardSet.DALARAN", "CardSet.ULDUM", "CardSet.DRAGONS",
        "CardSet.YEAR_OF_THE_DRAGON", "CardSet.DEMON_HUNTER_INITIATE", "CardSet.BLACK_TEMPLE", "CardSet.SCHOLOMANCE",
        "CardSet.DARKMOON_FAIRE", "CardSet.THE_BARRENS", "CardSet.VANILLA", "CardSet.STORMWIND", "CardSet.ALTERAC_VALLEY",
        "CardSet.THE_SUNKEN_CITY", "CardSet.REVENDRETH", "CardSet.PATH_OF_ARTHAS", "CardSet.RETURN_OF_THE_LICH_KING",
        "CardSet.BATTLE_OF_THE_BANDS", "CardSet.TITANS", "CardSet.WILD_WEST", "CardSet.WONDERS", "CardSet.WHIZBANGS_WORKSHOP",
        "CardSet.ISLAND_VACATION", "CardSet.SPACE", "CardSet.EMERALD_DREAM", "CardSet.THE_LOST_CITY", "CardSet.TIME_TRAVEL",
        "CardSet.CORE", "CardSet.CATACLYSM", "CardSet.ESCAPEFROM_VIOLET_HOLD", "CardSet.EVENT", "CardSet.BE",
    ];

    private static readonly Dictionary<string, int> Order =
        Keys.Select((key, i) => (Code: key[Prefix.Length..], Order: i + 1)).ToDictionary(p => p.Code, p => p.Order, StringComparer.Ordinal);

    // A code the table does not know is logged once per run, not once per view.
    private static readonly ConcurrentDictionary<string, byte> Reported = new(StringComparer.Ordinal);

    /// <summary>The set codes with a name and a place in the order, oldest first.</summary>
    public static IReadOnlyList<string> Known { get; } = [.. Keys.Select(key => key[Prefix.Length..])];

    /// <summary>The set's name in the interface language (Loc "CardSet.&lt;CODE&gt;"), else words made from the code ("Emerald Dream").</summary>
    public static string DisplayName(string set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!Order.ContainsKey(set))
        {
            Report(set);
        }

        return Loc.Or(Prefix + set, Words(set));
    }

    /// <summary>The set's place in the order of release: larger is newer; a code the table does not know is the newest (<see cref="int.MaxValue"/>).</summary>
    public static int ReleaseOrder(string set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (Order.TryGetValue(set, out var order))
        {
            return order;
        }

        Report(set);
        return int.MaxValue;
    }

    /// <summary>The code as words, as the card page shows a set ("EMERALD_DREAM" -> "Emerald Dream").</summary>
    internal static string Words(string set) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(set.Replace('_', ' ').ToLowerInvariant());

    private static void Report(string set)
    {
        if (CollectionLimits.IsCardId(set) && Reported.TryAdd(set, 0))
        {
            Diag.Info($"Card set {set} has no name or place in CardSets: shown as words, as the newest");
        }
    }
}

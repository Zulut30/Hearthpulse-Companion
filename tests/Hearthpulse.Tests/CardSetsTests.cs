using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Tests;

/// <summary>
/// Set names and release order (<see cref="CardSets"/>, <c>cardsets.json</c>): every known code has a name in both
/// languages and a place, the table covers the card data's collectible sets of today, an unknown code reads as words
/// and is the newest.
/// </summary>
public sealed class CardSetsTests
{
    // The collectible sets of the card data without HERO_SKINS and CORE_HIDDEN, as the task listed them (2026-11-19).
    private static readonly string[] Today =
    [
        "CORE", "EVENT", "EMERALD_DREAM", "THE_LOST_CITY", "TIME_TRAVEL", "CATACLYSM", "ESCAPEFROM_VIOLET_HOLD", "BE", "SPACE",
        "ISLAND_VACATION", "WILD_WEST", "BATTLE_OF_THE_BANDS", "WHIZBANGS_WORKSHOP", "RETURN_OF_THE_LICH_KING", "TITANS",
        "ALTERAC_VALLEY", "THE_BARRENS", "STORMWIND", "DARKMOON_FAIRE", "REVENDRETH", "THE_SUNKEN_CITY", "WONDERS", "DRAGONS",
        "BOOMSDAY", "DALARAN", "BLACK_TEMPLE", "GILNEAS", "ICECROWN", "LOOTAPALOOZA", "SCHOLOMANCE", "TROLL", "ULDUM", "UNGORO",
        "OG", "TGT", "GANGS", "GVG", "KARA", "LOE", "YEAR_OF_THE_DRAGON", "BRM", "NAXX", "PATH_OF_ARTHAS",
        "DEMON_HUNTER_INITIATE", "EXPERT1", "LEGACY", "VANILLA",
    ];

    [Fact]
    public void The_table_covers_the_sets_of_today()
    {
        Assert.Equal(47, Today.Length);
        Assert.Equal(Today.Order(StringComparer.Ordinal), CardSets.Known.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("HERO_SKINS", CardSets.Known);
        Assert.DoesNotContain("CORE_HIDDEN", CardSets.Known);
    }

    [Fact]
    public void Every_known_set_has_a_name_in_both_languages_and_nothing_else_is_named()
    {
        foreach (var language in new[] { Loc.English, Loc.Russian })
        {
            var table = Loc.Table(language);
            var keys = table.Keys.Where(k => k.StartsWith("CardSet.", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

            Assert.Equal(CardSets.Known.Select(code => "CardSet." + code).Order(StringComparer.Ordinal), keys);
            Assert.All(CardSets.Known, code => Assert.False(string.IsNullOrWhiteSpace(table["CardSet." + code]), language + " " + code));
        }
    }

    [Fact]
    public void Every_known_set_has_its_own_place_and_known_is_oldest_first()
    {
        var orders = CardSets.Known.Select(CardSets.ReleaseOrder).ToList();

        Assert.Equal(orders.Order(), orders);
        Assert.Equal(orders.Count, orders.Distinct().Count());
        Assert.All(orders, o => Assert.InRange(o, 1, CardSets.Known.Count));
        Assert.True(CardSets.ReleaseOrder("LEGACY") < CardSets.ReleaseOrder("NAXX"));
        Assert.True(CardSets.ReleaseOrder("TITANS") < CardSets.ReleaseOrder("WILD_WEST"));
        Assert.True(CardSets.ReleaseOrder("CATACLYSM") < CardSets.ReleaseOrder("ESCAPEFROM_VIOLET_HOLD"));
        Assert.Equal(CardSets.Known.Count, CardSets.ReleaseOrder("BE"));
    }

    [Fact]
    public void A_known_set_is_named_from_the_table()
    {
        var name = CardSets.DisplayName("CATACLYSM");

        Assert.Contains(name, new[] { Loc.Table(Loc.English)["CardSet.CATACLYSM"], Loc.Table(Loc.Russian)["CardSet.CATACLYSM"] });
    }

    [Fact]
    public void An_unknown_set_reads_as_words_and_is_the_newest()
    {
        Assert.Equal("Brand New Set", CardSets.DisplayName("BRAND_NEW_SET"));
        Assert.Equal(int.MaxValue, CardSets.ReleaseOrder("BRAND_NEW_SET"));
        Assert.Equal(string.Empty, CardSets.DisplayName(string.Empty));
    }
}

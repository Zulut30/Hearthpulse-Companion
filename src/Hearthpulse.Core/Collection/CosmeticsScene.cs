using System.Globalization;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Collection;

/// <summary>A made-up collection with its cosmetics, for the render scenes and view tests.</summary>
public sealed record CosmeticsSceneData(CardCollection Collection, CosmeticsCollection Cosmetics);

/// <summary>
/// Made-up data for the collection pages' scenes: every set of the card data filled to a different degree, part of the
/// hero skins, and card backs and coins with long names. Nothing here is a real player's collection.
/// </summary>
public static class CosmeticsScene
{
    /// <summary>When the made-up data was "read".</summary>
    public static readonly DateTime ReadAt = new(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>A long card back name, as Russian client names run, for the layout.</summary>
    public const string LongName = "Рубашка тестовых данных с очень длинным названием для проверки переноса";

    // How full the sets are, newest first, repeating: a new set mostly empty, older ones fuller, some complete.
    private static readonly int[] Fullness = [12, 45, 70, 88, 100, 60, 95, 30, 100, 80];

    private static readonly string[] BackNames =
    [
        "Classic", "Tavern Night", LongName, "Frozen Throne", "Golden Celebration", "Legend Season", "Darkmoon Lights",
        "Emerald Leaves", "Old Gods", "Starship Hull", "Pirate Flag", "Lunar New Year", "Hallow's End", "Winter Veil",
        "Fireside Gathering", "Esports Champion", "BlizzCon Badge", "Pre-purchase Bundle", "Twitch Prime", "Mystery Back",
    ];

    /// <summary>The made-up data, or null when <paramref name="cards"/> has no collectible set cards (the card data is not loaded).</summary>
    public static CosmeticsSceneData? Build(CardDb cards, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var sets = cards.All
            .Where(c => c.Collectible && c.Set.Length > 0 && c.Set is not ("HERO_SKINS" or "CORE_HIDDEN"))
            .GroupBy(c => c.Set, StringComparer.Ordinal)
            .OrderByDescending(g => CardSets.ReleaseOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (sets.Count == 0)
        {
            return null;
        }

        var owned = new Dictionary<string, OwnedCopies>(StringComparer.Ordinal);
        for (var k = 0; k < sets.Count; k++)
        {
            var full = Fullness[k % Fullness.Length];
            var setCards = sets[k].OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < setCards.Count; i++)
            {
                if (((i * 37) + (k * 11)) % 100 >= full)
                {
                    continue;
                }

                var limit = ExpansionCompletion.Limit(setCards[i]);
                var copies = i % 4 == 1 ? 1 : limit;
                owned[setCards[i].Id] = i % 7 == 0 ? new OwnedCopies(copies - 1, 1, 0) : new OwnedCopies(copies, 0, 0);
            }
        }

        var skins = cards.All.Where(c => c is { Collectible: true, Type: "HERO", Set: "HERO_SKINS" }).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < skins.Count; i++)
        {
            if (i % 3 != 0)
            {
                owned[skins[i].Id] = new OwnedCopies(1, 0, 0);
            }
        }

        var collection = new CardCollection(owned, 12_345, ReadAt) { Confirmed = confirmed };
        return new CosmeticsSceneData(collection, Cosmetics(cards, confirmed));
    }

    private static CosmeticsCollection Cosmetics(CardDb cards, bool confirmed)
    {
        var backs = new List<CosmeticCardBack>();
        var categories = Enum.GetValues<CardBackCategory>();
        for (var i = 0; i < 48; i++)
        {
            var name = BackNames[i % BackNames.Length] + (i < BackNames.Length ? string.Empty : " " + (i / BackNames.Length + 1).ToString(CultureInfo.InvariantCulture));
            var description = i % 5 == 0
                ? "Получена за то, что вы очень долго играли в режиме, название которого не помещается в одну строку подсказки."
                : "A made-up card back for the render scene.";
            backs.Add(new CosmeticCardBack(i + 1, name, description, categories[i % categories.Length], i));
        }

        var coinCards = cards.All.Where(c => c.Id == "GAME_005" || c.Id.Contains("_COIN", StringComparison.Ordinal))
            .OrderBy(c => c.DbfId)
            .Take(12)
            .ToList();
        var coins = new List<CosmeticCoin>();
        for (var i = 0; i < 12; i++)
        {
            var dbfId = i < coinCards.Count ? coinCards[i].DbfId : 0;
            coins.Add(new CosmeticCoin(i + 1, dbfId, i == 3 ? "Монетка с очень длинным названием из клиента игры" : "Coin " + (i + 1).ToString(CultureInfo.InvariantCulture), string.Empty));
        }

        return new CosmeticsCollection(
            backs,
            backs.Where(b => b.Id % 3 != 0).Select(b => b.Id).ToHashSet(),
            new HashSet<int> { 1, 2, 4 },
            coins,
            coins.Where(c => c.Id % 2 == 1).Select(c => c.Id).ToHashSet(),
            new HashSet<int> { 1 },
            ReadAt)
        {
            Confirmed = confirmed,
        };
    }
}

using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;

namespace Hearthpulse.Core.Constructed;

/// <summary>What <see cref="CollectionScene.Build"/> makes: stats with a list per answer, a collection and a deck of the player's.</summary>
public sealed record CollectionSceneData(ConstructedMeta Meta, CardCollection Collection, DeckFamily Deck);

/// <summary>
/// A made-up collection and made-up Standard lists for the UI render check (<c>--render-ui</c>, group
/// <c>collection</c>): every answer of the deck cost in the top decks and the missing cards on a deck page. The cards
/// are picked from the loaded card data, so their names are real; the collection, its dust and the lists are not
/// anybody's, and the player's own collection is never read or shown by it.
/// </summary>
public static class CollectionScene
{
    /// <summary>Dust of the made-up collection.</summary>
    public const long Dust = 1000;

    /// <summary>
    /// Mage lists of the newest cards in the data: all owned; two epics missing (craftable with the dust); epics and
    /// two legendaries missing (dust short); a card no dust buys; one legendary missing; and, when the data has one, a
    /// card the game hands out (an estimate). Null when the card data lacks the cards for it.
    /// </summary>
    /// <param name="confirmed">Whether the collection counts as read from the running game; false is a saved one.</param>
    public static CollectionSceneData? Build(CardDb cards, bool confirmed)
    {
        var pool = cards.All
            .Where(c => c.Collectible && c.CardClass is "MAGE" or "NEUTRAL" && c.Type is "MINION" or "SPELL" && c.DbfId > 0)
            .OrderByDescending(c => c.DbfId)
            .ToList();
        var priced = pool.Where(c => DeckCost.CraftCost(c) is not null && cards.Printings(c).Count == 1 && !c.HasHowToEarn).ToList();
        var commons = priced.Where(c => c.Rarity == "COMMON").Take(2).ToList();
        var rare = priced.FirstOrDefault(c => c.Rarity == "RARE");
        var epics = priced.Where(c => c.Rarity == "EPIC").Take(2).ToList();
        var legends = priced.Where(c => c.Rarity == "LEGENDARY").Take(2).ToList();
        var free = pool.FirstOrDefault(c => DeckCost.CraftCost(c) is null && cards.Printings(c).Count == 1);
        if (commons.Count < 2 || rare is null || epics.Count < 2 || legends.Count < 2 || free is null || cards.Get("HERO_08") is null)
        {
            return null;
        }

        var handedOut = pool.FirstOrDefault(c => c.HasHowToEarn && c.Rarity == "LEGENDARY" && DeckCost.CraftCost(c) is not null);
        var owned = new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            [commons[0].Id] = new(2, 0, 0),
            [commons[1].Id] = new(2, 0, 0),
            [rare.Id] = new(2, 0, 0),
        };
        var collection = new CardCollection(owned, Dust, new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc)) { Confirmed = confirmed };

        var lists = new List<(string Name, (Card Card, int Copies)[] Cards)>
        {
            ("Sample Mage: dust short", [(commons[0], 2), (epics[0], 2), (epics[1], 1), (legends[0], 1), (legends[1], 1)]),
            ("Sample Mage: a card no dust buys", [(commons[0], 2), (rare, 2), (free, 1)]),
            ("Sample Mage: one legendary", [(commons[1], 2), (legends[0], 1)]),
            ("Sample Mage: craftable", [(commons[0], 2), (commons[1], 2), (epics[0], 2)]),
            ("Sample Mage: owned", [(commons[0], 2), (commons[1], 2), (rare, 2)]),
        };
        if (handedOut is not null)
        {
            lists.Insert(3, ("Sample Mage: handed out", [(commons[0], 2), (handedOut, 1)]));
        }

        var seen = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var decks = new List<MetaDeck>(lists.Count);
        foreach (var (list, i) in lists.Select((list, i) => (list, i)))
        {
            if (DeckString.Encode(list.Cards.ToDictionary(c => c.Card.Id, c => c.Copies, StringComparer.Ordinal), "MAGE", "Standard", cards) is { } code)
            {
                decks.Add(new MetaDeck(code, 1, list.Name, "MAGE", 4000 - (i * 500), 2300 - (i * 300), 0.58 - (i * 0.01), seen));
            }
        }

        if (decks.Count == 0)
        {
            return null;
        }

        var archetype = new MetaArchetype(1, "Sample Mage", "Sample Mage", "MAGE", 31000, 17400, 0.561, 0.189, 0, 0, 0, 0, [], []);
        var meta = new ConstructedMeta("standard", "legend", "past_week", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc), 65000, [archetype], decks);

        // The player's deck of the page: the list with the most missing cards, as one played game makes a deck.
        var game = new GameRecord
        {
            Key = "collection-scene-game",
            StartedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local),
            EndedAt = new DateTime(2026, 11, 14, 10, 8, 0, DateTimeKind.Local),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            Result = "Win",
            PlayerClass = "MAGE",
            OpponentClass = "WARRIOR",
            DeckName = "Sample Mage",
            DeckCode = decks[0].DeckString,
        };
        var family = DeckFamilies.Build(DeckFamilies.Versions([game]), []).FirstOrDefault();
        return family is null ? null : new CollectionSceneData(meta, collection, family);
    }
}

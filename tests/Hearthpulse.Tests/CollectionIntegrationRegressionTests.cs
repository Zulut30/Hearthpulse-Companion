using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Tests;

public sealed class CollectionIntegrationRegressionTests
{
    [Fact]
    public void Unverified_printing_chains_cycles_self_links_and_missing_targets_do_not_join_cards()
    {
        var cards = Parse("""
            [
              {"id":"BASE","dbfId":1,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true},
              {"id":"CHAIN_END","dbfId":2,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":1},
              {"id":"CHAIN_START","dbfId":3,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":2},
              {"id":"CYCLE_A","dbfId":4,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":5},
              {"id":"CYCLE_B","dbfId":5,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":4},
              {"id":"SELF","dbfId":6,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":6},
              {"id":"MISSING","dbfId":7,"type":"MINION","cost":1,"cardClass":"MAGE","collectible":true,"countAsCopyOfDbfId":999}
            ]
            """);

        Assert.Equal(["BASE", "CHAIN_END"], cards.Printings(cards.Get("BASE")!).Select(c => c.Id));
        foreach (var id in new[] { "CHAIN_START", "CYCLE_A", "CYCLE_B", "SELF", "MISSING" })
        {
            Assert.Equal(id, Assert.Single(cards.Printings(cards.Get(id)!)).Id);
        }
    }

    [Fact]
    public void Supplement_reconstruction_preserves_printing_link_and_crafting_uncertainty()
    {
        var cards = Parse("""
            [{"id":"LINK","dbfId":2,"name":"Linked","type":"MINION","cost":1,"collectible":true,
              "countAsCopyOfDbfId":1,"howToEarn":"A reward."}]
            """);
        var original = cards.Get("LINK")!;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"data":[{"id":2,"externalId":"LINK","name":"Linked","text":"Supplement text.",
              "tier":2,"cardType":"minion","pool":true,"categories":["tavern"]}]}
            """));

        var patched = Assert.Single(CardDb.ApplySupplement(cards.All.ToList(), BgCardDb.Parse(stream)));

        Assert.NotSame(original, patched);
        Assert.Equal(2, patched.TechLevel);
        Assert.Equal(1, patched.CountAsCopyOfDbfId);
        Assert.True(patched.HasHowToEarn);
    }

    [Fact]
    public void Invalid_sideboard_flags_and_negative_counts_are_not_a_known_empty_sideboard()
    {
        byte[] main = [0, 1, 2, 1, 7, 1, 1, 1, 2, 0];
        byte[][] tails =
        [
            [2],
            [1, 255, 255, 255, 255, 15, 0, 0], // the first group count decodes as -1
            [1, 0, 0, 1, 3, 255, 255, 255, 255, 15, 4], // a negative explicit copy count
        ];
        foreach (var tail in tails)
        {
            var deck = DeckString.TryDecode(Convert.ToBase64String([.. main, .. tail]));
            Assert.NotNull(deck);
            Assert.True(deck.SideboardUnread);
            Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2 }, deck.Cards);
        }
    }

    /// <summary>
    /// Real public deck code from cache/api/hsguru-archetypes-wild.json, inspected 2026-11-16. The fixture projects
    /// only relevant game-data fields from cached HearthstoneJSON ruRU; inventory below is wholly synthetic.
    /// E.T.C. has three collectible sideboard cards; Zilliax has three noncollectible modules/parts.
    /// </summary>
    [Fact]
    public void A_real_zilliax_and_etc_code_needs_the_collectible_cards_but_not_the_modules()
    {
        const string code = "AAEBAQcoS8/nAvyjA7i5A4/OA5PQA/mMBOWwBIy3BO/OBI7UBLjZBJfvBOKkBf3EBaH7BYuUBveXBp+eBtGeBsekBpGoBq+oBuypBtCwBtW6Bo6/BrrBBvrJBvPKBqrqBqn1BsODB+6PB9uXB5ObB+qnB/yvB5TZB93xBwAAAQagzgL9xAXD6gL9xAXRngb9xAX1swbHpAb3swbHpAbu3gbHpAYAAA==";
        using var fixture = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Collection", "zilliax-deck-cards.json"));
        var cards = CardDb.Parse("ruRU", fixture);
        var deck = DeckString.TryDecode(code)!;
        Assert.False(deck.SideboardUnread);
        Assert.Equal(6, deck.Sideboard.Count);
        Assert.Equal([42784, 46403, 102225], deck.Sideboard.Keys.Where(id => cards.Get(id)!.Collectible).Order());
        Assert.Equal([104949, 104951, 110446], deck.Sideboard.Keys.Where(id => !cards.Get(id)!.Collectible).Order());

        var owned = cards.All.Where(c => c.Collectible).ToDictionary(c => c.Id, _ => new OwnedCopies(2, 0, 0));
        var collection = new CardCollection(owned, 0, new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)) { Confirmed = true };
        var result = DeckCost.For(deck, collection, cards);
        Assert.Equal(DeckBuild.Owned, result.State);
        Assert.Empty(result.Missing);
        Assert.Equal(0, result.DustNeeded);
    }

    private static CardDb Parse(string json) => CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
}

using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Tests;

/// <summary>
/// What a deck costs the owner of a collection (<see cref="DeckCost"/>, docs/collection.md), each case worked out by
/// hand in its comment: one state per deck, 40 / 100 / 400 / 1600 dust by rarity, one copy of a legendary, any finish
/// and any printing the card data joins cover a slot, a card that cannot be crafted is "not craftable" and never zero
/// dust, and what cannot be told is "unknown", never "owned".
/// </summary>
public sealed class DeckCostTests
{
    // CORE_ORIG counts as a copy of T_ORIG (the card data's link); CORE_ALIKE and T_ALIKE only share a name.
    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"T_COMMON","dbfId":1,"name":"Common","rarity":"COMMON","set":"TITANS","type":"MINION","cost":1,"collectible":true},
          {"id":"T_RARE","dbfId":2,"name":"Rare","rarity":"RARE","set":"TITANS","type":"SPELL","cost":2,"collectible":true},
          {"id":"T_EPIC","dbfId":3,"name":"Epic","rarity":"EPIC","set":"TITANS","type":"MINION","cost":3,"collectible":true},
          {"id":"T_LEGEND","dbfId":4,"name":"Legend","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":4,"collectible":true},
          {"id":"T_LEGEND2","dbfId":5,"name":"Other Legend","rarity":"LEGENDARY","set":"SPACE","type":"MINION","cost":5,"collectible":true},
          {"id":"CORE_RARE","dbfId":6,"name":"Core Rare","rarity":"RARE","set":"CORE","type":"MINION","cost":2,"collectible":true},
          {"id":"OLD_CORE","dbfId":7,"name":"Rotated Core","rarity":"EPIC","set":"CORE_HIDDEN","type":"MINION","cost":3,"collectible":true},
          {"id":"BASIC_FREE","dbfId":8,"name":"Basic","rarity":"FREE","set":"LEGACY","type":"SPELL","cost":1,"collectible":true},
          {"id":"LEGACY_RARE","dbfId":9,"name":"Legacy Rare","rarity":"RARE","set":"LEGACY","type":"SPELL","cost":2,"collectible":true},
          {"id":"EVENT_LEGEND","dbfId":10,"name":"Event Reward","rarity":"LEGENDARY","set":"EVENT","type":"MINION","cost":6,"collectible":true},
          {"id":"T_TOKEN","dbfId":11,"name":"Token","rarity":"COMMON","set":"TITANS","type":"MINION","cost":1},
          {"id":"NO_RARITY","dbfId":12,"name":"No rarity","set":"TITANS","type":"MINION","cost":1,"collectible":true},
          {"id":"T_ORIG","dbfId":20,"name":"Reprinted","rarity":"RARE","set":"TITANS","type":"MINION","cost":3,"collectible":true},
          {"id":"CORE_ORIG","dbfId":21,"name":"Reprinted","rarity":"RARE","set":"CORE","type":"MINION","cost":3,"collectible":true,"countAsCopyOfDbfId":20},
          {"id":"T_ORIG_L","dbfId":22,"name":"Reprinted Legend","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":7,"collectible":true},
          {"id":"CORE_ORIG_L","dbfId":23,"name":"Reprinted Legend","rarity":"LEGENDARY","set":"CORE","type":"MINION","cost":7,"collectible":true,"countAsCopyOfDbfId":22},
          {"id":"T_ALIKE","dbfId":24,"name":"Same Name","rarity":"EPIC","set":"TITANS","type":"SPELL","cost":4,"collectible":true},
          {"id":"CORE_ALIKE","dbfId":25,"name":"Same Name","rarity":"EPIC","set":"CORE_HIDDEN","type":"SPELL","cost":4,"collectible":true},
          {"id":"T_ALIKE_TOKEN","dbfId":26,"name":"Same Name","rarity":"EPIC","set":"TITANS","type":"SPELL","cost":4},
          {"id":"T_ALIKE_OTHER","dbfId":27,"name":"Same Name","rarity":"EPIC","set":"SPACE","type":"SPELL","cost":5,"collectible":true},
          {"id":"T_REWARD","dbfId":28,"name":"Reward","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":8,"collectible":true,"howToEarn":"Given at the start of the expansion."},
          {"id":"BAD_LINK","dbfId":29,"name":"Bad Link","rarity":"RARE","set":"CORE","type":"MINION","cost":9,"collectible":true,"countAsCopyOfDbfId":11},
          {"id":"OTHER_COST","dbfId":30,"name":"Other Cost","rarity":"RARE","set":"CORE","type":"MINION","cost":9,"collectible":true,"countAsCopyOfDbfId":2},
          {"id":"T_MODULE","dbfId":31,"name":"Module","rarity":"LEGENDARY","set":"TITANS","type":"MINION","cost":2}
        ]
        """)));

    private static CardCollection Collection(long dust, params (string Id, int Normal, int Golden, int Other)[] cards) =>
        new(cards.ToDictionary(c => c.Id, c => new OwnedCopies(c.Normal, c.Golden, c.Other), StringComparer.Ordinal), dust,
            new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));

    private static Dictionary<string, int> Deck(params (string Id, int Copies)[] cards) =>
        cards.ToDictionary(c => c.Id, c => c.Copies, StringComparer.Ordinal);

    // ------------------------------------------------------------------------------------------ the five states

    [Fact]
    public void A_deck_whose_cards_are_all_owned_is_owned_and_costs_nothing()
    {
        var cost = DeckCost.For(
            Deck(("T_COMMON", 2), ("T_RARE", 2), ("T_LEGEND", 1), ("CORE_RARE", 2)),
            Collection(0, ("T_COMMON", 2, 0, 0), ("T_RARE", 5, 0, 0), ("T_LEGEND", 1, 0, 0), ("CORE_RARE", 2, 0, 0)), Cards);

        Assert.Equal(DeckBuild.Owned, cost.State);
        Assert.Equal(DeckCostGap.None, cost.Gap);
        Assert.Empty(cost.Missing);
        Assert.Equal(0, cost.DustNeeded);
        Assert.Equal(0, cost.DustShort);
        Assert.Equal(0, cost.DustAvailable);
        Assert.False(cost.Estimated);
    }

    [Fact]
    public void Missing_cards_cost_their_rarity_per_missing_copy_and_too_little_dust_is_needs_dust()
    {
        // Rare: 2 wanted, 1 owned -> 1 x 100. Epic: 2 wanted, 0 owned -> 2 x 400. Legend: 1 x 1600. Common owned.
        // 100 + 800 + 1600 = 2500; 1000 dust at hand -> 1500 short.
        var cost = DeckCost.For(
            Deck(("T_COMMON", 2), ("T_RARE", 2), ("T_EPIC", 2), ("T_LEGEND", 1)),
            Collection(1000, ("T_COMMON", 2, 0, 0), ("T_RARE", 1, 0, 0)), Cards);

        Assert.Equal(DeckBuild.NeedsDust, cost.State);
        Assert.Equal(
            [new MissingCard("T_EPIC", 2, 400), new MissingCard("T_LEGEND", 1, 1600), new MissingCard("T_RARE", 1, 100)],
            cost.Missing);
        Assert.Equal([800, 1600, 100], cost.Missing.Select(m => m.Dust));
        Assert.Equal(2500, cost.DustNeeded);
        Assert.Equal(2500, cost.DustForCraftable);
        Assert.Equal(1000, cost.DustAvailable);
        Assert.Equal(1500, cost.DustShort);
        Assert.Empty(cost.Uncraftable);
        Assert.False(cost.Estimated);
    }

    [Fact]
    public void With_the_dust_at_hand_the_deck_is_craftable_now()
    {
        // 2 commons x 40 + 1 rare x 100 = 180, exactly the balance.
        var deck = Deck(("T_COMMON", 2), ("T_RARE", 1));

        var enough = DeckCost.For(deck, Collection(180), Cards);
        Assert.Equal(DeckBuild.CraftableNow, enough.State);
        Assert.Equal(180, enough.DustNeeded);
        Assert.Equal(0, enough.DustShort);

        var oneShort = DeckCost.For(deck, Collection(179), Cards);
        Assert.Equal(DeckBuild.NeedsDust, oneShort.State);
        Assert.Equal(1, oneShort.DustShort);
    }

    [Fact]
    public void A_missing_card_that_cannot_be_crafted_is_not_craftable_and_the_dust_is_a_part_not_a_total()
    {
        // Core rare: not craftable. Rare: 2 x 100 = 200, the cost of the rest; no amount completes the deck.
        var cost = DeckCost.For(Deck(("CORE_RARE", 2), ("T_RARE", 2), ("T_COMMON", 2)), Collection(5000, ("T_COMMON", 2, 0, 0)), Cards);

        Assert.Equal(DeckBuild.NotCraftable, cost.State);
        Assert.Equal([new MissingCard("CORE_RARE", 2, null), new MissingCard("T_RARE", 2, 100)], cost.Missing);
        Assert.False(cost.Missing[0].Craftable);
        Assert.Null(cost.DustNeeded);
        Assert.Null(cost.DustShort);
        Assert.Equal(200, cost.DustForCraftable);
        Assert.Equal(5000, cost.DustAvailable);
        Assert.Equal(["CORE_RARE"], cost.Uncraftable.Select(m => m.CardId));

        // The only missing card is the uncraftable one: still no "0 dust needed".
        var only = DeckCost.For(Deck(("CORE_RARE", 1)), Collection(5000), Cards);
        Assert.Equal(DeckBuild.NotCraftable, only.State);
        Assert.Null(only.DustNeeded);
        Assert.Null(only.DustShort);
        Assert.Equal(0, only.DustForCraftable);
    }

    [Fact]
    public void What_cannot_be_told_is_unknown_never_owned_and_never_zero_dust()
    {
        var deck = Deck(("T_COMMON", 2));
        var collection = Collection(700, ("T_COMMON", 2, 0, 0));

        void Unknown(DeckCost cost, DeckCostGap gap, long? dust)
        {
            Assert.Equal(DeckBuild.Unknown, cost.State);
            Assert.Equal(gap, cost.Gap);
            Assert.Empty(cost.Missing);
            Assert.Null(cost.DustNeeded);
            Assert.Null(cost.DustForCraftable);
            Assert.Null(cost.DustShort);
            Assert.Equal(dust, cost.DustAvailable);
            Assert.False(cost.Confirmed);
        }

        Unknown(DeckCost.For(deck, null, Cards), DeckCostGap.NoCollection, null);
        Unknown(DeckCost.For(deck, collection, CardDb.Empty), DeckCostGap.NoCardData, 700);
        Unknown(DeckCost.For(deck, collection, null), DeckCostGap.NoCardData, 700);
        Unknown(DeckCost.For(deck, null, null), DeckCostGap.NoCardData, null);
        Unknown(DeckCost.For((IReadOnlyDictionary<string, int>?)null, collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Deck(), collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Deck(("T_COMMON", 2), ("T_RARE", 0)), collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Deck(("T_COMMON", -1)), collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Deck(("T_COMMON", 2), ("NOT_IN_THE_CARD_DATA", 1)), collection, Cards), DeckCostGap.UnknownCard, 700);

        Unknown(DeckCost.For((DeckDefinition?)null, collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Code(new() { [1] = 2 }), null, Cards), DeckCostGap.NoCollection, null);
        Unknown(DeckCost.For(Code([]), collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Code(new() { [1] = 2 }), collection, CardDb.Empty), DeckCostGap.NoCardData, 700);
        Unknown(DeckCost.For(Code(new() { [1] = 2, [999] = 1 }), collection, Cards), DeckCostGap.UnknownCard, 700);
        Unknown(DeckCost.For(Code(new() { [1] = 0 }), collection, Cards), DeckCostGap.EmptyDeck, 700);
        Unknown(DeckCost.For(Code(new() { [1] = 2 }) with { SideboardUnread = true }, collection, Cards), DeckCostGap.UnreadableSideboard, 700);
    }

    [Fact]
    public void The_cost_says_whether_the_collection_behind_it_is_the_running_clients()
    {
        var deck = Deck(("T_COMMON", 2));

        Assert.False(DeckCost.For(deck, Collection(0), Cards).Confirmed);
        Assert.True(DeckCost.For(deck, Collection(0) with { Confirmed = true }, Cards).Confirmed);
    }

    // ------------------------------------------------------------------------------------------- copies and finishes

    [Fact]
    public void A_copy_of_any_finish_covers_a_slot()
    {
        // Common: 1 + 1 of two finishes = 2. Rare: 2 of the second finish. Epic: 1 of the third of 2 wanted -> 1 x 400.
        var cost = DeckCost.For(
            Deck(("T_COMMON", 2), ("T_RARE", 2), ("T_EPIC", 2), ("T_LEGEND", 1)),
            Collection(0, ("T_COMMON", 1, 1, 0), ("T_RARE", 0, 2, 0), ("T_EPIC", 0, 0, 1), ("T_LEGEND", 0, 0, 1)), Cards);

        Assert.Equal([new MissingCard("T_EPIC", 1, 400)], cost.Missing);
        Assert.Equal(400, cost.DustNeeded);

        // Which finish a copy is makes no difference to the cost.
        var moved = DeckCost.For(
            Deck(("T_COMMON", 2), ("T_RARE", 2), ("T_EPIC", 2), ("T_LEGEND", 1)),
            Collection(0, ("T_COMMON", 0, 0, 2), ("T_RARE", 2, 0, 0), ("T_EPIC", 1, 0, 0), ("T_LEGEND", 0, 1, 0)), Cards);
        Assert.Equal(cost.Missing, moved.Missing);
    }

    [Fact]
    public void A_legendary_is_needed_once_and_any_other_card_twice_whatever_the_list_says()
    {
        // A list asking for 2 legendaries and 3 commons: 1 x 1600 + 2 x 40 = 1680.
        var cost = DeckCost.For(Deck(("T_LEGEND", 2), ("T_COMMON", 3)), Collection(0), Cards);

        Assert.Equal([new MissingCard("T_COMMON", 2, 40), new MissingCard("T_LEGEND", 1, 1600)], cost.Missing);
        Assert.Equal(1680, cost.DustNeeded);

        // Extra copies of one card do not pay for another.
        var owned = DeckCost.For(Deck(("T_LEGEND", 1), ("T_LEGEND2", 1)), Collection(0, ("T_LEGEND", 5, 3, 0)), Cards);
        Assert.Equal([new MissingCard("T_LEGEND2", 1, 1600)], owned.Missing);
    }

    [Theory]
    [InlineData("T_COMMON", 40)]
    [InlineData("T_RARE", 100)]
    [InlineData("T_EPIC", 400)]
    [InlineData("T_LEGEND", 1600)]
    [InlineData("LEGACY_RARE", 100)]
    [InlineData("CORE_RARE", null)]
    [InlineData("OLD_CORE", null)]
    [InlineData("BASIC_FREE", null)]
    [InlineData("EVENT_LEGEND", null)]
    [InlineData("T_TOKEN", null)]
    public void The_craft_cost_follows_rarity_and_free_sets_have_none(string cardId, int? dust)
    {
        Assert.Equal(dust, DeckCost.CraftCost(Cards.Get(cardId)));

        var cost = DeckCost.For(Deck((cardId, 1)), Collection(100_000), Cards);
        Assert.Equal([new MissingCard(cardId, 1, dust)], cost.Missing);
        Assert.Equal(dust is null ? DeckBuild.NotCraftable : DeckBuild.CraftableNow, cost.State);
    }

    [Fact]
    public void A_missing_card_of_unknown_rarity_has_an_unknown_price_not_a_crafting_ban()
    {
        var unknown = DeckCost.For(Deck(("NO_RARITY", 1)), Collection(700), Cards);
        Assert.Equal((DeckBuild.Unknown, DeckCostGap.UnknownRarity), (unknown.State, unknown.Gap));
        Assert.Null(unknown.DustNeeded);
        Assert.Null(unknown.DustShort);
        Assert.Equal(700, unknown.DustAvailable);
        Assert.Equal(DeckBuild.Owned,
            DeckCost.For(Deck(("NO_RARITY", 1)), Collection(0, ("NO_RARITY", 1, 0, 0)), Cards).State);
    }

    [Fact]
    public void An_owned_uncraftable_card_is_no_obstacle()
    {
        var cost = DeckCost.For(Deck(("CORE_RARE", 2), ("BASIC_FREE", 2)), Collection(0, ("CORE_RARE", 2, 0, 0), ("BASIC_FREE", 0, 2, 0)), Cards);

        Assert.Equal(DeckBuild.Owned, cost.State);
    }

    [Fact]
    public void An_empty_collection_prices_the_whole_deck()
    {
        // 2 x 40 + 2 x 100 + 2 x 400 + 1600 = 2680.
        var cost = DeckCost.For(Deck(("T_COMMON", 2), ("T_RARE", 2), ("T_EPIC", 2), ("T_LEGEND", 1)), Collection(0), Cards);

        Assert.Equal(DeckBuild.NeedsDust, cost.State);
        Assert.Equal(2680, cost.DustNeeded);
        Assert.Equal(2680, cost.DustShort);
        Assert.Equal(7, cost.Missing.Sum(m => m.Copies));
    }

    // ------------------------------------------------------------------------------------------------- printings

    [Fact]
    public void The_card_data_joins_printings_by_its_link_only()
    {
        Assert.Equal(["CORE_ORIG", "T_ORIG"], Cards.Printings(Cards.Get("T_ORIG")!).Select(c => c.Id));
        Assert.Equal(["CORE_ORIG", "T_ORIG"], Cards.Printings(Cards.Get("CORE_ORIG")!).Select(c => c.Id));
        Assert.Equal(["T_COMMON"], Cards.Printings(Cards.Get("T_COMMON")!).Select(c => c.Id));

        // The same name alone joins nothing; it is only a hint, among collectible cards of the same type, class and cost.
        Assert.Equal(["T_ALIKE"], Cards.Printings(Cards.Get("T_ALIKE")!).Select(c => c.Id));
        Assert.Equal(["CORE_ALIKE"], Cards.LookAlikes(Cards.Get("T_ALIKE")!).Select(c => c.Id));
        Assert.Equal(["T_ALIKE"], Cards.LookAlikes(Cards.Get("CORE_ALIKE")!).Select(c => c.Id));
        Assert.Empty(Cards.LookAlikes(Cards.Get("T_ORIG")!));
        Assert.Empty(Cards.LookAlikes(Cards.Get("T_ALIKE_TOKEN")!));

        // A link to a card that is not collectible, or of another cost, is not followed.
        Assert.Equal(["BAD_LINK"], Cards.Printings(Cards.Get("BAD_LINK")!).Select(c => c.Id));
        Assert.Equal(["OTHER_COST"], Cards.Printings(Cards.Get("OTHER_COST")!).Select(c => c.Id));
        Assert.Equal(["T_RARE"], Cards.Printings(Cards.Get("T_RARE")!).Select(c => c.Id));
    }

    [Fact]
    public void A_joined_printing_fills_the_slot_in_either_direction()
    {
        // The deck names the original, the player owns the Core printing.
        Assert.Equal(DeckBuild.Owned, DeckCost.For(Deck(("T_ORIG", 2)), Collection(0, ("CORE_ORIG", 2, 0, 0)), Cards).State);
        // The deck names the Core printing, the player owns the original.
        Assert.Equal(DeckBuild.Owned, DeckCost.For(Deck(("CORE_ORIG", 2)), Collection(0, ("T_ORIG", 0, 2, 0)), Cards).State);
        // One of each fills two slots.
        Assert.Equal(DeckBuild.Owned, DeckCost.For(Deck(("CORE_ORIG", 2)), Collection(0, ("T_ORIG", 1, 0, 0), ("CORE_ORIG", 1, 0, 0)), Cards).State);
    }

    [Fact]
    public void A_missing_joined_card_costs_the_printing_that_can_be_crafted()
    {
        // The Core printing itself cannot be crafted, the original can: 1 missing x 100.
        var cost = DeckCost.For(Deck(("CORE_ORIG", 2)), Collection(100, ("T_ORIG", 1, 0, 0)), Cards);

        Assert.Equal(DeckBuild.CraftableNow, cost.State);
        Assert.Equal([new MissingCard("CORE_ORIG", 1, 100)], cost.Missing);
        Assert.False(cost.Estimated);

        // Named twice, as both printings: the two share their slots (2 for a rare, 1 for a legendary).
        var both = DeckCost.For(Deck(("CORE_ORIG", 2), ("T_ORIG", 2)), Collection(0), Cards);
        Assert.Equal([new MissingCard("CORE_ORIG", 2, 100)], both.Missing);
        var legend = DeckCost.For(Deck(("CORE_ORIG_L", 1), ("T_ORIG_L", 1)), Collection(0, ("T_ORIG_L", 1, 0, 0)), Cards);
        Assert.Equal(DeckBuild.Owned, legend.State);
    }

    [Fact]
    public void A_card_with_only_the_same_name_is_not_counted_but_the_result_says_it_may_be_owned()
    {
        // The player owns the rotated Core printing; the card data does not join it to the deck's card.
        var cost = DeckCost.For(Deck(("T_ALIKE", 2), ("T_COMMON", 1)), Collection(10_000, ("CORE_ALIKE", 2, 0, 0)), Cards);

        Assert.Equal(DeckBuild.CraftableNow, cost.State);
        Assert.Equal(
            [new MissingCard("T_ALIKE", 2, 400) { MayOwnOtherPrinting = true }, new MissingCard("T_COMMON", 1, 40)],
            cost.Missing);
        Assert.Equal(840, cost.DustNeeded);
        Assert.True(cost.Estimated);

        // Not owned in any printing: a plain missing card, no hint.
        var plain = DeckCost.For(Deck(("T_ALIKE", 2)), Collection(0, ("T_ALIKE_OTHER", 2, 0, 0)), Cards);
        Assert.Equal([new MissingCard("T_ALIKE", 2, 400)], plain.Missing);
        Assert.False(plain.Estimated);
    }

    [Fact]
    public void A_card_the_game_hands_out_is_priced_and_marked_as_maybe_not_craftable_now()
    {
        var cost = DeckCost.For(Deck(("T_REWARD", 1), ("T_COMMON", 1)), Collection(2000), Cards);

        Assert.Equal(DeckBuild.CraftableNow, cost.State);
        Assert.Equal([new MissingCard("T_COMMON", 1, 40), new MissingCard("T_REWARD", 1, 1600) { CraftUncertain = true }], cost.Missing);
        Assert.Equal(1640, cost.DustNeeded);
        Assert.True(cost.Estimated);

        // Owned: nothing to estimate.
        Assert.False(DeckCost.For(Deck(("T_REWARD", 1)), Collection(0, ("T_REWARD", 1, 0, 0)), Cards).Estimated);
    }

    // -------------------------------------------------------------------------------------------------- deck codes

    private static DeckDefinition Code(Dictionary<int, int> cards, Dictionary<int, int>? sideboard = null) =>
        new("Deck", 0, string.Empty, "Standard", [], cards) { Sideboard = sideboard ?? [] };

    [Fact]
    public void A_deck_code_is_priced_by_the_printings_its_dbf_ids_name()
    {
        // dbf 2 = rare x2 (1 owned -> 100), dbf 4 = legend (owned), dbf 21 = the Core printing (the original is owned).
        var cost = DeckCost.For(Code(new() { [2] = 2, [4] = 1, [21] = 2 }),
            Collection(100, ("T_RARE", 1, 0, 0), ("T_LEGEND", 0, 1, 0), ("T_ORIG", 2, 0, 0)), Cards);

        Assert.Equal(DeckBuild.CraftableNow, cost.State);
        Assert.Equal([new MissingCard("T_RARE", 1, 100)], cost.Missing);
    }

    [Fact]
    public void Sideboard_cards_are_cards_to_own_and_modules_come_with_their_owner()
    {
        // Sideboard: an epic (missing, 400), a common that is also in the deck (2 + 1 wanted = 2 needed, 2 owned), and a
        // module that is not collectible (not counted).
        var deck = Code(new() { [1] = 2, [4] = 1 }, new() { [3] = 1, [1] = 1, [31] = 1 });

        var cost = DeckCost.For(deck, Collection(0, ("T_COMMON", 2, 0, 0), ("T_LEGEND", 1, 0, 0)), Cards);

        Assert.Equal(DeckBuild.NeedsDust, cost.State);
        Assert.Equal([new MissingCard("T_EPIC", 1, 400)], cost.Missing);
    }

    [Fact]
    public void A_deck_code_with_a_sideboard_is_decoded_and_one_cut_short_says_so()
    {
        static byte[] Bytes(params int[] values) => [.. values.SelectMany(v =>
        {
            var bytes = new List<byte>();
            do
            {
                var b = (byte)(v & 0x7F);
                v >>= 7;
                bytes.Add(v > 0 ? (byte)(b | 0x80) : b);
            }
            while (v > 0);
            return bytes;
        })];

        // 0, version 1, Standard, one hero 7; one card x1 (dbf 1), one x2 (dbf 2), none xN.
        int[] main = [0, 1, 2, 1, 7, 1, 1, 1, 2, 0];
        var without = DeckString.TryDecode(Convert.ToBase64String(Bytes(main)))!;
        Assert.Empty(without.Sideboard);
        Assert.False(without.SideboardUnread);
        var flagOff = DeckString.TryDecode(Convert.ToBase64String(Bytes([.. main, 0])))!;
        Assert.Empty(flagOff.Sideboard);
        Assert.False(flagOff.SideboardUnread);

        // Sideboards: two cards x1 (dbf 3 and 300, owner 4), one x2 (dbf 5, owner 4), one xN (dbf 6, 3 copies, owner 4).
        int[] side = [1, 2, 3, 4, 300, 4, 1, 5, 4, 1, 6, 3, 4];
        var with = DeckString.TryDecode(Convert.ToBase64String(Bytes([.. main, .. side])))!;
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2 }, with.Cards);
        Assert.Equal(3, with.CardCount);
        Assert.Equal(new Dictionary<int, int> { [3] = 1, [300] = 1, [5] = 2, [6] = 3 }, with.Sideboard);
        Assert.False(with.SideboardUnread);

        // The flag is set and the list ends early: the deck is read, the sideboard is marked unread.
        var cut = DeckString.TryDecode(Convert.ToBase64String(Bytes([.. main, 1, 2, 3, 4, 300])))!;
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2 }, cut.Cards);
        Assert.True(cut.SideboardUnread);
    }
}

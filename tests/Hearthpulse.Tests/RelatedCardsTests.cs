using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.EctoplasmListTests;

namespace Hearthpulse.Tests;

/// <summary>
/// A card in hand's related cards (the tokens it makes, the cards it names) and the Graveyard panel's dead minions;
/// synthetic card data and catalog, synthetic Power.log lines.
/// </summary>
public sealed class RelatedCardsTests
{
    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"CS2_101","dbfId":1,"name":"Reinforce","type":"HERO_POWER","set":"CORE","collectible":false},
          {"id":"GEN_1","dbfId":10,"name":"Muster for Battle","type":"SPELL","cost":3,"set":"TGT","collectible":true},
          {"id":"GEN_1t","dbfId":11,"name":"Silver Hand Recruit","type":"MINION","cost":1,"set":"TGT"},
          {"id":"GEN_1t2","dbfId":12,"name":"Light's Justice","type":"WEAPON","cost":1,"set":"TGT"},
          {"id":"GEN_1e","dbfId":13,"name":"Muster buff","type":"ENCHANTMENT","set":"TGT"},
          {"id":"GEN_1t3","dbfId":14,"name":"Other set token","type":"MINION","cost":1,"set":"OTHER"},
          {"id":"CORE_GEN_1","dbfId":15,"name":"Muster for Battle","type":"SPELL","cost":3,"set":"CORE","collectible":true},
          {"id":"SHUF_1","dbfId":20,"name":"Shuffler","type":"MINION","cost":2,"set":"X","collectible":true},
          {"id":"FIRE_1","dbfId":21,"name":"Fireball","type":"SPELL","cost":4,"set":"X","collectible":true},
          {"id":"CHARGE_1","dbfId":22,"name":"Charge","type":"SPELL","cost":1,"set":"X","collectible":true},
          {"id":"PLAIN","dbfId":30,"name":"Chillwind Yeti","type":"MINION","cost":4,"set":"X","collectible":true},
          {"id":"BE_017","dbfId":128389,"name":"N'Zoth, Dream's End","type":"MINION","cost":10,"set":"BE","collectible":true},
          {"id":"BE_017t3","dbfId":128390,"name":"Ability Three","type":"SPELL","cost":0,"set":"BE"},
          {"id":"BE_017t4","dbfId":128391,"name":"Ability Four","type":"SPELL","cost":0,"set":"BE"},
          {"id":"BE_017t5","dbfId":128392,"name":"Ability Five","type":"SPELL","cost":0,"set":"BE"},
          {"id":"BE_017t6","dbfId":128393,"name":"Ability Six","type":"SPELL","cost":0,"set":"BE"},
          {"id":"BE_017t6e","dbfId":128394,"name":"Ability Six buff","type":"ENCHANTMENT","set":"BE"},
          {"id":"EDR_872","dbfId":40,"name":"Spark of Life","type":"SPELL","cost":2,"set":"EDR","collectible":true},
          {"id":"EDR_872A","dbfId":41,"name":"Spark A","type":"SPELL","cost":2,"set":"EDR"},
          {"id":"EDR_872B","dbfId":42,"name":"Spark B","type":"SPELL","cost":2,"set":"EDR"},
          {"id":"LOE_115","dbfId":50,"name":"Raven Idol","type":"SPELL","cost":0,"set":"LOE","collectible":true},
          {"id":"LOE_115a","dbfId":51,"name":"Break Free","type":"SPELL","cost":0,"set":"LOE"},
          {"id":"LOE_115b","dbfId":52,"name":"Awakened","type":"SPELL","cost":0,"set":"LOE"},
          {"id":"Core_LOE_115","dbfId":53,"name":"Raven Idol","type":"SPELL","cost":0,"set":"CORE","collectible":true},
          {"id":"BE_013","dbfId":80,"name":"C'Thun, the Shattered","type":"MINION","cost":10,"set":"BE","collectible":true},
          {"id":"BE_013t3","dbfId":81,"name":"Eye of C'Thun","type":"SPELL","cost":5,"text":"Deal 7 damage.","set":"BE"},
          {"id":"BE_013t6","dbfId":82,"name":"Eye of C'Thun","type":"SPELL","cost":5,"text":"Deal 7 damage.","set":"BE"},
          {"id":"BE_013t4","dbfId":83,"name":"Eye of C'Thun","type":"SPELL","cost":5,"text":"Deal 10 damage.","set":"BE"},
          {"id":"BE_013t5","dbfId":84,"name":"Body of C'Thun","type":"SPELL","cost":5,"text":"Deal 7 damage.","set":"BE"},
          {"id":"BIG_1","dbfId":60,"name":"Many Tokens","type":"SPELL","cost":5,"set":"BIG","collectible":true},
          {"id":"BIG_1t1","dbfId":61,"name":"T1","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t2","dbfId":62,"name":"T2","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t3","dbfId":63,"name":"T3","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t4","dbfId":64,"name":"T4","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t5","dbfId":65,"name":"T5","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t6","dbfId":66,"name":"T6","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t7","dbfId":67,"name":"T7","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t8","dbfId":68,"name":"T8","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t9","dbfId":69,"name":"T9","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t10","dbfId":70,"name":"T10","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t11","dbfId":71,"name":"T11","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t12","dbfId":72,"name":"T12","type":"MINION","cost":1,"set":"BIG"},
          {"id":"BIG_1t13","dbfId":73,"name":"T13","type":"MINION","cost":1,"set":"BIG"}
        ]
        """u8.ToArray()));

    private static PoolCard Pool(string id, string name, string text) =>
        new(id, 0, name, text, "SPELL", "COMMON", 1, null, null, [], [], null, [], [], "X", true);

    private static readonly CardPoolCatalog Catalog = new(
    [
        Pool("SHUF_1", "Shuffler", "<b>Battlecry:</b> Shuffle a Fireball into your deck. <b>Charge</b>"),
        Pool("FIRE_1", "Fireball", "Deal $6 damage."),
        Pool("CHARGE_1", "Charge", "Give a minion <b>Charge</b>."),
        Pool("PLAIN", "Chillwind Yeti", "A Fireballer's tale."),
        Pool("GEN_1", "Muster for Battle", "Summon three 1/1 Silver Hand Recruits. Equip a 1/4 weapon."),
    ], "test", DateTime.UtcNow);

    [Fact]
    public void A_cards_tokens_by_id_are_its_related_cards_also_for_its_core_reprint()
    {
        var list = RelatedCards.Of("GEN_1", Cards, Catalog)!;

        Assert.Equal(Loc.T("Related.Title"), list.Title);
        Assert.Equal(["GEN_1t", "GEN_1t2"], list.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
        Assert.All(list.Rows, r => Assert.Equal(1, r.Count));
        Assert.Equal(["GEN_1t", "GEN_1t2"], RelatedCards.Of("CORE_GEN_1", Cards, Catalog)!.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_card_named_in_the_text_is_related_but_not_a_bold_keyword_or_part_of_a_word()
    {
        var list = RelatedCards.Of("SHUF_1", Cards, Catalog)!;

        Assert.Equal(["FIRE_1"], list.Rows.Select(r => r.CardId));
        Assert.Null(RelatedCards.Of("PLAIN", Cards, Catalog));
        Assert.Null(RelatedCards.Of("CHARGE_1", Cards, Catalog));
        Assert.Null(RelatedCards.Of("NOPE", Cards, Catalog));
    }

    /// <summary>
    /// 81 real catalog records, cut as they are from the app's disk cache of the Hearthpulse API catalog
    /// (<c>api/v1/constructed-cards?format=wild</c>, 6 501 cards, fetched 2026-11-15T21:38:55Z) by card id: the cards
    /// of the cases below and every card their texts could mean.
    /// </summary>
    private static readonly Lazy<CardPoolCatalog> Real = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RelatedCards", "catalog.json"));
        return CardPoolCatalog.Read(file)!;
    });

    private static CardPoolCatalog Reversed(CardPoolCatalog catalog) => new(catalog.Cards.Reverse().ToList(), "test", DateTime.UtcNow);

    private static CardPoolCatalog With(CardPoolCatalog catalog, params PoolCard[] more) => new([.. catalog.Cards, .. more], "test", DateTime.UtcNow);

    /// <summary>The ids of the cards <paramref name="cardId"/>'s text names, with card data made of the catalog's own cards (no tokens).</summary>
    private static string[] NamedBy(string cardId, CardPoolCatalog catalog)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog.Cards.Select(c =>
            new { id = c.Id, dbfId = c.DbfId, name = c.Name, text = c.Text, type = c.Type, cost = c.Cost, set = c.Set, rarity = c.Rarity, collectible = true }));
        var cards = CardDb.Parse("enUS", new MemoryStream(json));
        return RelatedCards.Of(cardId, cards, catalog)?.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal).ToArray() ?? [];
    }

    /// <summary>
    /// The owner's screenshot (2026-11-16): Dark Deacon <c>BE_012</c>, "choose a C'Thun ability to trigger", showed the
    /// Old Gods C'Thun (8 mana 6/6, <c>OG_280</c>). "C'Thun" is that card's whole name and the short name of two more;
    /// in a card of the 2026 set it is that set's <c>BE_013</c>, whatever order the catalog lists them in.
    /// </summary>
    [Theory]
    [InlineData("BE_012")] // Dark Deacon
    [InlineData("BE_014")] // Spawn of C'Thun: "If C'Thun started in your deck"
    [InlineData("BE_204")] // Gargantuan Slitherer
    [InlineData("BE_726")] // Ritual of C'Thun
    public void A_short_name_means_the_card_of_the_texts_own_set(string cardId)
    {
        Assert.Equal(["BE_013"], NamedBy(cardId, Real.Value));
        Assert.Equal(["BE_013"], NamedBy(cardId, Reversed(Real.Value)));
    }

    /// <summary>The Old Gods cards keep the old C'Thun, each set its own printing: the same card, so no guess.</summary>
    [Theory]
    [InlineData("OG_281", "OG_280")] // Beckoner of Evil: "Give your C'Thun +2/+2"
    [InlineData("WON_131", "WON_135")] // Crazed Worshipper, the Wonders reprint
    [InlineData("CATA_725", "CATA_726")] // "Herald Cho'gall": Cho'gall, Mastermind of that set, not the Old Gods Cho'gall
    [InlineData("CATA_158", "CATA_154")] // "Herald Sinestra": the only Sinestra
    [InlineData("FP1_014", "FP1_015")] // Stalagg: "If Feugen also died this game"
    [InlineData("FP1_015", "FP1_014")]
    public void A_named_card_is_found_in_either_catalog_order(string cardId, string expected)
    {
        Assert.Equal([expected], NamedBy(cardId, Real.Value));
        Assert.Equal([expected], NamedBy(cardId, Reversed(Real.Value)));
    }

    /// <summary>
    /// Reprints of one card (same type, cost, stats and text) are one card: the printing of the text's own set, else the
    /// lowest id. The old code took whichever the catalog listed first.
    /// </summary>
    [Fact]
    public void A_reprinted_card_gives_the_same_row_in_either_catalog_order()
    {
        foreach (var catalog in new[] { Real.Value, Reversed(Real.Value) })
        {
            // Broll Bearmantle (Emerald Dream): Animal Companion is NEW1_031 and CORE_NEW1_031.
            Assert.Equal(["CORE_NEW1_031"], NamedBy("EDR_853", catalog));
            // Jaina's Gift (LEGACY): "a Temporary Frostbolt, Arcane Intellect, or Fireball", each also in CORE.
            Assert.Equal(["CS2_023", "CS2_024", "CS2_029"], NamedBy("GIFT_02", catalog));
        }
    }

    /// <summary>
    /// Two different cards of one name (Reincarnate: <c>FP1_025</c> any minion, <c>WON_335</c> a non-Legendary one) and
    /// a text that names it (the texts here are written for the test): only the one of the text's own set; from another
    /// set, or with two different ones in the set, nothing.
    /// </summary>
    [Fact]
    public void Different_cards_of_one_name_are_told_apart_by_the_texts_set_or_not_shown()
    {
        var naxx = Pool("TEST_1", "Naxx Caster", "Get a Reincarnate.") with { Set = "NAXX" };
        var other = Pool("TEST_2", "Other Caster", "Get a Reincarnate.") with { Set = "TGT" };
        var catalog = With(Real.Value, naxx, other);

        foreach (var c in new[] { catalog, Reversed(catalog) })
        {
            Assert.Equal(["FP1_025"], NamedBy("TEST_1", c));
            Assert.Empty(NamedBy("TEST_2", c));
        }

        var twoInTheSet = With(catalog, Pool("TEST_3", "Reincarnate", "Another card of the name.") with { Set = "NAXX" });
        Assert.Empty(NamedBy("TEST_1", twoInTheSet));
        Assert.Empty(NamedBy("TEST_1", Reversed(twoInTheSet)));
    }

    /// <summary>
    /// Review M1 (2026-11-16): Stickybomb Saboteur <c>CATA_186</c>, "Give your opponent a 2-Cost Sabotage", makes its
    /// own token <c>CATA_186t</c> (a 2-Cost spell in HearthstoneJSON); the only collectible Sabotage, <c>GVG_047</c>,
    /// costs 4 and is another card. The stated cost rules it out: the token alone is shown.
    /// </summary>
    [Fact]
    public void A_stated_cost_that_differs_rules_the_named_card_out()
    {
        Assert.Equal(4, Real.Value.Get("GVG_047")!.Cost);
        Assert.Empty(NamedBy("CATA_186", Real.Value));
        Assert.Empty(NamedBy("CATA_186", Reversed(Real.Value)));

        var cards = CardDb.Parse("enUS", new MemoryStream("""
            [
              {"id":"CATA_186","dbfId":1,"name":"Stickybomb Saboteur","type":"MINION","cost":4,"set":"CATACLYSM","collectible":true},
              {"id":"CATA_186t","dbfId":2,"name":"Sabotage","type":"SPELL","cost":2,"set":"CATACLYSM"},
              {"id":"GVG_047","dbfId":3,"name":"Sabotage","type":"SPELL","cost":4,"text":"Destroy a random enemy minion.","set":"GVG","collectible":true}
            ]
            """u8.ToArray()));
        Assert.Equal(["CATA_186t"], RelatedCards.Of("CATA_186", cards, Real.Value)!.Rows.Select(r => r.CardId));

        // The same name with the card's real cost, or with none stated, is that card.
        var catalog = With(Real.Value, Pool("TEST_4", "Four", "Get a 4-Cost Sabotage."), Pool("TEST_5", "Plain", "Get a Sabotage."));
        Assert.Equal(["GVG_047"], NamedBy("TEST_4", catalog));
        Assert.Equal(["GVG_047"], NamedBy("TEST_5", catalog));
    }

    /// <summary>
    /// Stated stats are the card's attack and, for a minion, its health; a weapon's second number is its durability,
    /// which the catalog does not have (Truesilver Champion is 4/0 there), so only its attack is compared.
    /// </summary>
    [Theory]
    [InlineData("DRG_232", "CS2_097")] // Lightforged Zealot: "equip a 4/2 Truesilver Champion"
    [InlineData("SW_313", "CS2_091")] // Rise to the Occasion: "Equip a 1/4 Light's Justice"
    [InlineData("ICC_833", "CS2_033")] // Frost Lich Jaina: "Summon a 3/6 Water Elemental"
    public void Stated_stats_that_fit_keep_the_named_card(string cardId, string expected)
    {
        Assert.Equal([expected], NamedBy(cardId, Real.Value));
        Assert.Equal([expected], NamedBy(cardId, Reversed(Real.Value)));
    }

    /// <summary>
    /// The stated stats come before the set (texts written for the test, the cards real). Deathwing is five cards:
    /// four 12/12 minions (<c>NEW1_030</c>, <c>OG_317</c>, <c>DRG_026</c>, <c>LEG_CS3_036</c>) and the Cataclysm hero
    /// <c>CATA_190h</c>. "A 12/12 Deathwing" in a Cataclysm text is not that set's hero, and which minion it is the data
    /// does not say; other stats are no Deathwing at all.
    /// </summary>
    [Fact]
    public void Stated_stats_that_differ_rule_a_card_out_before_its_set_counts()
    {
        var catalog = With(
            Real.Value,
            Pool("TEST_10", "Cata Summoner", "Summon a 12/12 Deathwing.") with { Set = "CATACLYSM" },
            Pool("TEST_11", "Classic Summoner", "Summon a 12/12 Deathwing.") with { Set = "EXPERT1" },
            Pool("TEST_12", "Small Summoner", "Summon a 4/4 Deathwing.") with { Set = "EXPERT1" },
            Pool("TEST_13", "Cata Herald", "Give your Deathwing +2 Attack.") with { Set = "CATACLYSM" },
            Pool("TEST_14", "Blunt Smith", "Equip a 5/2 Truesilver Champion."),
            Pool("TEST_15", "Sturdy Smith", "Equip a 4/9 Truesilver Champion."),
            Pool("TEST_16", "Odd Caster", "Get a 3/3 Sabotage."));

        foreach (var c in new[] { catalog, Reversed(catalog) })
        {
            Assert.Empty(NamedBy("TEST_10", c));
            Assert.Equal(["NEW1_030"], NamedBy("TEST_11", c));
            Assert.Empty(NamedBy("TEST_12", c));
            // Nothing stated: the set's own card, the reading docs/discover-pool.md calls a heuristic.
            Assert.Equal(["CATA_190h"], NamedBy("TEST_13", c));
            Assert.Empty(NamedBy("TEST_14", c));
            Assert.Equal(["CS2_097"], NamedBy("TEST_15", c));
            // Stats before a spell's name are not that spell.
            Assert.Empty(NamedBy("TEST_16", c));
        }
    }

    /// <summary>
    /// The second audit (2026-11-16, each named card checked against the card's own tokens in HearthstoneJSON): Dreaded
    /// Mount <c>SW_087</c>, "summon an endless Dreadsteed", makes its own <c>SW_087t</c> (a 4-Cost 1/1 minion of its
    /// set, with another name and text than the Grand Tournament Dreadsteed <c>AT_019</c>), and Jan'alai
    /// <c>TRL_316</c> its own Ragnaros <c>TRL_316t</c>. A named card of another set gives way to the card's own token
    /// of that type; without such a token the name stands. Token ids, types, costs and sets are HearthstoneJSON's.
    /// </summary>
    [Fact]
    public void A_named_card_of_another_set_gives_way_to_the_cards_own_token_of_that_type()
    {
        var cards = CardDb.Parse("enUS", new MemoryStream("""
            [
              {"id":"SW_087","dbfId":1,"name":"Dreaded Mount","type":"SPELL","cost":3,"set":"STORMWIND","collectible":true},
              {"id":"SW_087t","dbfId":71221,"name":"Own steed","type":"MINION","cost":4,"attack":1,"health":1,"set":"STORMWIND"},
              {"id":"AT_019","dbfId":2,"name":"Dreadsteed","type":"MINION","cost":4,"set":"TGT","collectible":true},
              {"id":"TRL_316","dbfId":3,"name":"Jan'alai, the Dragonhawk","type":"MINION","cost":7,"set":"TROLL","collectible":true},
              {"id":"TRL_316t","dbfId":53181,"name":"Own Ragnaros","type":"MINION","cost":8,"attack":8,"health":8,"set":"TROLL"},
              {"id":"EX1_298","dbfId":4,"name":"Ragnaros the Firelord","type":"MINION","cost":8,"set":"EXPERT1","collectible":true},
              {"id":"FP1_014","dbfId":5,"name":"Stalagg","type":"MINION","cost":5,"set":"NAXX","collectible":true},
              {"id":"FP1_014t","dbfId":6,"name":"Thaddius","type":"MINION","cost":10,"set":"NAXX"},
              {"id":"FP1_015","dbfId":7,"name":"Feugen","type":"MINION","cost":5,"set":"NAXX","collectible":true},
              {"id":"SW_313","dbfId":64308,"name":"Rise to the Occasion","type":"SPELL","cost":1,"set":"STORMWIND","collectible":true},
              {"id":"SW_313t","dbfId":8,"name":"Next stage","type":"SPELL","cost":1,"set":"STORMWIND"},
              {"id":"SW_313t4","dbfId":9,"name":"Quest reward","type":"MINION","cost":5,"set":"STORMWIND"},
              {"id":"CS2_091","dbfId":383,"name":"Light's Justice","type":"WEAPON","cost":1,"set":"LEGACY","collectible":true}
            ]
            """u8.ToArray()));
        string[] Related(string id, CardPoolCatalog catalog) => [.. RelatedCards.Of(id, cards, catalog)!.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal)];

        foreach (var catalog in new[] { Real.Value, Reversed(Real.Value) })
        {
            Assert.Equal(["SW_087t"], Related("SW_087", catalog));
            Assert.Equal(["TRL_316t"], Related("TRL_316", catalog));
            // A card of the text's own set stays beside the token: Stalagg names Feugen and makes Thaddius.
            Assert.Equal(["FP1_014t", "FP1_015"], Related("FP1_014", catalog));
            // Spell/minion tokens do not hide the explicitly named weapon (review m5).
            Assert.Equal(["CS2_091", "SW_313t", "SW_313t4"], Related("SW_313", catalog));
        }

        // With no token of that type in the card data the name is all there is.
        Assert.Equal(["AT_019"], NamedBy("SW_087", Real.Value));
        Assert.Equal(["EX1_298"], NamedBy("TRL_316", Real.Value));
    }

    /// <summary>Real normalized texts from the same 2026-11-15 catalog: replacement heroes/powers are not their collectible namesakes.</summary>
    [Theory]
    [InlineData("BRM_027")]
    [InlineData("GDB_846")]
    public void A_replacement_hero_or_power_is_not_a_collectible_minion_or_spell(string id)
    {
        foreach (var catalog in new[] { Real.Value, Reversed(Real.Value) })
        {
            Assert.Empty(NamedBy(id, catalog));
        }
    }

    [Fact]
    public void The_replacement_type_keeps_a_matching_hero_and_does_not_hide_an_unrelated_spell()
    {
        // Synthetic hero candidate and text, to distinguish a type restriction from suppressing the whole name.
        var catalog = With(Real.Value,
            Pool("TEST_HERO_RAG", "Ragnaros the Firelord", "Hero text.") with { Type = "HERO", Set = "BRM" },
            Pool("TEST_POWER_SOURCE", "Power teacher", "Your Hero Power adds a Tracking to your hand."));
        foreach (var c in new[] { catalog, Reversed(catalog) })
        {
            Assert.Equal(["TEST_HERO_RAG"], NamedBy("BRM_027", c));
            Assert.Equal(["DS1_184"], NamedBy("TEST_POWER_SOURCE", c));
        }
    }

    /// <summary>
    /// Review m1: a verb that opens a clause after a keyword's number or after "; or" is no card (texts written for the
    /// test; Duplicate <c>FP1_018</c> and Consume <c>SC_020</c> are real cards). A plain count still leads into a card.
    /// </summary>
    [Fact]
    public void A_verb_after_a_keywords_number_or_after_or_is_no_card()
    {
        var catalog = With(
            Real.Value,
            Pool("TEST_20", "Number", "Spell Damage +1 Duplicate your hand."),
            Pool("TEST_21", "Colossal", "Colossal +2 Consume a friendly minion."),
            Pool("TEST_22", "Choice", "Choose One - Summon two copies; or Duplicate your hand."),
            Pool("TEST_23", "Overloaded", "Overload: (2) Duplicate your hand."),
            Pool("TEST_24", "Count", "Get 2 Duplicate cards, or a Consume."));

        foreach (var c in new[] { catalog, Reversed(catalog) })
        {
            Assert.Empty(NamedBy("TEST_20", c));
            Assert.Empty(NamedBy("TEST_21", c));
            Assert.Empty(NamedBy("TEST_22", c));
            Assert.Empty(NamedBy("TEST_23", c));
            Assert.Equal(["FP1_018", "SC_020"], NamedBy("TEST_24", c));
        }
    }

    /// <summary>
    /// Review m2: a spell's "Infest the Scullery" (<c>JAIL_200</c>) is a phrase, not another Infest: in a text of its
    /// set "Infest" is still the one card of that name, <c>OG_045</c> (the text is written for the test).
    /// </summary>
    [Fact]
    public void A_spells_name_with_the_is_no_short_name()
    {
        var catalog = With(Real.Value, Pool("TEST_30", "Scullery Hand", "Your Infest costs (1) less.") with { Set = "ESCAPEFROM_VIOLET_HOLD" });

        Assert.Equal("SPELL", Real.Value.Get("JAIL_200")!.Type);
        Assert.Equal(["OG_045"], NamedBy("TEST_30", catalog));
        Assert.Equal(["OG_045"], NamedBy("TEST_30", Reversed(catalog)));
    }

    /// <summary>Real texts whose words are a card's name and no reference to it: nothing is shown, in either order.</summary>
    [Theory]
    [InlineData("BE_013")] // C'Thun, Eye of the Empire: "all 4 C'Thun abilities" is the card itself
    [InlineData("TID_712")] // Neptulon the Tidehunter: "Whenever Neptulon attacks" is itself, not GVG_042
    [InlineData("TOY_330")] // Zilliax Deluxe 3000 names itself, not BOT_548 Zilliax
    [InlineData("ONY_010")] // Dragonbane Shot: "Add a Dragonbane Shot" is not DRG_256 Dragonbane
    [InlineData("CATA_156")] // "Herald Onyxia": the set's Arisen Onyxia CATA_155 is no candidate (a known limit), EX1_562 and Onyxia the Broodmother are other sets' cards
    [InlineData("TTN_940")] // "Choose One - Duplicate your hand; or Summon copies": a verb, not FP1_018 Duplicate
    [InlineData("JAIL_384")] // "Start of Game: Duplicate all other Legendary cards"
    [InlineData("RLK_214")] // "Start of Game: Consume 3 different minions": not SC_020 Consume
    [InlineData("RLK_061")] // "a 1/3 Risen Footman" is a token, not TOY_102 Footman
    [InlineData("RLK_070")] // "a 2/2 Undead Peasant", not SW_319 Peasant
    [InlineData("TLC_631")] // "Reward: Gorishi Colossus", not SC_758 Colossus
    [InlineData("TRL_308")] // "Battlecry: Convert all but 1 of your Hero's Health": a verb, not AT_015 Convert
    [InlineData("ETC_320")] // "Tradeable Convert a friendly Divine Shield"
    [InlineData("CATA_489")] // "Shatter Deal 4 damage": the keyword, not OG_081 Shatter
    public void Words_that_only_look_like_a_card_name_give_no_related_card(string cardId)
    {
        Assert.NotNull(Real.Value.Get(cardId));
        Assert.Empty(NamedBy(cardId, Real.Value));
        Assert.Empty(NamedBy(cardId, Reversed(Real.Value)));
    }

    /// <summary>
    /// Both apps read related cards through <see cref="GameSession.RelatedCardsOf"/> (the hover list and
    /// <c>CardPreview.Related</c>): the screenshot's card gives the 2026 C'Thun there, and its row.
    /// </summary>
    [Fact]
    public void The_session_gives_both_apps_the_new_cthun_for_dark_deacon()
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Real.Value.Cards.Select(c =>
            new { id = c.Id, dbfId = c.DbfId, name = c.Name, text = c.Text, type = c.Type, cost = c.Cost, set = c.Set, rarity = c.Rarity, collectible = true }));
        using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { Cards = CardDb.Parse("enUS", new MemoryStream(json)) };
        session.SetCardPoolCatalog(Real.Value);

        var row = Assert.Single(session.RelatedCardsOf("BE_012"));
        Assert.Equal(("BE_013", "C'Thun, Eye of the Empire", 10), (row.CardId, row.Name, row.Cost));
    }

    /// <summary>A hero skin's id with a suffix is another skin (<c>HERO_01b</c> → <c>HERO_01ba</c>), not its token.</summary>
    [Fact]
    public void A_hero_skin_has_no_tokens()
    {
        var cards = CardDb.Parse("enUS", new MemoryStream("""
            [
              {"id":"HERO_01b","dbfId":1,"name":"Skin","type":"HERO","set":"HERO_SKINS","collectible":true},
              {"id":"HERO_01ba","dbfId":2,"name":"Another skin","type":"HERO","set":"HERO_SKINS"}
            ]
            """u8.ToArray()));

        Assert.Null(RelatedCards.Of("HERO_01b", cards, CardPoolCatalog.Empty));
    }

    /// <summary>
    /// TASK-2026-11-09 AC7: a card that looks back at this game shows that list and, beside it, its related cards
    /// (N'Zoth's four abilities, not the enchantment); a card without a rule has its related cards as its list.
    /// </summary>
    [Fact]
    public void The_this_game_list_and_related_cards_show_together()
    {
        var (game, tracker) = Play([[.. Minion(40, "PLAIN", 1), .. Minion(41, "PLAIN", 1)]]);
        var rule = new MatchRule(MatchEvent.Summoned, MatchSide.Friendly);
        MatchListView? Related(string id) => RelatedCards.Of(id, Cards, Catalog);

        var (list, related) = GameSession.HoverList("BE_017", rule, game, tracker.Counters, Cards, Related);
        Assert.Equal(MatchLists.Title(rule), list!.Title);
        Assert.Equal(Loc.T("Related.Title"), related!.Title);
        Assert.Equal(["BE_017t3", "BE_017t4", "BE_017t5", "BE_017t6"], related.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
        Assert.Equal(4, related.Total);

        var (withoutRule, none) = GameSession.HoverList("GEN_1", null, game, tracker.Counters, Cards, Related);
        Assert.Equal(Loc.T("Related.Title"), withoutRule!.Title);
        Assert.Null(none);

        // A rule and no related cards: the rule's list alone.
        var (onlyRule, noRelated) = GameSession.HoverList("PLAIN", rule, game, tracker.Counters, Cards, Related);
        Assert.Equal(MatchLists.Title(rule), onlyRule!.Title);
        Assert.Null(noRelated);
    }

    /// <summary>AC8: Choose One options with capital suffixes and a Core_ reprint in another case are tokens too.</summary>
    [Theory]
    [InlineData("BE_017", "BE_017t3 BE_017t4 BE_017t5 BE_017t6")]
    [InlineData("EDR_872", "EDR_872A EDR_872B")]
    [InlineData("LOE_115", "LOE_115a LOE_115b")]
    [InlineData("Core_LOE_115", "LOE_115a LOE_115b")]
    [InlineData("CORE_GEN_1", "GEN_1t GEN_1t2")]
    public void Each_token_id_scheme_gives_the_related_cards(string cardId, string expected)
    {
        var list = RelatedCards.Of(cardId, Cards, Catalog)!;

        Assert.Equal(expected.Split(' '), list.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Coordinator 2026-11-09 (design.md question 4): related cards with the same name and the same text are one row
    /// (C'Thun's two identical Eyes); the same name with another text, or another name with the same text, stay apart.
    /// </summary>
    [Fact]
    public void Related_cards_with_the_same_name_and_text_are_one_row()
    {
        var list = RelatedCards.Of("BE_013", Cards, Catalog)!;

        Assert.Equal(["BE_013t3", "BE_013t4", "BE_013t5"], list.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
        Assert.Equal(3, list.Total);
        Assert.All(list.Rows, r => Assert.Equal(1, r.Count));
    }

    /// <summary>AC9: more than 12 is no list; no catalog still gives the tokens; no card data gives nothing.</summary>
    [Fact]
    public void Too_many_no_catalog_and_no_card_data()
    {
        Assert.Null(RelatedCards.Of("BIG_1", Cards, Catalog));
        Assert.Equal(["GEN_1t", "GEN_1t2"], RelatedCards.Of("GEN_1", Cards, CardPoolCatalog.Empty)!.Rows.Select(r => r.CardId).Order(StringComparer.Ordinal));
        // The named Fireball needs the catalog's English text.
        Assert.Null(RelatedCards.Of("SHUF_1", Cards, CardPoolCatalog.Empty));
        Assert.Null(RelatedCards.Of("BE_017", CardDb.Empty, Catalog));
    }

    /// <summary>
    /// AC8/AC10: the session gives any card's related cards by id alone (no game state), for card previews; empty for a
    /// card without them or with more than 12.
    /// </summary>
    [Fact]
    public void The_session_gives_any_cards_related_cards_by_id_alone()
    {
        using var memory = new HearthstoneMemory();
        var session = new GameSession(HistoryStore.InMemory(), memory) { Cards = Cards };
        session.SetCardPoolCatalog(Catalog);

        Assert.Equal(["BE_017t3", "BE_017t4", "BE_017t5", "BE_017t6"], session.RelatedCardsOf("BE_017").Select(r => r.CardId).Order(StringComparer.Ordinal));
        Assert.Equal(["Ability Five", "Ability Four", "Ability Six", "Ability Three"], session.RelatedCardsOf("BE_017").Select(r => r.Name));
        Assert.Equal(["FIRE_1"], session.RelatedCardsOf("SHUF_1").Select(r => r.CardId));
        Assert.Empty(session.RelatedCardsOf("BIG_1"));
        Assert.Empty(session.RelatedCardsOf("PLAIN"));
        Assert.Empty(session.RelatedCardsOf(""));
        Assert.DoesNotContain(typeof(GameSession).GetMethod(nameof(GameSession.RelatedCardsOf))!.GetParameters(), p => p.ParameterType == typeof(GameState));

        // Other card data (a language switch loads it): the cache is built again from it.
        session.Cards = CardDb.Empty;
        Assert.Empty(session.RelatedCardsOf("BE_017"));
    }

    /// <summary>AC4: a list only for a card in your own hand; the opponent's hand and the board get none.</summary>
    [Fact]
    public void Related_cards_show_only_for_a_card_in_your_own_hand()
    {
        var (game, _) = Play([[.. Card(41, "GEN_1", "HAND", 1, "SPELL"), .. Card(60, "SHUF_1", "HAND", 2, "MINION"), .. Minion(50, "PLAIN", 1)]]);
        var hover = new CardPoolTracker();

        hover.SetHovered("GEN_1", DateTime.UtcNow);
        Assert.Equal("GEN_1", hover.HoveredInHand(game));

        // The opponent's Shuffler has related cards, but it is not in your hand.
        Assert.NotNull(RelatedCards.Of("SHUF_1", Cards, Catalog));
        foreach (var notMine in new[] { "SHUF_1", "PLAIN" })
        {
            hover.SetHovered(notMine, DateTime.UtcNow);
            Assert.Null(hover.HoveredInHand(game));
        }
    }

    /// <summary>AC4: a minion that never stood on the board (from the deck or set aside straight to the graveyard) is no death.</summary>
    [Fact]
    public void A_minion_that_goes_to_the_graveyard_from_the_deck_or_set_aside_is_not_listed()
    {
        var (game, tracker) = Play(
        [
            [.. Card(60, "PLAIN", "DECK", 2, "MINION"), .. Card(61, "SHUF_1", "SETASIDE", 2, "MINION"), .. Minion(62, "PLAIN", 2)],
            ["DebugPrintPower() - TAG_CHANGE Entity=[entityName=X id=60 zone=DECK zonePos=0 cardId=PLAIN player=2] tag=ZONE value=GRAVEYARD "],
            ["DebugPrintPower() - TAG_CHANGE Entity=[entityName=X id=61 zone=SETASIDE zonePos=0 cardId=SHUF_1 player=2] tag=ZONE value=GRAVEYARD "],
        ]);
        Assert.Null(GraveyardView.Of(game, tracker.Counters, Cards));

        var (died, diedTracker) = Play([[.. Minion(62, "PLAIN", 2)], Die(62, "PLAIN", 2)]);
        Assert.Equal([("PLAIN", 1)], GraveyardView.Of(died, diedTracker.Counters, Cards)!.Opponent.Select(r => (r.CardId, r.Count)));
    }

    /// <summary>m3: a dead minion missing from the card data shows as "Unknown card", never its id.</summary>
    [Fact]
    public void An_unknown_dead_minion_is_named_unknown_card()
    {
        var row = Assert.Single(GraveyardView.Rows(["NOT_IN_DB_1"], Cards));
        Assert.Equal(Loc.T("Related.Unknown"), row.Name);
        Assert.NotEqual("Related.Unknown", row.Name);
        Assert.Equal("Неизвестная карта", Loc.Table(Loc.Russian)["Related.Unknown"]);
    }

    /// <summary>m4: the view is built again only when a side's death count changes.</summary>
    [Fact]
    public void The_graveyard_view_is_rebuilt_only_on_a_new_death()
    {
        var (game, tracker) = Play([[.. Minion(40, "PLAIN", 1), .. Minion(41, "SHUF_1", 2)], Die(40, "PLAIN", 1)]);
        var cache = new GraveyardView.Cache();

        var first = cache.Of(game, tracker.Counters, Cards);
        Assert.Same(first, cache.Of(game, tracker.Counters, Cards));

        tracker.Counters.For(2).Died.Add("SHUF_1");
        var second = cache.Of(game, tracker.Counters, Cards)!;
        Assert.NotSame(first, second);
        Assert.Equal(1, second.OpponentTotal);
    }

    [Fact]
    public void The_graveyard_lists_each_sides_dead_minions_and_nothing_else()
    {
        var (before, beforeTracker) = Play([[.. Minion(40, "PLAIN", 1)]]);
        Assert.Null(GraveyardView.Of(before, beforeTracker.Counters, Cards));

        var (game, tracker) = Play(
        [
            [.. Minion(40, "PLAIN", 1), .. Minion(41, "PLAIN", 1), .. Minion(42, "SHUF_1", 1), .. Minion(50, "SHUF_1", 2)],
            // A card in the opponent's hand that goes to the graveyard (discarded) is no death on the board.
            [.. Card(60, "PLAIN", "HAND", 2, "MINION")],
            [.. Die(40, "PLAIN", 1), .. Die(41, "PLAIN", 1), .. Die(50, "SHUF_1", 2)],
            ["DebugPrintPower() - TAG_CHANGE Entity=[entityName=X id=60 zone=HAND zonePos=1 cardId=PLAIN player=2] tag=ZONE value=GRAVEYARD "],
        ]);

        var view = GraveyardView.Of(game, tracker.Counters, Cards)!;

        Assert.Equal(2, view.LocalTotal);
        Assert.Equal([("PLAIN", 2)], view.Local.Select(r => (r.CardId, r.Count)));
        Assert.Equal(1, view.OpponentTotal);
        Assert.Equal([("SHUF_1", 1)], view.Opponent.Select(r => (r.CardId, r.Count)));
    }
}

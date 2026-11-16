using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;

namespace Hearthpulse.Tests;

/// <summary>
/// The card page's model (<see cref="CardPages"/>, docs/card-page.md) on a hand-made meta and history, every number
/// worked out in the test's comments: the archetypes that run the card, its mulligan by archetype, the player's own
/// games, and what is left out when the source has nothing.
/// </summary>
public sealed class CardPageTests
{
    private const string CardsJson = """
        [
          {"id": "HERO_08", "dbfId": 637, "name": "Jaina Proudmoore", "type": "HERO", "cardClass": "MAGE", "set": "HERO_SKINS"},
          {"id": "HERO_09", "dbfId": 813, "name": "Anduin Wrynn", "type": "HERO", "cardClass": "PRIEST", "set": "HERO_SKINS"},
          {"id": "T_BOLT", "dbfId": 1001, "name": "Test Bolt", "cost": 2, "type": "SPELL", "cardClass": "MAGE", "set": "EMERALD_DREAM",
           "text": "[x]<b>Discover</b> a spell.\nDeal $3 damage.", "collectible": true},
          {"id": "T_FILL", "dbfId": 1002, "name": "Test Filler", "cost": 1, "type": "MINION", "cardClass": "NEUTRAL", "set": "CORE", "collectible": true},
          {"id": "CORE_T_BOLT", "dbfId": 1003, "name": "Test Bolt", "cost": 2, "type": "SPELL", "cardClass": "MAGE", "set": "CORE", "collectible": true}
        ]
        """;

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(CardsJson)));

    private static Card Bolt => Cards.Get("T_BOLT")!;

    [Fact]
    public void Duplicate_archetype_ids_do_not_close_the_card_page()
    {
        var meta = Meta();
        var duplicate = new ConstructedMeta("standard", "legend", "past_week", DateTime.UtcNow, 1000,
            [.. meta.Archetypes, meta.Archetypes[0]], meta.Decks);
        var rows = CardPages.Archetypes(Bolt, duplicate);
        Assert.Equal(CardPages.Archetypes(Bolt, meta).Count, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Archetypes_are_the_ones_whose_lists_run_the_card_with_the_share_of_lists_and_copies()
    {
        var meta = Meta();

        var rows = CardPages.Archetypes(Bolt, meta);

        // Tempo Mage: three lists, two run the card (2 copies and 1 copy) -> 2 of 3, 1.5 copies on average.
        // Big Mage: one list, and it runs 1 copy -> 1 of 1. Control Priest: its list has no copy -> not listed.
        // The list that cannot be decoded is counted on neither side.
        Assert.Equal(["Big Mage", "Tempo Mage"], rows.Select(r => r.Name));
        var big = rows[0];
        Assert.Equal((1, 1), (big.Lists, big.ListsWithCard));
        Assert.Equal(1.0, big.ListShare, 6);
        Assert.Equal(1.0, big.AverageCopies, 6);
        var tempo = rows[1];
        Assert.Equal((3, 2), (tempo.Lists, tempo.ListsWithCard));
        Assert.Equal(2.0 / 3, tempo.ListShare, 6);
        Assert.Equal(1.5, tempo.AverageCopies, 6);
        // The archetype's own win rate and games, as the meta has them.
        Assert.Equal(0.55, tempo.WinRate, 6);
        Assert.Equal(1000, tempo.Games);
        Assert.Equal("MAGE", tempo.Class);
    }

    [Fact]
    public void Mulligan_rows_carry_the_keep_rate_the_impact_against_the_same_games_and_the_sample()
    {
        var rows = CardPages.Mulligan(Bolt, Meta(), Cards);

        // Tempo Mage: in hand 60 % against the mulligan baseline 52 % -> +8 points (not against its 55 % win rate).
        // Big Mage has no baseline of its own: 48 % in hand against its win rate 50 % -> -2 points.
        // Mirror Mage shares Tempo Mage's mulligan numbers (the same archetype id there): one row, the most played.
        // The largest sample first: 400 hands, then 200.
        Assert.Equal(["Big Mage", "Tempo Mage"], rows.Select(r => r.Name));
        Assert.Equal(0.30, rows[0].KeepRate, 6);
        Assert.Equal(0.48, rows[0].InHandWinRate, 6);
        Assert.Equal(-0.02, rows[0].Impact, 6);
        Assert.Equal(400, rows[0].InHand);
        Assert.Equal(0.80, rows[1].KeepRate, 6);
        Assert.Equal(0.08, rows[1].Impact, 6);
        Assert.Equal(200, rows[1].InHand);
    }

    [Fact]
    public void Another_printing_named_in_the_mulligan_numbers_is_the_same_card()
    {
        // The meta names the Core printing; the page was opened for a card with the same dbf id.
        var reprint = new Card { Id = "T_BOLT_ALT", DbfId = 1003, Name = "Test Bolt" };
        var meta = new ConstructedMeta("standard", "legend", "past_week", DateTime.MinValue, 100,
            [Archetype(1, "Tempo Mage", "MAGE", 100, 0.5, [new MetaMulliganCard("CORE_T_BOLT", 50, 0.5, 40, 0.6)])], []);

        var row = Assert.Single(CardPages.Mulligan(reprint, meta, Cards));

        Assert.Equal(0.10, row.Impact, 6);
    }

    [Fact]
    public void My_numbers_count_only_my_own_games_of_the_format_per_game()
    {
        var games = new[]
        {
            // Offered twice and one copy kept, drawn and played: a win.
            Game("a", "Win", offered: ["T_BOLT", "T_BOLT", "T_FILL"], replaced: ["T_BOLT"], drawn: ["T_BOLT", "T_BOLT"], played: ["T_BOLT", "T_BOLT"]),
            // Offered and sent back, drawn later, never played: a loss.
            Game("b", "Loss", offered: ["T_BOLT"], replaced: ["T_BOLT"], drawn: ["T_FILL", "T_BOLT"], played: ["T_FILL"]),
            // Card data, but the card never showed up: a win.
            Game("c", "Win", offered: ["T_FILL"], replaced: [], drawn: ["T_FILL"], played: ["T_FILL"]),
            // A game saved before card data: counted as a game, not as a tracked one.
            new GameRecord { Key = "d", Mode = "Casual", Format = "FT_STANDARD", Result = "Win" },
            // Not this page's games: Wild, Arena, an abandoned game.
            Game("e", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]) with { Format = "FT_WILD" },
            Game("f", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]) with { Mode = "Arena" },
            Game("g", GameRecord.AbandonedResult, ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]),
        };

        var mine = CardPages.Mine(Bolt, "FT_STANDARD", games);

        Assert.Equal(4, mine.Games);
        Assert.Equal(3, mine.TrackedGames);
        // Drawn in a (win) and b (loss): two games, though a drew two copies.
        Assert.Equal((2, 1, 1), (mine.WhenDrawn.Games, mine.WhenDrawn.Wins, mine.WhenDrawn.Losses));
        // Played in a only.
        Assert.Equal((1, 1, 0), (mine.WhenPlayed.Games, mine.WhenPlayed.Wins, mine.WhenPlayed.Losses));
        // Offered in a and b, a copy kept in a.
        Assert.Equal((2, 1), (mine.Offered, mine.Kept));

        // The Wild page counts its one game.
        var wild = CardPages.Mine(Bolt, "FT_WILD", games);
        Assert.Equal((1, 1, 1), (wild.Games, wild.WhenDrawn.Games, wild.WhenPlayed.Games));
    }

    [Fact]
    public void The_page_joins_the_card_its_numbers_the_meta_and_my_games()
    {
        var stats = Slice("standard", new CardStat("T_BOLT", 1001, 0.2, 0.55, 1.9, 5000, 0.56, 0.54, 0.7, 0.57, 3.2));
        var games = new[] { Game("a", "Win", ["T_BOLT"], [], ["T_BOLT"], ["T_BOLT"]) };

        var page = CardPages.Build(Bolt, "FT_STANDARD", stats, Meta(), games, Cards);

        Assert.Same(Bolt, page.Card);
        Assert.Equal("Discover a spell. Deal 3 damage.", page.Text);
        Assert.True(page.StatsLoaded);
        Assert.Equal(5000, page.Stats!.TimesPlayed);
        Assert.Equal(("GOLD", "LAST_14_DAYS"), (page.RankRange, page.TimeRange));
        Assert.Equal(new DateTime(2026, 11, 16, 6, 4, 0, DateTimeKind.Utc), page.StatsDate);
        Assert.Equal(new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), page.MetaDate);
        Assert.Equal(2, page.Archetypes.Count);
        Assert.Equal(2, page.Mulligan.Count);
        Assert.Equal(1, page.Mine.WhenPlayed.Games);
    }

    [Fact]
    public void A_card_the_list_does_not_have_and_a_list_that_was_not_loaded_are_told_apart()
    {
        var other = Slice("standard", new CardStat("T_FILL", 1002, 0.1, 0.5, 2, 10, 0.5, 0.5, 0.5, 0.5, 1));

        var missing = CardPages.Build(Bolt, "FT_STANDARD", other, null, [], Cards);
        var notLoaded = CardPages.Build(Bolt, "FT_STANDARD", null, null, [], Cards);

        Assert.Null(missing.Stats);
        Assert.True(missing.StatsLoaded);
        Assert.NotNull(missing.StatsDate);
        Assert.Null(notLoaded.Stats);
        Assert.False(notLoaded.StatsLoaded);
        Assert.Null(notLoaded.StatsDate);
        // No meta: no rows and no date, nothing made up.
        Assert.Empty(missing.Archetypes);
        Assert.Empty(missing.Mulligan);
        Assert.Null(missing.MetaDate);
        Assert.Equal(0, missing.Mine.Games);
    }

    [Fact]
    public void Numbers_and_meta_of_the_other_format_are_not_shown()
    {
        var wildStats = Slice("wild", new CardStat("T_BOLT", 1001, 0.2, 0.55, 1.9, 5000, 0.56, 0.54, 0.7, 0.57, 3.2));

        // The Standard page with the Wild list and (below) the Wild page with the Standard meta.
        var standard = CardPages.Build(Bolt, "FT_STANDARD", wildStats, Meta(), [], Cards);
        var wild = CardPages.Build(Bolt, "FT_WILD", wildStats, Meta(), [], Cards);

        Assert.Null(standard.Stats);
        Assert.False(standard.StatsLoaded);
        Assert.NotEmpty(standard.Archetypes);
        Assert.NotNull(wild.Stats);
        Assert.Empty(wild.Archetypes);
        Assert.Empty(wild.Mulligan);
        Assert.Null(wild.MetaDate);
    }

    [Theory]
    [InlineData("[x]<b>Battlecry:</b> Deal $2 damage.\nRestore #3_Health.", "enUS", "Battlecry: Deal 2 damage. Restore 3 Health.")]
    [InlineData("Draw 1 |4(card,cards). Draw 3 |4(card,cards).", "enUS", "Draw 1 card. Draw 3 cards.")]
    [InlineData("Возьмите 1 |4(карту,карты,карт), 3 |4(карту,карты,карт), 5 |4(карту,карты,карт), 11 |4(карту,карты,карт), 21 |4(карту,карты,карт).", "ruRU",
        "Возьмите 1 карту, 3 карты, 5 карт, 11 карт, 21 карту.")]
    [InlineData("Play 3 cards. <i>(@ left!)</i>", "enUS", "Play 3 cards.")]
    [InlineData("[x]<b>Боевой клич:</b>&\n заклинания противника\nна следующем ходу\nстоят на (1) больше.", "ruRU", "Боевой клич: заклинания противника на следующем ходу стоят на (1) больше.")]
    [InlineData("{0} Deal 2 damage. {1}", "enUS", "Deal 2 damage.")]
    [InlineData("", "enUS", "")]
    [InlineData(null, "enUS", "")]
    [InlineData("Deal {0} damage.", "enUS", "Deal ? damage.")]
    [InlineData("Deal @ damage.", "enUS", "Deal ? damage.")]
    public void Card_text_is_plain(string? text, string locale, string expected) => Assert.Equal(expected, CardPages.PlainText(text, locale));

    /// <summary>
    /// Standard, updated 15 Nov 12:00 UTC. Tempo Mage (id 1, 1000 games, 55 %, mulligan baseline 52 %) with three lists
    /// and one that cannot be decoded; Big Mage (id 2, 400 games, 50 %) with one; Mirror Mage (id 3, 300 games) sharing
    /// Tempo Mage's mulligan numbers, no lists; Control Priest (id 4) whose list has no copy of the card.
    /// </summary>
    private static ConstructedMeta Meta()
    {
        MetaMulliganCard[] tempoMulligan = [new("T_BOLT", 100, 0.80, 200, 0.60), new("T_FILL", 100, 0.5, 200, 0.5)];
        return new ConstructedMeta("standard", "legend", "past_week", new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), 2000,
            [
                Archetype(1, "Tempo Mage", "MAGE", 1000, 0.55, tempoMulligan) with { MulliganBaseline = 0.52, MulliganArchetypeId = 70 },
                Archetype(2, "Big Mage", "MAGE", 400, 0.50, [new MetaMulliganCard("T_BOLT", 500, 0.30, 400, 0.48)]),
                Archetype(3, "Mirror Mage", "MAGE", 300, 0.51, tempoMulligan) with { MulliganBaseline = 0.52, MulliganArchetypeId = 70 },
                Archetype(4, "Control Priest", "PRIEST", 300, 0.49, []),
            ],
            [
                Deck(1, "MAGE", ("T_BOLT", 2), ("T_FILL", 2)),
                Deck(1, "MAGE", ("T_BOLT", 1), ("T_FILL", 2)),
                Deck(1, "MAGE", ("T_FILL", 2)),
                new MetaDeck("not a deck code", 1, "Tempo Mage", "MAGE", 100, 50, 0.5, default),
                Deck(2, "MAGE", ("T_BOLT", 1)),
                Deck(4, "PRIEST", ("T_FILL", 2)),
            ]);
    }

    private static MetaArchetype Archetype(int id, string name, string heroClass, int games, double winRate, IReadOnlyList<MetaMulliganCard> mulligan) =>
        new(id, name, name, heroClass, games, (int)(games * winRate), winRate, 0, 0, 0, 0, 0, [], mulligan);

    private static MetaDeck Deck(int archetype, string heroClass, params (string Id, int Copies)[] cards)
    {
        var code = DeckString.Encode(cards.ToDictionary(c => c.Id, c => c.Copies), heroClass, "FT_STANDARD", Cards);
        Assert.NotNull(code);
        return new MetaDeck(code, archetype, string.Empty, heroClass, 100, 50, 0.5, default);
    }

    private static CardStatsSlice Slice(string format, CardStat stat) => new(format, "GOLD", "LAST_14_DAYS",
        new DateTime(2026, 11, 16, 6, 4, 0, DateTimeKind.Utc), new Dictionary<string, CardStat>(StringComparer.Ordinal) { [stat.CardId] = stat });

    private static GameRecord Game(string key, string result, string[] offered, string[] replaced, string[] drawn, string[] played) => new()
    {
        Key = key,
        Mode = "Ranked",
        Format = "FT_STANDARD",
        Result = result,
        Mulligan = GameCards.MulliganJson(new MulliganFacts(offered, replaced, [])),
        CardsDrawn = GameCards.TurnsJson(drawn.Select((id, i) => new CardTurn(id, i)).ToList()),
        CardsPlayed = GameCards.TurnsJson(played.Select((id, i) => new CardTurn(id, i + 1)).ToList()),
    };
}

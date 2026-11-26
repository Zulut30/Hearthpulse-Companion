using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The own deck's archetype in the history row (<c>deck_archetype</c>): the English HSGuru key the site compares every
/// player's games by, whatever the interface language, and only where it belongs: a Ranked or Casual game, the stats of
/// the game's format, a deck of the game's class. Synthetic Power.log lines in the grammar of the October 2026 logs.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class OwnArchetypeTests
{
    private const int Me = 5;
    private const int Them = 6;

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"HERO_11","dbfId":78065,"name":"The Lich King","type":"HERO","cardClass":"DEATHKNIGHT"},
          {"id":"HERO_08","dbfId":637,"name":"Jaina","type":"HERO","cardClass":"MAGE"},
          {"id":"CARD_A","dbfId":1001,"name":"Alpha","type":"SPELL","cost":1,"cardClass":"DEATHKNIGHT"},
          {"id":"CARD_B","dbfId":1002,"name":"Bravo","type":"MINION","cost":2,"cardClass":"DEATHKNIGHT"},
          {"id":"CARD_C","dbfId":1003,"name":"Charlie","type":"MINION","cost":3,"cardClass":"DEATHKNIGHT"},
          {"id":"CARD_D","dbfId":1004,"name":"Delta","type":"MINION","cost":4,"cardClass":"DEATHKNIGHT"},
          {"id":"NEUTRAL_1","dbfId":3001,"name":"One","type":"MINION","cost":1,"cardClass":"NEUTRAL"},
          {"id":"NEUTRAL_2","dbfId":3002,"name":"Two","type":"MINION","cost":2,"cardClass":"NEUTRAL"}
        ]
        """u8.ToArray()));

    private static DeckDefinition Deck(Dictionary<string, int> cards, string name = "Blood DK", long id = 42) =>
        DeckString.TryDecode(DeckString.Encode(cards, "DEATHKNIGHT", "Standard", Cards)!, name, id)!;

    /// <summary>A Blood Death Knight list with two neutral cards (the core of <see cref="Meta"/>'s Mage archetype).</summary>
    private static DeckDefinition BloodDeck() =>
        Deck(new() { ["CARD_A"] = 2, ["CARD_B"] = 2, ["CARD_C"] = 1, ["NEUTRAL_1"] = 2, ["NEUTRAL_2"] = 2 });

    /// <summary>
    /// Community stats as the Russian interface loads them (<c>ApiConstructedMeta</c>): the shown name is Russian, the
    /// key stays HSGuru's English name.
    /// </summary>
    private static ConstructedMeta Meta(string format = "standard") => new(format, "all", "past_week", DateTime.UtcNow, 10_000,
    [
        new MetaArchetype(1, "Blood Death Knight", "Рыцарь смерти Крови", "DEATHKNIGHT", 1000, 520, 0.52, 0.05, 0, 0, 0, 0, ["CARD_A", "CARD_B", "CARD_C"], []),
        new MetaArchetype(2, "Frost Death Knight", "Рыцарь смерти Льда", "DEATHKNIGHT", 900, 450, 0.5, 0.04, 0, 0, 0, 0, ["CARD_D"], []),
        new MetaArchetype(3, "Curve Mage", "Маг на кривой", "MAGE", 800, 400, 0.5, 0.04, 0, 0, 0, 0, ["NEUTRAL_1", "NEUTRAL_2"], []),
    ], []);

    /// <summary>A finished game of the local player (<paramref name="hero"/>) against a Mage, as the log tells it.</summary>
    private static GameRecord Played(DeckDefinition? deck, ConstructedMeta? meta, string gameType = "GT_RANKED", string format = "FT_STANDARD", string hero = "HERO_11")
    {
        var parser = new PowerParser(new DateOnly(2026, 11, 26));
        var battlegrounds = new BattlegroundsTracker { Cards = Cards };
        parser.AddListener(battlegrounds);
        string[] lines =
        [
            "DebugPrintPower() - CREATE_GAME",
            "DebugPrintPower() -     GameEntity EntityID=1",
            "DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "DebugPrintPower() -         tag=TURN value=7",
            $"DebugPrintPower() -     Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Me}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=30",
            $"DebugPrintPower() -     Player EntityID=3 PlayerID={Them} GameAccountId=[hi=144115198130930503 lo=12]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Them}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=40",
            $"DebugPrintGame() - GameType={gameType}",
            $"DebugPrintGame() - FormatType={format}",
            $"DebugPrintGame() - PlayerID={Me}, PlayerName=Me#1234",
            $"DebugPrintGame() - PlayerID={Them}, PlayerName=You#5678",
            $"DebugPrintPower() - FULL_ENTITY - Creating ID=30 CardID={hero}",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            $"DebugPrintPower() -     tag=CONTROLLER value={Me}",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=40 CardID=HERO_08",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            $"DebugPrintPower() -     tag=CONTROLLER value={Them}",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            // A card in hand the player sees: the log's way of naming the local player.
            "DebugPrintPower() - FULL_ENTITY - Creating ID=60 CardID=NEUTRAL_1",
            "DebugPrintPower() -     tag=ZONE value=HAND",
            $"DebugPrintPower() -     tag=CONTROLLER value={Me}",
            "DebugPrintPower() -     tag=CARDTYPE value=MINION",
            "DebugPrintPower() - TAG_CHANGE Entity=Me#1234 tag=PLAYSTATE value=WON ",
        ];
        foreach (var line in lines)
        {
            parser.Feed("D 20:00:00.0000000 GameState." + line);
        }

        parser.Flush();
        var record = GameRecords.Build(parser.Game!, battlegrounds, Cards, deck, "live", meta: meta);
        Assert.NotNull(record);
        return record;
    }

    [Fact]
    public void A_ranked_or_casual_game_saves_the_english_key_under_the_russian_interface()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            var ranked = Played(BloodDeck(), Meta());
            var casual = Played(BloodDeck(), Meta(), gameType: "GT_CASUAL");

            Assert.Equal("Blood Death Knight", ranked.DeckArchetype);
            Assert.Equal("Blood Death Knight", casual.DeckArchetype);

            // The history keeps it in every read: lists (My Stats), the profile upload's pages and the full row.
            var history = HistoryStore.InMemory();
            Assert.True(history.Add(ranked));
            Assert.Equal("Blood Death Knight", Assert.Single(history.Recent()).DeckArchetype);
            Assert.Equal("Blood Death Knight", Assert.Single(history.PageForUpload(0, 10)).DeckArchetype);
            Assert.Equal("Blood Death Knight", history.Game(ranked.Key)!.DeckArchetype);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    [Fact]
    public void Stats_of_another_format_name_no_archetype()
    {
        // Wild stats do not place a Standard game, nor Standard stats a Wild one; Twist has no stats at all.
        Assert.Null(Played(BloodDeck(), Meta("wild")).DeckArchetype);
        Assert.Null(Played(BloodDeck(), Meta(), format: "FT_WILD").DeckArchetype);
        Assert.Null(Played(BloodDeck(), Meta(), format: "FT_TWIST").DeckArchetype);
        Assert.Equal("Blood Death Knight", Played(BloodDeck(), Meta("wild"), format: "FT_WILD").DeckArchetype);
    }

    [Fact]
    public void A_deck_of_another_class_names_no_archetype()
    {
        // The Death Knight deck is still held from an earlier game while this one is played as a Mage. Its neutral cards
        // are a Mage archetype's core, but it is not this game's deck.
        var record = Played(BloodDeck(), Meta(), hero: "HERO_08");

        Assert.Equal("MAGE", record.PlayerClass);
        Assert.Null(record.DeckId);
        Assert.Null(record.DeckArchetype);
    }

    [Theory]
    [InlineData("GT_VS_FRIEND")]
    [InlineData("GT_VS_AI")]
    [InlineData("GT_ARENA")]
    [InlineData("GT_TAVERNBRAWL")]
    public void Other_modes_name_no_archetype(string gameType) =>
        Assert.Null(Played(BloodDeck(), Meta(), gameType: gameType).DeckArchetype);

    [Fact]
    public void Without_a_deck_stats_or_a_match_there_is_no_archetype()
    {
        Assert.Null(Played(null, Meta()).DeckArchetype);
        Assert.Null(Played(BloodDeck(), null).DeckArchetype);

        // The archetype whose core cards the list holds is the one named; a list holding too few of any core, none.
        Assert.Equal("Frost Death Knight", Played(Deck(new() { ["CARD_A"] = 2, ["CARD_D"] = 2 }, "Frost DK", 43), Meta()).DeckArchetype);
        Assert.Null(Played(Deck(new() { ["CARD_A"] = 2 }, "Alpha", 44), Meta()).DeckArchetype);
    }
}

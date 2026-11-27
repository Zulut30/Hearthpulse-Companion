using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests.OwnStats;

/// <summary>
/// AC11 of TASK-2026-11-26-pro-collection (B1): the stats pool names a record's archetypes as the app does. The pool's
/// dictionary (<c>server/pool/data/archetypes.json</c>) loaded as a meta; for every agreement case
/// (<c>server/pool/test/fixtures/archetype-cases.json</c>) the own archetype <see cref="GameRecords.Build"/> names for
/// the game the record tells, and <see cref="ArchetypeGuess.FromSeenCards"/> for the cards it shows, are the answers the
/// pool's deriver gave it (<c>server/pool/test/golden/archetypes-agreement.json</c>, from
/// <c>derive-archetypes.test.mjs</c>); and the dictionary's builder finds the classes, order, core cards and hero classes
/// <see cref="ApiConstructedMeta"/> and <see cref="CardDb"/> find.
/// </summary>
public sealed class ArchetypeAgreementTests
{
    private const int Me = 5;
    private const int Them = 6;

    /// <summary>The hero of a game whose class is <c>NEUTRAL</c>: a hero card of no playable class.</summary>
    private const string NeutralHero = "ZZ_NEUTRAL_HERO";

    private static readonly string Data = Path.Combine(RepoFiles.Root, "server", "pool", "data");
    private static readonly string Test = Path.Combine(RepoFiles.Root, "server", "pool", "test");

    [Fact]
    public void The_app_names_every_agreement_case_as_the_pool_does()
    {
        using var dictionary = Json(Path.Combine(Data, "archetypes.json"));
        using var cardIds = Json(Path.Combine(Data, "card-ids.json"));
        using var cases = Json(Path.Combine(Test, "fixtures", "archetype-cases.json"));
        using var golden = Json(Path.Combine(Test, "golden", "archetypes-agreement.json"));
        var rules = dictionary.RootElement.GetProperty("rules").GetString();
        Assert.Equal(rules, cases.RootElement.GetProperty("rules").GetString());
        Assert.Equal(rules, golden.RootElement.GetProperty("rules").GetString());

        var metas = Metas(dictionary.RootElement);
        var cards = Cards(dictionary.RootElement, cardIds.RootElement);
        var records = cases.RootElement.GetProperty("records").EnumerateArray().ToList();
        var answers = golden.RootElement.GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal(records.Count, answers.Count);
        Assert.True(records.Count(r => Has(r, "cards") && r.GetProperty("cards").GetArrayLength() > 0 && Text(r, "deck") is not null) >= 200, "200 deck codes or more");
        Assert.True(records.Count(r => Has(r, "oplays") && r.GetProperty("oplays").GetArrayLength() > 0) >= 200, "200 opponents or more");

        var wrong = new List<string>();
        int arch = 0, oarch = 0;
        for (var i = 0; i < records.Count; i++)
        {
            var (own, opponent) = AppAnswer(records[i], metas, cards);
            string? wantOwn = answers[i][0].GetString(), wantOpponent = answers[i][1].GetString();
            arch += own is null ? 0 : 1;
            oarch += opponent is null ? 0 : 1;
            if (!string.Equals(own, wantOwn, StringComparison.Ordinal) || !string.Equals(opponent, wantOpponent, StringComparison.Ordinal))
            {
                wrong.Add($"{i} {records[i].GetProperty("case").GetString()}: app {own ?? "null"} / {opponent ?? "null"}, pool {wantOwn ?? "null"} / {wantOpponent ?? "null"}");
            }
        }

        Assert.Empty(wrong);
        Assert.True(arch > 0 && oarch > 0, "the cases name archetypes on both sides");
    }

    [Fact]
    public void The_dictionary_builder_finds_the_classes_and_core_cards_the_app_finds()
    {
        using var fixture = Json(Path.Combine(Test, "fixtures", "archetype-catalog.json"));
        var root = fixture.RootElement;
        var cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(root.GetProperty("cards").GetRawText())));
        var expected = root.GetProperty("expected").GetProperty("archetypes");
        foreach (var format in root.GetProperty("catalogs").EnumerateObject())
        {
            // As ApiConstructedMeta.LoadAsync reads the catalogue: the first archetype of a name, its class, its core
            // cards, the most played first; one that has no class or no core card never matches, so the dictionary
            // leaves it out.
            var app = HearthpulseData.ParseHsguruArchetypes(format.Value.GetProperty("data"))
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(a => (Archetype: a, Class: ApiConstructedMeta.ClassOf(a.Name, a.Decks), Core: Core(a, cards)))
                .Where(x => x.Class is not null && x.Core.Count > 0)
                .GroupBy(x => x.Class!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Archetype.Games)
                    .Select(x => Row(x.Archetype.Name, x.Core, x.Core.Select(id => cards.Get(id)!.DbfId))).ToList(), StringComparer.Ordinal);
            var built = expected.GetProperty("formats").GetProperty(format.Name).EnumerateObject().ToDictionary(c => c.Name, c => c.Value.EnumerateArray()
                .Select(a => Row(a.GetProperty("key").GetString()!,
                    a.GetProperty("coreIds").EnumerateArray().Select(x => x.GetString()!), a.GetProperty("coreDbf").EnumerateArray().Select(x => x.GetInt32())))
                .ToList(), StringComparer.Ordinal);

            Assert.Equal(built.Keys.OrderBy(k => k, StringComparer.Ordinal), app.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (cls, rows) in built)
            {
                Assert.Equal(rows, app[cls]);
            }
        }

        // The class the app reads off a deck code's hero (GameRecords.DeckFits) for every hero card that has one.
        var heroes = root.GetProperty("cards").EnumerateArray().Select(c => cards.Get(c.GetProperty("dbfId").GetInt32())!)
            .Where(c => c is { Type: "HERO", CardClass.Length: > 0 })
            .GroupBy(c => c.CardClass, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => c.DbfId).Order().ToList(), StringComparer.Ordinal);
        var builtHeroes = expected.GetProperty("heroes").EnumerateObject()
            .ToDictionary(c => c.Name, c => c.Value.EnumerateArray().Select(d => d.GetInt32()).ToList(), StringComparer.Ordinal);
        Assert.Equal(builtHeroes.Keys.OrderBy(k => k, StringComparer.Ordinal), heroes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (cls, dbfs) in builtHeroes)
        {
            Assert.Equal(dbfs, heroes[cls]);
        }
    }

    /// <summary>
    /// The app's answer for a record the way the pool reads it. Own: what <see cref="GameRecords.Build"/> names for the
    /// game the record tells (its mode, format and hero class) played with the record's deck code. Opponent:
    /// <see cref="ArchetypeGuess.FromSeenCards"/> over the cards the record shows, where the pool names one (ranked,
    /// casual) in a format the dictionary has.
    /// </summary>
    private static (string? Own, string? Opponent) AppAnswer(JsonElement record, IReadOnlyDictionary<string, ConstructedMeta> metas, CardDb cards)
    {
        var format = Text(record, "fmt");
        var meta = format is not null && metas.TryGetValue(format, out var ofFormat) ? ofFormat : metas["standard"];
        var own = Played(record, meta, cards)?.DeckArchetype;
        if (Text(record, "mode") is not ("ranked" or "casual") || format is null || !metas.ContainsKey(format))
        {
            return (own, null);
        }

        var seen = Has(record, "oplays") ? record.GetProperty("oplays").EnumerateArray().Select(p => p[1].GetString()!).ToList() : [];
        return (own, ArchetypeGuess.FromSeenCards(meta, Text(record, "ocls"), seen)?.Key);
    }

    /// <summary>
    /// The finished game a record tells, as the log tells one (the grammar of <c>OwnArchetypeTests</c>): its game type
    /// and format, the local player's hero of the record's class (none known without a class), and the record's deck
    /// code as the deck the client held.
    /// </summary>
    private static GameRecord? Played(JsonElement record, ConstructedMeta meta, CardDb cards)
    {
        var gameType = Text(record, "mode") switch
        {
            "ranked" => "GT_RANKED",
            "casual" => "GT_CASUAL",
            "arena" => "GT_ARENA",
            "brawl" => "GT_TAVERNBRAWL",
            "bg" => "GT_BATTLEGROUNDS",
            var mode => throw new InvalidDataException($"No game type for the mode {mode}."),
        };
        var formatType = Text(record, "fmt") switch
        {
            "standard" => "FT_STANDARD",
            "wild" => "FT_WILD",
            "classic" => "FT_CLASSIC",
            "twist" => "FT_TWIST",
            _ => "FT_UNKNOWN",
        };
        var hero = Text(record, "cls") switch
        {
            null => "ZZ_NO_HERO",
            "NEUTRAL" => NeutralHero,
            var cls => DeckString.ClassHero(cls)!,
        };
        var parser = new PowerParser(new DateOnly(2026, 11, 26));
        var battlegrounds = new BattlegroundsTracker { Cards = cards };
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
            $"DebugPrintGame() - FormatType={formatType}",
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
            "DebugPrintPower() - FULL_ENTITY - Creating ID=60 CardID=ZZ_CARD",
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
        var deck = Text(record, "deck") is { } code ? DeckString.TryDecode(code) : null;
        return GameRecords.Build(parser.Game!, battlegrounds, cards, deck, "test", meta: meta);
    }

    /// <summary>The dictionary as a meta per format in its order, the most played first as ApiConstructedMeta orders one: the file holds no game counts.</summary>
    private static Dictionary<string, ConstructedMeta> Metas(JsonElement dictionary)
    {
        var metas = new Dictionary<string, ConstructedMeta>(StringComparer.Ordinal);
        foreach (var format in dictionary.GetProperty("formats").EnumerateObject())
        {
            var archetypes = new List<MetaArchetype>();
            foreach (var cls in format.Value.EnumerateObject())
            {
                foreach (var a in cls.Value.EnumerateArray())
                {
                    var key = a.GetProperty("key").GetString()!;
                    var core = a.GetProperty("coreIds").EnumerateArray().Select(c => c.GetString()!).ToList();
                    archetypes.Add(new MetaArchetype(ApiConstructedMeta.StableId(key), key, key, cls.Name, 0, 0, 0, 0, 0, 0, 0, 0, core, []));
                }
            }

            metas[format.Name] = new ConstructedMeta(format.Name, "all", "past_week", DateTime.MinValue, 0, archetypes, []);
        }

        return metas;
    }

    /// <summary>
    /// The cards the pool's data names: every card of card-ids.json, and every hero of the dictionary as a hero card of
    /// its class (one card-ids.json does not list gets an id of its own) - what the app needs to read a deck code - and
    /// <see cref="NeutralHero"/> for a game of no class.
    /// </summary>
    private static CardDb Cards(JsonElement dictionary, JsonElement cardIds)
    {
        var heroes = dictionary.GetProperty("heroes").EnumerateObject()
            .SelectMany(c => c.Value.EnumerateArray().Select(d => (Dbf: d.GetInt32(), Class: c.Name)))
            .ToDictionary(h => h.Dbf, h => h.Class);
        var ids = cardIds.GetProperty("cards").EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name, CultureInfo.InvariantCulture), p => p.Value.GetString()!);
        var list = new JsonArray { new JsonObject { ["id"] = NeutralHero, ["dbfId"] = 999_999_998, ["type"] = "HERO", ["cardClass"] = "NEUTRAL" } };
        foreach (var dbf in ids.Keys.Union(heroes.Keys))
        {
            var card = new JsonObject
            {
                ["id"] = ids.TryGetValue(dbf, out var id) ? id : "ZZ_HERO_" + dbf.ToString(CultureInfo.InvariantCulture),
                ["dbfId"] = dbf,
            };
            if (heroes.TryGetValue(dbf, out var cls))
            {
                card["type"] = "HERO";
                card["cardClass"] = cls;
            }

            list.Add(card);
        }

        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(list.ToJsonString())));
    }

    private static IReadOnlyList<string> Core(HsguruArchetype archetype, CardDb cards) =>
        ApiConstructedMeta.CoreCards(archetype.Decks.Select(d => (d.Games, Deck: DeckString.TryDecode(d.Code))).Where(d => d.Deck is not null)
            .Select(d => (d.Games, d.Deck!)).ToList(), cards);

    private static string Row(string key, IEnumerable<string> ids, IEnumerable<int> dbf) =>
        $"{key}: {string.Join(",", ids)} / {string.Join(",", dbf.Select(d => d.ToString(CultureInfo.InvariantCulture)))}";

    private static bool Has(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonDocument Json(string path) => JsonDocument.Parse(File.ReadAllText(path));
}

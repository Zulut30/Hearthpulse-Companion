using System.Net;
using System.Text.Json.Nodes;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The fields record analytics added to the game record (TASK-2026-11-26-pro-collection AC1, contract K1): the client
/// build in every mode, the Diamond 4-1 band of a Ranked game and the main tribe of a Battlegrounds final board seen
/// live; nothing else new. Records are built as the uploader builds them, from the saved history row.
/// </summary>
public sealed partial class GameStatsTests
{
    private const int LogBuild = HistoryStoreBuildTests.LogBuild;

    /// <summary>The keys of a full Constructed record before record analytics (docs/game-stats.md).</summary>
    private static readonly string[] ConstructedKeys =
        ["id", "day", "mode", "res", "turns", "secs", "app", "fmt", "rank", "cls", "ocls", "hero", "deck", "oarch", "first", "mull", "plays", "oplays", "choices", "draws", "exc"];

    /// <summary>The keys of <c>bg</c> for a game seen live, without its replay, before record analytics.</summary>
    private static readonly string[] BattlegroundsKeys =
        ["placement", "mmr", "picked", "offered", "anomaly", "tier", "triples", "board", "trinkets", "tierups", "combats", "choices", "plays", "tribes", "quests"];

    private static readonly CardDb BgCards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"BG_HERO_1","dbfId":70001,"name":"Hero","type":"HERO"},
          {"id":"BG_BEAST_1","dbfId":70011,"name":"Beast 1","type":"MINION","races":["BEAST"],"techLevel":1},
          {"id":"BG_BEAST_2","dbfId":70012,"name":"Beast 2","type":"MINION","races":["BEAST"],"techLevel":2},
          {"id":"BG_BEAST_3","dbfId":70013,"name":"Beast 3","type":"MINION","races":["BEAST"],"techLevel":3},
          {"id":"BG_AMALGAM","dbfId":70014,"name":"Amalgam","type":"MINION","races":["ALL"],"techLevel":4},
          {"id":"BG_DRAGON","dbfId":70015,"name":"Dragon","type":"MINION","races":["DRAGON"],"techLevel":4},
          {"id":"BG_MURLOC","dbfId":70016,"name":"Murloc","type":"MINION","races":["MURLOC"],"techLevel":2}
        ]
        """u8.ToArray()));

    private static string[] Keys(JsonNode? node) => node!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    private static string[] Sorted(IEnumerable<string> keys) => keys.Order(StringComparer.Ordinal).ToArray();

    private GameStatsUploader FieldsUploader(HistoryStore history, CardDb cards) =>
        new(new AppSettings { PoolEndpoint = Endpoint, PoolSecret = new string('5', 64) }, new HttpClient(new FakePool(HttpStatusCode.OK)), history,
            () => null, () => cards, "0.30.0", new GameStatsQueues(_folder));

    [Fact]
    public void A_ranked_game_sends_the_build_of_its_log_and_only_diamond_3_gets_the_diamond_4_to_1_band()
    {
        var history = new HistoryStore(Path.Combine(_folder, "h.db"));
        var diamond3 = HistoryStoreBuildTests.Finished(LogBuild, starLevel: 48, time: "20:00:00");
        var diamond5 = HistoryStoreBuildTests.Finished(LogBuild, starLevel: 46, time: "20:30:00");
        Assert.Equal(("Diamond 3", "Diamond 5"), (diamond3.Rank, diamond5.Rank));
        history.Add(diamond3);
        history.Add(diamond5);
        using var uploader = FieldsUploader(history, HistoryStoreBuildTests.Cards);

        var high = uploader.Build(new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08") { Key = diamond3.Key })!;
        var low = uploader.Build(new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08") { Key = diamond5.Key })!;

        Assert.Equal(LogBuild, (int)high["build"]!);
        Assert.Equal(LogBuild, (int)low["build"]!);
        Assert.Equal(("diamond", "diamond"), ((string?)high["rank"], (string?)low["rank"]));
        Assert.Equal("d4-1", (string?)high["band"]);
        Assert.Null(low["band"]);
    }

    [Theory]
    [InlineData(41, null)]
    [InlineData(46, null)]
    [InlineData(47, "d4-1")]
    [InlineData(48, "d4-1")]
    [InlineData(50, "d4-1")]
    public void The_band_comes_from_the_star_level_of_a_diamond_rank(int starLevel, string? band)
    {
        var rank = new Hearthpulse.Core.Memory.RankInfo("FT_STANDARD", 0, starLevel, 0, 0).Text;
        var record = GameStatsRecord.Build(Ranked(null) with { Rank = rank, RankStarLevel = starLevel }, new string('a', 32), null, null, null, null)!;
        Assert.Equal("diamond", (string?)record["rank"]);
        Assert.Equal(band, (string?)record["band"]);
    }

    [Theory]
    [InlineData("Casual", "Diamond 3", 48)]
    [InlineData("Ranked", "Diamond 3", null)]
    [InlineData("Ranked", "Gold 4", 48)]
    [InlineData("Ranked", "Legend 1,204", 51)]
    [InlineData("Ranked", null, 48)]
    public void Without_ranked_a_diamond_league_and_its_star_level_there_is_no_band(string mode, string? rank, int? starLevel)
    {
        var record = GameStatsRecord.Build(Ranked(null) with { Mode = mode, Rank = rank, RankStarLevel = starLevel }, new string('a', 32), null, null, null, null)!;
        Assert.Null(record["band"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(LogBuild, true)]
    [InlineData(GameStatsRecord.MaxBuild, true)]
    [InlineData(GameStatsRecord.MaxBuild + 1, false)]
    public void Only_a_build_the_server_takes_is_sent(int? build, bool sent)
    {
        var record = GameStatsRecord.Build(Ranked(null) with { Build = build }, new string('a', 32), null, null, null, null)!;
        Assert.Equal(sent ? build : null, (int?)record["build"]);
    }

    [Fact]
    public void A_battlegrounds_game_seen_live_sends_the_main_tribe_of_its_final_board_and_a_recovered_one_does_not()
    {
        var history = new HistoryStore(Path.Combine(_folder, "h.db"));
        var game = new GameRecord
        {
            Key = "20261125200000|Me#1234|GT_BATTLEGROUNDS",
            StartedAt = new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc),
            EndedAt = new DateTime(2026, 11, 25, 20, 21, 0, DateTimeKind.Utc),
            Mode = "Battlegrounds",
            GameType = "GT_BATTLEGROUNDS",
            Result = "Win",
            Placement = 2,
            RatingBefore = 6789,
            Turns = 13,
            TavernTier = 5,
            HeroCardId = "BG_HERO_1",
            Build = LogBuild,
        };
        history.Add(game);
        using var uploader = FieldsUploader(history, BgCards);
        BgMinion Minion(string card) => new(card, card, 5, 5, false, 2, []);
        GameOutcome Live(params string[] board) => new("Battlegrounds", "Win", 2, 6789, null, "BG_HERO_1", ["BG_HERO_1"])
        {
            Key = game.Key,
            GameType = "GT_BATTLEGROUNDS",
            Details = new BgGameDetails(13, 5, 1, null, ["BEAST", "DRAGON", "MURLOC"], board.Select(Minion).ToList(), [], [], [], []),
        };

        // Three Beasts and an Amalgam (every tribe) make four: the board is a Beast board.
        var beasts = Live("BG_BEAST_1", "BG_BEAST_2", "BG_BEAST_3", "BG_AMALGAM", "BG_DRAGON");
        var record = uploader.Build(beasts)!;
        var pool = PoolGame.From(beasts, BgCards.BaseHeroId, id => BgCards.Get(id)?.Races ?? [], details: true, secret: new string('5', 64))!;
        Assert.Equal("BEAST", (string?)record["bg"]!["comp"]);
        Assert.Equal(PoolDetails.MainTribe(pool.Details!.Board, id => BgCards.Get(id)?.Races ?? []), (string?)record["bg"]!["comp"]);
        Assert.Equal(LogBuild, (int)record["build"]!);

        Assert.Equal("MIXED", (string?)uploader.Build(Live("BG_BEAST_1", "BG_DRAGON", "BG_MURLOC"))!["bg"]!["comp"]);
        Assert.Null(uploader.Build(Live())!["bg"]!["comp"]);

        // Found when the app started, not watched live: what the history row has, no final board, no main tribe.
        var recovered = uploader.Build(beasts with { Recovered = true, Details = null })!;
        Assert.Null(recovered["bg"]!["comp"]);
        Assert.Equal(LogBuild, (int)recovered["build"]!);
    }

    /// <summary>The record table of docs/game-stats.md names both cases the test above sends no <c>bg.comp</c> in.</summary>
    [Fact]
    public void The_record_table_tells_when_a_battlegrounds_record_goes_without_the_main_tribe()
    {
        var row = File.ReadLines(Path.Combine(RepoFiles.Root, "docs", "game-stats.md")).Single(l => l.StartsWith("| `bg.comp` |", StringComparison.Ordinal));
        Assert.Contains("left out when the final board is empty", row, StringComparison.Ordinal);
        Assert.Contains("not one recovered when the app started", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_else_is_new_in_a_record_and_a_game_without_a_build_goes_without_one()
    {
        var cards = Cards();
        var constructed = GameStatsRecord.Build(Ranked(DeckCode(cards)) with { Rank = "Diamond 3", RankStarLevel = 48, Build = LogBuild },
            new string('a', 32), Replay(), null, cards, "0.30.0")!;
        Assert.Equal(Sorted([.. ConstructedKeys, "build", "band"]), Keys(constructed));

        var details = new PoolDetails(12, 5, 1, "BG_ANOMALY_1", ["BEAST", "DRAGON"], "BEAST", [new PoolMinion("BG_BEAST_1", true, 10, 12)],
            ["T_A", "T_B"], ["T_A"], [new TierStep(3, 2)], [new PoolCombat(4, "W", 0, 5, 0.5)])
        {
            Choices = [new BgChoice(5, "quest", null, ["Q_A", "Q_B"], ["Q_A"])],
            Plays = [new BgPlay(3, "BG_BEAST_1")],
        };
        var bgGame = new GameRecord { Key = "k", Mode = "Battlegrounds", GameType = "GT_BATTLEGROUNDS", Result = "Win", Placement = 3, RatingBefore = 6789, HeroCardId = "BG_HERO_1", Build = LogBuild };
        var pool = new PoolGame(new string('b', 32), "solo", 6500, ["BG_HERO_1", "BG_HERO_2"], "BG_HERO_1", 3) { Details = details };
        var battlegrounds = GameStatsRecord.Build(bgGame, new string('c', 32), null, pool, null, "0.30.0")!;
        Assert.Equal(Sorted(["id", "day", "mode", "res", "turns", "secs", "app", "build", "bg"]), Keys(battlegrounds));
        Assert.Equal(Sorted([.. BattlegroundsKeys, "comp"]), Keys(battlegrounds["bg"]));

        var withoutBuild = GameStatsRecord.Build(HistoryStoreBuildTests.Finished(build: null, starLevel: 48), new string('a', 32), null, null, HistoryStoreBuildTests.Cards, null)!;
        Assert.Null(withoutBuild["build"]);
        Assert.Equal("d4-1", (string?)withoutBuild["band"]);
        Assert.Null(GameStatsRecord.Build(bgGame with { Build = null }, new string('c', 32), null, pool, null, null)!["build"]);
    }
}

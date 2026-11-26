using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using Microsoft.Data.Sqlite;

namespace Hearthpulse.Tests;

/// <summary>
/// The client build in the history (<c>build</c>, TASK-2026-11-26-pro-collection AC2): a column added to a history file
/// of the previous version when the app opens it, the log's <c>BuildNumber</c> saved with each finished game, given to
/// games imported from kept logs and filled into rows saved before the column by a re-read of their log. Synthetic
/// Power.log lines in the order of the November 2026 logs (build 255790).
/// </summary>
public sealed class HistoryStoreBuildTests : IDisposable
{
    /// <summary>The build of the kept November 2026 logs.</summary>
    internal const int LogBuild = 255790;

    private const int Me = 1;
    private const int Them = 2;

    private readonly string _dir = TestDataFolder.Scratch("hp-history-build");

    public HistoryStoreBuildTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    internal static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"HERO_08","dbfId":637,"name":"Jaina","type":"HERO","cardClass":"MAGE"},
          {"id":"HERO_02","dbfId":1066,"name":"Thrall","type":"HERO","cardClass":"SHAMAN"},
          {"id":"CS2_029","dbfId":1002,"name":"Fireball","type":"SPELL","cost":4,"cardClass":"MAGE"}
        ]
        """u8.ToArray()));

    /// <summary>
    /// A game the local player (a Mage) won against a Shaman, as the log tells it: the CREATE_GAME block, the game's
    /// <c>DebugPrintGame</c> lines with <c>BuildNumber</c> first, as the client writes them (none when
    /// <paramref name="build"/> is null), the heroes, a card in hand (how the log names the local player), and the
    /// game-over lines.
    /// </summary>
    internal static IEnumerable<string> Game(int? build, string time = "20:00:00", string gameType = "GT_RANKED")
    {
        string[] lines =
        [
            "DebugPrintPower() - CREATE_GAME",
            "DebugPrintPower() -     GameEntity EntityID=1",
            "DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "DebugPrintPower() -         tag=TURN value=9",
            $"DebugPrintPower() -     Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Me}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=30",
            $"DebugPrintPower() -     Player EntityID=3 PlayerID={Them} GameAccountId=[hi=144115198130930503 lo=12]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Them}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=40",
            .. build is { } number ? (string[])[$"DebugPrintGame() - BuildNumber={number}"] : [],
            $"DebugPrintGame() - GameType={gameType}",
            "DebugPrintGame() - FormatType=FT_STANDARD",
            "DebugPrintGame() - ScenarioID=2",
            $"DebugPrintGame() - PlayerID={Me}, PlayerName=Me#1234",
            $"DebugPrintGame() - PlayerID={Them}, PlayerName=You#5678",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=30 CardID=HERO_08",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            $"DebugPrintPower() -     tag=CONTROLLER value={Me}",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=40 CardID=HERO_02",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            $"DebugPrintPower() -     tag=CONTROLLER value={Them}",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=60 CardID=CS2_029",
            "DebugPrintPower() -     tag=ZONE value=HAND",
            $"DebugPrintPower() -     tag=CONTROLLER value={Me}",
            "DebugPrintPower() -     tag=CARDTYPE value=SPELL",
            "DebugPrintPower() - TAG_CHANGE Entity=You#5678 tag=PLAYSTATE value=LOST ",
            "DebugPrintPower() - TAG_CHANGE Entity=Me#1234 tag=PLAYSTATE value=WON ",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER ",
            "DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
        ];
        return lines.Select(line => $"D {time}.0000000 GameState.{line}");
    }

    /// <summary>
    /// The history row of a <see cref="Game"/> as the live path builds it when the game ends
    /// (<c>GameSession.RecordIfFinished</c>): with the rank the client gave for <paramref name="starLevel"/>, none
    /// without one (memory reading off).
    /// </summary>
    internal static GameRecord Finished(int? build, int? starLevel = null, string time = "20:00:00", string gameType = "GT_RANKED")
    {
        var parser = new PowerParser(new DateOnly(2026, 11, 25));
        var battlegrounds = new BattlegroundsTracker { Cards = Cards };
        parser.AddListener(battlegrounds);
        foreach (var line in Game(build, time, gameType))
        {
            parser.Feed(line);
        }

        parser.Flush();
        var rank = starLevel is { } stars ? new RankInfo("FT_STANDARD", 0, stars, 0, 0) : null;
        var record = GameRecords.Build(parser.Game!, battlegrounds, Cards, null, "live", rank: rank?.Text, rankInfo: rank);
        Assert.NotNull(record);
        return record;
    }

    private static HashSet<string> Columns(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(games);";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1) + " " + reader.GetString(2));
        }

        return columns;
    }

    /// <summary>A history file as the previous version left it: every column but <c>build</c>, one game saved.</summary>
    private string PreviousVersion(GameRecord saved)
    {
        var path = Path.Combine(_dir, "history.db");
        new HistoryStore(path).Add(saved);
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE games DROP COLUMN build;";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(Columns(path), c => c.StartsWith("build ", StringComparison.Ordinal));
        return path;
    }

    [Fact]
    public void The_game_record_keeps_the_build_its_log_gave_and_none_without_one()
    {
        Assert.Equal(LogBuild, Finished(LogBuild).Build);
        Assert.Null(Finished(build: null).Build);
        Assert.Null(Finished(0).Build);
    }

    [Fact]
    public void A_previous_version_file_gets_the_column_old_games_stay_empty_and_a_new_game_saves_its_build()
    {
        var old = Finished(LogBuild, time: "19:00:00") with { Build = null };
        var path = PreviousVersion(old);

        var history = new HistoryStore(path);
        Assert.Contains("build INTEGER", Columns(path));
        Assert.Null(Assert.Single(history.Recent()).Build);
        Assert.Null(history.Game(old.Key)!.Build);

        // A game ends after the app opened the history.
        var game = Finished(LogBuild);
        Assert.True(history.Add(game));
        Assert.Equal(LogBuild, history.Game(game.Key)!.Build);
        Assert.Equal(LogBuild, history.Recent().Single(g => g.Key == game.Key).Build);
        Assert.Equal(LogBuild, history.PageForUpload(0, 10).Single(g => g.Key == game.Key).Build);
        Assert.Equal(LogBuild, Assert.Single(history.WithCards([game])).Build);

        // Opening again changes nothing.
        history = new HistoryStore(path);
        Assert.Equal(2, history.Recent().Count);
        Assert.Null(history.Game(old.Key)!.Build);
    }

    [Fact]
    public void Games_imported_from_kept_logs_get_the_build_and_rows_saved_before_it_are_filled_in()
    {
        var install = Path.Combine(_dir, "install");
        var session = Path.Combine(install, "Logs", "Hearthstone_2026_11_25_18_25_47");
        Directory.CreateDirectory(session);
        File.WriteAllLines(Path.Combine(session, "Power.log"), Game(LogBuild, "19:00:00").Concat(Game(LogBuild, "19:20:00")).Concat(Game(LogBuild, "19:40:00")));

        var imported = LogBackfill.Parse(Path.Combine(session, "Power.log"), new DateTime(2026, 11, 25, 18, 25, 47), Cards);
        Assert.Equal(3, imported.Count);
        Assert.All(imported, g => Assert.Equal(LogBuild, g.Build));

        // The first game was saved before the column (no build), the second by a build that gave another number.
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        history.Add(imported[0] with { Build = null });
        history.Add(imported[1] with { Build = 255_000 });

        var result = LogBackfill.Run(install, history, Cards);

        Assert.Equal(1, result.GamesAdded);
        Assert.Equal(LogBuild, history.Game(imported[0].Key)!.Build);
        Assert.Equal(255_000, history.Game(imported[1].Key)!.Build);
        Assert.Equal(LogBuild, history.Game(imported[2].Key)!.Build);
        Assert.Equal("backfill", history.Game(imported[2].Key)!.Source);
    }

    [Fact]
    public void A_game_that_ends_after_it_was_saved_as_abandoned_keeps_a_build()
    {
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        var game = Finished(LogBuild);
        Assert.True(history.Add(game with { Result = GameRecord.AbandonedResult, Build = null }));
        Assert.True(history.Add(game));
        Assert.Equal(LogBuild, history.Game(game.Key)!.Build);

        var other = Finished(LogBuild, time: "21:00:00");
        Assert.True(history.Add(other with { Result = GameRecord.AbandonedResult }));
        Assert.True(history.Add(other with { Build = null }));
        Assert.Equal(LogBuild, history.Game(other.Key)!.Build);
        Assert.Equal("Win", history.Game(other.Key)!.Result);
    }

    [Fact]
    public void Filling_missing_fields_takes_a_build_only_where_none_was_saved()
    {
        var history = new HistoryStore(Path.Combine(_dir, "history.db"));
        var game = Finished(LogBuild);
        history.Add(game with { Build = null });

        Assert.False(history.FillMissingFields(game with { Build = null }));
        Assert.True(history.FillMissingFields(game));
        Assert.Equal(LogBuild, history.Game(game.Key)!.Build);
        Assert.False(history.FillMissingFields(game with { Build = 1 }));
        Assert.Equal(LogBuild, history.Game(game.Key)!.Build);
    }
}

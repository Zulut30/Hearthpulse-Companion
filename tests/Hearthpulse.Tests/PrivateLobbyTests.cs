using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// Battlegrounds games outside public matchmaking (a friendly lobby, a game against the AI) stay in History and My Stats
/// but go nowhere: not to the profile, the stats pool or the game records (owner, 2026-11-26). The games are synthetic
/// Power.log lines in the grammar of the October 2026 Battlegrounds logs; the private type names are HearthSim
/// python-hearthstone's GameType (MIT), not checked against a log of a real private lobby.
/// </summary>
public sealed class PrivateLobbyTests
{
    private const int Me = 5;
    private const int Bob = 6;
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream("""
        [{"id":"TB_BaconShop_HERO_50","dbfId":59000,"name":"Tess","type":"HERO"}]
        """u8.ToArray()));

    private static string Base(string id) => id;

    /// <summary>A Battlegrounds game of <paramref name="gameType"/> the player finished second, as the log tells it.</summary>
    private static GameRecord Played(string gameType)
    {
        var parser = new PowerParser(new DateOnly(2026, 11, 26));
        var battlegrounds = new BattlegroundsTracker { Cards = Cards };
        parser.AddListener(battlegrounds);
        string[] lines =
        [
            "DebugPrintPower() - CREATE_GAME",
            "DebugPrintPower() -     GameEntity EntityID=1",
            "DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "DebugPrintPower() -         tag=TURN value=9",
            $"DebugPrintPower() -     Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Me}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=50",
            $"DebugPrintPower() -     Player EntityID=3 PlayerID={Bob} GameAccountId=[hi=0 lo=0]",
            $"DebugPrintPower() -         tag=CONTROLLER value={Bob}",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=40",
            "DebugPrintPower() -         tag=BACON_DUMMY_PLAYER value=1",
            $"DebugPrintGame() - GameType={gameType}",
            "DebugPrintGame() - FormatType=FT_WILD",
            $"DebugPrintGame() - PlayerID={Me}, PlayerName=Me#1234",
            $"DebugPrintGame() - PlayerID={Bob}, PlayerName=You#5678",
            "DebugPrintPower() - FULL_ENTITY - Creating ID=50 CardID=TB_BaconShop_HERO_50",
            "DebugPrintPower() -     tag=ZONE value=PLAY",
            $"DebugPrintPower() -     tag=CONTROLLER value={Me}",
            "DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "DebugPrintPower() -     tag=PLAYER_LEADERBOARD_PLACE value=2",
            "DebugPrintPower() - TAG_CHANGE Entity=Me#1234 tag=PLAYSTATE value=LOST ",
        ];
        foreach (var line in lines)
        {
            parser.Feed("D 20:00:00.0000000 GameState." + line);
        }

        parser.Flush();
        var record = GameRecords.Build(parser.Game!, battlegrounds, Cards, null, "live", ratingBefore: 6400);
        Assert.NotNull(record);
        return record;
    }

    /// <summary>The session's outcome of a saved game (as <c>GameSession.RecordIfFinished</c> raises it).</summary>
    private static GameOutcome Outcome(GameRecord game) =>
        new(game.Mode, game.Result, game.Placement, game.RatingBefore, null, game.HeroCardId) { Key = game.Key, GameType = game.GameType, Format = game.Format };

    [Theory]
    [InlineData("GT_BATTLEGROUNDS_FRIENDLY")]
    [InlineData("GT_BATTLEGROUNDS_DUO_FRIENDLY")]
    [InlineData("GT_BATTLEGROUNDS_PLAYER_VS_AI")]
    [InlineData("GT_BATTLEGROUNDS_AI_VS_AI")]
    [InlineData("GT_BATTLEGROUNDS_DUO_VS_AI")]
    [InlineData("GT_BATTLEGROUNDS_DUO_AI_VS_AI")]
    public void A_private_lobby_stays_in_history_and_my_stats_and_goes_nowhere(string gameType)
    {
        var history = HistoryStore.InMemory();
        Assert.True(history.Add(Played(gameType)));

        var saved = Assert.Single(history.Recent());
        Assert.True(GameRecords.IsPrivateBattlegrounds(saved.GameType));
        // My Stats files it under its mode as before: the mode of the type is unchanged.
        Assert.True(BattlegroundsStats.IsGame(saved, duos: gameType.Contains("DUO", StringComparison.Ordinal)));

        Assert.Null(ProfileEvents.ForGame(saved));
        Assert.Null(ProfileEvents.ProfileGameId(saved));
        Assert.Null(PoolGame.From(Outcome(saved), Base, details: true, secret: Secret));
        Assert.Null(PoolGame.FromHistory(saved, Secret, Base));
        Assert.Null(GameStatsRecord.Build(saved, new string('a', 32), null, null, null, "1.0.0"));
    }

    [Theory]
    [InlineData("GT_BATTLEGROUNDS")]
    [InlineData("GT_BATTLEGROUNDS_DUO")]
    [InlineData("GT_BATTLEGROUNDS_DUOS")]
    public void A_game_of_public_matchmaking_goes_out_as_before(string gameType)
    {
        var game = Played(gameType);

        Assert.False(GameRecords.IsPrivateBattlegrounds(gameType));
        Assert.NotNull(ProfileEvents.ForGame(game));
        Assert.NotNull(ProfileEvents.ProfileGameId(game));
        Assert.NotNull(PoolGame.From(Outcome(game), Base, details: true, secret: Secret));
        Assert.NotNull(PoolGame.FromHistory(game, Secret, Base));
        Assert.NotNull(GameStatsRecord.Build(game, new string('a', 32), null, null, null, "1.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GT_RANKED")]
    [InlineData("GT_VS_FRIEND")]
    [InlineData("GT_MERCENARIES_FRIENDLY")]
    public void Only_a_battlegrounds_type_can_be_private(string? gameType)
    {
        // An empty type is a game built without one (older outcomes, tests): it is not held back.
        Assert.False(GameRecords.IsPrivateBattlegrounds(gameType));
    }
}

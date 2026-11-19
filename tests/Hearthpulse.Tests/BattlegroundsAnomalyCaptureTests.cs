using System.Text;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// The Battlegrounds tracker gives a lobby anomaly (Echoes of Argus, BG27_Anomaly_802) to both captured sides and a quest
/// reward only to its owner. What the anomaly does in a fight: <c>Sim/Anomalies/EchoesOfArgusTests</c>.
/// </summary>
public sealed class BattlegroundsAnomalyCaptureTests
{
    private const string Echoes = "BG27_Anomaly_802";

    [Fact]
    public void The_tracker_gives_the_anomaly_to_both_sides_and_a_reward_to_its_owner()
    {
        // Synthetic lines in the shape of a Battlegrounds Power.log: the anomaly on the game entity (by dbfId), our quest
        // reward in play, one minion a side, the first regular attack. Not checked against a real log: no game of the
        // owner has an anomaly or a reward in play.
        const int me = 5, them = 13;
        var cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
            [
              {"id":"BG27_Anomaly_802","dbfId":102177,"name":"Echoes of Argus","type":"BATTLEGROUND_ANOMALY","text":"Your <b>Battlecries</b> and <b>Deathrattles</b> trigger an extra time."},
              {"id":"BG27_Reward_803","dbfId":104670,"name":"Turbulent Tombs","type":"BATTLEGROUND_QUEST_REWARD","text":"Get a copy of '{0}'. Your <b>Deathrattles</b> trigger an extra time."},
              {"id":"BG28_300","dbfId":104551,"name":"Harmless Bonehead","type":"MINION","attack":0,"health":2,"races":["UNDEAD"],"mechanics":["DEATHRATTLE"],"techLevel":1,"isBattlegroundsPoolMinion":true,"text":"<b>Deathrattle:</b> Summon two 1/1 Skeletons."}
            ]
            """)));
        const string power = "D 21:15:00.0000000 GameState.DebugPrintPower() - ";
        const string game = "D 21:15:00.0000000 GameState.DebugPrintGame() - ";
        string[] lines =
        [
            power + "CREATE_GAME",
            power + "    GameEntity EntityID=1",
            power + "        tag=CARDTYPE value=GAME",
            power + "        tag=BACON_GLOBAL_ANOMALY_DBID value=102177",
            power + $"    Player EntityID=2 PlayerID={me} GameAccountId=[hi=144115198130930503 lo=11]",
            power + $"        tag=CONTROLLER value={me}",
            power + "        tag=CARDTYPE value=PLAYER",
            power + $"    Player EntityID=3 PlayerID={them} GameAccountId=[hi=0 lo=0]",
            power + $"        tag=CONTROLLER value={them}",
            power + "        tag=CARDTYPE value=PLAYER",
            power + "        tag=BACON_DUMMY_PLAYER value=1",
            game + "GameType=GT_BATTLEGROUNDS",
            game + $"PlayerID={me}, PlayerName=Me#1234",
            game + $"PlayerID={them}, PlayerName=Bob's Opponent",
            power + "FULL_ENTITY - Creating ID=20 CardID=BG27_Reward_803",
            power + $"    tag=CONTROLLER value={me}",
            power + "    tag=CARDTYPE value=BATTLEGROUND_QUEST_REWARD",
            power + "    tag=ZONE value=PLAY",
            .. MinionLines(30, me),
            .. MinionLines(40, them),
            power + "TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=2 ",
            power + "BLOCK_START BlockType=ATTACK Entity=[entityName=Harmless Bonehead id=30 zone=PLAY zonePos=1 cardId=BG28_300 player=5] "
                  + "EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ",
        ];

        var parser = new PowerParser(new DateOnly(2026, 11, 19));
        var tracker = new BattlegroundsTracker { Cards = cards, Effects = EffectRegistry.Default };
        parser.AddListener(tracker);
        foreach (var line in lines)
        {
            parser.Feed(line);
        }

        parser.Flush();

        var capture = tracker.LastCapture!;
        Assert.Contains(Echoes, capture.Ours.Extras);
        Assert.Contains(Echoes, capture.Theirs.Extras);
        Assert.Contains("BG27_Reward_803", capture.Ours.Extras);
        Assert.DoesNotContain("BG27_Reward_803", capture.Theirs.Extras);
        Assert.Empty(CombatSimulator.Explain(capture.Ours, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(capture.Theirs, isOpponent: true));

        static string[] MinionLines(int id, int controller) =>
        [
            power + $"FULL_ENTITY - Creating ID={id} CardID=BG28_300",
            power + $"    tag=CONTROLLER value={controller}",
            power + "    tag=CARDTYPE value=MINION",
            power + "    tag=ZONE value=PLAY",
            power + "    tag=ZONE_POSITION value=1",
            power + "    tag=ATK value=1",
            power + "    tag=HEALTH value=1",
            power + "    tag=DEATHRATTLE value=1",
        ];
    }
}

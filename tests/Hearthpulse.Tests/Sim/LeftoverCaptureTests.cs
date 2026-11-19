using System.Text;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// What the capture gives the E5 effects (TASK-2026-11-19): TAG_SCRIPT_DATA_NUM_3/4 of a minion, the local player's quests and
/// waiting Tavern spells in the SECRET zone with their values (never the opponent's), and the crew the local player
/// Discovered for a Timewarped Nellie's Ship. Synthetic GameState lines in the shape of the real log of 2026-11-15
/// (quest L500730, choices L173242/L1255).
/// </summary>
public sealed class LeftoverCaptureTests
{
    private const int Me = 5;
    private const int Them = 13;

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"BG31_809","dbfId":2001,"name":"Turquoise Skitterer","type":"MINION","attack":5,"health":5,"races":["BEAST"],"mechanics":["DEATHRATTLE"],"techLevel":5,"isBattlegroundsPoolMinion":true,"text":"<b>Deathrattle:</b> Your Beetles have +5/+5 this game. Summon a 2/2 Beetle."},
          {"id":"BG34_Giant_074t","dbfId":2002,"name":"Timewarped Nellie's Ship","type":"MINION","attack":2,"health":6,"races":["BEAST","PIRATE"],"mechanics":["DEATHRATTLE"],"techLevel":3,"text":"At the start of each turn, <b>Discover</b> a Pirate to crew the ship. <b>Deathrattle:</b> Summon and get that Pirate."},
          {"id":"BG_PIRATE_A","dbfId":2003,"name":"Pirate A","type":"MINION","attack":3,"health":4,"races":["PIRATE"],"mechanics":["TAUNT"],"techLevel":2,"text":"<b>Taunt</b>"},
          {"id":"BG_PIRATE_B","dbfId":2004,"name":"Pirate B","type":"MINION","attack":5,"health":1,"races":["PIRATE"],"techLevel":3,"text":""},
          {"id":"BG24_Quest_124","dbfId":2005,"name":"Reenact the Murder","type":"SPELL","mechanics":["QUEST"],"text":"<b>Quest:</b> Have 0 friendly minions die."},
          {"id":"BG28_603","dbfId":2006,"name":"Boon of Beetles","type":"BATTLEGROUND_SPELL","techLevel":4,"text":"When you have space in combat, summon two 2/2 Beetles and give them <b>Taunt</b>. <i>(2 left!)</i>"}
        ]
        """)));

    [Fact]
    public void Script_data_three_and_four_reach_the_minion()
    {
        var log = new PowerLog().BattlegroundsGame()
            .Minion(30, "BG31_809", Me, 1, 5, 5, ("TAG_SCRIPT_DATA_NUM_1", "2"), ("TAG_SCRIPT_DATA_NUM_2", "2"), ("TAG_SCRIPT_DATA_NUM_3", "5"), ("TAG_SCRIPT_DATA_NUM_4", "6"))
            .Minion(40, "BG31_809", Them, 1, 5, 5, ("TAG_SCRIPT_DATA_NUM_3", "9"), ("TAG_SCRIPT_DATA_NUM_4", "9"))
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Turquoise Skitterer", "BG31_809", Me, "PLAY"));
        var capture = log.Replay(Cards).Tracker.LastCapture!;

        Assert.Equal((5, 6), (capture.Ours.Minions[0].ScriptData3, capture.Ours.Minions[0].ScriptData4));
        // The opponent's minion text is on the player's screen too.
        Assert.Equal((9, 9), (capture.Theirs.Minions[0].ScriptData3, capture.Theirs.Minions[0].ScriptData4));
    }

    [Fact]
    public void Our_quest_and_waiting_spell_are_extras_with_their_values_and_the_opponents_are_never_read()
    {
        var log = new PowerLog().BattlegroundsGame()
            .Minion(30, "BG31_809", Me, 1, 5, 5)
            .Minion(40, "BG31_809", Them, 1, 5, 5)
            .Entity(60, "BG24_Quest_124", Me, "SECRET", "SPELL", ("QUEST", "1"), ("QUEST_PROGRESS", "2"), ("QUEST_PROGRESS_TOTAL", "5"),
                    ("QUEST_REWARD_DATABASE_ID", "104670"), ("TAG_SCRIPT_DATA_NUM_4", "8"))
            .Entity(61, "BG28_603", Me, "SECRET", "BATTLEGROUND_SPELL", ("TAG_SCRIPT_DATA_NUM_1", "4"), ("TAG_SCRIPT_DATA_NUM_2", "4"), ("TAG_SCRIPT_DATA_NUM_3", "1"))
            .Entity(62, "BG24_Quest_124", Them, "SECRET", "SPELL", ("QUEST", "1"), ("QUEST_PROGRESS", "4"), ("QUEST_PROGRESS_TOTAL", "5"))
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Turquoise Skitterer", "BG31_809", Me, "PLAY"));
        var capture = log.Replay(Cards).Tracker.LastCapture!;

        Assert.Contains("BG24_Quest_124", capture.Ours.Extras);
        Assert.Contains("BG28_603", capture.Ours.Extras);
        Assert.Equal(2, capture.Ours.ExtraTag("BG24_Quest_124", SimInputs.QuestProgress));
        Assert.Equal(5, capture.Ours.ExtraTag("BG24_Quest_124", SimInputs.QuestTotal));
        Assert.Equal(104670, capture.Ours.ExtraTag("BG24_Quest_124", SimInputs.QuestReward));
        Assert.Null(capture.Ours.ExtraTag("BG24_Quest_124", "TAG_SCRIPT_DATA_NUM_4"));
        Assert.Equal(1, capture.Ours.ExtraTag("BG28_603", "TAG_SCRIPT_DATA_NUM_3"));
        Assert.DoesNotContain("BG24_Quest_124", capture.Theirs.Extras);
        Assert.Empty(capture.Theirs.ExtraTags);
    }

    [Fact]
    public void Our_ship_crew_comes_from_our_own_discover_pick_and_the_opponents_is_estimated()
    {
        // Our ship's start-of-turn Discover: we pick Pirate A (3/4 Taunt). Their ship has no crew we could know.
        var ship = Ref(30, "Timewarped Nellie's Ship", "BG34_Giant_074t", Me, "PLAY");
        var log = new PowerLog().BattlegroundsGame()
            .Minion(30, "BG34_Giant_074t", Me, 1, 2, 6)
            .Minion(40, "BG34_Giant_074t", Them, 1, 2, 6)
            .Entity(50, "BG_PIRATE_A", Me, "SETASIDE", "MINION", ("ATK", "3"), ("HEALTH", "4"))
            .Entity(51, "BG_PIRATE_B", Me, "SETASIDE", "MINION", ("ATK", "5"), ("HEALTH", "1"))
            .Choices(ship, Ref(50, "Pirate A", "BG_PIRATE_A", Me, "SETASIDE"), Ref(51, "Pirate B", "BG_PIRATE_B", Me, "SETASIDE"))
            .Chosen(Ref(50, "Pirate A", "BG_PIRATE_A", Me, "SETASIDE"))
            .CombatStarts()
            .Block("ATTACK", ship);
        var capture = log.Replay(Cards).Tracker.LastCapture!;

        var crew = Assert.Single(capture.Ours.Minions[0].Crew);
        Assert.Equal(("BG_PIRATE_A", 3, 4, true), (crew.CardId, crew.Attack, crew.Health, crew.Taunt));
        Assert.Empty(capture.Theirs.Minions[0].Crew);
        var estimate = Assert.Single(capture.Theirs.Estimates, e => e.Input == SimInputs.ShipCrew);
        Assert.Equal(1, estimate.Value1);
    }

    [Fact]
    public void The_crew_survives_the_capture_file()
    {
        var crewed = SimMinion.Create("BG34_Giant_074t", 2, 6, Tribe.Beast | Tribe.Pirate) with
        {
            EntityId = 30,
            ScriptData3 = 4,
            Crew = [SimMinion.Create("BG_PIRATE_A", 3, 4, Tribe.Pirate, Keyword.Taunt)],
        };
        var side = new SimSide([crewed]);
        var inputs = SavedInputsRoundTrip(side);

        Assert.Equal("BG_PIRATE_A", Assert.Single(inputs.Minions[0].Crew).CardId);
        Assert.Equal(4, inputs.Minions[0].ScriptData3);
    }

    /// <summary>The side through the capture file's JSON: the minion's own fields and its saved inputs.</summary>
    private static SimSide SavedInputsRoundTrip(SimSide side)
    {
        var dir = TestDataFolder.Scratch("hp-e5-captures");
        try
        {
            var store = new CombatCaptureStore(dir);
            var fight = new SavedFight { Ours = side, Theirs = new SimSide([]), OursInputs = SavedInputs.Of(side) };
            var name = store.Save("e5", new SavedCaptures { Fights = [fight] });
            Assert.NotNull(name);
            return Assert.Single(store.Load(name!)!.Fights).Ours;
        }
        finally
        {
            TestDataFolder.TryDelete(dir);
        }
    }

    private static string Ref(int id, string name, string cardId, int player, string zone) =>
        $"[entityName={name} id={id} zone={zone} zonePos=1 cardId={cardId} player={player}]";

    /// <summary>Builds GameState lines of a Battlegrounds Power.log (same shape as CaptureTests' builder).</summary>
    private sealed class PowerLog
    {
        private readonly List<string> _lines = new();

        public PowerLog Power(string body)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintPower() - " + body);
            return this;
        }

        public PowerLog BattlegroundsGame() => Power("CREATE_GAME")
            .Power("    GameEntity EntityID=1")
            .Power("        tag=CARDTYPE value=GAME")
            .Power($"    Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]")
            .Power($"        tag=CONTROLLER value={Me}")
            .Power("        tag=CARDTYPE value=PLAYER")
            .Power($"    Player EntityID=3 PlayerID={Them} GameAccountId=[hi=0 lo=0]")
            .Power($"        tag=CONTROLLER value={Them}")
            .Power("        tag=CARDTYPE value=PLAYER")
            .Power("        tag=BACON_DUMMY_PLAYER value=1")
            .GameLine("GameType=GT_BATTLEGROUNDS")
            .GameLine($"PlayerID={Me}, PlayerName=Me#1234")
            .GameLine($"PlayerID={Them}, PlayerName=Bob's Opponent");

        public PowerLog GameLine(string body)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintGame() - " + body);
            return this;
        }

        public PowerLog Entity(int id, string cardId, int controller, string zone, string cardType, params (string Tag, string Value)[] tags)
        {
            Power($"FULL_ENTITY - Creating ID={id} CardID={cardId}");
            Power($"    tag=CONTROLLER value={controller}");
            Power($"    tag=CARDTYPE value={cardType}");
            Power($"    tag=ZONE value={zone}");
            foreach (var (tag, value) in tags)
            {
                Power($"    tag={tag} value={value}");
            }

            return this;
        }

        public PowerLog Minion(int id, string cardId, int controller, int position, int attack, int health, params (string Tag, string Value)[] tags) =>
            Entity(id, cardId, controller, "PLAY", "MINION",
                   [("ZONE_POSITION", position.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("ATK", attack.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("HEALTH", health.ToString(System.Globalization.CultureInfo.InvariantCulture)), .. tags]);

        /// <summary>A GENERAL choice of the local player with its source and options (real shape: L173242-L173247).</summary>
        public PowerLog Choices(string source, params string[] options)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintEntityChoices() - id=3 Player=Me#1234 TaskList=1485 ChoiceType=GENERAL CountMin=1 CountMax=1");
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintEntityChoices() -   Source=" + source);
            for (var i = 0; i < options.Length; i++)
            {
                _lines.Add($"D 21:15:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[{i}]={options[i]}");
            }

            return this;
        }

        /// <summary>The local player's pick (real shape: L1255-L1256).</summary>
        public PowerLog Chosen(string entity)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintEntitiesChosen() - id=3 Player=Me#1234 EntitiesCount=1");
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintEntitiesChosen() -   Entities[0]=" + entity);
            return this;
        }

        public PowerLog CombatStarts() => Power("TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=2 ");

        public PowerLog Block(string type, string entity) =>
            Power($"BLOCK_START BlockType={type} Entity={entity} EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ");

        public (GameState Game, BattlegroundsTracker Tracker) Replay(CardDb cards)
        {
            var parser = new PowerParser(new DateOnly(2026, 11, 15));
            var tracker = new BattlegroundsTracker { Cards = cards, Effects = EffectRegistry.Default };
            parser.AddListener(tracker);
            foreach (var line in _lines)
            {
                parser.Feed(line);
            }

            parser.Flush();
            Assert.Equal(Me, parser.Game!.LocalPlayerId);
            return (parser.Game!, tracker);
        }
    }
}

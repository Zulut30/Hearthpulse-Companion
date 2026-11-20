using System.Text;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Duos input from the tracker (TASK-2026-11-19 package D, Tracking/BattlegroundsTracker.Duos.cs): each team's second
/// board for the simulator. Synthetic lines in the model docs/match-replay.md describes (a team's player entity shows the
/// fighting member's hero through HERO_ENTITY, their minions come in under that player's controller); no Duos Power.log
/// was available to check it.
/// </summary>
public sealed class DuosCaptureTests
{
    private const int Me = 5;
    private const int Them = 13;

    // Lobby PLAYER_IDs: we are 1, our partner 2 (team 1); the opponents 3 and 4 (team 2).
    private const int OwnId = 1;
    private const int PartnerId = 2;
    private const int FirstOpponentId = 3;
    private const int SecondOpponentId = 4;

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"BG32_236","dbfId":1004,"name":"Aureate Laureate","type":"MINION","attack":2,"health":2,"races":["PIRATE"],"mechanics":["DIVINE_SHIELD"],"techLevel":1,"isBattlegroundsPoolMinion":true,"text":"<b>Divine Shield</b>"},
          {"id":"BG25_001","dbfId":1005,"name":"Risen Rider","type":"MINION","attack":2,"health":1,"races":["UNDEAD"],"mechanics":["REBORN","TAUNT"],"techLevel":1,"isBattlegroundsPoolMinion":true,"text":"<b>Taunt</b> <b>Reborn</b>"}
        ]
        """)));

    [Fact]
    public void A_solo_game_has_no_teammates_and_no_duos_estimates()
    {
        var log = Game(duos: false).Turn(1)
            .Minion(40, "BG32_236", Me, 1, 2, 2)
            .Minion(50, "BG25_001", Them, 1, 2, 1)
            .CombatStarts(30, FirstOpponentId)
            .Attack(40, Me);

        var capture = log.Replay().LastCapture!;

        Assert.Null(capture.Ours.Teammate);
        Assert.Null(capture.Theirs.Teammate);
        Assert.DoesNotContain(capture.Theirs.Estimates, e => e.Input == SimInputs.DuosOpponentTeammate);
    }

    [Fact]
    public void A_duos_fight_counts_hero_damage()
    {
        // TASK-2026-11-19 ACC2: Duos damage goes to the team's shared health (DamageRules.TeamHealth), so it is counted.
        var tracker = Game(duos: true).Turn(1)
            .Minion(40, "BG32_236", Me, 1, 2, 2)
            .Minion(50, "BG25_001", Them, 1, 2, 1)
            .CombatStarts(30, FirstOpponentId)
            .Attack(40, Me)
            .Replay();

        Assert.True(tracker.LastCaptureOptions!.CountDamage);
    }

    [Fact]
    public void The_first_fight_marks_the_unseen_opponent_teammate_as_an_estimate()
    {
        var log = Game(duos: true).Turn(1)
            .Minion(40, "BG32_236", Me, 1, 2, 2)
            .Minion(50, "BG25_001", Them, 1, 2, 1)
            .CombatStarts(30, FirstOpponentId)
            .Attack(40, Me);

        var capture = log.Replay().LastCapture!;

        Assert.Null(capture.Theirs.Teammate);
        var estimate = Assert.Single(capture.Theirs.Estimates, e => e.Input == SimInputs.DuosOpponentTeammate);
        Assert.Contains("not seen", estimate.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Boards_that_entered_a_fight_are_the_second_boards_of_the_next_one()
    {
        var log = Game(duos: true).Turn(1)
            // Fight 1: our 40 against their first member's 50.
            .Minion(40, "BG32_236", Me, 1, 2, 2)
            .Minion(50, "BG25_001", Them, 1, 2, 1, ("DIVINE_SHIELD", "1"))
            .CombatStarts(30, FirstOpponentId)
            .Attack(40, Me)
            .Dies(50, Them)
            // Their second member comes in: the opponent player shows that member's hero, a new minion under its controller.
            .ShowsHero(Them, 31, SecondOpponentId)
            .Minion(51, "BG32_236", Them, 1, 7, 7)
            .Attack(51, Them)
            .Dies(40, Me)
            // Ours: the partner's hero on our player entity, the partner's minion under our controller.
            .ShowsHero(Me, 32, PartnerId)
            .Minion(41, "BG25_001", Me, 1, 4, 4)
            .Attack(41, Me)
            .Dies(41, Me)
            .Dies(51, Them)
            .CombatEnds()
            // Fight 2, next turn: our own hero again, our board against their first member.
            .Turn(2)
            .ShowsHero(Me, 10, OwnId)
            .Hand(45, "BG32_236", Me)
            .CombatStarts(33, FirstOpponentId)
            .Minion(42, "BG32_236", Me, 1, 3, 3)
            .Minion(52, "BG25_001", Them, 1, 5, 5)
            .Attack(42, Me);

        var tracker = log.Replay();
        var capture = tracker.LastCapture!;

        // The opponent team's second board: as last seen in fight 1, an estimate.
        var theirs = capture.Theirs.Teammate!;
        Assert.Equal((7, 7), (Assert.Single(theirs.Minions).Attack, theirs.Minions[0].Health));
        var opponentEstimate = Assert.Single(theirs.Estimates, e => e.Input == SimInputs.DuosOpponentTeammate);
        Assert.Equal(1, opponentEstimate.Value1);
        Assert.Contains(CombatSimulator.ExplainEstimates(capture.Theirs, isOpponent: true), e => e.Input == SimInputs.DuosOpponentTeammate);

        // Our team's second board: the partner's, without our hand or player values.
        var partner = capture.Ours.Teammate!;
        Assert.Equal("BG25_001", Assert.Single(partner.Minions).CardId);
        Assert.Empty(partner.Hand);
        Assert.Empty(partner.Tags);
        Assert.Contains(partner.Estimates, e => e.Input == SimInputs.DuosPartnerBoard);
        // Our own first board keeps our hand.
        Assert.Equal("BG32_236", Assert.Single(capture.Ours.Minions).CardId);
        Assert.NotEmpty(capture.Ours.Hand);
        Assert.Null(theirs.Teammate);
    }

    [Fact]
    public void When_our_partner_fights_first_our_hand_is_not_put_on_their_board()
    {
        var log = Game(duos: true).Turn(1)
            .Hand(45, "BG32_236", Me)
            .CombatStarts(30, FirstOpponentId)
            .ShowsHero(Me, 32, PartnerId)
            .Minion(41, "BG25_001", Me, 1, 4, 4)
            .Minion(50, "BG25_001", Them, 1, 2, 1)
            .Attack(41, Me);

        var capture = log.Replay().LastCapture!;

        Assert.Equal(PartnerId, capture.Ours.PlayerId);
        Assert.Empty(capture.Ours.Hand);
        Assert.Empty(capture.Ours.Tags);
    }

    private static PowerLog Game(bool duos) => new PowerLog()
        .Power("CREATE_GAME")
        .Power("    GameEntity EntityID=1")
        .Power("        tag=CARDTYPE value=GAME")
        .Power($"    Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]")
        .Power($"        tag=CONTROLLER value={Me}")
        .Power("        tag=CARDTYPE value=PLAYER")
        .Power("        tag=HERO_ENTITY value=10")
        .Power(duos ? "        tag=BACON_DUO_TEAM_ID value=1" : "        tag=CARDTYPE value=PLAYER")
        .Power($"    Player EntityID=3 PlayerID={Them} GameAccountId=[hi=0 lo=0]")
        .Power($"        tag=CONTROLLER value={Them}")
        .Power("        tag=CARDTYPE value=PLAYER")
        .Power("        tag=BACON_DUMMY_PLAYER value=1")
        .GameLine(duos ? "GameType=GT_BATTLEGROUNDS_DUO" : "GameType=GT_BATTLEGROUNDS")
        .GameLine($"PlayerID={Me}, PlayerName=Me#1234")
        .GameLine($"PlayerID={Them}, PlayerName=Bob's Opponent")
        // Leaderboard heroes: ours in play, the others set aside, each with its lobby PLAYER_ID and team.
        .Hero(10, Me, "PLAY", OwnId, team: duos ? 1 : 0, place: 1)
        .Hero(20, Them, "SETASIDE", PartnerId, team: duos ? 1 : 0, place: 2)
        .Hero(21, Them, "SETASIDE", FirstOpponentId, team: duos ? 2 : 0, place: 3)
        .Hero(22, Them, "SETASIDE", SecondOpponentId, team: duos ? 2 : 0, place: 4);

    private sealed class PowerLog
    {
        private readonly List<string> _lines = new();

        public PowerLog Power(string body)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintPower() - " + body);
            return this;
        }

        public PowerLog GameLine(string body)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintGame() - " + body);
            return this;
        }

        private PowerLog Entity(int id, string cardId, int controller, string zone, string cardType, params (string Tag, string Value)[] tags)
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

        public PowerLog Hero(int id, int controller, string zone, int playerId, int team, int place)
        {
            (string, string)[] tags = team > 0
                ? [("PLAYER_ID", playerId.ToString()), ("HEALTH", "30"), ("PLAYER_LEADERBOARD_PLACE", place.ToString()), ("BACON_DUO_TEAM_ID", team.ToString())]
                : [("PLAYER_ID", playerId.ToString()), ("HEALTH", "30"), ("PLAYER_LEADERBOARD_PLACE", place.ToString())];
            return Entity(id, "TB_BaconShop_HERO_0" + (id % 10), controller, zone, "HERO", tags);
        }

        public PowerLog Minion(int id, string cardId, int controller, int position, int attack, int health, params (string Tag, string Value)[] tags) =>
            Entity(id, cardId, controller, "PLAY", "MINION",
                   [("ZONE_POSITION", position.ToString()), ("ATK", attack.ToString()), ("HEALTH", health.ToString()), .. tags]);

        public PowerLog Hand(int id, string cardId, int controller) => Entity(id, cardId, controller, "HAND", "MINION", ("ATK", "2"), ("HEALTH", "2"));

        public PowerLog Turn(int bgTurn) => Power($"TAG_CHANGE Entity=GameEntity tag=TURN value={(bgTurn * 2) - 1} ");

        /// <summary>The fight begins: the opponent's combat hero copy (a set-aside hero would be a leaderboard one), its id named.</summary>
        public PowerLog CombatStarts(int heroCopy, int opponentId) =>
            Entity(heroCopy, "TB_BaconShop_HERO_0" + (heroCopy % 10), Them, "PLAY", "HERO", ("PLAYER_ID", opponentId.ToString()))
                .Power($"TAG_CHANGE Entity=Bob's Opponent tag=HERO_ENTITY value={heroCopy} ")
                .Power($"TAG_CHANGE Entity=Bob's Opponent tag=BACON_CURRENT_COMBAT_PLAYER_ID value={opponentId} ")
                .Power("TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=2 ");

        public PowerLog CombatEnds() => Power("TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=1 ");

        /// <summary>A team's player entity shows another member's hero (a combat copy with that member's PLAYER_ID).</summary>
        public PowerLog ShowsHero(int player, int hero, int playerId)
        {
            if (hero != 10)
            {
                Entity(hero, "TB_BaconShop_HERO_0" + (hero % 10), player, "PLAY", "HERO", ("PLAYER_ID", playerId.ToString()));
            }

            var name = player == Me ? "Me#1234" : "Bob's Opponent";
            return Power($"TAG_CHANGE Entity={name} tag=HERO_ENTITY value={hero} ");
        }

        public PowerLog Attack(int id, int controller) =>
            Power($"BLOCK_START BlockType=ATTACK Entity=[entityName=X id={id} zone=PLAY zonePos=1 cardId=BG32_236 player={controller}] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ")
                .Power("BLOCK_END");

        public PowerLog Dies(int id, int controller) =>
            Power($"TAG_CHANGE Entity=[entityName=X id={id} zone=PLAY zonePos=1 cardId=BG32_236 player={controller}] tag=ZONE value=GRAVEYARD ");

        public BattlegroundsTracker Replay()
        {
            var parser = new PowerParser(new DateOnly(2026, 11, 19));
            var tracker = new BattlegroundsTracker { Cards = Cards, Effects = Hearthpulse.Core.Battlegrounds.Effects.EffectRegistry.Default };
            parser.AddListener(tracker);
            foreach (var line in _lines)
            {
                parser.Feed(line);
            }

            parser.Flush();
            Assert.Equal(Me, parser.Game!.LocalPlayerId);
            return tracker;
        }
    }
}

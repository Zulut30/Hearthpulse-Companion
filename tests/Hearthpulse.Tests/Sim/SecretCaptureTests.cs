using System.Globalization;
using System.Text;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Secrets;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// What the simulator knows of Battlegrounds Secrets (TASK-2026-11-19 E7a): our own from the SECRET zone, the opponent's only
/// as a count during the game (an estimate, <see cref="SimInputs.OpponentSecrets"/>), and the ids the log showed by the end
/// of the game for the post-game captures. The hero damage rule of Ice Block and Eleventh Hour counts once. Synthetic
/// GameState lines in the shape of the real log of 2026-11-15 (our Ice Block in the SECRET zone, L954458; an opponent's
/// SECRET zone copied at the combat's start, L746400). No kept log has an opponent's Akazamzarak Secret: that the count shows
/// as hidden entities is an assumption.
/// </summary>
public sealed class SecretCaptureTests
{
    private const int Me = 5;
    private const int Them = 13;
    private const int AnomalyDbf = 106633;

    private static readonly EffectRegistry Rules = Effects();

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"BG_WALL","dbfId":3001,"name":"Wall","type":"MINION","attack":1,"health":5,"techLevel":1,"text":""},
          {"id":"TB_Bacon_Secrets_02","dbfId":58500,"name":"Snake Trap","type":"SPELL","mechanics":["SECRET"],"text":"<b>Secret:</b> When one of your minions is attacked, summon three 1/1 Snakes."},
          {"id":"TB_Bacon_Secrets_04","dbfId":58502,"name":"Splitting Image","type":"SPELL","text":"<b>Secret:</b> When one of your minions is attacked, summon a copy of it."},
          {"id":"TB_BaconShop_HP_020","dbfId":58022,"name":"Prestidigitation","type":"HERO_POWER","text":"Choose a <b>Secret</b>."},
          {"id":"BG27_Anomaly_575","dbfId":106633,"name":"Eleventh Hour","type":"BATTLEGROUND_ANOMALY","text":"The first time your hero takes fatal damage in combat, prevent it and gain 11 Gold next turn instead."},
          {"id":"BG27_Anomaly_575t","dbfId":106634,"name":"Eleventh Hour","type":"SPELL","mechanics":["SECRET"],"text":"<b>Secret:</b> When your hero takes fatal damage in combat, prevent it."}
        ]
        """)));

    [Fact]
    public void Our_secrets_are_read_and_the_opponents_akazamzarak_secrets_are_a_count()
    {
        var (_, tracker) = Game()
            .Entity(60, "TB_Bacon_Secrets_02", Me, "SECRET", "SPELL", ("SECRET", "1"))
            .Entity(70, "TB_BaconShop_HP_020", Them, "PLAY", "HERO_POWER")
            .Entity(71, "", Them, "SECRET", "SPELL")
            .Entity(72, "", Them, "SECRET", "SPELL")
            .Entity(73, SimInputs.DeityObjective, Them, "SECRET", "SPELL")
            .Fight()
            .Replay(Cards, Rules);
        var capture = tracker.LastCapture!;

        Assert.Equal([SnakeTrap.Id], capture.Ours.Secrets);
        Assert.Empty(capture.Theirs.Secrets);
        var estimate = Assert.Single(capture.Theirs.Estimates, e => e.Input == SimInputs.OpponentSecrets);
        // Two hidden Secrets (the Deity's objective is not one); which ones is hidden: left out, no Ice Block assumed.
        Assert.Equal((2, 0), (estimate.Value1, estimate.Value2));
        Assert.False(DamageRules.HasIceBlock(capture.Theirs));
        Assert.Contains(tracker.LastCapture!.Theirs.Estimates, e => e.Input == SimInputs.OpponentSecrets);
    }

    [Fact]
    public void A_revealed_opponent_secret_stays_hidden_during_the_game_and_counts_after_it()
    {
        // The log shows the opponent's Splitting Image when it triggers: during the game only the count reaches the
        // simulator; once the game is over the capture carries the id for the post-game simulation.
        var log = Game()
            .Entity(70, "TB_BaconShop_HP_020", Them, "PLAY", "HERO_POWER")
            .Entity(71, "", Them, "SECRET", "SPELL")
            .Fight()
            .Show(71, SplittingImage.Id);
        var (_, live) = log.Replay(Cards, Rules);
        Assert.Empty(live.LastCapture!.Theirs.Secrets);
        Assert.All(live.Captures, c => Assert.Empty(c.Capture.Theirs.HiddenSecrets));

        var (_, ended) = log.Tag("2", "PLAYSTATE", "LOST").Replay(Cards, Rules);
        var fight = Assert.Single(ended.Captures);
        Assert.True(fight.Options.PostGame);
        Assert.Equal([SplittingImage.Id], fight.Capture.Theirs.HiddenSecrets);
        var exact = SimInputs.PostGame(fight.Capture.Theirs);
        Assert.Equal([SplittingImage.Id], exact.Secrets);
        Assert.DoesNotContain(exact.Estimates, e => e.Input == SimInputs.OpponentSecrets);
        // The live capture is untouched.
        Assert.Empty(ended.LastCapture!.Theirs.HiddenSecrets);
    }

    [Fact]
    public void In_an_eleventh_hour_lobby_the_opponents_one_secret_without_akazamzarak_blocks_lethal()
    {
        var (_, tracker) = Game()
            .Tag("GameEntity", GameTag.BaconAnomaly, AnomalyDbf.ToString(CultureInfo.InvariantCulture))
            .Entity(60, DamageRules.EleventhHour, Me, "SECRET", "SPELL", ("SECRET", "1"))
            .Entity(71, "", Them, "SECRET", "SPELL")
            .Fight()
            .Replay(Cards, Rules);
        var capture = tracker.LastCapture!;

        Assert.Equal([DamageRules.EleventhHour], capture.Ours.Secrets);
        var estimate = Assert.Single(capture.Theirs.Estimates, e => e.Input == SimInputs.OpponentSecrets);
        Assert.Equal((1, 1), (estimate.Value1, estimate.Value2));
        Assert.True(DamageRules.HasIceBlock(capture.Ours));
        Assert.True(DamageRules.HasIceBlock(capture.Theirs));
    }

    [Fact]
    public void An_opponent_who_cannot_hold_secrets_gets_no_estimate_and_none_shown_none_is_assumed()
    {
        // A hidden card in the SECRET zone of an opponent without Akazamzarak in a lobby without Eleventh Hour is not a Secret.
        var (_, plain) = Game().Entity(71, "", Them, "SECRET", "SPELL").Fight().Replay(Cards, Rules);
        Assert.DoesNotContain(plain.LastCapture!.Theirs.Estimates, e => e.Input == SimInputs.OpponentSecrets);

        // Akazamzarak with no Secret in play: nothing to estimate.
        var (_, empty) = Game().Entity(70, "TB_BaconShop_HP_020", Them, "PLAY", "HERO_POWER").Fight().Replay(Cards, Rules);
        Assert.DoesNotContain(empty.LastCapture!.Theirs.Estimates, e => e.Input == SimInputs.OpponentSecrets);
    }

    [Fact]
    public void Eleventh_hour_prevents_lethal_and_with_ice_block_it_counts_once()
    {
        var ours = Side(Minion(1, 1)) with { Health = 5, TavernTier = 3 };
        var theirs = Side(Minion(10, 10) with { TechLevel = 6 }) with { Health = 30, TavernTier = 4 };
        var options = new SimOptions { Runs = 50, Effects = EffectRegistry.Default };

        var plain = CombatSimulator.Run(ours, theirs, true, options);
        var eleventh = CombatSimulator.Run(ours with { Secrets = [DamageRules.EleventhHour] }, theirs, true, options);
        var both = CombatSimulator.Run(ours with { Secrets = [DamageRules.IceBlock, DamageRules.EleventhHour] }, theirs, true, options);

        Assert.Equal(1.0, plain.LethalTaken);
        Assert.Equal((0.0, 0.0), (eleventh.LethalTaken, eleventh.DamageTaken!.Mean));
        Assert.Equal((0.0, 0.0), (both.LethalTaken, both.DamageTaken!.Mean));
        // A hit that is not fatal goes through in full with both Secrets: nothing subtracts or blocks twice.
        var healthy = CombatSimulator.Run(ours with { Health = 40, Secrets = [DamageRules.IceBlock, DamageRules.EleventhHour] }, theirs, true, options);
        Assert.Equal(10.0, healthy.DamageTaken!.Mean);
        // Both are registered without a board hook: the fight on the board is the same with and without them.
        var bare = Fight(Side(Minion(1, 3)), Side(Minion(2, 2)), oursFirst: true);
        var blocked = Fight(Side(Minion(1, 3)) with { Secrets = [DamageRules.IceBlock, DamageRules.EleventhHour] }, Side(Minion(2, 2)), oursFirst: true);
        Assert.Equal(bare.Log, blocked.Log);
        Assert.Equal(0, plain.Unsimulated);
        Assert.Equal(0, both.Unsimulated);
    }

    [Fact]
    public void The_capture_file_keeps_the_opponents_post_game_secrets_but_never_live_ones()
    {
        var dir = TestDataFolder.Scratch("hp-e7a-captures");
        try
        {
            var theirs = Side(Minion(1, 1)) with { Secrets = ["BG_THEIR_LIVE"], HiddenSecrets = [SplittingImage.Id] };
            var fight = new SavedFight { Ours = Side(Minion(1, 1)), Theirs = theirs, TheirsInputs = SavedInputs.Of(theirs) };
            var store = new CombatCaptureStore(dir);
            var name = store.Save("e7a", new SavedCaptures { Fights = [fight] })!;

            var read = Assert.Single(store.Load(name)!.Fights).Theirs;
            Assert.Empty(read.Secrets);
            Assert.Equal([SplittingImage.Id], read.HiddenSecrets);
        }
        finally
        {
            TestDataFolder.TryDelete(dir);
        }
    }

    private static PowerLog Game() => new PowerLog().BattlegroundsGame()
        .Minion(30, "BG_WALL", Me, 1, 1, 5)
        .Minion(40, "BG_WALL", Them, 1, 1, 5);

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

        public PowerLog BattlegroundsGame()
        {
            Power("CREATE_GAME")
                .Power("    GameEntity EntityID=1")
                .Power("        tag=CARDTYPE value=GAME")
                .Power($"    Player EntityID=2 PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]")
                .Power($"        tag=CONTROLLER value={Me}")
                .Power("        tag=CARDTYPE value=PLAYER")
                .Power($"    Player EntityID=3 PlayerID={Them} GameAccountId=[hi=0 lo=0]")
                .Power($"        tag=CONTROLLER value={Them}")
                .Power("        tag=CARDTYPE value=PLAYER")
                .Power("        tag=BACON_DUMMY_PLAYER value=1");
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS");
            _lines.Add($"D 21:15:00.0000000 GameState.DebugPrintGame() - PlayerID={Me}, PlayerName=Me#1234");
            _lines.Add($"D 21:15:00.0000000 GameState.DebugPrintGame() - PlayerID={Them}, PlayerName=Bob's Opponent");
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

        public PowerLog Minion(int id, string cardId, int controller, int position, int attack, int health) =>
            Entity(id, cardId, controller, "PLAY", "MINION", ("ZONE_POSITION", position.ToString(CultureInfo.InvariantCulture)),
                   ("ATK", attack.ToString(CultureInfo.InvariantCulture)), ("HEALTH", health.ToString(CultureInfo.InvariantCulture)));

        /// <summary>The combat starts and our Wall attacks first: the boards are captured.</summary>
        public PowerLog Fight() => Power("TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=2 ")
            .Power($"BLOCK_START BlockType=ATTACK Entity={Ref(30, "Wall", "BG_WALL", Me, "PLAY")} EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ");

        public PowerLog Tag(string entity, string tag, string value) => Power($"TAG_CHANGE Entity={entity} tag={tag} value={value} ");

        /// <summary>A SHOW_ENTITY of a hidden entity: its card id.</summary>
        public PowerLog Show(int id, string cardId) => Power($"SHOW_ENTITY - Updating Entity={id} CardID={cardId}");

        public (GameState Game, BattlegroundsTracker Tracker) Replay(CardDb cards, EffectRegistry effects)
        {
            var parser = new PowerParser(new DateOnly(2026, 10, 21));
            var tracker = new BattlegroundsTracker { Cards = cards, Effects = effects };
            parser.AddListener(tracker);
            foreach (var line in _lines)
            {
                parser.Feed(line);
            }

            parser.Flush();
            Assert.Equal(Me, parser.Game!.LocalPlayerId);
            return (parser.Game, tracker);
        }
    }
}

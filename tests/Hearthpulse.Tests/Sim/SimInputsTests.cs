using System.Globalization;
using System.Text;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Inputs the simulator gets besides the boards (package I of TASK-2026-11-19): exact values the log shows the player,
/// read from synthetic Power.log lines in the shape of the owner's log of 2026-11-15 (line numbers in the comments), and
/// explicit estimates for the opponent's values the player cannot see.
/// </summary>
public sealed class SimInputsTests
{
    private const int Me = 5;
    private const int Them = 13;
    private const int OurPlayer = 2;
    private const int TheirPlayer = 3;
    private const int CThunDbf = 9001;

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes($$"""
        [
          {"id":"BG36_207","dbfId":1001,"name":"Wolf Pup","type":"MINION","attack":3,"health":6,"races":["BEAST"],"mechanics":["BACON_RALLY"],"techLevel":3,"isBattlegroundsPoolMinion":true,"text":"<b>Rally:</b> Give your other minions +4/+1."},
          {"id":"BG27_556","dbfId":1002,"name":"Diremuck Forager","type":"MINION","attack":4,"health":5,"races":["MURLOC"],"techLevel":4,"isBattlegroundsPoolMinion":true,"text":"<b>Start of Combat:</b> When you have space, summon the highest-Attack Murloc from your hand for this combat only."},
          {"id":"BG32_330","dbfId":1003,"name":"Flighty Scout","type":"MINION","attack":3,"health":3,"races":["MURLOC"],"techLevel":2,"isBattlegroundsPoolMinion":true,"text":"<b>Start of Combat:</b> If this minion is in your hand, summon a copy of it."},
          {"id":"BG36_116","dbfId":1004,"name":"Underrot Spawn","type":"MINION","attack":2,"health":2,"races":["ABERRATION"],"mechanics":["DEATHRATTLE"],"techLevel":2,"isBattlegroundsPoolMinion":true,"text":"<b>Deathrattle:</b> Summon a 1/1."},
          {"id":"BG_QB","dbfId":1005,"name":"Test Quilboar","type":"MINION","attack":2,"health":2,"races":["QUILBOAR"],"techLevel":1,"isBattlegroundsPoolMinion":true,"text":""},
          {"id":"BGS_008","dbfId":1006,"name":"Ghastcoiler","type":"MINION","attack":7,"health":7,"races":["BEAST"],"mechanics":["DEATHRATTLE"],"techLevel":6,"isBattlegroundsPoolMinion":true,"text":"<b>Deathrattle:</b> Summon 2 random <b>Deathrattle</b> minions."},
          {"id":"BGFYM_000","dbfId":{{CThunDbf}},"name":"C'Thun","type":"MINION","attack":1,"health":1,"races":["ABERRATION"],"techLevel":1,"text":""}
        ]
        """)));

    // ------------------------------------------------------------------------------------------------ exact values

    [Fact]
    public void A_hand_card_summoned_from_the_hand_this_combat_is_left_out_of_the_hand()
    {
        // G1 L277497: Flighty Scout summoned itself "for this combat only"; the card stays in HAND with BG26_350e2 attached
        // (and LITERALLY_UNPLAYABLE). Bassgill then skips it (L350734): no hand summon may take it again.
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BG36_207", Me, 1, 3, 6)
            .Minion(40, "BG36_207", Them, 1, 3, 6)
            .Entity(60, "BG32_330", Me, "HAND", "MINION", ("ATK", "3"), ("HEALTH", "3"), ("ZONE_POSITION", "1"))
            .Entity(61, "BG27_556", Me, "HAND", "MINION", ("ATK", "4"), ("HEALTH", "5"), ("ZONE_POSITION", "2"))
            .CombatStarts()
            .Enchantment(62, "BG26_350e2", Me, attachedTo: 60)
            .Tag(Ref(60, "Flighty Scout", "BG32_330", Me), "LITERALLY_UNPLAYABLE", "1")
            .Block("ATTACK", Ref(30, "Wolf Pup", "BG36_207", Me));

        var (_, tracker) = log.Replay(Cards);

        Assert.Equal(["BG27_556"], tracker.LastCapture!.Ours.Hand.Select(m => m.CardId));
    }

    [Fact]
    public void Enchantment_values_are_captured_on_both_boards()
    {
        // L180359: Blood Gems played on a minion add up on its BG20_GEMe2 (NUM_1/NUM_2 = the total, 8/8 there).
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BG_QB", Me, 1, 10, 13)
            .Minion(40, "BG_QB", Them, 1, 5, 5)
            .Enchantment(50, "BG20_GEMe2", Me, attachedTo: 30, ("TAG_SCRIPT_DATA_NUM_1", "8"), ("TAG_SCRIPT_DATA_NUM_2", "8"), ("CREATOR", "49"))
            .Enchantment(51, "BG_TEST_BUFF_e", Them, attachedTo: 40, ("ATK", "3"), ("HEALTH", "2"))
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Test Quilboar", "BG_QB", Me));

        var (_, tracker) = log.Replay(Cards);
        var capture = tracker.LastCapture!;

        var gems = Assert.Single(capture.Ours.Minions[0].EnchantmentValues);
        Assert.Equal(("BG20_GEMe2", 8, 8, 49), (gems.CardId, gems.ScriptData1, gems.ScriptData2, gems.CreatorEntityId));
        Assert.Equal(["BG20_GEMe2"], capture.Ours.Minions[0].Enchantments);
        var buff = Assert.Single(capture.Theirs.Minions[0].EnchantmentValues);
        Assert.Equal(("BG_TEST_BUFF_e", 3, 2), (buff.CardId, buff.Attack, buff.Health));
    }

    [Fact]
    public void Diremuck_pending_summons_and_the_beetle_bonus_come_from_our_player_enchantments_only()
    {
        // L348841: the Forager's Start of Combat with a full board makes BG27_556pe on the player (ATTACHED = the player
        // entity, CREATOR = the Forager) with NUM_1 = 1 summon pending. G4 L1389006: BG31_808pe NUM_1/NUM_2 = Beetle bonus.
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BG27_556", Me, 1, 4, 5)
            .Minion(40, "BG27_556", Them, 1, 4, 5)
            .Enchantment(70, "BG31_808pe", Me, attachedTo: OurPlayer, ("TAG_SCRIPT_DATA_NUM_1", "75"), ("TAG_SCRIPT_DATA_NUM_2", "73"))
            .Enchantment(71, "BG31_808pe", Them, attachedTo: TheirPlayer, ("TAG_SCRIPT_DATA_NUM_1", "9"), ("TAG_SCRIPT_DATA_NUM_2", "9"))
            .CombatStarts()
            .Block("TRIGGER", Ref(30, "Diremuck Forager", "BG27_556", Me))
            .Enchantment(72, "BG27_556pe", Me, attachedTo: OurPlayer, ("CREATOR", "30"))
            .Tag("72", "TAG_SCRIPT_DATA_NUM_1", "1")
            .End()
            .Block("ATTACK", Ref(30, "Diremuck Forager", "BG27_556", Me));

        var (_, tracker) = log.Replay(Cards);
        var capture = tracker.LastCapture!;

        Assert.Equal(1, SimInputs.PendingForagerSummons(capture.Ours, 30));
        Assert.Null(SimInputs.PendingForagerSummons(capture.Ours, 31));
        Assert.Equal((77, 75), SimInputs.BeetleStatsOf(capture.Ours));
        // Whether the client shows the opponent's player enchantments is not established: none is read.
        Assert.Empty(capture.Theirs.PlayerEnchantments);
        Assert.Null(SimInputs.BeetleStatsOf(capture.Theirs));
    }

    [Fact]
    public void Deaths_during_start_of_combat_reach_the_capture_with_who_saw_them()
    {
        // Start of Combat: our Aberration 31 dies, a minion 33 is summoned, our Aberration 32 dies, their 41 dies. The board
        // is captured at the first regular attack after that.
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BG36_207", Me, 1, 3, 6)
            .Minion(31, "BG36_116", Me, 2, 2, 2)
            .Minion(32, "BG36_116", Me, 3, 2, 2)
            .Minion(40, "BG36_207", Them, 1, 3, 6)
            .Minion(41, "BG36_116", Them, 2, 2, 2)
            .CombatStarts()
            .Block("TRIGGER", Ref(30, "Wolf Pup", "BG36_207", Me))
            .Tag(Ref(31, "Underrot Spawn", "BG36_116", Me), "ZONE", "GRAVEYARD")
            .Minion(33, "BG36_207", Me, 4, 3, 6)
            .Tag(Ref(32, "Underrot Spawn", "BG36_116", Me), "ZONE", "GRAVEYARD")
            .Tag(Ref(41, "Underrot Spawn", "BG36_116", Them), "ZONE", "GRAVEYARD")
            .End()
            .Block("ATTACK", Ref(30, "Wolf Pup", "BG36_207", Me));

        var (_, tracker) = log.Replay(Cards);
        var capture = tracker.LastCapture!;

        Assert.Equal([31, 32], capture.Ours.DeathsBeforeCapture.Select(m => m.EntityId));
        Assert.All(capture.Ours.DeathsBeforeCapture, m => Assert.Equal(Tribe.Aberration, m.Tribes));
        Assert.Equal([41], capture.Theirs.DeathsBeforeCapture.Select(m => m.EntityId));
        Assert.Equal([(30, 2), (33, 1)], capture.Ours.Minions.Select(m => (m.EntityId, m.FriendlyDeathsBeforeCapture)));
        Assert.Equal(1, Assert.Single(capture.Theirs.Minions).FriendlyDeathsBeforeCapture);
        Assert.Equal(2, SimInputs.DeityDeathsStillNeeded(capture.Ours, Tokens.CThunId, Deity.AberrationDeathsToAwaken));
    }

    [Fact]
    public void Lobby_tribes_from_client_memory_reach_both_sides()
    {
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BG36_207", Me, 1, 3, 6)
            .Minion(40, "BG36_207", Them, 1, 3, 6)
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Wolf Pup", "BG36_207", Me));

        var (_, tracker) = log.Replay(Cards, races: ["BEAST", "MECHANICAL", "MURLOC", "UNDEAD", "NAGA"]);
        var capture = tracker.LastCapture!;

        var lobby = Tribe.Beast | Tribe.Mech | Tribe.Murloc | Tribe.Undead | Tribe.Naga;
        Assert.Equal(lobby, capture.Ours.LobbyTribes);
        Assert.Equal(lobby, capture.Theirs.LobbyTribes);
        Assert.Empty(capture.Ours.Estimates);
        Assert.Empty(capture.Theirs.Estimates);
    }

    // ------------------------------------------------------------------------------------------- estimated vs exact

    [Fact]
    public void Hidden_opponent_deity_stats_are_estimated_from_ours_and_counted_apart_from_not_simulated()
    {
        // Their Deity has no stats in the log (tag 0), an Aberration can wake it: an explicit estimate, our own 10/12.
        var log = new PowerLog()
            .BattlegroundsGame(ourTags: [("BACON_OLD_GOD_ATTACK", "10"), ("BACON_OLD_GOD_HEALTH", "12")], theirTags: [("BACON_OLD_GOD_ATTACK", "0")])
            .Deity(CThunDbf)
            .Minion(30, "BG36_116", Me, 1, 2, 2)
            .Minion(40, "BG36_116", Them, 1, 2, 2)
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Underrot Spawn", "BG36_116", Me));

        var (game, tracker) = log.Replay(Cards, EffectRegistry.Empty);
        var capture = tracker.LastCapture!;

        Assert.Empty(capture.Ours.Estimates);
        var deity = Assert.Single(capture.Theirs.Estimates);
        Assert.Equal((SimInputs.DeityStats, 10, 12), (deity.Input, deity.Value1, deity.Value2));
        Assert.Equal(10, capture.Theirs.Tags[Deity.AttackTag]);
        Assert.Equal(SimInputs.DeityStats, Assert.Single(CombatSimulator.ExplainEstimates(capture.Theirs, isOpponent: true)).Input);
        var odds = tracker.BuildView(game).CombatOdds!;
        Assert.Equal(1, odds.Estimated);
        // "Not simulated" is what Explain lists (cards without an effect in the empty registry), the estimate is not in it.
        var unsimulated = CombatSimulator.Explain(capture.Ours, isOpponent: false, EffectRegistry.Empty).Count
                          + CombatSimulator.Explain(capture.Theirs, isOpponent: true, EffectRegistry.Empty).Count;
        Assert.Equal(unsimulated, odds.Unsimulated);
        Assert.DoesNotContain(CombatSimulator.Explain(capture.Theirs, isOpponent: true, EffectRegistry.Empty), i => i.Reason == UnsimulatedReason.MissingInput);
    }

    [Fact]
    public void Opponent_values_the_log_has_are_used_as_they_are()
    {
        // Their Deity 726/793 (G1 Gimrion) and Blood Gem value are in the log: no estimate.
        var log = new PowerLog()
            .BattlegroundsGame(theirTags: [("BACON_OLD_GOD_ATTACK", "726"), ("BACON_OLD_GOD_HEALTH", "793"), ("BACON_BLOODGEMBUFFATKVALUE", "11"), ("BACON_BLOODGEMBUFFHEALTHVALUE", "5")])
            .Deity(CThunDbf)
            .Minion(30, "BG36_116", Me, 1, 2, 2)
            .Minion(40, "BG36_116", Them, 1, 2, 2)
            .Minion(41, "BG_QB", Them, 2, 2, 2)
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Underrot Spawn", "BG36_116", Me));

        var (game, tracker) = log.Replay(Cards, EffectRegistry.Empty);

        Assert.Empty(tracker.LastCapture!.Theirs.Estimates);
        Assert.Equal(726, tracker.LastCapture.Theirs.Tags[Deity.AttackTag]);
        Assert.Equal(0, tracker.BuildView(game).CombatOdds!.Estimated);
    }

    [Fact]
    public void A_missing_opponent_blood_gem_value_and_unknown_lobby_tribes_are_estimated()
    {
        var log = new PowerLog()
            .BattlegroundsGame()
            .Minion(30, "BGS_008", Me, 1, 7, 7)
            .Minion(40, "BG_QB", Them, 1, 2, 2)
            .CombatStarts()
            .Block("ATTACK", Ref(30, "Ghastcoiler", "BGS_008", Me));

        var (_, tracker) = log.Replay(Cards, EffectRegistry.Empty);
        var capture = tracker.LastCapture!;

        Assert.Equal([SimInputs.LobbyTribes], capture.Ours.Estimates.Select(e => e.Input));
        Assert.Equal([SimInputs.BloodGemBonus], capture.Theirs.Estimates.Select(e => e.Input));
    }

    [Fact]
    public void Run_counts_both_sides_estimates_and_keeps_them_out_of_unsimulated()
    {
        var theirs = Side(Minion(1, 1)) with
        {
            Estimates = [new SimEstimate(SimInputs.DeityStats, 1, 1, "x"), new SimEstimate(SimInputs.BeetleStats, 2, 2, "y")],
        };

        var odds = CombatSimulator.Run(Side(Minion(5, 5)), theirs, true, new SimOptions { Runs = 10 });

        Assert.Equal((2, 0), (odds.Estimated, odds.Unsimulated));
        Assert.False(odds.Approximate);
    }

    // ---------------------------------------------------------------------------------------- effects use the inputs

    [Fact]
    public void A_diremuck_forager_with_nothing_pending_summons_nothing_on_a_full_board()
    {
        // Full board, a 5/2 Murloc in hand. The player enchantment says no summon is pending (NUM_1 = 0): when a
        // bystander dies, nothing comes. Without the enchantment (older capture) the Forager still waits for space.
        var forager = Minion(4, 5, cardId: "BG27_556", tribes: Tribe.Murloc) with { EntityId = 30 };
        var full = Side(forager, Minion(0, 1), Minion(0, 1), Minion(0, 1), Minion(0, 1), Minion(0, 1), Minion(0, 1))
            with { Hand = [Minion(5, 2, cardId: "MUR", tribes: Tribe.Murloc)] };
        SimSide Pending(int count) => full with { PlayerEnchantments = [new SimEnchantment(SimInputs.DiremuckEnchantment) { CreatorEntityId = 30, ScriptData1 = count }] };

        var none = Fight(Pending(0), Side(Minion(1, 3)), oursFirst: false, Effects(typeof(DiremuckForager)), 1);
        var one = Fight(Pending(1), Side(Minion(1, 3)), oursFirst: false, Effects(typeof(DiremuckForager)), 1);
        var older = Fight(full, Side(Minion(1, 3)), oursFirst: false, Effects(typeof(DiremuckForager)), 1);

        Assert.Empty(none.Lines("summoned"));
        Assert.Equal(["summoned our MUR 5/2 at 1"], one.Lines("summoned"));
        Assert.Equal(["summoned our MUR 5/2 at 1"], older.Lines("summoned"));
    }

    [Fact]
    public void Stormpike_follows_the_hero_power_flag()
    {
        // A full board without a copy: by the board alone a copy is owed. The flag (NUM_1) says whether it really is.
        SimSide Full(int? flag) => Side(Minion(1, 1, cardId: "A"), Minion(2, 9, cardId: "H"), Minion(1, 1), Minion(1, 1), Minion(1, 1), Minion(1, 1), Minion(1, 1)) with
        {
            Turn = 8,
            Extras = ["BG22_HERO_003p"],
            ExtraTags = flag is { } value
                ? new Dictionary<string, IReadOnlyDictionary<string, int>> { ["BG22_HERO_003p"] = new Dictionary<string, int> { ["TAG_SCRIPT_DATA_NUM_1"] = value } }
                : SimSide.EmptyExtraTags,
        };

        Assert.DoesNotContain(Fight(Full(0), Side(Minion(1, 1)), false, Effects(typeof(SpaceCopyHeroPower))).Log, l => l.StartsWith("summoned", StringComparison.Ordinal));
        Assert.Single(Fight(Full(1), Side(Minion(1, 1)), false, Effects(typeof(SpaceCopyHeroPower))).Log, l => l.StartsWith("summoned our H 2/9", StringComparison.Ordinal));
        Assert.Single(Fight(Full(null), Side(Minion(1, 1)), false, Effects(typeof(SpaceCopyHeroPower))).Log, l => l.StartsWith("summoned our H 2/9", StringComparison.Ordinal));
    }

    [Fact]
    public void A_beetle_without_a_shown_summoner_uses_the_beetle_army_bonus_or_the_estimate()
    {
        // Buzzing Vermin made without script data: ours takes the Beetle Army enchantment (2/2 + 75/73), theirs the estimate.
        var vermin = Minion(1, 1, cardId: "BG31_803", tribes: Tribe.Beast);
        var ours = Side(vermin) with { PlayerEnchantments = [new SimEnchantment(SimInputs.BeetleArmyEnchantment) { ScriptData1 = 75, ScriptData2 = 73 }] };
        var theirs = Side(vermin) with { Estimates = [new SimEstimate(SimInputs.BeetleStats, 5, 6, "test")] };

        var ourFight = Fight(ours, Side(Minion(5, 300)), oursFirst: false, Effects(typeof(BuzzingVermin)));
        var theirFight = Fight(Side(Minion(5, 300)), theirs, oursFirst: true, Effects(typeof(BuzzingVermin)));

        Assert.Contains(ourFight.Log, l => l.StartsWith("summoned our BG28_603t 77/75", StringComparison.Ordinal));
        Assert.Contains(theirFight.Log, l => l.StartsWith("summoned their BG28_603t 5/6", StringComparison.Ordinal));
    }

    [Fact]
    public void Aberrations_that_died_in_start_of_combat_bring_the_deity_closer()
    {
        // Three Aberrations died before the capture: the fourth death in the fight awakens C'Thun.
        var dead = Minion(0, 1, cardId: "AB", tribes: Tribe.Aberration);
        var ours = new SimSide([Minion(0, 1, cardId: "AB4", tribes: Tribe.Aberration)])
        {
            Extras = [Tokens.CThunId],
            Tags = new Dictionary<string, int> { [Deity.AttackTag] = 3, [Deity.HealthTag] = 2 },
            DeathsBeforeCapture = [dead, dead, dead],
        };

        var woke = Fight(ours, Side(Minion(1, 50)), oursFirst: false, Effects(typeof(CThun)));
        var asleep = Fight(ours with { DeathsBeforeCapture = [] }, Side(Minion(1, 50)), oursFirst: false, Effects(typeof(CThun)));

        Assert.True(woke.IndexOf("Deity BGFYM_000 awakens") > 0);
        Assert.Equal(-1, asleep.IndexOf("awakens"));
        // Already awake before the capture (on the board): it does not awaken twice.
        Assert.Equal(int.MaxValue, SimInputs.DeityDeathsStillNeeded(ours with { Minions = [Minion(3, 2, cardId: Tokens.CThunId)] }, Tokens.CThunId, 4));
    }

    [Fact]
    public void Random_summons_draw_only_lobby_tribes_and_tribeless_minions()
    {
        var pool = GhastcoilerPool.Minions;
        var lobby = Tribe.Beast | Tribe.Mech | Tribe.Murloc | Tribe.Undead | Tribe.Naga;

        var drawn = SimInputs.InLobby(pool, lobby);

        Assert.NotEmpty(drawn);
        Assert.True(drawn.Count < pool.Count);
        Assert.All(drawn, m => Assert.True(m.Tribes == Tribe.None || (m.Tribes & lobby) != 0, m.CardId));
        Assert.Contains(drawn, m => (m.Tribes & Tribe.Undead) != 0);
        Assert.DoesNotContain(drawn, m => m.Tribes == Tribe.Aberration);
        Assert.Same(drawn, SimInputs.InLobby(pool, lobby));
        Assert.Same(pool, SimInputs.InLobby(pool, Tribe.None));
    }

    [Fact]
    public void A_scarlet_survivor_that_already_triggered_does_not_gain_divine_shield_again()
    {
        // NUM_2 = 1 once it has triggered (G2 L717505). It enters at 5 Attack and Wolf Pup's Rally lifts it to 9.
        SimSide Ours(int flag) => Side(Minion(3, 6, cardId: "BG36_207"), Minion(5, 5, cardId: "BG35_814") with { ScriptData2 = flag });

        var fresh = Fight(Ours(0), Side(Minion(0, 30)), true, Effects(typeof(WolfPup), typeof(ScarletSurvivor)));
        var done = Fight(Ours(1), Side(Minion(0, 30)), true, Effects(typeof(WolfPup), typeof(ScarletSurvivor)));

        Assert.True(fresh.IndexOf("gains Divine Shield") >= 0);
        Assert.Equal(-1, done.IndexOf("gains Divine Shield"));
    }

    private static string Ref(int id, string name, string cardId, int player) =>
        $"[entityName={name} id={id} zone=PLAY zonePos=1 cardId={cardId} player={player}]";

    /// <summary>Builds GameState lines of a Battlegrounds Power.log (as in <see cref="CaptureTests"/>).</summary>
    private sealed class PowerLog
    {
        private readonly List<string> _lines = new();

        public PowerLog Power(string body)
        {
            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintPower() - " + body);
            return this;
        }

        public PowerLog BattlegroundsGame((string Tag, string Value)[]? ourTags = null, (string Tag, string Value)[]? theirTags = null)
        {
            Power("CREATE_GAME")
                .Power("    GameEntity EntityID=1")
                .Power("        tag=CARDTYPE value=GAME")
                .Power($"    Player EntityID={OurPlayer} PlayerID={Me} GameAccountId=[hi=144115198130930503 lo=11]")
                .Power($"        tag=CONTROLLER value={Me}")
                .Power("        tag=CARDTYPE value=PLAYER");
            foreach (var (tag, value) in ourTags ?? [])
            {
                Power($"        tag={tag} value={value}");
            }

            Power($"    Player EntityID={TheirPlayer} PlayerID={Them} GameAccountId=[hi=0 lo=0]")
                .Power($"        tag=CONTROLLER value={Them}")
                .Power("        tag=CARDTYPE value=PLAYER")
                .Power("        tag=BACON_DUMMY_PLAYER value=1");
            foreach (var (tag, value) in theirTags ?? [])
            {
                Power($"        tag={tag} value={value}");
            }

            _lines.Add("D 21:15:00.0000000 GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS");
            _lines.Add($"D 21:15:00.0000000 GameState.DebugPrintGame() - PlayerID={Me}, PlayerName=Me#1234");
            _lines.Add($"D 21:15:00.0000000 GameState.DebugPrintGame() - PlayerID={Them}, PlayerName=Bob's Opponent");
            return this;
        }

        /// <summary>The lobby's Deity, named by the game entity.</summary>
        public PowerLog Deity(int dbfId) => Tag("GameEntity", GameTag.BaconDeity, dbfId.ToString(CultureInfo.InvariantCulture));

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

        public PowerLog Enchantment(int id, string cardId, int controller, int attachedTo, params (string Tag, string Value)[] tags) =>
            Entity(id, cardId, controller, "PLAY", "ENCHANTMENT", [("ATTACHED", attachedTo.ToString(CultureInfo.InvariantCulture)), .. tags]);

        public PowerLog Minion(int id, string cardId, int controller, int position, int attack, int health, params (string Tag, string Value)[] tags) =>
            Entity(id, cardId, controller, "PLAY", "MINION",
                   [("ZONE_POSITION", position.ToString(CultureInfo.InvariantCulture)), ("ATK", attack.ToString(CultureInfo.InvariantCulture)),
                    ("HEALTH", health.ToString(CultureInfo.InvariantCulture)), .. tags]);

        public PowerLog CombatStarts() => Power("TAG_CHANGE Entity=GameEntity tag=BOARD_VISUAL_STATE value=2 ");

        public PowerLog Block(string type, string entity) =>
            Power($"BLOCK_START BlockType={type} Entity={entity} EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ");

        public PowerLog Tag(string entity, string tag, string value) => Power($"TAG_CHANGE Entity={entity} tag={tag} value={value} ");

        public PowerLog End() => Power("BLOCK_END");

        public (GameState Game, BattlegroundsTracker Tracker) Replay(CardDb cards, EffectRegistry? effects = null, IReadOnlyList<string>? races = null)
        {
            var parser = new PowerParser(new DateOnly(2026, 10, 21));
            var tracker = new BattlegroundsTracker { Cards = cards, Effects = effects ?? EffectRegistry.Default };
            parser.AddListener(tracker);
            var applied = false;
            foreach (var line in _lines)
            {
                parser.Feed(line);
                // Client memory is read once the game is running (OnGameStarted clears it).
                if (!applied && races is not null && parser.Game is { IsBattlegrounds: true })
                {
                    tracker.MemoryRaces = races;
                    applied = true;
                }
            }

            parser.Flush();
            Assert.Equal(Me, parser.Game!.LocalPlayerId);
            return (parser.Game, tracker);
        }
    }
}

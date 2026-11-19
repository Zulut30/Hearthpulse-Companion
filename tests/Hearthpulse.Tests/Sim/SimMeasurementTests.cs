using System.Globalization;
using Hearthpulse.Bench;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.BattlegroundsReplayTests;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// The simulator's measurement (Bench <c>simcover</c>, docs/bg-combat-sim.md): its statistics, the fights it reads from
/// a log, how it scores and compares them, and what a local replay holds of a fight.
/// </summary>
public sealed class SimMeasurementTests
{
    private static readonly DateOnly Day = new(2026, 10, 23);

    // --------------------------------------------------------------------------------------------------- statistics

    [Fact]
    public void Wilson_interval_matches_a_hand_calculation()
    {
        // 66 of 76: p = 0.8684, z = 1.96 -> centre 0.8508, half-width 0.0775 (the interval docs/bg-combat-sim.md quotes).
        var (low, high) = SimStats.Wilson(66, 76);
        Assert.Equal(0.774, low, 3);
        Assert.Equal(0.927, high, 3);
        Assert.Equal((0, 0), SimStats.Wilson(0, 0));
        Assert.Equal(1, SimStats.Wilson(5, 5).High, 9);
    }

    [Theory]
    [InlineData(0, 0, 1.0)]
    // Five pairs changed, all one way: 2 * (1/2)^5.
    [InlineData(5, 0, 0.0625)]
    [InlineData(0, 5, 0.0625)]
    // Ten changed, 8 against 2: 2 * (1 + 10 + 45) / 1024.
    [InlineData(8, 2, 0.109375)]
    // Even split: the two tails overlap, p is capped at 1.
    [InlineData(3, 3, 1.0)]
    [InlineData(1000, 1000, 1.0)]
    [InlineData(1000, 1001, 1.0)]
    [InlineData(1001, 999, 0.9821609888541457)]
    [InlineData(1100, 900, 8.457089535503927e-6)]
    public void Exact_mcnemar_is_the_two_sided_binomial_of_the_changed_pairs(int gained, int lost, double p) =>
        Assert.Equal(p, SimStats.McNemarExact(gained, lost), 9);

    [Fact]
    public void A_paired_share_counts_only_the_fights_that_changed()
    {
        var paired = SimStats.Paired([true, true, false, false, false], [true, false, true, true, false]);

        Assert.Equal(new PairedShare(5, 2, 3, 2, 1, 1.0), paired);
        Assert.Throws<ArgumentException>(() => SimStats.Paired([true], [true, false]));
    }

    [Fact]
    public void A_paired_mean_has_its_interval_from_two_pairs_on()
    {
        // Differences -0.1, -0.3, 0.1: mean -0.1, s = 0.2, half-width 1.96 * 0.2 / sqrt(3) = 0.2263.
        var mean = SimStats.Mean([-0.1, -0.3, 0.1]);
        Assert.Equal(-0.1, mean.Mean, 9);
        Assert.Equal(-0.3263, mean.Low!.Value, 4);
        Assert.Equal(0.1263, mean.High!.Value, 4);
        Assert.Null(SimStats.Mean([0.5]).Low);
        Assert.Equal(0, SimStats.Mean([]).Pairs);
    }

    [Fact]
    public void Brier_sums_the_squared_error_over_the_three_results()
    {
        Assert.Equal(0.0, SimStats.Brier(1, 0, 0, "Win")!.Value, 9);
        Assert.Equal(2.0, SimStats.Brier(1, 0, 0, "Loss")!.Value, 9);
        // 0.6 / 0.1 / 0.3 and a tie: 0.36 + 0.81 + 0.09.
        Assert.Equal(1.26, SimStats.Brier(0.6, 0.1, 0.3, "Tie")!.Value, 9);
        Assert.Null(SimStats.Brier(1, 0, 0, "Unknown"));
    }

    // --------------------------------------------------------------------------------------------- fights of a log

    [Fact]
    public void The_fights_of_a_log_come_with_the_boards_and_options_the_app_simulated()
    {
        var game = Assert.Single(SimSample.Read(BattlegroundsFixtureTests.Lines(), "session/Power.log", Day, CardDb.Empty));

        Assert.Equal("session/Power.log#1", game.Key);
        Assert.False(game.Duos);
        Assert.Equal(254477, game.Build);
        var fight = Assert.Single(game.Fights);
        Assert.Equal(("session/Power.log#1/1", 1, "Tie"), (fight.Key, fight.Turn, fight.Result));
        Assert.NotNull(fight.Capture);
        Assert.NotEmpty(fight.Capture!.Ours.Minions);
        Assert.NotEmpty(fight.Capture.Theirs.Minions);
        // What the tracker was given besides the boards: the lobby's damage cap of turn 1 (BattlegroundsFixtureTests).
        Assert.Equal(5, fight.Options!.DamageCap);
        Assert.True(fight.Options.CountDamage);
    }

    [Fact]
    public void A_log_without_a_battlegrounds_game_has_no_fights() =>
        Assert.Empty(SimSample.Read(["D 12:30:00.0000000 GameState.DebugPrintPower() - CREATE_GAME"], "x", Day, CardDb.Empty));

    [Fact]
    public void The_budgets_are_the_apps_own_passes_and_the_full_run()
    {
        var captured = new SimOptions { DamageCap = 5, CountDamage = false, FirstAttackerEntityId = 7 };
        var app = new BattlegroundsTracker().SimOptions;

        var quick = SimSample.Budgeted(captured, SimBudget.Quick);
        Assert.Equal(BattlegroundsTracker.QuickPass(captured), quick);
        Assert.True(quick.Runs < app.Runs && quick.TimeBudget < app.TimeBudget);
        Assert.Equal(captured with
        {
            Runs = app.Runs, MinRuns = app.MinRuns, TimeBudget = app.TimeBudget, TargetHalfWidth = app.TargetHalfWidth, Parallelism = app.Parallelism,
        }, SimSample.Budgeted(captured, SimBudget.App));
        var full = SimSample.Budgeted(captured, SimBudget.Full);
        Assert.Equal((SimOptions.Default.Runs, SimOptions.Default.Runs, (TimeSpan?)null, 0.0), (full.Runs, full.MinRuns, full.TimeBudget, full.TargetHalfWidth));
        // The fight's own facts stay as captured under every budget.
        Assert.All(new[] { quick, full }, o => Assert.Equal((5, false, 7), (o.DamageCap, o.CountDamage, o.FirstAttackerEntityId)));
    }

    [Fact]
    public void A_fight_scored_under_the_full_budget_runs_every_fight_and_is_the_same_every_time()
    {
        var fight = Assert.Single(Assert.Single(SimSample.Read(BattlegroundsFixtureTests.Lines(), "s", Day, CardDb.Empty)).Fights);

        var first = SimSample.Score(fight, SimBudget.Full)!;
        var second = SimSample.Score(fight, SimBudget.Full)!;

        Assert.Equal(SimSample.FullRuns, first.Runs);
        Assert.Equal(first, second);
        Assert.Equal(1.0, first.Win + first.Tie + first.Loss, 9);
        Assert.Null(SimSample.Score(fight with { Capture = null }, SimBudget.Full));
    }

    [Theory]
    [InlineData("Win", 4, 0, true, true, true)]
    // The damage dealt is outside the forecast's 10th to 90th percentile.
    [InlineData("Win", 9, 0, true, true, false)]
    [InlineData("Loss", 0, 3, false, true, true)]
    // A tie has no damage to check; neither has a result read from the boards with no health change.
    [InlineData("Tie", 0, 0, false, false, false)]
    [InlineData("Win", 0, 0, true, false, false)]
    public void A_score_counts_result_and_damage_as_the_battlegrounds_page_does(string result, int dealt, int taken, bool hit, bool seen, bool damageHit)
    {
        var odds = new CombatOdds(0.6, 0.1, 0.3, 0)
        {
            Runs = 100,
            DamageDealt = new DamageSpread(4, 2, 6, 8, []),
            DamageTaken = new DamageSpread(3, 1, 5, 7, []),
        };
        var fight = new SimFight("k", 3, result, taken, dealt, null, null);

        var score = SimSample.Score(fight, SimBudget.App, odds);

        Assert.Equal((hit, seen, damageHit, true), (score.Hit, score.DamageSeen, score.DamageHit, score.Clean));
        // The same fight as a history row, counted by the page's own accuracy line.
        var record = new GameRecord
        {
            Key = "k",
            Mode = "Battlegrounds",
            CombatResults = System.Text.Json.JsonSerializer.Serialize(new[] { new { turn = 3, result, opponent = "H", taken, dealt } }),
            CombatOdds = GameRecords.CombatOddsJson([new CombatRecord(3, 4, "H", result, taken, dealt, odds)]),
        };
        var page = BattlegroundsStats.SimAccuracy([record]);
        Assert.Equal((1, hit ? 1 : 0, seen ? 1 : 0, damageHit ? 1 : 0), (page.Fights, page.ResultHits, page.DamageFights, page.DamageHits));
    }

    [Fact]
    public void Scores_survive_the_per_fight_file()
    {
        SimScore[] scores =
        [
            new("s/Power.log#1/1", SimBudget.Quick, "Win", 150, 0.61234567890123, 0.1, 0.28765432109877, 0, true, true, false, 0.24),
            new("s/Power.log#1/2", SimBudget.Full, "Unknown", 0, 0, 1, 0, 2, false, false, false, null),
        ];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        SimSample.Write(writer, scores);
        var read = SimSample.Parse(writer.ToString().Split('\n').Select(l => l.TrimEnd('\r')));

        Assert.Equal(scores, read);
        Assert.Throws<FormatException>(() => SimSample.Parse(["not,a,score"]));
    }

    [Fact]
    public void Two_runs_are_compared_fight_by_fight_within_a_budget()
    {
        static SimScore S(string key, SimBudget budget, int unsimulated, bool hit, double brier, bool damageSeen = false, bool damageHit = false) =>
            new(key, budget, "Win", 100, 0.5, 0, 0.5, unsimulated, hit, damageSeen, damageHit, brier);
        SimScore[] before =
        [
            S("a", SimBudget.Full, 1, false, 0.50, damageSeen: true), S("b", SimBudget.Full, 1, true, 0.20), S("c", SimBudget.Full, 0, true, 0.10),
            S("a", SimBudget.Quick, 1, false, 0.60),
        ];
        SimScore[] after =
        [
            // Written in another order: fights pair up by key.
            S("c", SimBudget.Full, 0, false, 0.30), S("a", SimBudget.Full, 0, true, 0.10, damageSeen: true, damageHit: true), S("b", SimBudget.Full, 0, true, 0.20),
            S("a", SimBudget.Quick, 1, false, 0.60),
        ];

        var compared = SimSample.Compare(before, after);

        Assert.Equal([SimBudget.Quick, SimBudget.Full], compared.Select(c => c.Budget));
        var full = compared[1];
        // "not simulated" left two fights and came to none; one result was gained and one lost.
        Assert.Equal(new PairedShare(3, 1, 3, 2, 0, 0.5), full.Clean);
        Assert.Equal(new PairedShare(3, 2, 2, 1, 1, 1.0), full.Hit);
        Assert.Equal(new PairedShare(1, 0, 1, 1, 0, 1.0), full.Damage);
        // Brier differences -0.4, 0, +0.2: mean -0.0667.
        Assert.Equal(-0.0667, full.Brier.Mean, 4);
        Assert.Equal(3, full.Brier.Pairs);
        Assert.Equal(new PairedShare(1, 0, 0, 0, 0, 1.0), compared[0].Clean);
    }

    [Fact]
    public void Runs_over_different_fights_are_not_compared()
    {
        SimScore[] before = [new("a", SimBudget.Full, "Win", 1, 1, 0, 0, 0, true, false, false, 0)];

        Assert.Throws<InvalidOperationException>(() => SimSample.Compare(before, [before[0] with { Key = "b" }]));
        Assert.Throws<InvalidOperationException>(() => SimSample.Compare(before, [before[0] with { Result = "Loss" }]));
        Assert.Throws<InvalidOperationException>(() => SimSample.Compare(before, []));
        var b = before[0] with { Key = "b" };
        Assert.Throws<InvalidOperationException>(() => SimSample.Compare([before[0], before[0]], [before[0], b]));
        Assert.Throws<InvalidOperationException>(() => SimSample.Compare([before[0], b], [before[0], before[0]]));
        Assert.Throws<InvalidOperationException>(() => SimSample.Compare([], before));
    }

    // -------------------------------------------------------------------------------------------------- replays

    /// <summary>A turn with a combat: our 4/6 carries an enchantment and a script value when the first attack starts.</summary>
    private static string[] EnchantedFight() =>
    [
        .. Setup,
        Tag(3, "BACON_CURRENT_COMBAT_PLAYER_ID", 4),
        // The whole of Bob's offer goes when the combat starts (CombatSetup takes the other two).
        Tag(51, "ZONE", "REMOVEDFROMGAME"),
        .. CombatSetup(),
        .. Entity(130, "BG28_503e", Me, "PLAY", "ENCHANTMENT", "ATTACHED=101"),
        Tag(101, "TAG_SCRIPT_DATA_NUM_1", 7),
        Tag(101, "DEATHRATTLE", 1),
        .. Fight(win: true),
    ];

    [Fact]
    public void A_replay_holds_both_tables_at_the_first_attack_as_the_simulator_got_them()
    {
        var (game, recorder, tracker) = Play(EnchantedFight());
        var replay = BattlegroundsMatchReplay.Parse(recorder.Battlegrounds.Text(new ReplayHeader(), tracker.ReplayData(game)))!;

        var fight = Assert.Single(ReplayFights.Of(replay));
        var capture = tracker.LastCapture!;

        static List<(int, int, bool, bool)> Seen(IEnumerable<BgReplayCard> cards) => cards.Select(c => (c.Attack, c.Health, c.Taunt, c.DivineShield)).ToList();
        static List<(int, int, bool, bool)> Captured(SimSide side) => side.Minions.Select(m => (m.Attack, m.Health, m.Taunt, m.DivineShield)).ToList();
        Assert.True(fight.FirstAttack);
        Assert.Equal("WIN", fight.Result);
        Assert.Equal([(4, 6, false, false)], Seen(fight.Ours));
        Assert.Equal(Captured(capture.Ours), Seen(fight.Ours));
        Assert.Equal([(1, 2, false, false), (2, 3, false, false)], Seen(fight.Theirs));
        Assert.Equal(Captured(capture.Theirs), Seen(fight.Theirs));
        Assert.Equal(["BG26_801"], fight.Ours.Select(c => c.CardId));
    }

    [Fact]
    public void A_replay_does_not_hold_the_rest_of_what_a_fight_is_captured_with()
    {
        var (game, recorder, tracker) = Play(EnchantedFight());
        var text = recorder.Battlegrounds.Text(new ReplayHeader(), tracker.ReplayData(game));

        // The capture has them...
        var ours = Assert.Single(tracker.LastCapture!.Ours.Minions);
        Assert.Equal(["BG28_503e"], ours.Enchantments);
        Assert.Equal(7, ours.ScriptData1);
        Assert.True((ours.Mechanics & CardMechanic.Deathrattle) != 0);
        // ...the replay does not, and its grammar has no line that could: the format would have to change (ADR 0004).
        Assert.DoesNotContain("BG28_503e", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TAG_SCRIPT_DATA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEATHRATTLE", text, StringComparison.Ordinal);
        Assert.True(BattlegroundsReplayGrammar.IsLine("E 5 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6"));
        Assert.False(BattlegroundsReplayGrammar.IsLine("E 6 L PLAY 0 BG28_503e ENCHANTMENT"));
        Assert.False(BattlegroundsReplayGrammar.IsLine("E 5 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6 TAG_SCRIPT_DATA_NUM_1=7"));
        Assert.False(BattlegroundsReplayGrammar.IsLine("V 5 TAG_SCRIPT_DATA_NUM_1 7"));
        Assert.False(BattlegroundsReplayGrammar.IsLine("V 5 DEATHRATTLE 1"));
        Assert.False(BattlegroundsReplayGrammar.IsLine("V 5 CREATOR 9"));
        Assert.NotEmpty(ReplayFights.Missing);
    }

    [Fact]
    public void A_real_games_replay_gives_its_fight_with_the_boards_of_the_first_attack()
    {
        var parser = new PowerParser(Day);
        var tracker = new BattlegroundsTracker();
        var recorder = new ReplayRecorder();
        parser.AddListener(tracker);
        parser.AddListener(recorder);
        foreach (var line in BattlegroundsFixtureTests.Lines())
        {
            parser.Feed(line);
        }

        parser.Flush();
        var replay = BattlegroundsMatchReplay.Parse(recorder.Battlegrounds.Text(new ReplayHeader(), tracker.ReplayData(parser.Game!)))!;

        var fight = Assert.Single(ReplayFights.Of(replay));
        var capture = tracker.LastCapture!;

        Assert.Equal(1, fight.Turn);
        Assert.True(fight.FirstAttack);
        Assert.Equal(capture.Ours.Minions.Select(m => (m.Attack, m.Health)), fight.Ours.Select(c => (c.Attack, c.Health)));
        Assert.Equal(capture.Theirs.Minions.Select(m => (m.Attack, m.Health)), fight.Theirs.Select(c => (c.Attack, c.Health)));
    }

    /// <summary>A replay file with the given mode and event lines, each a line of the format's grammar.</summary>
    private static string Replay(string mode, params string[] events)
    {
        Assert.All(events, e => Assert.True(BattlegroundsReplayGrammar.IsLine(e), e));
        var header = new ReplayHeader { Summary = new ReplaySummary { Mode = mode } };
        return ReplayFile.WriteBattlegrounds(header, new BattlegroundsReplayData(), events);
    }

    [Fact]
    public void Replays_are_counted_and_duos_fights_are_checked_for_both_tables_of_the_team()
    {
        string[] solo =
        [
            "T 1 10", "E 1 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6",
            "F 2 60", "E 2 O PLAY 1 BGS_119 MINION ATK=1 HEALTH=2",
            "M ATTACK 1 2", "Y 2", "f WIN 0 3",
        ];
        string[] duos =
        [
            "T 1 10", "E 1 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6",
            // Our table fights, dies, and our partner's comes in.
            "F 2 60", "E 2 O PLAY 1 BGS_119 MINION ATK=9 HEALTH=9",
            "M ATTACK 2 1", "Y 1",
            "M DEATHS -", "E 3 P PLAY 1 BGS_061 MINION ATK=2 HEALTH=3",
            "M ATTACK 2 3", "Y 3", "f LOSS 5 0",
            // The next fight is over before our partner's table is needed.
            "T 3 90", "E 4 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6",
            "F 4 120", "E 5 O PLAY 1 BGS_119 MINION ATK=1 HEALTH=2",
            "M ATTACK 4 5", "Y 5", "f WIN 0 3",
        ];

        var inventory = ReplayFights.Inventory([Replay("Battlegrounds", solo), Replay("Duos", duos), "not a replay", string.Empty]);

        Assert.Equal(new ReplayInventory(Games: 2, Fights: 3, FightsWithFirstAttack: 3, DuosGames: 1, DuosFights: 2, DuosBothTables: 1), inventory);
        // The partner's minions make the file format 5, and it reads.
        Assert.StartsWith(ReplayFile.Magic5, Replay("Duos", duos), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_without_a_minion_attack_or_without_an_end_still_counts()
    {
        string[] events =
        [
            "T 1 10", "E 1 L PLAY 1 BG26_801 MINION ATK=4 HEALTH=6",
            // Nobody on the other side: no attack, the fight ends at once.
            "F 2 60", "f WIN 0 3",
            // The recording stopped in the middle of the next one.
            "T 3 90", "F 4 120", "E 2 O PLAY 1 BGS_119 MINION ATK=1 HEALTH=2", "M ATTACK 1 2",
        ];

        var fights = ReplayFights.Of(BattlegroundsMatchReplay.Parse(Replay("Battlegrounds", events))!);

        Assert.Equal([(2, false, "WIN"), (4, true, null)], fights.Select(f => (f.Turn, f.FirstAttack, f.Result)));
        Assert.Empty(fights[0].Theirs);
        Assert.Equal([(1, 2)], fights[1].Theirs.Select(c => (c.Attack, c.Health)));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Duos team combat in the engine (TASK-2026-11-19 package D, Simulation/Fight.Duos.cs): a team's second board
/// (<see cref="SimSide.Teammate"/>) enters when its first board is cleared. By the general Duos rules; no Duos Power.log
/// was available to check the entry's timing or the attack order after it.
/// </summary>
public sealed class DuosCombatTests
{
    [Fact]
    public void The_opponents_teammate_enters_when_their_first_board_is_cleared()
    {
        var ours = Side(Minion(5, 10, cardId: "OURS"));
        var theirs = Side(Minion(1, 1, cardId: "FIRST")) with { Teammate = Side(Minion(2, 2, cardId: "SECOND")) };

        var result = Fight(ours, theirs, oursFirst: true);

        Assert.Equal(1, result.Outcome);
        var entered = result.IndexOf("theirs: teammate enters");
        Assert.True(entered > result.IndexOf("our OURS"), "the second board enters after the first is cleared");
        // Sides keep alternating: theirs attacks next, with the board that entered.
        var next = result.Log.Skip(entered + 1).First(l => l.Contains(" attacks ", StringComparison.Ordinal));
        Assert.StartsWith("their SECOND", next);
        var survivor = Assert.Single(result.Ours);
        Assert.Equal((5, 7), (survivor.Attack, survivor.Health));
    }

    [Fact]
    public void Our_teammate_can_win_the_fight_our_first_board_lost()
    {
        var ours = Side(Minion(1, 1, cardId: "WEAK")) with { Teammate = Side(Minion(6, 6, cardId: "STRONG")) };
        var theirs = Side(Minion(3, 3, cardId: "THEIRS"));

        var result = Fight(ours, theirs, oursFirst: true);

        Assert.Equal(1, result.Outcome);
        Assert.Equal("STRONG", Assert.Single(result.Ours).CardId);
        Assert.Contains(result.Log, l => l.StartsWith("ours: teammate enters", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_teammates_a_cleared_board_still_ends_the_fight()
    {
        var result = Fight(Side(Minion(1, 1)), Side(Minion(3, 3)), oursFirst: true);

        Assert.Equal(-1, result.Outcome);
        Assert.DoesNotContain(result.Log, l => l.Contains("teammate enters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_team_loses_only_when_both_boards_are_cleared()
    {
        // Each side: a 2/2 first board and a 2/2 second board. Our first attack trades; then theirs; a tie at the end.
        var ours = Side(Minion(2, 2, cardId: "A")) with { Teammate = Side(Minion(2, 2, cardId: "B")) };
        var theirs = Side(Minion(2, 2, cardId: "C")) with { Teammate = Side(Minion(2, 2, cardId: "D")) };

        var result = Fight(ours, theirs, oursFirst: true);

        // Both first boards die together: both teammates enter, and they trade too.
        Assert.Equal(0, result.Outcome);
        Assert.Contains(result.Log, l => l.StartsWith("ours: teammate enters", StringComparison.Ordinal));
        Assert.Contains(result.Log, l => l.StartsWith("theirs: teammate enters", StringComparison.Ordinal));
    }

    [Fact]
    public void The_teammates_player_level_effects_replace_the_first_players()
    {
        // The first player's "+1 Attack to minions summoned this fight"-style counter must not carry over: per-fight
        // player counters belong to the player who fights. Checked through Side.Counter after the teammate entered.
        var registry = Effects(typeof(SetsCounterAtStart), typeof(ReadsCounterAtStart));
        var ours = Side(Minion(1, 1, cardId: "WEAK")) with
        {
            Extras = [SetsCounterAtStart.Id],
            Teammate = Side(Minion(9, 9, cardId: ReadsCounterAtStart.Id)) with { Tags = new Dictionary<string, int> { ["MARK"] = 7 } },
        };

        var fight = CombatSimulator.RunSingle(ours, Side(Minion(3, 3)), true, new ScriptedRandom([]), registry);

        Assert.Equal(1, fight.Outcome);
        Assert.Contains(fight.Log, l => l == "start: MARK=1");
        Assert.Contains(fight.Log, l => l == "teammate side MARK=7");
    }

    [Fact]
    public void An_empty_first_board_gives_way_to_its_teammate_at_once()
    {
        var ours = new SimSide([]) { Teammate = Side(Minion(6, 6)) };

        var odds = CombatSimulator.Run(ours, Side(Minion(3, 3)), firstAttackerIsOurs: true, new SimOptions { Runs = 200, CountDamage = false });

        Assert.Equal(1.0, odds.Win);
    }

    [Fact]
    public void The_teammate_changes_the_odds_and_counts_in_estimates_and_unsimulated()
    {
        var first = Side(Minion(2, 2), Minion(2, 2));
        var theirs = Side(Minion(5, 5));
        var options = new SimOptions { Runs = 400, CountDamage = false };
        var alone = CombatSimulator.Run(first, theirs, null, options);

        var flagged = Minion(6, 6) with { HasUnsimulatedEffect = true };
        var mate = Side(flagged) with { Estimates = [new SimEstimate(SimInputs.DuosPartnerBoard, 3, 1, "test")] };
        var team = CombatSimulator.Run(first with { Teammate = mate }, theirs, null, options);

        Assert.Equal(1.0, alone.Loss);
        Assert.Equal(1.0, team.Win);
        Assert.Equal(1, team.Estimated);
        Assert.Equal(1, team.Unsimulated);
        Assert.Single(CombatSimulator.Explain(first with { Teammate = mate }, isOpponent: false));
        Assert.Equal(SimInputs.DuosPartnerBoard, Assert.Single(CombatSimulator.ExplainEstimates(first with { Teammate = mate }, isOpponent: false)).Input);
    }

    [Fact]
    public void A_solo_side_keeps_its_seed_and_a_teammate_changes_it()
    {
        var ours = Side(Minion(2, 3));
        var theirs = Side(Minion(3, 2));

        var solo = CombatSimulator.TeamSeed(ours, theirs, true);

        Assert.Equal(CombatSimulator.Seed(ours, theirs, true), solo);
        Assert.NotEqual(solo, CombatSimulator.TeamSeed(ours, theirs with { Teammate = Side(Minion(1, 1)) }, true));
    }

    [Fact]
    public void A_teammate_that_enters_gives_the_undead_it_summons_its_undead_attack()
    {
        // TASK-2026-11-26 AC5: the second board is its own player's, so "Your Undead have +X Attack this game" is that
        // player's (SimInputs.UndeadAttackBonus: the Undead Army enchantment its Undead show), as on a first board
        // (BuddiesAndGapsTests.Undead_summoned_in_combat_get_the_players_undead_attack). Their W kills our first board,
        // our teammate's Harmless Bonehead comes in and dies to W: its two Skeletons are 1/1 + 5 Attack, and one of them
        // kills W (1/3) and the other survives.
        var bonehead = Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle) with
        {
            Enchantments = [SimInputs.UndeadArmyEnchantment],
            EnchantmentValues = [new SimEnchantment(SimInputs.UndeadArmyEnchantment) { Attack = 5 }],
        };
        var ours = Side(Minion(0, 1, cardId: "FIRST")) with { Teammate = Side(bonehead) };

        var result = Fight(ours, Side(Minion(1, 3, cardId: "W")), oursFirst: false);

        Assert.Equal(1, result.Outcome);
        var skeleton = Assert.Single(result.Ours);
        Assert.Equal((Tokens.SkeletonId, 6, 1), (skeleton.CardId, skeleton.Attack, skeleton.Health));
    }

    [Fact]
    public void A_teammate_board_holds_at_most_seven_minions()
    {
        var eight = Enumerable.Range(0, 8).Select(i => Minion(1, 1, cardId: "T" + i)).ToArray();
        var ours = Side(Minion(1, 1)) with { Teammate = Side(eight) };

        var result = Fight(ours, Side(Minion(1, 1)), oursFirst: false);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(7, result.Ours.Count);
    }

    /// <summary>A test-only player-level effect: sets the per-fight counter MARK at the fight's start and logs what it sees.</summary>
    [CardEffect(Id)]
    private sealed class SetsCounterAtStart : CombatEffect
    {
        public const string Id = "TEST_DUOS_MARK";

        public override void OnFightStart(Fight fight, Unit self)
        {
            self.Side.SetCounter("MARK", 1);
            fight.Trace($"start: MARK={self.Side.Counter("MARK")}");
        }
    }

    /// <summary>A test-only minion effect: logs the per-fight counter MARK its side reads at the fight's start (or entry).</summary>
    [CardEffect(Id)]
    private sealed class ReadsCounterAtStart : CombatEffect
    {
        public const string Id = "TEST_DUOS_READ";

        public override void OnFightStart(Fight fight, Unit self) => fight.Trace($"teammate side MARK={self.Side.Counter("MARK")}");
    }
}

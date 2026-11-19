using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Bench;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Runs in seeded chunks spread over threads (<see cref="SimOptions.Parallelism"/>) and the precision stop
/// (<see cref="SimOptions.TargetHalfWidth"/>): the same boards give the same odds on any number of threads, a stop by
/// time or cancellation keeps a prefix of the chunks, and the precision stop is the same on every thread count.
/// </summary>
public sealed class ParallelRunsTests
{
    private static readonly EffectRegistry Registry = Effects(typeof(EngineTests.SummonTwo), typeof(EngineTests.ExtraTrigger),
        typeof(EngineTests.Cleave), typeof(EngineTests.AvengeTwo), typeof(Boom));

    /// <summary>Random targets, deathrattle summons, cleave and avenge: every fight takes many random picks.</summary>
    private static readonly SimSide Ours = Side(
        Minion(4, 6, cardId: "DR"), Minion(2, 8, cardId: "TITUS"), Minion(3, 5, Keyword.Reborn, "DR"), Minion(5, 5, cardId: "AV"),
        Minion(6, 6, Keyword.DivineShield, "DR")) with { TavernTier = 5, Health = 20 };

    private static readonly SimSide Theirs = Side(
        Minion(3, 3, cardId: "CLEAVE"), Minion(4, 9, Keyword.Taunt), Minion(7, 3, Keyword.Reborn, "DR"), Minion(6, 6, Keyword.DivineShield),
        Minion(2, 10, Keyword.Taunt, "AV"), Minion(4, 4, Keyword.Poisonous)) with { TavernTier = 5, Health = 15 };

    private static CombatOdds Run(int runs, int threads, SimSide? ours = null, SimSide? theirs = null, double target = 0, int minRuns = 0) =>
        CombatSimulator.Run(ours ?? Ours, theirs ?? Theirs, null,
            new SimOptions { Runs = runs, MinRuns = minRuns, Effects = Registry, Parallelism = threads, TargetHalfWidth = target, DamageCap = 15 });

    [Fact]
    public void The_odds_do_not_depend_on_the_number_of_threads()
    {
        var one = Run(2000, 1);

        Assert.Equal(2000, one.Runs);
        Assert.True(one.Win is > 0 and < 1 && one.Loss is > 0 and < 1, $"a fight with both results: {one}");
        foreach (var threads in new[] { 2, 3, 4, 8 })
        {
            // Record equality: win/tie/loss, runs, half-width, both damage spreads and lethal shares.
            Assert.Equal(one, Run(2000, threads));
        }
    }

    [Fact]
    public void Parallel_runs_give_the_same_numbers_every_time()
    {
        var first = Run(1500, 4);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(first, Run(1500, 4));
        }
    }

    [Fact]
    public void A_run_count_that_is_not_a_whole_number_of_chunks_makes_every_run()
    {
        var odds = Run(CombatSimulator.ChunkRuns * 3 + 7, 4);

        Assert.Equal(CombatSimulator.ChunkRuns * 3 + 7, odds.Runs);
        Assert.Equal(odds, Run(CombatSimulator.ChunkRuns * 3 + 7, 1));
    }

    [Fact]
    public void Chunk_seeds_are_stable_across_processes()
    {
        // SplitMix64 over (seed, chunk); these constants must not change between runs, machines or .NET versions.
        Assert.Equal((-197930548, 1167010561), (CombatSimulator.ChunkSeed(745727955, 0), CombatSimulator.ChunkSeed(745727955, 1)));
    }

    [Fact]
    public void Neighbouring_chunks_and_boards_get_different_seeds()
    {
        var seeds = Enumerable.Range(0, 2000).Select(chunk => CombatSimulator.ChunkSeed(745727955, chunk))
            .Concat(Enumerable.Range(0, 2000).Select(chunk => CombatSimulator.ChunkSeed(745727956, chunk)));

        Assert.Equal(4000, seeds.Distinct().Count());
    }

    // ------------------------------------------------------------------------------------------- precision stop

    [Fact]
    public void A_lopsided_fight_stops_at_the_minimum_once_it_is_precise_enough()
    {
        // 200 wins of 200: the Wilson interval reaches 0.94 points below 100 %, inside a 1-point target.
        var odds = Run(10_000, 1, Side(Minion(10, 10)), Side(Minion(1, 1)), target: 0.01, minRuns: 200);

        Assert.Equal(200, odds.Runs);
        Assert.Equal(1.0, odds.Win);
        Assert.InRange(odds.HalfWidth, 0.009, 0.01);
    }

    [Fact]
    public void A_close_fight_runs_until_the_target_is_met_on_any_number_of_threads()
    {
        var mirror = Side(Minion(3, 3), Minion(2, 4), Minion(4, 2), Minion(1, 6));
        var one = Run(20_000, 1, mirror, mirror, target: 0.01, minRuns: 200);

        // About 9 600 runs give ±1 point around 50 %; the stop comes at the first chunk that reaches it.
        Assert.InRange(one.Runs, 5000, 10_000);
        Assert.Equal(0, one.Runs % CombatSimulator.ChunkRuns);
        Assert.True(one.HalfWidth <= 0.01, $"half-width {one.HalfWidth}");
        Assert.True(Run(one.Runs - CombatSimulator.ChunkRuns, 1, mirror, mirror).HalfWidth > 0.01, "the first chunk at the target, not a later one");
        foreach (var threads in new[] { 2, 4, 8 })
        {
            Assert.Equal(one, Run(20_000, threads, mirror, mirror, target: 0.01, minRuns: 200));
        }
    }

    [Fact]
    public void The_precision_stop_never_comes_before_the_minimum()
    {
        var odds = Run(4000, 4, target: 0.5, minRuns: 300);

        Assert.Equal(300, odds.Runs);
    }

    [Fact]
    public void Without_a_target_every_run_is_made()
    {
        Assert.Equal(1000, Run(1000, 4, Side(Minion(10, 10)), Side(Minion(1, 1))).Runs);
    }

    [Fact]
    public void The_half_width_is_the_widest_wilson_interval_of_the_three_shares()
    {
        var odds = Run(1000, 2);
        int wins = (int)Math.Round(odds.Win * odds.Runs), losses = (int)Math.Round(odds.Loss * odds.Runs);
        var widest = new[] { wins, losses, odds.Runs - wins - losses }.Max(k =>
        {
            var (low, high) = SimStats.Wilson(k, odds.Runs);
            return (high - low) / 2;
        });

        Assert.Equal(widest, odds.HalfWidth, 9);
        Assert.Equal(0, CombatSimulator.Run(Side(Minion(1, 1)), Side(), null).HalfWidth);
    }

    // ------------------------------------------------------------------------------------------- stops and failures

    [Fact]
    public void A_spent_time_budget_stops_parallel_runs_exactly_at_the_minimum()
    {
        var options = new SimOptions { Runs = 4000, MinRuns = 300, TimeBudget = TimeSpan.Zero, Effects = Registry, Parallelism = 4, DamageCap = 15 };

        var odds = CombatSimulator.Run(Ours, Theirs, null, options);

        Assert.Equal(300, odds.Runs);
        Assert.Equal(Run(300, 1), odds);
    }

    [Fact]
    public void A_cancelled_simulation_still_makes_its_first_chunk_on_any_number_of_threads()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        foreach (var threads in new[] { 1, 4 })
        {
            var odds = CombatSimulator.Run(Ours, Theirs, null, new SimOptions { Runs = 4000, Effects = Registry, Parallelism = threads, Cancel = cancel.Token, DamageCap = 15 });
            Assert.Equal(CombatSimulator.ChunkRuns, odds.Runs);
            Assert.Equal(Run(CombatSimulator.ChunkRuns, 1), odds);
        }
    }

    [Fact]
    public void Cancelling_a_parallel_simulation_keeps_the_chunks_done_and_the_same_numbers_for_them()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var odds = CombatSimulator.Run(Ours, Theirs, null,
            new SimOptions { Runs = 2_000_000, MinRuns = 0, Effects = Registry, Parallelism = 4, Cancel = cancel.Token, DamageCap = 15 });

        Assert.InRange(odds.Runs, CombatSimulator.ChunkRuns, 1_999_999);
        Assert.Equal(0, odds.Runs % CombatSimulator.ChunkRuns);
        // What was kept is a prefix of the chunks: the same runs on one thread give the same numbers.
        Assert.Equal(Run(odds.Runs, 1), odds);
    }

    [Fact]
    public void An_effect_that_throws_on_a_helper_thread_reaches_the_caller()
    {
        var ours = Side(Minion(1, 1, cardId: "BOOM"), Minion(1, 1, cardId: "BOOM"));

        var error = Assert.Throws<InvalidOperationException>(() => Run(4000, 4, ours, Side(Minion(5, 5), Minion(5, 5))));
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public void The_quick_pass_stays_on_the_log_thread_and_the_background_pass_stops_at_one_point()
    {
        var app = new BattlegroundsTracker().SimOptions;
        var quick = BattlegroundsTracker.QuickPass(app);

        Assert.Equal(1, quick.Parallelism);
        Assert.Equal((150, (TimeSpan?)TimeSpan.FromMilliseconds(40)), (quick.Runs, quick.TimeBudget));
        Assert.Equal(SimOptions.BackgroundParallelism, app.Parallelism);
        Assert.InRange(SimOptions.BackgroundParallelism, 1, 4);
        Assert.Equal((10_000, 200, 0.01, (TimeSpan?)TimeSpan.FromMilliseconds(200)), (app.Runs, app.MinRuns, app.TargetHalfWidth, app.TimeBudget));
    }

    [CardEffect("BOOM")]
    private sealed class Boom : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => throw new InvalidOperationException("boom");
    }
}

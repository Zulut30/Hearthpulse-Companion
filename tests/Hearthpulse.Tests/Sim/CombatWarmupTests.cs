using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;
using Microsoft.Data.Sqlite;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// The combat simulator's warm-up (<see cref="CombatWarmup"/>, docs/perf-architecture-audit.md stage 14): it leaves
/// the odds of real fights untouched, its boards take real effects, and the live session starts it only for a
/// Battlegrounds game in progress.
/// </summary>
public sealed class CombatWarmupTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(TestDataFolder.Scratch("hp-warmup")).FullName;
    private readonly HearthstoneMemory _memory = new();
    private HistoryStore? _history;

    public void Dispose()
    {
        _memory.Dispose();
        _history = null;
        SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_folder);
    }

    [Fact]
    public void Warming_up_on_the_same_thread_does_not_change_the_odds_of_a_fight()
    {
        var (ours, theirs, first) = CombatWarmup.Boards()[1];
        var options = BattlegroundsTracker.QuickPass(BattlegroundsTracker.DefaultSimOptions) with { TimeBudget = null, Parallelism = 1 };
        var before = CombatSimulator.Run(theirs, ours, !first, options);

        Assert.True(CombatWarmup.Run(rounds: 1, TimeSpan.Zero, TimeSpan.Zero) > 0);

        // Record equality: win/tie/loss, runs, damage spreads; the seed comes from the boards, not from earlier fights.
        Assert.Equal(before, CombatSimulator.Run(theirs, ours, !first, options));
    }

    [Fact]
    public void The_warm_up_boards_name_only_cards_with_a_registered_effect()
    {
        var unknown = CombatWarmup.Boards()
            .SelectMany(b => b.Ours.Minions.Concat(b.Theirs.Minions))
            .Where(m => m.CardId.Length > 0 && EffectRegistry.Default.Get(m.CardId, m.Golden) is null)
            .Select(m => m.CardId)
            .ToList();

        // A renamed effect would quietly leave its paths cold: the board must say so.
        Assert.Empty(unknown);
    }

    [Fact]
    public void A_live_battlegrounds_game_starts_the_warm_up()
    {
        var warmUps = 0;
        var session = Session(running: true, () => warmUps++);

        session.OnLines("Power.log", BattlegroundsFixtureTests.Lines().ToList(), catchingUp: false);
        session.OnTick();

        Assert.True(warmUps > 0);
    }

    [Fact]
    public void A_battlegrounds_log_without_a_running_client_does_not_start_the_warm_up()
    {
        var warmUps = 0;
        var session = Session(running: false, () => warmUps++);

        session.OnLines("Power.log", BattlegroundsFixtureTests.Lines().ToList(), catchingUp: false);
        session.OnTick();

        Assert.Equal(0, warmUps);
    }

    private GameSession Session(bool running, Action warmUp)
    {
        _history = new HistoryStore(Path.Combine(_folder, "history.db"));
        var now = new DateTime(2026, 10, 23, 0, 10, 0, DateTimeKind.Utc);
        var session = new GameSession(_history, _memory)
        {
            Clock = () => now,
            IsHearthstoneRunning = () => running,
            ReadsMemory = false,
            WarmUpSimulator = warmUp,
        };
        session.OnSessionStarted("Hearthstone_2026_10_23_02_01_43", new DateTime(2026, 10, 23, 2, 1, 43), attachedMidSession: false);
        return session;
    }
}

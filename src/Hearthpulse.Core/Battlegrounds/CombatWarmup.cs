using System.Diagnostics;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// Warms the combat simulator up once per process, on a low-priority thread of its own, when a Battlegrounds game
/// starts (docs/perf-architecture-audit.md, stage 14). A cold process runs the first fights on unoptimised (tier-0)
/// code and pays for compiling the engine on the way: the quick pass of the session's first fight then finished
/// fewer of its runs inside its budget than later fights. The warm-up simulates a few synthetic boards with its own
/// inputs, so the seeds and results of real fights are untouched (a fight's seed comes from its boards only); the
/// runtime promotes the engine's hot methods to optimised code before the first real fight.
/// </summary>
internal static class CombatWarmup
{
    private static int _started;

    /// <summary>Simulating time of one round: the engine's methods are called enough times to be counted.</summary>
    internal static TimeSpan Slice { get; set; } = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// The pause after a round. The runtime starts counting calls only after a quiet spell with no new unoptimised
    /// code (100 ms by default) and compiles optimised code in the background; without pauses the warm-up would keep
    /// that from happening, as back-to-back fights do.
    /// </summary>
    internal static TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Rounds of the warm-up.</summary>
    internal static int Rounds { get; set; } = 5;

    /// <summary>Whether the warm-up has been started in this process.</summary>
    public static bool Started => Volatile.Read(ref _started) != 0;

    /// <summary>Starts the warm-up unless it already ran; cheap to call on every snapshot.</summary>
    public static void EnsureStarted()
    {
        if (!Started)
        {
            _ = Start();
        }
    }

    /// <summary>
    /// Starts the warm-up unless it already ran in this process; the task completes when it is over (it never
    /// faults). A completed task when it had already been started.
    /// </summary>
    public static Task Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var clock = Stopwatch.StartNew();
                var fights = Run(Rounds, Slice, Pause);
                Diag.Info($"Combat simulator warmed up: {fights} fights in {clock.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Diag.Error("Combat simulator warm-up failed", ex);
            }

            done.SetResult();
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Lowest,
            Name = "Combat simulator warm-up",
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>
    /// <paramref name="rounds"/> rounds of simulating the warm-up boards in turn for <paramref name="slice"/>, each
    /// followed by <paramref name="pause"/>. Returns the fights simulated.
    /// </summary>
    internal static long Run(int rounds, TimeSpan slice, TimeSpan pause)
    {
        var boards = Boards();
        var options = new SimOptions { Runs = 32, MinRuns = 32, Parallelism = 1 };
        var clock = new Stopwatch();
        long fights = 0;
        for (var round = 0; round < rounds; round++)
        {
            clock.Restart();
            do
            {
                foreach (var (ours, theirs, oursFirst) in boards)
                {
                    fights += CombatSimulator.Run(ours, theirs, oursFirst, options).Runs;
                }
            }
            while (clock.Elapsed < slice);

            Thread.Sleep(pause);
        }

        return fights;
    }

    /// <summary>Boards that take the engine's common paths: keywords, deaths, deathrattles, Reborn, Avenge, cleave.</summary>
    internal static IReadOnlyList<(SimSide Ours, SimSide Theirs, bool? OursFirst)> Boards()
    {
        static SimMinion M(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false,
                           CardMechanic mechanics = CardMechanic.None, Tribe tribe = Tribe.None) =>
            SimMinion.Create(id, attack, health, tribe, keywords, golden, mechanics) with { TechLevel = 3 };

        static SimMinion Dr(string id, int attack, int health, Keyword keywords = Keyword.None) =>
            M(id, attack, health, keywords, mechanics: CardMechanic.Deathrattle);

        static SimSide S(params SimMinion[] minions) => new(minions) { TavernTier = 4, Health = 30 };

        return
        [
            (S(M(string.Empty, 2, 3), M(string.Empty, 1, 2, Keyword.Taunt)), S(M(string.Empty, 3, 2), M(string.Empty, 2, 2)), null),
            (S(Dr("BG28_300", 2, 2), Dr("BGS_008", 7, 7), M(string.Empty, 4, 6, Keyword.DivineShield | Keyword.Taunt), Dr("BG29_611", 3, 3)),
             S(M("BG26_817", 6, 9, Keyword.Windfury), M(string.Empty, 5, 5, Keyword.Venomous), Dr("BG31_809", 4, 4), M(string.Empty, 3, 8, Keyword.Reborn)),
             true),
            (S(M("BG31_835", 5, 8), M("BG34_403", 4, 10), M("BG34_405", 3, 12, Keyword.Taunt), Dr("BG25_022", 3, 3, Keyword.Reborn), M("BGS_126", 8, 6)),
             S(M("BG27_017", 7, 7), Dr("BG31_803", 2, 6, Keyword.Taunt), M(string.Empty, 1, 1, Keyword.Poisonous | Keyword.DivineShield),
               M(string.Empty, 9, 9, Keyword.MegaWindfury), M(string.Empty, 6, 18, Keyword.DivineShield)),
             false),
        ];
    }
}

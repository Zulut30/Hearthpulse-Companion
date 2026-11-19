using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>The runs of <see cref="CombatSimulator.Run(SimSide, SimSide, bool?, SimOptions?)"/>: seeded chunks, threads, stops and tallies.</summary>
public static partial class CombatSimulator
{
    /// <summary>z of a two-sided 95 % interval.</summary>
    private const double Z = 1.959964;

    /// <summary>
    /// The 95 % half-width (Wilson score interval) of the least certain of win, tie and loss after
    /// <paramref name="runs"/> fights; 0 without fights.
    /// </summary>
    internal static double HalfWidth(int wins, int losses, int runs)
    {
        if (runs <= 0)
        {
            return 0;
        }

        static double Wilson(int k, int n)
        {
            var p = k / (double)n;
            const double z2 = Z * Z;
            return Z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4.0 * n * n))) / (1 + (z2 / n));
        }

        return Math.Max(Wilson(wins, runs), Math.Max(Wilson(losses, runs), Wilson(runs - wins - losses, runs)));
    }

    /// <summary>
    /// The seed of chunk <paramref name="chunk"/> of a simulation seeded <paramref name="seed"/> (SplitMix64 over both):
    /// fixed, like <see cref="Seed"/>, and far apart for neighbouring chunks.
    /// </summary>
    internal static int ChunkSeed(int seed, int chunk)
    {
        var z = (((ulong)(uint)seed << 32) | (uint)chunk) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z ^ (z >> 32));
    }

    /// <summary>The damage share (<see cref="DamageRules.MinionTier"/>) of the minions left alive on a side.</summary>
    private static int SurvivorTiers(Side side)
    {
        var total = 0;
        var minions = side.Minions;
        for (var i = 0; i < minions.Count; i++)
        {
            if (minions[i].IsAlive)
            {
                total += DamageRules.MinionTier(minions[i].Spec.TechLevel);
            }
        }

        return total;
    }

    private static int Tiers(IReadOnlyList<SimMinion> minions)
    {
        var total = 0;
        foreach (var minion in minions)
        {
            total += DamageRules.MinionTier(minion.TechLevel);
        }

        return total;
    }

    /// <summary>Fights, results and hero damage counted over some runs: per chunk, then summed in chunk order.</summary>
    private sealed class Tally
    {
        // Damage above this is counted here (no real board gets near it: tier 7 + 7 golden tier-7 minions = 56).
        public const int MaxDamage = 80;

        public int[] Taken { get; } = new int[MaxDamage + 1];

        public int[] Dealt { get; } = new int[MaxDamage + 1];

        public int Done { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int LethalTaken { get; set; }

        public int LethalDealt { get; set; }

        /// <summary>Fights an engine limit cut short (<see cref="CombatOdds.Truncated"/>).</summary>
        public int Truncated { get; set; }

        public void Add(Tally other)
        {
            Done += other.Done;
            Truncated += other.Truncated;
            Wins += other.Wins;
            Losses += other.Losses;
            LethalTaken += other.LethalTaken;
            LethalDealt += other.LethalDealt;
            for (var d = 0; d <= MaxDamage; d++)
            {
                Taken[d] += other.Taken[d];
                Dealt[d] += other.Dealt[d];
            }
        }

        public void Clear()
        {
            Done = Wins = Losses = LethalTaken = LethalDealt = Truncated = 0;
            Array.Clear(Taken);
            Array.Clear(Dealt);
        }
    }

    /// <summary>Hero damage per finished fight, by who won (<see cref="DamageRules"/>), and the fights that were lethal.</summary>
    private sealed class DamageRule(SimSide ours, SimSide theirs, int cap, bool count)
    {
        private readonly bool _ourIceBlock = DamageRules.HasIceBlock(ours);
        private readonly bool _theirIceBlock = DamageRules.HasIceBlock(theirs);

        /// <summary>Damage is estimated (off for Duos).</summary>
        public bool Count => count;

        public void Add(Tally tally, int outcome, int ourSurvivorTiers, int theirSurvivorTiers)
        {
            tally.Done++;
            if (outcome > 0)
            {
                tally.Wins++;
            }
            else if (outcome < 0)
            {
                tally.Losses++;
            }

            if (!count)
            {
                return;
            }

            if (outcome > 0)
            {
                var dealt = DamageRules.Taken(DamageRules.DamageOfTiers(ours.TavernTier, ourSurvivorTiers, cap), theirs.Health, _theirIceBlock);
                tally.Dealt[Math.Min(dealt, Tally.MaxDamage)]++;
                tally.LethalDealt += DamageRules.IsLethal(dealt, theirs.Health) ? 1 : 0;
            }
            else if (outcome < 0)
            {
                var taken = DamageRules.Taken(DamageRules.DamageOfTiers(theirs.TavernTier, theirSurvivorTiers, cap), ours.Health, _ourIceBlock);
                tally.Taken[Math.Min(taken, Tally.MaxDamage)]++;
                tally.LethalTaken += DamageRules.IsLethal(taken, ours.Health) ? 1 : 0;
            }
        }

        public CombatOdds With(CombatOdds odds, Tally tally) => tally.Done == 0
            ? odds
            : odds with
            {
                DamageTaken = DamageSpread.From(tally.Taken),
                DamageDealt = DamageSpread.From(tally.Dealt),
                LethalTaken = ours.Health > 0 ? tally.LethalTaken / (double)tally.Done : null,
                LethalDealt = theirs.Health > 0 ? tally.LethalDealt / (double)tally.Done : null,
                DamageCap = cap,
            };
    }

    /// <summary>
    /// The runs of one simulation. Threads (the caller and up to <see cref="SimOptions.Parallelism"/> - 1 thread-pool
    /// helpers) claim chunks in index order; finished chunks are summed strictly in index order and the precision stop
    /// is checked after each one, so the total is a prefix of the chunks whatever the scheduling (a chunk finished past
    /// the precision stop is dropped). The caller waits only for helpers that are making a chunk: a helper the pool
    /// starts late finds the batch closed and returns, so the caller never waits for a free pool thread.
    /// </summary>
    private sealed class Batch(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs, SimOptions options, EffectRegistry effects,
                               DamageRule rules, int seed)
    {
        private readonly object _gate = new();
        private readonly Fight.PreparedEffects _prepared = Fight.PreparedEffects.For(ours, theirs, effects);
        private readonly Stopwatch? _clock = options.TimeBudget is { } ? Stopwatch.StartNew() : null;
        private readonly Dictionary<int, Tally> _finished = new();
        private readonly Stack<Tally> _free = new();
        private readonly Tally _total = new();
        private int _nextChunk;
        private int _claimedRuns;
        private int _counted;
        private bool _stop;
        private bool _precise;
        private bool _closed;
        private int _helpers;
        private Exception? _error;

        public Tally Run()
        {
            var chunks = (int)Math.Min(int.MaxValue, ((long)Math.Max(0, options.Runs) + ChunkRuns - 1) / ChunkRuns);
            var threads = Math.Min(Math.Min(Math.Max(1, options.Parallelism), Environment.ProcessorCount), chunks);
            for (var i = 1; i < threads; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(static batch => batch.Help(), this, preferLocal: false);
            }

            try
            {
                Work();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            lock (_gate)
            {
                _closed = true;
                while (_helpers > 0)
                {
                    Monitor.Wait(_gate);
                }
            }

            if (_error is not null)
            {
                ExceptionDispatchInfo.Throw(_error);
            }

            return _total;
        }

        private void Help()
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                _helpers++;
            }

            try
            {
                Work();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                lock (_gate)
                {
                    if (--_helpers == 0)
                    {
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }

        private void Work()
        {
            var scratch = new Fight.Scratch();
            while (Claim() is var (chunk, runs, tally))
            {
                // One chooser per chunk, seeded for that chunk only: a chunk's fights never depend on another chunk's.
                var chooser = new RandomChooser(new Random(ChunkSeed(seed, chunk)));
                for (var r = 0; r < runs; r++)
                {
                    var fight = new Fight(ours, theirs, effects, chooser, trace: false, _prepared, scratch);
                    // Which side entered play first decides the death order (rules-evidence 4a). Without both PLAYER_IDs the
                    // runs alternate between the two orders: an even mix, not the attacking side first (right in 47 of 89).
                    fight.EnteredFirst ??= r % 2 == 0 ? fight.Ours : fight.Theirs;
                    var outcome = fight.Run(firstAttackerIsOurs, options.FirstAttackerEntityId);
                    rules.Add(tally, outcome, rules.Count ? SurvivorTiers(fight.Ours) : 0, rules.Count ? SurvivorTiers(fight.Theirs) : 0);
                    tally.Truncated += fight.Truncated ? 1 : 0;
                }

                Finish(chunk, tally);
            }
        }

        /// <summary>The next chunk to make, or null once the runs are all handed out or something stopped them.</summary>
        private (int Chunk, int Runs, Tally Tally)? Claim()
        {
            lock (_gate)
            {
                if (_stop || _claimedRuns >= options.Runs)
                {
                    return null;
                }

                // Cancellation is not checked before the first runs; the time budget is (it may be zero).
                if ((_claimedRuns > 0 && options.Cancel.IsCancellationRequested)
                    || (_clock is not null && _claimedRuns >= options.MinRuns && _clock.Elapsed > options.TimeBudget))
                {
                    _stop = true;
                    return null;
                }

                var runs = Math.Min(ChunkRuns, options.Runs - _claimedRuns);
                _claimedRuns += runs;
                return (_nextChunk++, runs, _free.Count > 0 ? _free.Pop() : new Tally());
            }
        }

        /// <summary>Counts every finished chunk that continues the counted prefix, stopping at the precision target.</summary>
        private void Finish(int chunk, Tally tally)
        {
            lock (_gate)
            {
                if (_precise)
                {
                    Recycle(tally);
                    return;
                }

                _finished.Add(chunk, tally);
                while (_finished.Remove(_counted, out var next))
                {
                    _total.Add(next);
                    _counted++;
                    Recycle(next);
                    if (options.TargetHalfWidth > 0 && _total.Done >= options.MinRuns
                        && HalfWidth(_total.Wins, _total.Losses, _total.Done) <= options.TargetHalfWidth)
                    {
                        _precise = _stop = true;
                        foreach (var dropped in _finished.Values)
                        {
                            Recycle(dropped);
                        }

                        _finished.Clear();
                        return;
                    }
                }
            }
        }

        private void Recycle(Tally tally)
        {
            tally.Clear();
            _free.Push(tally);
        }

        private void Fail(Exception ex)
        {
            lock (_gate)
            {
                _error ??= ex;
                _stop = true;
            }
        }
    }
}

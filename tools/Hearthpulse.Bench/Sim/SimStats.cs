namespace Hearthpulse.Bench;

/// <summary>A share measured twice on the same fights: how many had it before, after, and the pairs that changed.</summary>
/// <param name="Gained">Fights that have it only after.</param>
/// <param name="Lost">Fights that had it only before.</param>
/// <param name="P">Two-sided exact p-value of the discordant pairs (<see cref="SimStats.McNemarExact"/>).</param>
internal sealed record PairedShare(int Pairs, int Before, int After, int Gained, int Lost, double P);

/// <summary>A mean difference over paired fights with its 95 % interval; no interval below two pairs.</summary>
internal sealed record PairedMean(int Pairs, double Mean, double? Low, double? High);

/// <summary>
/// The statistics the simulator's measurement reports (docs/bg-combat-sim.md): an interval for a share, a score for a
/// win/tie/loss forecast, and paired comparisons of two runs over the same fights. Two runs on the same fights are
/// compared pair by pair: their two intervals overlap whenever the fights are mostly the same, whatever changed.
/// </summary>
internal static class SimStats
{
    private const double Z = 1.959964;

    /// <summary>Wilson score interval (95 %) of a share <paramref name="k"/> / <paramref name="n"/>.</summary>
    public static (double Low, double High) Wilson(int k, int n)
    {
        if (n <= 0)
        {
            return (0, 0);
        }

        var p = k / (double)n;
        var center = (p + Z * Z / (2 * n)) / (1 + Z * Z / n);
        var half = Z * Math.Sqrt(p * (1 - p) / n + Z * Z / (4.0 * n * n)) / (1 + Z * Z / n);
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    /// Brier score of a win/tie/loss forecast against <paramref name="result"/> ("Win", "Tie", "Loss"): the squared error
    /// summed over the three outcomes, 0 = certain and right, 2 = certain and wrong, 0.67 = always a third each. Null
    /// for any other result.
    /// </summary>
    public static double? Brier(double win, double tie, double loss, string result)
    {
        var (w, t, l) = result switch
        {
            "Win" => (1, 0, 0),
            "Tie" => (0, 1, 0),
            "Loss" => (0, 0, 1),
            _ => (-1, -1, -1),
        };
        return w < 0 ? null : Math.Pow(win - w, 2) + Math.Pow(tie - t, 2) + Math.Pow(loss - l, 2);
    }

    /// <summary>
    /// McNemar's test in its exact form: of the <paramref name="gained"/> + <paramref name="lost"/> pairs that changed,
    /// were the two directions equally likely? Two-sided binomial p-value with p = 1/2; 1 when no pair changed.
    /// </summary>
    public static double McNemarExact(int gained, int lost)
    {
        var n = gained + lost;
        if (n == 0)
        {
            return 1;
        }

        // P(X <= min) for X ~ Binomial(n, 1/2), each term built from the one before so nothing overflows.
        var term = Math.Pow(0.5, n);
        var tail = 0.0;
        for (var i = 0; i <= Math.Min(gained, lost); i++)
        {
            tail += term;
            term *= (n - i) / (double)(i + 1);
        }

        return Math.Min(1, 2 * tail);
    }

    /// <summary>A yes/no measure taken before and after on the same fights, in the same order.</summary>
    public static PairedShare Paired(IReadOnlyList<bool> before, IReadOnlyList<bool> after)
    {
        if (before.Count != after.Count)
        {
            throw new ArgumentException("The two runs do not cover the same fights.", nameof(after));
        }

        int gained = 0, lost = 0;
        for (var i = 0; i < before.Count; i++)
        {
            gained += !before[i] && after[i] ? 1 : 0;
            lost += before[i] && !after[i] ? 1 : 0;
        }

        return new PairedShare(before.Count, before.Count(b => b), after.Count(a => a), gained, lost, McNemarExact(gained, lost));
    }

    /// <summary>The mean of per-fight differences (after minus before) with mean ± 1.96·s/√n.</summary>
    public static PairedMean Mean(IReadOnlyList<double> differences)
    {
        var n = differences.Count;
        if (n == 0)
        {
            return new PairedMean(0, 0, null, null);
        }

        var mean = differences.Average();
        if (n < 2)
        {
            return new PairedMean(n, mean, null, null);
        }

        var deviation = Math.Sqrt(differences.Sum(d => (d - mean) * (d - mean)) / (n - 1));
        var half = Z * deviation / Math.Sqrt(n);
        return new PairedMean(n, mean, mean - half, mean + half);
    }
}

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// How much the losing hero takes after a fight: the winner's tavern tier plus the tier of every minion the winner has
/// left, capped by the lobby's combat damage cap while one is in force. Tokens without a tier count as tier 1.
/// </summary>
/// <remarks>
/// The cap is a lobby-wide rule that changes between seasons (15 until the first death in 2022, a 5/10/15 ramp until
/// Top 4 in patch 31.2, 2 then 5 on the first turns in build 254477). The game announces the current value on the game
/// entity (<c>BACON_COMBAT_DAMAGE_CAP</c>, switched by <c>BACON_COMBAT_DAMAGE_CAP_ENABLED</c> in newer builds), so it is
/// read from there instead of being hard-coded per season: see <see cref="Cap"/>.
/// </remarks>
public static class DamageRules
{
    /// <summary>Damage dealt by a winner on <paramref name="tavernTier"/> with surviving minions of the given tiers.</summary>
    /// <param name="cap">Combat damage cap; 0 or less means none.</param>
    public static int Damage(int tavernTier, IEnumerable<int> survivorTiers, int cap) =>
        DamageOfTiers(tavernTier, survivorTiers.Sum(MinionTier), cap);

    /// <summary>
    /// <see cref="Damage(int, IEnumerable{int}, int)"/> with the survivors' shares (<see cref="MinionTier"/>) already
    /// summed: the simulator adds them up per fight without a list.
    /// </summary>
    internal static int DamageOfTiers(int tavernTier, int survivorTierTotal, int cap)
    {
        var damage = Math.Max(0, tavernTier) + survivorTierTotal;
        return cap > 0 ? Math.Min(damage, cap) : damage;
    }

    /// <summary>A minion's share of the damage: its tier, 1 for tokens (no tier).</summary>
    public static int MinionTier(int techLevel) => techLevel > 0 ? techLevel : 1;

    /// <summary>
    /// The cap in force from the game entity's tags: <paramref name="capTag"/> while the cap is on. Builds that send
    /// <c>BACON_COMBAT_DAMAGE_CAP_ENABLED</c> (<paramref name="enabledTag"/> not null) switch it off with 0 there; older
    /// builds set the cap itself to 0 once it is lifted. 0 = no cap.
    /// </summary>
    public static int Cap(int capTag, int? enabledTag) => enabledTag is 0 ? 0 : Math.Max(0, capTag);

    /// <summary>The fight kills the hero: damage reaches its health plus armor. Unknown health (0) never counts.</summary>
    public static bool IsLethal(int damage, int healthAndArmor) => healthAndArmor > 0 && damage >= healthAndArmor;

    /// <summary>
    /// Ice Block, the Battlegrounds secret: "prevent fatal damage to your hero". Power.log 15.11 g4c9 (line 1000169 attack,
    /// 1000193 the secret triggers): a lethal hero attack dealt no damage (rules-evidence 8b).
    /// </summary>
    public const string IceBlock = "TB_Bacon_Secrets_12";

    /// <summary>The side has Ice Block among its known secrets (only the local player's own are ever known).</summary>
    public static bool HasIceBlock(SimSide side)
    {
        foreach (var secret in side.Secrets)
        {
            if (string.Equals(secret, IceBlock, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Damage the hero actually takes: none when the hit would be fatal and the hero has Ice Block, else
    /// <paramref name="damage"/>. Unknown health (0) is never fatal, so the damage stands.
    /// </summary>
    public static int Taken(int damage, int healthAndArmor, bool iceBlock) =>
        iceBlock && IsLethal(damage, healthAndArmor) ? 0 : damage;
}

/// <summary>
/// Hero damage over the simulated fights one side won: the mean, an 80 % interval (<see cref="Low"/>..<see cref="High"/>,
/// the 10th and 90th percentiles), the worst case and the whole distribution.
/// </summary>
/// <param name="Counts">Fights per damage value, index = damage.</param>
public sealed record DamageSpread(double Mean, int Low, int High, int Max, IReadOnlyList<int> Counts)
{
    /// <summary>Fights behind the numbers.</summary>
    public int Runs => Counts.Sum();

    /// <summary>
    /// Builds the spread from fights per damage value; null when no fight is counted.
    /// </summary>
    public static DamageSpread? From(IReadOnlyList<int> counts)
    {
        var runs = 0;
        long total = 0;
        var max = 0;
        for (var d = 0; d < counts.Count; d++)
        {
            runs += counts[d];
            total += (long)d * counts[d];
            if (counts[d] > 0)
            {
                max = d;
            }
        }

        if (runs == 0)
        {
            return null;
        }

        return new DamageSpread(total / (double)runs, Percentile(counts, runs, 0.1), Percentile(counts, runs, 0.9), max,
            counts.Take(max + 1).ToArray());
    }

    /// <summary>Whether a damage value seen after the fight falls inside the predicted interval.</summary>
    public bool Covers(int damage) => damage >= Low && damage <= High;

    private static int Percentile(IReadOnlyList<int> counts, int runs, double share)
    {
        var target = share * runs;
        var seen = 0;
        for (var d = 0; d < counts.Count; d++)
        {
            seen += counts[d];
            if (seen >= target && seen > 0)
            {
                return d;
            }
        }

        return counts.Count - 1;
    }

    // Value equality over the distribution too, so two equal simulations compare equal.
    public bool Equals(DamageSpread? other) =>
        other is not null && Mean.Equals(other.Mean) && Low == other.Low && High == other.High && Max == other.Max
        && Counts.SequenceEqual(other.Counts);

    public override int GetHashCode() => HashCode.Combine(Mean, Low, High, Max, Counts.Count);
}

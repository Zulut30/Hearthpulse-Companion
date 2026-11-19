namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// The minions "a random Pirate" can be (Timewarped Tide Razor): public card data only (the Battlegrounds pool),
/// generated into <c>PiratePool.g.cs</c> by <c>tools/effects/gen_pirate_pool.py</c>. Each entry is the normal card with
/// its printed stats and keywords and no script data, so card effects fall back to their printed numbers.
/// </summary>
internal static partial class PiratePool
{
    /// <summary>The Pirates (shared, immutable).</summary>
    public static IReadOnlyList<SimMinion> Minions => Generated;

    /// <summary>Index of a card among the Pirates a player of <paramref name="tavernTier"/> can get, or -1 (tests script picks with it).</summary>
    public static int IndexOf(string cardId, int tavernTier = 0)
    {
        var index = 0;
        foreach (var minion in Generated)
        {
            if (Allowed(minion, tavernTier))
            {
                if (minion.CardId == cardId)
                {
                    return index;
                }

                index++;
            }
        }

        return -1;
    }

    /// <summary>
    /// A random Pirate up to the player's Tavern tier (every tier when the tier is unknown), picked for
    /// <paramref name="source"/> (<see cref="ChoiceKind.RandomSummon"/>). Null when there is none.
    /// </summary>
    public static SimMinion? Draw(Fight fight, Unit source, int tavernTier)
    {
        var candidates = new List<SimMinion>();
        foreach (var minion in Generated)
        {
            if (Allowed(minion, tavernTier))
            {
                candidates.Add(minion);
            }
        }

        return candidates.Count == 0 ? null : candidates[fight.Choose(ChoiceKind.RandomSummon, source, candidates)];
    }

    private static bool Allowed(SimMinion minion, int tavernTier) => tavernTier <= 0 || minion.TechLevel <= tavernTier;

    private static SimMinion M(string cardId, int attack, int health, int tier, Tribe tribes, Keyword keywords, CardMechanic mechanics) =>
        SimMinion.Create(cardId, attack, health, tribes, keywords, golden: false, mechanics) with { TechLevel = tier };
}

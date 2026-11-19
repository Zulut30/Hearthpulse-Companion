namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>Small board checks the hero powers and trinkets share, without LINQ or allocations.</summary>
internal static class PlayerCards
{
    /// <summary>A captured minion was created by the card <paramref name="cardId"/> (its CREATOR tag).</summary>
    public static bool AnyCreatedBy(SimSide side, string cardId)
    {
        foreach (var minion in side.Minions)
        {
            if (minion.CreatorCardId == cardId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The side still has a living minion (dying ones waiting for the death phase do not count).</summary>
    public static bool AnyAlive(Side side)
    {
        foreach (var unit in side.Minions)
        {
            if (unit.IsAlive)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The minions <paramref name="cardId"/> removed at Start of Combat that it has not summoned back yet (none of the
    /// captured minions comes from it), or null.
    /// </summary>
    public static IReadOnlyList<SimMinion>? Owed(SimSide side, string cardId) =>
        side.RemovedByExtras.TryGetValue(cardId, out var removed) && removed.Count > 0 && !AnyCreatedBy(side, cardId) ? removed : null;

    /// <summary>
    /// Uses left of a "(N times per combat.)" card: its TAG_SCRIPT_DATA_NUM_1 counts them down (Faerie Dragon Scale 3, 2, 1
    /// in a real fight, 2026-11-15); <paramref name="limit"/> when not captured (the opponent's, an older capture).
    /// </summary>
    public static int UsesLeft(SimSide side, string cardId, int limit) =>
        Math.Clamp(side.ExtraTag(cardId, GrowingValue.AttackTag) ?? limit, 0, limit);

    /// <summary>An exact copy of a removed minion, made by <paramref name="cardId"/>.</summary>
    public static SimMinion CopyOf(SimMinion removed, string cardId) =>
        removed with { EntityId = 0, CreatorCardId = cardId, HasUnsimulatedEffect = false };
}

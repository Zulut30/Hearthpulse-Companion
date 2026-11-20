namespace Hearthpulse.Core.Battlegrounds.Effects.Secrets;

/// <summary>
/// The Tavern's minions for "summon a random minion" by tier (Effigy): public card data only (the Battlegrounds pool),
/// generated into <c>MinionPool.g.cs</c> by <c>tools/effects/gen_minion_pool.py</c>. Each entry is the normal card with its
/// printed stats and keywords and no script data, so card effects fall back to their printed numbers.
/// </summary>
internal static partial class MinionPool
{
    /// <summary>The pool minions (shared, immutable).</summary>
    public static IReadOnlyList<SimMinion> Minions => Generated;

    /// <summary>The minions of <paramref name="tier"/> in a lobby of <paramref name="lobby"/> (all tribes when unknown).</summary>
    public static IReadOnlyList<SimMinion> OfTier(int tier, Tribe lobby)
    {
        var candidates = new List<SimMinion>();
        foreach (var minion in SimInputs.InLobby(Generated, lobby))
        {
            if (minion.TechLevel == tier)
            {
                candidates.Add(minion);
            }
        }

        return candidates;
    }

    /// <summary>Index of a card among <see cref="OfTier"/> (tests script picks with it), or -1.</summary>
    public static int IndexOf(string cardId, int tier, Tribe lobby = Tribe.None)
    {
        var candidates = OfTier(tier, lobby);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].CardId == cardId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A random minion of <paramref name="tier"/> from the lobby's tribes, picked for <paramref name="source"/>
    /// (<see cref="ChoiceKind.RandomSummon"/>). Null when there is none.
    /// </summary>
    public static SimMinion? Draw(Fight fight, Unit source, int tier)
    {
        var candidates = OfTier(tier, source.Side.Info.LobbyTribes);
        return candidates.Count == 0 ? null : candidates[fight.Choose(ChoiceKind.RandomSummon, source, candidates)];
    }

    private static SimMinion M(string cardId, int attack, int health, int tier, Tribe tribes, Keyword keywords, CardMechanic mechanics) =>
        SimMinion.Create(cardId, attack, health, tribes, keywords, golden: false, mechanics) with { TechLevel = tier };
}

namespace Hearthpulse.Core.CardPool;

/// <summary>Pools the --render-ui scenes of both apps show, picked from the catalog the same way in both.</summary>
public static class CardPoolScenes
{
    /// <summary>
    /// The first Standard card (by id) of <paramref name="context"/>'s class, or neutral, whose pool on hover has 4 to 12
    /// cards: the panel lists such a pool whole, with no browser button. Null when the catalog has none.
    /// </summary>
    public static string? SmallPoolCard(CardPoolCatalog catalog, CardPoolEngine engine, PoolContext context) =>
        catalog.Cards
            .Where(c => c.Standard && (c.IsNeutral || (context.OwnClass is { } own && c.HasClass(own))))
            .Select(c => c.Id)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(id => engine.Build(id, context, PoolTrigger.Hover) is { Uncomputed: null, Excavate: null, Count: >= 4 and <= 12 });
}

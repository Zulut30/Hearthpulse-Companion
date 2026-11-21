using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Collection;

/// <summary>A hero skin of the card data and whether the collection holds it.</summary>
/// <param name="CardId">The skin's card id in the card data (art comes from there, never from an id of the collection).</param>
/// <param name="CardClass">The class the skin belongs to, as the card data writes it ("MAGE").</param>
public sealed record HeroSkin(string CardId, string CardClass, bool Owned);

/// <summary>
/// The hero skins a player can own: the collectible HERO cards of the HERO_SKINS set. They arrive in the collection's
/// stacks, so no read of their own is needed (docs/collection-cosmetics.md).
/// </summary>
public static class HeroSkins
{
    /// <summary>The skins of <paramref name="cards"/> by class, then name; <see cref="HeroSkin.Owned"/> when the collection has a copy (null: none owned).</summary>
    public static IReadOnlyList<HeroSkin> Build(CardCollection? collection, CardDb cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.All
            .Where(c => c is { Collectible: true, Type: "HERO", Set: "HERO_SKINS" })
            .OrderBy(c => c.CardClass, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new HeroSkin(c.Id, c.CardClass, collection?.Owned(c.Id).Total > 0))
            .ToList();
    }
}

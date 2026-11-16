namespace Hearthpulse.Core.Cards;

public sealed class Card
{
    public required string Id { get; init; }

    public int DbfId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public int Cost { get; init; }

    public int Attack { get; init; }

    public int Health { get; init; }

    public int Armor { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Rarity { get; init; } = string.Empty;

    public string CardClass { get; init; } = string.Empty;

    public string Set { get; init; } = string.Empty;

    public IReadOnlyList<string> Races { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Mechanics { get; init; } = Array.Empty<string>();

    /// <summary>Death Knight rune types in the card's cost (BLOOD, FROST, UNHOLY; HearthstoneJSON <c>runeCost</c>); empty for other cards.</summary>
    public IReadOnlyList<string> Runes { get; init; } = Array.Empty<string>();

    public bool Collectible { get; init; }

    public int TechLevel { get; init; }

    public bool IsBattlegroundsPoolMinion { get; init; }

    public bool IsBattlegroundsPoolSpell { get; init; }

    public bool IsBattlegroundsHero { get; init; }

    public bool IsBattlegroundsDuosExclusive { get; init; }

    public int BattlegroundsPremiumDbfId { get; init; }

    public int BattlegroundsNormalDbfId { get; init; }

    public int BattlegroundsSkinParentId { get; init; }

    /// <summary>
    /// HearthstoneJSON <c>countAsCopyOfDbfId</c>: the card whose copy this printing counts as for deck building (a
    /// Core printing names the original); 0 when the card data links it to no other printing.
    /// </summary>
    public int CountAsCopyOfDbfId { get; init; }

    /// <summary>
    /// The card data has a <c>howToEarn</c> text: the game gives the card in some way (a reward, an adventure, a free
    /// set). A hint only: such a card may or may not be craftable right now.
    /// </summary>
    public bool HasHowToEarn { get; init; }

    public bool IsLegendary => Rarity == "LEGENDARY";

    public bool HasRace(string race) => Races.Contains(race) || Races.Contains("ALL");

    public override string ToString() => $"{Name} ({Id})";
}

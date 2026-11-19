namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Cyclist Portrait (BG36_MagicItem_362): "Get a Showy Cyclist. Your Showy Cyclists' Deathrattles give permanent stats."
/// Registered without hooks on purpose: "permanent" only decides whether the stats stay after the fight. In this fight the
/// Showy Cyclist's deathrattle gives the same stats either way (<see cref="Deaths.ShowyCyclist"/>), and a reborn copy is a
/// fresh copy of the card in both cases, so the trinket changes nothing the simulator measures. The class tells the
/// coverage tables the card is accounted for.
/// </summary>
[CardEffect("BG36_MagicItem_362")]
internal sealed class CyclistPortrait : CombatEffect
{
}

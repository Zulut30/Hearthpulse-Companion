namespace Hearthpulse.Core.Battlegrounds.Effects.Rewards;

/// <summary>Gilnean War Horn (BG27_Reward_802): "Get a copy of '{0}'. Your Battlecries trigger an extra time."</summary>
/// <remarks>
/// In today's pool. Battlecries trigger in the Tavern, and in combat only when an effect triggers one
/// (<see cref="Fight.TriggerBattlecry"/>, e.g. Timewarped Greenskeeper's Rally): then it triggers once more. Assumption
/// (no log with the reward): it follows the engine's Battlecry rule, where the largest multiplier wins, so with Brann
/// Bronzebeard a Battlecry triggers twice, not three times.
/// </remarks>
[CardEffect("BG27_Reward_802")]
internal sealed class GilneanWarHorn : CombatEffect
{
    public override int ExtraBattlecries(Fight fight, Unit self) => 1;
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Rewards;

// Quest rewards (card type BATTLEGROUND_QUEST_REWARD) that act in combat after the boards are captured. The tracker
// passes a player's rewards in play as extras (off-board units of its side, BattlegroundsTracker.ExtraCardTypes), so a
// reward receives the side's hooks like a trinket. Only rewards in today's pool are here (Hearthpulse API
// /api/v1/rewards in_pool and hsbg.cards agree on 2026-11-19): of the rewards whose text acts in combat Turbulent Tombs
// and Gilnean War Horn (through Battlecries triggered in combat) are in the pool. Ritual Dagger, The Smoking Gun, Volatile Venom, Boom Squad, Tumbling Disaster, Stable
// Amalgamation and Rallying Cry are out of it; the first six are in RotationRewards.cs (E6a) for when they return.

/// <summary>Turbulent Tombs (BG27_Reward_803): "Get a copy of '{0}'. Your Deathrattles trigger an extra time."</summary>
/// <remarks>
/// The copy goes to the hand in the Tavern. In combat each friendly deathrattle triggers once more. Assumption (no log
/// with the reward): it adds to Titus Rivendare's extra triggers rather than replacing them, as the engine sums every
/// extra-trigger source of a side.
/// </remarks>
[CardEffect("BG27_Reward_803")]
internal sealed class TurbulentTombs : CombatEffect
{
    public override int ExtraDeathrattles(Fight fight, Unit self) => 1;
}

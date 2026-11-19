namespace Hearthpulse.Core.Battlegrounds.Effects.Rewards;

/// <summary>Temporal Tampering (BG28_Reward_501): "Your Tavern spells cast an extra time."</summary>
/// <remarks>
/// In today's pool. In combat a Tavern spell is cast only by another effect (Crimson Vindicator's Rally casting Mighty
/// Dragonbreath, <see cref="Fight.CastSpell(in SpellCast)"/>); every spell cast in combat today is a Tavern spell, so each
/// one is cast once more. Assumption (no log with the reward): it follows the engine's spell rule, where the largest
/// multiplier wins (<see cref="Fight.SpellCasts"/>), so with Balinda Stonehearth a spell on a friendly minion is cast
/// twice, not three times.
/// </remarks>
[CardEffect("BG28_Reward_501")]
internal sealed class TemporalTampering : CombatEffect
{
    public override int ExtraSpellCasts(Fight fight, Unit self, string spellCardId, Unit? target) => 1;
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Rewards;

/// <summary>Rallying Cry (BG33_Reward_021): "Get a copy of '{0}'. Your Rallies trigger an extra time."</summary>
/// <remarks>
/// Out of today's pool (Hearthpulse API v1 in_pool, 2026-11-19); simulated so a capture or a quest that gives it is not
/// "not simulated". When a friendly Rally minion attacks, its Rally (the attack hook of its effects) runs once more, on
/// the same target, while it is alive. The engine has no Rally multiplier: the extra trigger runs from the friendly-attack
/// hook, so it comes next to the minion's own Rally rather than strictly after it. Assumption (no log with the reward):
/// one extra trigger per attack, each Windfury swing included.
/// </remarks>
[CardEffect("BG33_Reward_021")]
internal sealed class RallyingCry : CombatEffect
{
    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (!attacker.IsRally || !attacker.IsAlive)
        {
            return;
        }

        foreach (var effect in attacker.Effects)
        {
            if ((effect.Hooks & Hook.Attack) != 0)
            {
                effect.OnAttack(fight, attacker, target);
            }
        }
    }
}

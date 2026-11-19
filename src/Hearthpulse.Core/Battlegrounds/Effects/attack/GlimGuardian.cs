namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Glim Guardian (BG29_888): "Rally: Gain +{0} Attack." (+2; golden +4.) The amount is the captured
/// TAG_SCRIPT_DATA_NUM_1 (real log 15.11.2026: 2, golden 4), else the printed value.
/// </summary>
[CardEffect("BG29_888")]
internal sealed class GlimGuardian : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => fight.Buff(self, Deaths.DeathKit.Shown1(self, 2, 4), 0);
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Roaring Recruiter (BG29_816): "Whenever another friendly Dragon attacks, give it +3/+1." Golden: "+6/+2."
/// Before damage, after the attacker's own Rally. The amount is the captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026:
/// 3/1), else the printed value.
/// </summary>
[CardEffect("BG29_816")]
internal sealed class RoaringRecruiter : CombatEffect
{
    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (attacker != self && attacker.IsOnBoard && attacker.Is(Tribe.Dragon))
        {
            var (attack, health) = Deaths.DeathKit.Shown(self, (3, 1), (6, 2));
            fight.Buff(attacker, attack, health);
        }
    }
}

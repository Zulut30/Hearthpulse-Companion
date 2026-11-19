namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Wolf Pup (BG36_207): "Rally: Give your other minions +{0}/+{1}." (+4/+1; golden +8/+2.) The amount is the captured
/// TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026: 4/1), else the printed value.
/// </summary>
[CardEffect("BG36_207")]
internal sealed class WolfPup : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var (attack, health) = Deaths.DeathKit.Shown(self, (4, 1), (8, 2));
        foreach (var unit in fight.Alive(self.Side, except: self))
        {
            fight.Buff(unit, attack, health);
        }
    }
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Bonker (BG20_104): "Windfury. Rally: This plays a Blood Gem on all your other minions." Golden: "plays 2 Blood Gems".
/// Windfury is a captured keyword; the Rally runs on every attack, so on both Windfury swings. The number of gems is the
/// captured TAG_SCRIPT_DATA_NUM_1 (real log 15.11.2026: 1), else the printed 1 (golden 2).
/// </summary>
[CardEffect("BG20_104")]
internal sealed class Bonker : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var gems = Deaths.DeathKit.Shown1(self, 1, 2);
        foreach (var unit in fight.Alive(self.Side, except: self))
        {
            BloodGems.Play(fight, self, unit, gems);
        }
    }
}

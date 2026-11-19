namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Razorfen Vineweaver (BG33_883): "Rally: This plays 4 permanent Blood Gems on itself." Golden: "8 permanent Blood Gems".
/// "Permanent" only matters after combat. The number of gems is the captured TAG_SCRIPT_DATA_NUM_1 (real log 15.11.2026: 4),
/// else the printed value.
/// </summary>
[CardEffect("BG33_883")]
internal sealed class RazorfenVineweaver : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => BloodGems.Play(fight, self, self, Deaths.DeathKit.Shown1(self, 4, 8));
}

using Hearthpulse.Core.Battlegrounds.Effects.Deaths;

namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Sanguine Refiner (BG33_885): "Rally: Your Blood Gems give an extra +1/+1 this game." Golden (BG33_885_G): "an extra
/// +2/+2".
/// </summary>
/// <remarks>
/// The Rally runs when it attacks, before damage, and raises the owner's Blood Gem bonus for the rest of the fight (see
/// <see cref="SanguineChampion"/>): its own attack is not changed, Blood Gems played after it give more. A real log
/// (2026-11-15) shows a side's player tags rise by +2/+2 in combat at a golden Refiner's attack. The numbers come from the
/// minion's captured TAG_SCRIPT_DATA_NUM_1/2 (1/1, golden 2/2 in that log); the card text is the fallback.
/// </remarks>
[CardEffect("BG33_885")]
internal sealed class SanguineRefiner : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) =>
        BloodGemBonus.Add(fight, self.Side, BloodGemBonus.Captured(self.Spec.ScriptData1, self.Pick(1, 2)),
                          BloodGemBonus.Captured(self.Spec.ScriptData2, self.Pick(1, 2)));
}

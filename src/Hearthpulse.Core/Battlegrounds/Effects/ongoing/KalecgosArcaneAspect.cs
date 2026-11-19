namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Kalecgos, Arcane Aspect (BGS_041): "After you trigger a Battlecry, give your Dragons +2/+2." Golden (TB_BaconUps_109):
/// "+4/+4."
/// </summary>
/// <remarks>
/// In combat it reacts only to Battlecries an effect triggers there (Timewarped Greenskeeper's Rally,
/// <see cref="Fight.TriggerBattlecry"/>), once per trigger: Brann's extra triggers count. "Your Dragons" are the living
/// friendly Dragons, Kalecgos included. The numbers come from its captured TAG_SCRIPT_DATA_NUM_1/2 (2/2 on a normal one in
/// a real log of 2026-11-15); the card text is the fallback.
/// </remarks>
[CardEffect("BGS_041")]
internal sealed class KalecgosArcaneAspect : CombatEffect
{
    public override void AfterFriendlyBattlecry(Fight fight, Unit self, Unit source)
    {
        if (!self.IsAlive)
        {
            return;
        }

        var attack = self.Spec.ScriptData1 > 0 ? self.Spec.ScriptData1 : self.Pick(2, 4);
        var health = self.Spec.ScriptData2 > 0 ? self.Spec.ScriptData2 : self.Pick(2, 4);
        var side = self.Side;
        for (var i = 0; i < side.Count; i++)
        {
            var unit = side.Minions[i];
            if (unit.IsAlive && unit.Is(Tribe.Dragon))
            {
                fight.Buff(unit, attack, health);
            }
        }
    }
}

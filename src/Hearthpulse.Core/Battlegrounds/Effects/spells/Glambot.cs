namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Glambot (BG36_853): "Whenever you cast a spell on a Mech, Magnetize a 5/5 Satellite to it." Golden: "Magnetize a 5/5
/// Satellite to it twice." A Satellite (BG31_171t) is a 5/5 Magnetic minion with no other text: each one magnetized adds
/// its stats to the Mech the spell targets (TAG_SCRIPT_DATA_NUM_1/2 of the Glambot, 5/5 in real logs). Before the spell
/// resolves.
/// </summary>
[CardEffect("BG36_853")]
internal sealed class Glambot : CombatEffect
{
    public const string SatelliteId = "BG31_171t";

    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target is not { IsAlive: true } target || !target.Is(Tribe.Mech))
        {
            return;
        }

        var (attack, health) = (SpellTriggers.Shown1(self, 5, 5), SpellTriggers.Shown2(self, 5, 5));
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            if (fight.Tracing) fight.Trace($"magnetize {SatelliteId} {attack}/{health} to {target}");
            fight.Buff(target, attack, health);
        }
    }
}

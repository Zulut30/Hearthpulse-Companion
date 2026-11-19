namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Relentless Deflector (BG34_405): "Has Taunt while this has Divine Shield. Avenge (3): Gain Divine Shield." Golden: same
/// text. The Taunt of a shield gained in combat follows it: it goes when that shield breaks and comes back with the next
/// Avenge shield (Power.log 15.11 g4c9, Deflector #10059: DIVINE_SHIELD 1/0/1/0 with TAUNT 1/0/1/0 from line 998047). A
/// Deflector that came to the fight with Taunt keeps it when its shield breaks (g3c9, #9636: DIVINE_SHIELD 0 at its first
/// hit, TAUNT stays 1, and our next three attacks all had to hit it). Avenge counts friendly deaths from the start of the
/// fight (engine rule).
/// The X of Avenge (X) is the captured TAG_SCRIPT_DATA_NUM_1 (real log 15.11.2026: 3, unchanged while it counts), else
/// the printed 3.
/// </summary>
[CardEffect("BG34_405")]
internal sealed class RelentlessDeflector : AvengeEffect
{
    protected override int Threshold(Unit self) => Deaths.DeathKit.Shown1(self, 3, 3);

    protected override void Avenge(Fight fight, Unit self)
    {
        if (fight.Tracing) fight.Trace($"avenge: {self} gains Divine Shield");
        fight.GiveKeywords(self, Keyword.DivineShield);
    }

    public override void Refresh(Fight fight, Unit self) => self.Taunt = self.DivineShield || HasOwnTaunt(self);

    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit)
    {
        if (unit == self)
        {
            Refresh(fight, self);
        }
    }

    /// <summary>The Taunt it came to the fight with (with or without Divine Shield): the shield breaking does not take it.</summary>
    private static bool HasOwnTaunt(Unit self) => self.Spec.Taunt;
}

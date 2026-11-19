namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Charging Czarina (BG28_741): "Divine Shield. Whenever you cast a Tavern spell, give your minions with Divine Shield
/// +4 Attack." Golden: "+8 Attack". Before the spell resolves; "with Divine Shield" is checked then. Divine Shield itself
/// is a captured keyword.
/// </summary>
[CardEffect("BG28_741")]
internal sealed class ChargingCzarina : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (!spell.IsTavernSpell)
        {
            return;
        }

        var attack = SpellTriggers.Shown1(self, 4, 8);
        foreach (var unit in fight.Alive(self.Side))
        {
            if (unit.DivineShield)
            {
                fight.Buff(unit, attack, 0);
            }
        }
    }
}

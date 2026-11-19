namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Vicious Mindslasher (BG36_108): "Whenever you cast a Tavern spell, give this and your Deity +1/+2." Golden: "+2/+4".
/// </summary>
[CardEffect("BG36_108")]
internal sealed class ViciousMindslasher : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (!spell.IsTavernSpell)
        {
            return;
        }

        var (attack, health) = (SpellTriggers.Shown1(self, 1, 2), SpellTriggers.Shown2(self, 2, 4));
        fight.Buff(self, attack, health);
        SpellTriggers.GiveDeity(fight, self.Side, attack, health);
    }
}

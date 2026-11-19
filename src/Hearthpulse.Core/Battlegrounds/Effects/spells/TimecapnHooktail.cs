namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Timecap'n Hooktail (BG27_005): "Whenever you cast a Tavern spell, give your minions +1 Attack." Golden: "give your
/// minions +1 Attack twice" (two separate +1 Attack buffs). Before the spell resolves; "your minions" includes this one.
/// </summary>
[CardEffect("BG27_005")]
internal sealed class TimecapnHooktail : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (!spell.IsTavernSpell)
        {
            return;
        }

        var attack = SpellTriggers.Shown1(self, 1, 1);
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            SpellTriggers.BuffAll(fight, self.Side, attack, 0);
        }
    }
}

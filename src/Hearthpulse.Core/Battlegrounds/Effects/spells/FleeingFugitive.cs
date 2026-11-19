namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>Fleeing Fugitive (BG36_921): "Whenever you cast a spell on this, gain +1 Health." Golden: "+2 Health".</summary>
[CardEffect("BG36_921")]
internal sealed class FleeingFugitive : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target == self)
        {
            fight.Buff(self, 0, SpellTriggers.Shown1(self, 1, 2));
        }
    }
}

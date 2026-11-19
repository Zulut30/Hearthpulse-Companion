namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Abyssal Bruiser (BG35_921): "Divine Shield. Has +2/+1 for each Tavern spell you've cast this game." Golden: "+4/+2".
/// The captured stats hold the bonus for spells cast before the fight; each Tavern spell cast in this fight adds it once
/// more, after the spell resolved. The per-spell +2/+1 stays the printed one: what its script data would hold (the
/// per-spell value or the running total) is not seen in a log.
/// </summary>
[CardEffect("BG35_921")]
internal sealed class AbyssalBruiser : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (SpellTriggers.AfterTavernSpell(fight) && self.IsAlive)
        {
            fight.Buff(self, self.Pick(2, 4), self.Pick(1, 2));
        }
    }
}

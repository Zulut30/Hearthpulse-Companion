namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Forsaken Weaver (BG34_692): "After you cast a Tavern spell, your Undead have +3 Attack this game (wherever they
/// are)." Golden: "+6 Attack". After the spell resolved: living friendly Undead now, and Undead that enter the board
/// later this fight (tokens, reborn copies, from the hand) when they arrive.
/// </summary>
[CardEffect("BG34_692")]
internal sealed class ForsakenWeaver : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (SpellTriggers.AfterTavernSpell(fight))
        {
            SpellTriggers.BuffThisGame(fight, self.Side, Tribe.Undead, SpellTriggers.Shown1(self, 3, 6), 0);
        }
    }
}

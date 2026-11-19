namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Gatekeeper Amalgam (BG36_640): "Whenever you cast a spell on this, it casts Misplaced Tea Set." Golden: "it casts
/// Misplaced Tea Set twice." The Tea Set is the player's Tavern spell (any spell a minion casts in combat is, see
/// <see cref="Fight.CastSpell(in SpellCast)"/>), cast before the spell on the Amalgam resolves. It has no target, so it
/// does not set this off again.
/// </summary>
[CardEffect("BG36_640")]
internal sealed class GatekeeperAmalgam : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target != self)
        {
            return;
        }

        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            TavernSpells.Cast(fight, self, TavernSpells.MisplacedTeaSet, null, TavernSpells.MisplacedTeaSetSpell);
        }
    }
}

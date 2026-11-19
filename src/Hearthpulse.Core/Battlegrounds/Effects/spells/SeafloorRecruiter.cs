namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Seafloor Recruiter (BG34_925): "Rally: Cast Chef's Choice on the minion to the right." Golden: "Cast Chef's Choice on
/// the minion to the right twice."
/// </summary>
/// <remarks>
/// When it attacks (before damage), Chef's Choice (BG28_518, a Tavern spell: "Choose a minion. Get a different minion of
/// the same type.") is cast on the living friendly minion right of it, so "whenever you cast a spell on X" effects react
/// to that minion. With no living minion to its right nothing is cast (assumption: the spell needs a target). The golden
/// one casts twice on the same minion. Not seen in a real log yet.
/// </remarks>
[CardEffect("BG34_925")]
internal sealed class SeafloorRecruiter : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            if (fight.Neighbours(self).Right is not { IsAlive: true } right)
            {
                return;
            }

            TavernSpells.Cast(fight, self, TavernSpells.ChefsChoice, right, TavernSpells.ChefsChoiceSpell);
        }
    }
}

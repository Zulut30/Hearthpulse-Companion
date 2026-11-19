namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Sha of Fear (BG36_111): "Whenever you cast a Tavern spell, give your minions and Deity +3/+3." Golden: "+6/+6".
/// Before the spell resolves. The Deity part follows <see cref="Ongoing.Deity.Give"/> (asleep: its stats grow; awake:
/// the minion on the board).
/// </summary>
[CardEffect("BG36_111")]
internal sealed class ShaOfFear : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (!spell.IsTavernSpell)
        {
            return;
        }

        var (attack, health) = (SpellTriggers.Shown1(self, 3, 6), SpellTriggers.Shown2(self, 3, 6));
        SpellTriggers.BuffAll(fight, self.Side, attack, health);
        SpellTriggers.GiveDeity(fight, self.Side, attack, health);
    }
}

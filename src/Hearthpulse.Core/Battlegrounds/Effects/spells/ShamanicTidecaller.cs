namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Shamanic Tidecaller (BG36_704): "Whenever you cast a spell on a Murloc, give Murlocs in your hand and board +3/+4."
/// Golden: "+6/+8".
/// </summary>
/// <remarks>
/// Real log (2026-11-15, a spell played on a Murloc in the Tavern): each Tidecaller triggers before the spell resolves,
/// left to right, and buffs every Murloc on the board (itself included) and every Murloc in the hand. In combat the hand
/// part changes only minions later summoned from the hand: the local player's hand list is buffed; the opponent's hand is
/// hidden (its list holds only cards got in this fight).
/// </remarks>
[CardEffect("BG36_704")]
internal sealed class ShamanicTidecaller : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target is not { } target || !target.Is(Tribe.Murloc))
        {
            return;
        }

        var (attack, health) = (SpellTriggers.Shown1(self, 3, 6), SpellTriggers.Shown2(self, 4, 8));
        SpellTriggers.BuffHand(self.Side, attack, health, static m => (m.Tribes & Tribe.Murloc) != 0);
        SpellTriggers.BuffAll(fight, self.Side, attack, health, Tribe.Murloc);
    }
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Torrential Ruiner (BG36_622): "Whenever you cast a spell on a Naga, give your minions +2/+3." Golden: "+4/+6".
/// Before the spell resolves; "your minions" includes this one, and the Naga may be this one.
/// </summary>
[CardEffect("BG36_622")]
internal sealed class TorrentialRuiner : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target is { } target && target.Is(Tribe.Naga))
        {
            SpellTriggers.BuffAll(fight, self.Side, SpellTriggers.Shown1(self, 2, 4), SpellTriggers.Shown2(self, 3, 6));
        }
    }
}

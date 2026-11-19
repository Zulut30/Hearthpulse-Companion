namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Twilight Tidehunter (BG36_703): "Whenever you cast a spell on this, give the left-most minion in your hand +9/+9."
/// Golden: "+18/+18". It matters in combat only for a minion later summoned from the hand. The hand list holds minions
/// only, left to right, so its first entry is the left-most minion in hand; the opponent's list holds only cards got in
/// this fight (its hand is hidden).
/// </summary>
[CardEffect("BG36_703")]
internal sealed class TwilightTidehunter : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.Target != self)
        {
            return;
        }

        var hand = self.Side.Hand;
        if (hand.Count > 0)
        {
            hand[0] = SpellTriggers.Buffed(hand[0], SpellTriggers.Shown1(self, 9, 18), SpellTriggers.Shown2(self, 9, 18));
        }
    }
}

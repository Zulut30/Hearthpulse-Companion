namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Sneed's New Shredder (BG21_HERO_030t, 2/1, from Sneed's hero power Pilot the Shredder): "Deathrattle: Summon the
/// highest-Health minion from your hand and give it Divine Shield for this combat." Golden (BG21_HERO_030t_G): "the 2
/// highest-Health minions … give them Divine Shield".
/// </summary>
/// <remarks>
/// Found by the relevance audit (2026-11-19): the token was combat-relevant with no effect. As Bassgill: it needs the
/// owner's hand, so the opponent's Shredder stays "not simulated" (<see cref="NeedsHand"/>); the minion leaves the per-fight
/// copy of the hand and is summoned where the Shredder stood, the golden card's second to the right of the first; ties on
/// Health are broken at random; with a full board nothing is summoned and the minion stays in hand.
/// </remarks>
[CardEffect("BG21_HERO_030t")]
internal sealed class SneedsNewShredder : CombatEffect
{
    public override bool NeedsHand => true;

    public override void Deathrattle(Fight fight, Unit self)
    {
        var hand = self.Side.Hand;
        var place = self;
        for (var n = 0; n < self.Pick(1, 2); n++)
        {
            var index = fight.BestCard(hand, static _ => true, m => m.Health, self);
            if (index < 0 || fight.SummonAt(place, hand[index]) is not { } summoned)
            {
                return;
            }

            hand.RemoveAt(index);
            fight.GiveKeywords(summoned, Keyword.DivineShield);
            place = summoned;
        }
    }
}

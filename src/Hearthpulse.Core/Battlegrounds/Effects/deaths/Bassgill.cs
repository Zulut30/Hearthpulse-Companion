namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Bassgill (BG26_350): "Deathrattle: Summon the highest-Health Murloc from your hand for this combat only."
/// Golden: "Summon the 2 highest-Health Murlocs from your hand for this combat only."
/// </summary>
/// <remarks>
/// Needs the owner's hand, so it is simulated for the local player only: the opponent's hand is hidden
/// (<see cref="Side.Hand"/> is empty for them) and their Bassgill stays "not simulated" (<see cref="NeedsHand"/>).
/// The Murloc leaves the (per-fight copy of the) hand and is summoned where Bassgill stood, with the stats it has in
/// hand; the golden card's second Murloc goes to the right of the first. Ties on Health are broken at random. With a
/// full board nothing is summoned and the Murloc stays in hand, so a later trigger of the deathrattle can still
/// summon it.
/// </remarks>
[CardEffect("BG26_350")]
internal sealed class Bassgill : CombatEffect
{
    public override bool NeedsHand => true;

    public override void Deathrattle(Fight fight, Unit self)
    {
        var hand = self.Side.Hand;
        var place = self;
        for (var n = 0; n < self.Pick(1, 2); n++)
        {
            var index = fight.BestCard(hand, IsMurloc, m => m.Health, self);
            if (index < 0 || fight.SummonAt(place, hand[index]) is not { } summoned)
            {
                return;
            }

            hand.RemoveAt(index);
            place = summoned;
        }
    }

    private static bool IsMurloc(SimMinion minion) => (minion.Tribes & Tribe.Murloc) != 0;
}

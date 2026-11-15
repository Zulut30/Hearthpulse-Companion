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
            var index = HighestHealthMurloc(fight, hand);
            if (index < 0 || fight.SummonAt(place, hand[index]) is not { } summoned)
            {
                return;
            }

            hand.RemoveAt(index);
            place = summoned;
        }
    }

    private static int HighestHealthMurloc(Fight fight, List<SimMinion> hand)
    {
        int best = -1, ties = 0;
        for (var i = 0; i < hand.Count; i++)
        {
            if ((hand[i].Tribes & Tribe.Murloc) == 0)
            {
                continue;
            }

            if (best < 0 || hand[i].Health > hand[best].Health)
            {
                best = i;
                ties = 1;
            }
            else if (hand[i].Health == hand[best].Health && fight.Random.Next(++ties) == 0)
            {
                best = i;
            }
        }

        return best;
    }
}

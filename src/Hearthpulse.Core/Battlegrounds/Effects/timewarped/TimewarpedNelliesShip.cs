namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// Timewarped Nellie's Ship (BG34_Giant_074t): "At the start of each turn, Discover a Pirate to crew the ship.
/// Deathrattle: Summon and get that Pirate." Golden: "Discover 2 Pirates ... Summon and get those Pirates."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Our ship: the crew is the Pirate(s) the player picked in the ship's Discover on the latest turn
/// (<see cref="SimMinion.Crew"/>, recorded by the tracker from the player's own pick), each summoned in the ship's slot
/// as the fresh card, then a copy added to the hand. A ship without a recorded crew (bought this turn: no start of turn
/// yet; a copy made in combat) summons nothing.</item>
/// <item>The opponent's ship: their pick is hidden, so the crew is estimated (<see cref="SimInputs.ShipCrew"/>, listed in
/// the side's estimates): a random Pirate up to their Tavern tier per crew member, for a ship captured on the board.</item>
/// <item>No real log with the ship was available (2026-11-19): the summon-then-get order follows the card text.</item>
/// </list>
/// </remarks>
[CardEffect("BG34_Giant_074t")]
internal sealed class TimewarpedNelliesShip : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var side = self.Side;
        var crew = self.Spec.Crew;
        if (crew.Count > 0)
        {
            foreach (var pirate in crew)
            {
                fight.SummonAt(self, pirate);
                fight.AddToHand(side, pirate.CardId, pirate);
            }

            return;
        }

        if (!side.IsOpponent || self.Spec.EntityId == 0)
        {
            return;
        }

        for (var i = self.Pick(1, 2); i > 0; i--)
        {
            if (PiratePool.Draw(fight, self, side.Info.TavernTier) is { } pirate)
            {
                fight.SummonAt(self, pirate);
                fight.AddToHand(side, pirate.CardId, pirate);
            }
        }
    }
}

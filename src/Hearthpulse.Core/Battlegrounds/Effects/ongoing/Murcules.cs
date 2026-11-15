namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Murcules (BG27_023): "Whenever this kills a minion, give a minion in your hand +3/+3." Golden: +6/+6.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The buff goes to a random minion of the owner's hand (the per-fight copy). It changes this fight only through
/// a card that summons from the hand (Bassgill, Expert Aviator, Diremuck Forager): the Murloc they bring in is the
/// one with the buffed stats, and the buff can change which Murloc is the highest.</item>
/// <item>The opponent's hand is hidden and stays empty here, so their Murcules does nothing, and that is the whole
/// truth of the fight unless they also hold a card that summons from the hand. Such a card is "not simulated" on
/// their side by itself (<see cref="CombatEffect.NeedsHand"/>), so Murcules does not repeat the flag.</item>
/// </list>
/// </remarks>
[CardEffect("BG27_023")]
internal sealed class Murcules : CombatEffect
{
    public override void AfterKill(Fight fight, Unit self, Unit victim)
    {
        var hand = self.Side.Hand;
        if (hand.Count == 0)
        {
            return;
        }

        var index = fight.Random.Next(hand.Count);
        var bonus = self.Pick(3, 6);
        var minion = hand[index];
        hand[index] = minion with
        {
            Attack = minion.Attack + bonus,
            Health = minion.Health + bonus,
            MaxHealth = (minion.MaxHealth > 0 ? minion.MaxHealth : minion.Health) + bonus,
        };
    }
}

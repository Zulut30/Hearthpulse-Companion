namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

// Timewarped minions that read their owner's hand. The opponent's hand is hidden (Side.Hand is empty for them), so these
// stay "not simulated" on the opponent's side (NeedsHand). The hand is the fight's own copy: a minion summoned from it
// leaves it, a buff given to it lasts for this fight.

/// <summary>
/// Timewarped Bassgill (BG34_Giant_071): "Deathrattle: Summon the highest-Health minion from your hand and give it Divine
/// Shield for this combat only." Golden: "the 2 highest-Health minions … give them Divine Shield".
/// </summary>
/// <remarks>
/// Any minion of the hand (unlike Bassgill, which takes Murlocs); ties on Health are broken at random. The minion is
/// summoned where the Bassgill stood with its hand stats, then gets Divine Shield; the golden card's second minion goes
/// to the right of the first. With a full board nothing is summoned and the minion stays in hand.
/// </remarks>
[CardEffect("BG34_Giant_071")]
internal sealed class TimewarpedBassgill : CombatEffect
{
    public override bool NeedsHand => true;

    public override void Deathrattle(Fight fight, Unit self)
    {
        var hand = self.Side.Hand;
        var place = self;
        for (var n = 0; n < self.Pick(1, 2); n++)
        {
            var index = fight.BestCard(hand, static _ => true, static m => m.Health, self);
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

/// <summary>
/// Timewarped Murcules (BG34_Giant_207): "Divine Shield. Whenever this kills a minion, give the left-most minion in your
/// hand +4/+4." Golden: +8/+8.
/// </summary>
/// <remarks>
/// Changes the fight only through a card that later summons from the hand (Bassgill, Timewarped Scourfin...). The hand
/// holds the captured minions in hand order, so the left-most minion is its first.
/// </remarks>
[CardEffect("BG34_Giant_207")]
internal sealed class TimewarpedMurcules : CombatEffect
{
    public override bool NeedsHand => true;

    public override void AfterKill(Fight fight, Unit self, Unit victim)
    {
        var hand = self.Side.Hand;
        if (hand.Count > 0)
        {
            var bonus = self.Pick(4, 8);
            hand[0] = Buffed(hand[0], bonus);
        }
    }

    internal static SimMinion Buffed(SimMinion minion, int bonus) => minion with
    {
        Attack = minion.Attack + bonus,
        Health = minion.Health + bonus,
        MaxHealth = (minion.MaxHealth > 0 ? minion.MaxHealth : minion.Health) + bonus,
    };
}

/// <summary>
/// Timewarped Scourfin (BG34_Giant_017): "Taunt. Deathrattle: Give a random minion in your hand +7/+7 and summon it for
/// this combat only." Golden: +14/+14.
/// </summary>
/// <remarks>
/// The minion is buffed, then summoned where the Scourfin stood. With a full board it stays in hand with the buff. Taunt
/// is a keyword the engine handles.
/// </remarks>
[CardEffect("BG34_Giant_017")]
internal sealed class TimewarpedScourfin : CombatEffect
{
    public override bool NeedsHand => true;

    public override void Deathrattle(Fight fight, Unit self)
    {
        var hand = self.Side.Hand;
        if (hand.Count == 0)
        {
            return;
        }

        var index = hand.Count > 1 ? fight.Choose(ChoiceKind.RandomHandCard, self, hand) : 0;
        hand[index] = TimewarpedMurcules.Buffed(hand[index], self.Pick(7, 14));
        if (fight.SummonAt(self, hand[index]) is not null)
        {
            hand.RemoveAt(index);
        }
    }
}

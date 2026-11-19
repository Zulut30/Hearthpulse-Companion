namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Very Hungry Winterfinner (BG29_300): "Taunt. Whenever this takes damage, give a minion in your hand +2/+1." Golden
/// (BG29_300_G): +4/+2.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Real log 15.11.2026 (game 2, fights 9 and 12): the trigger follows damage that got through (a hit absorbed by
/// Divine Shield does not fire it), also on the hit that kills it; the hand card gets enchantment BG29_300e with
/// +2/+1 (179/172 → 181/173). The minion carries no script data, so the amount is the printed one (or the captured
/// TAG_SCRIPT_DATA_NUM_1/2 if a later build adds it).</item>
/// <item>The buff goes to a random minion of the owner's hand (the per-fight copy). It changes the fight only through a
/// card that summons from the hand (Bassgill, Expert Aviator, Diremuck Forager, Dark Paradox): that minion comes in with
/// the buffed stats. Needs the hand, so the opponent's Winterfinner stays "not simulated" (<see cref="NeedsHand"/>).</item>
/// </list>
/// </remarks>
[CardEffect("BG29_300")]
internal sealed class VeryHungryWinterfinner : CombatEffect
{
    public override bool NeedsHand => true;

    public override void OnDamaged(Fight fight, Unit self, Unit? source, int amount)
    {
        var hand = self.Side.Hand;
        if (hand.Count == 0 || amount <= 0)
        {
            return;
        }

        var index = fight.Choose(ChoiceKind.RandomHandCard, self, hand);
        var (attack, health) = Deaths.DeathKit.Shown(self, (2, 1), (4, 2));
        var minion = hand[index];
        hand[index] = minion with
        {
            Attack = minion.Attack + attack,
            Health = minion.Health + health,
            MaxHealth = (minion.MaxHealth > 0 ? minion.MaxHealth : minion.Health) + health,
        };
    }
}

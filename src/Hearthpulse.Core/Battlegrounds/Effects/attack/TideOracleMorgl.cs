namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Tide Oracle Morgl (BG27_513): "Poisonous. When this attacks and kills a minion, give its maximum stats to a minion in
/// your hand." Golden (BG27_513_G): "give double its maximum stats".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only its own attacks count (not a minion it kills while defending). "Its maximum stats" are the killed minion's
/// Attack and maximum Health when the attack resolved (damage taken does not lower them); a minion that survived its
/// Divine Shield was not killed.</item>
/// <item>The stats go to a random minion of the owner's hand (the per-fight copy). This changes the fight only through a
/// card that later summons from the hand (Bassgill, Expert Aviator, Diremuck Forager), as Murcules does.</item>
/// <item>Needs the owner's hand, so it is simulated for the local player only: the opponent's hand is hidden and their
/// Morgl stays "not simulated" (<see cref="NeedsHand"/>). Poisonous is a captured keyword.</item>
/// <item>No real log with this minion was available (2026-11-19): the effect follows the card text.</item>
/// </list>
/// </remarks>
[CardEffect("BG27_513")]
internal sealed class TideOracleMorgl : CombatEffect
{
    public override bool NeedsHand => true;

    public override void AfterAttack(Fight fight, Unit self, Unit target, AttackOutcome outcome)
    {
        var hand = self.Side.Hand;
        if (!outcome.Killed || hand.Count == 0)
        {
            return;
        }

        var factor = self.Pick(1, 2);
        var attack = target.Attack * factor;
        var health = Math.Max(target.MaxHealth, 0) * factor;
        // "A minion in your hand": one at random, a decision of this Morgl.
        var index = fight.Choose(ChoiceKind.RandomHandCard, self, hand);
        var minion = hand[index];
        hand[index] = minion with
        {
            Attack = (int)Math.Min((long)minion.Attack + attack, int.MaxValue),
            Health = (int)Math.Min((long)minion.Health + health, int.MaxValue),
            MaxHealth = (int)Math.Min((long)(minion.MaxHealth > 0 ? minion.MaxHealth : minion.Health) + health, int.MaxValue),
        };
        if (fight.Tracing) fight.Trace($"{minion.CardId} in hand gets +{attack}/+{health}");
    }
}

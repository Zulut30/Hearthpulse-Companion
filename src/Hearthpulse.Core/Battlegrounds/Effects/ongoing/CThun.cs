namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// C'Thun (BGFYM_000), the Deity: "After this awakens, give this minion's stats split amongst your other minions."
/// Golden: "give double this minion's stats". When C'Thun is summoned (it awakens), its Attack and its Health (golden:
/// doubled) are each split evenly among the other living friendly minions: every one gets the whole part, and the
/// remainder goes +1 at a time to the left-most ones. C'Thun keeps its own stats. With no other minion nothing happens.
/// See <see cref="DeityEffect"/> for how it awakens.
/// </summary>
/// <remarks>
/// Real log 15.11.2026 (build 255790): C'Thun 726/793 with three other minions gave each +242/+264 and the remaining
/// +1 Health to the left-most one. One fight only, so a random owner of the remainder is not ruled out; it changes at
/// most one point per minion.
/// </remarks>
[CardEffect(Tokens.CThunId)]
internal sealed class CThun : DeityEffect
{
    protected override CardMechanic Mechanics => CardMechanic.None;

    public override void OnSummoned(Fight fight, Unit self)
    {
        var others = fight.Alive(self.Side, Tribe.All, self);
        if (others.Count == 0)
        {
            return;
        }

        var factor = self.Pick(1, 2);
        var attack = Math.Max(0, self.Attack) * factor;
        var health = Math.Max(0, self.Health) * factor;
        for (var i = 0; i < others.Count; i++)
        {
            var attackPart = Part(attack, others.Count, i);
            var healthPart = Part(health, others.Count, i);
            if (attackPart != 0 || healthPart != 0)
            {
                fight.Buff(others[i], attackPart, healthPart);
            }
        }
    }

    /// <summary>The share of minion <paramref name="index"/> (left to right) when <paramref name="total"/> is split among <paramref name="count"/>.</summary>
    private static int Part(int total, int count, int index) => total / count + (index < total % count ? 1 : 0);
}

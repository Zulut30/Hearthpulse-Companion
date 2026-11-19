namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Death's Embrace, a Dark Gift (BG36_MidGameEffect_000t29 / 000t29t) acting through the enchantment it leaves on the
/// minion: "Has +2/+2 (…t29: …t29e) or +1/+1 (…t29t: …t29te) for each Deathrattle you've triggered this game."
/// </summary>
/// <remarks>
/// The captured stats already hold every trigger before the fight. In the fight the gifted minion gains the bonus once
/// per friendly deathrattle trigger, multiplied triggers (Titus Rivendare, Turbulent Tombs) one by one, as
/// <see cref="Side.DeathrattlesTriggered"/> counts them; only deathrattles with a registered effect are seen. A dead
/// gifted minion gains nothing, and a reborn copy comes back without the gift (Reborn drops enchantments).
/// Assumptions, not checked against a log (no game of the owner has the gift): each multiplied trigger counts as one
/// "Deathrattle you've triggered", and a mortally wounded minion is not saved by the bonus (as Falling Sky Golem).
/// </remarks>
internal abstract class DeathsEmbrace : CombatEffect
{
    /// <summary>Attack and Health per deathrattle trigger.</summary>
    protected abstract int PerTrigger { get; }

    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        if (self.IsAlive)
        {
            fight.Buff(self, PerTrigger, PerTrigger);
        }
    }
}

/// <summary>Death's Embrace (enchantment BG36_MidGameEffect_000t29e): "+2/+2 for each Deathrattle you've triggered this game."</summary>
[CardEffect("BG36_MidGameEffect_000t29e")]
internal sealed class DeathsEmbraceGreater : DeathsEmbrace
{
    protected override int PerTrigger => 2;
}

/// <summary>Death's Embrace (enchantment BG36_MidGameEffect_000t29te): "+1/+1 for each Deathrattle you've triggered this game."</summary>
[CardEffect("BG36_MidGameEffect_000t29te")]
internal sealed class DeathsEmbraceLesser : DeathsEmbrace
{
    protected override int PerTrigger => 1;
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// Timewarped Hag (BG34_Giant_342): "Start of Combat: Give the Undead to the right Reborn and 'This is Reborn with full
/// Health and enchantments'." Golden: "Give adjacent Undead …".
/// </summary>
/// <remarks>
/// The Start of Combat resolved before the boards were captured: the neighbour already carries Reborn and the granted
/// text, as an enchantment whose card id is not in HearthstoneJSON and was never seen in a log. So the Hag hands the
/// granted behaviour over itself at the start of the simulated fight: the Undead to its right (golden: on both sides)
/// that has Reborn gets <see cref="RebornWithFullHealth"/>. Assumptions: the neighbours at capture are the ones the Start
/// of Combat chose (nothing moved them in between), and a Hag that died during Start of Combat leaves the neighbour with
/// a plain Reborn here.
/// </remarks>
[CardEffect("BG34_Giant_342")]
internal sealed class TimewarpedHag : CombatEffect
{
    private static readonly RebornWithFullHealth Granted = new();

    public override void OnFightStart(Fight fight, Unit self)
    {
        var side = self.Side;
        var position = self.Position;
        if (position < 0)
        {
            return;
        }

        Grant(side, position + 1);
        if (self.Golden)
        {
            Grant(side, position - 1);
        }
    }

    private static void Grant(Side side, int index)
    {
        if (index >= 0 && index < side.Count && side.Minions[index] is { IsAlive: true, Reborn: true } unit && unit.Is(Tribe.Undead))
        {
            unit.AddEffect(Granted);
        }
    }
}

/// <summary>
/// "This is Reborn with full Health and enchantments": instead of a 1-Health copy with printed Attack, the minion comes
/// back with the Attack and maximum Health it had when it died, the keywords it had when the fight started (minus Reborn)
/// and its captured enchantments (so their effects come back too). Summon buffs and "after you summon" effects then apply
/// on top, as to any summoned minion. Not registered for a card: the Timewarped Hag gives it.
/// </summary>
internal sealed class RebornWithFullHealth : CombatEffect
{
    public override SimMinion RebornAs(Fight fight, Unit dead, SimMinion copy) =>
        SimMinion.Create(copy.CardId, Math.Max(0, dead.Attack), Math.Max(dead.MaxHealth, 1), copy.Tribes,
            (copy.BaseKeywords | dead.Spec.Keywords) & ~Keyword.Reborn, copy.Golden, copy.Mechanics | dead.Spec.Mechanics) with
        {
            RebornAttack = copy.RebornAttack,
            BaseHealth = copy.BaseHealth,
            BaseKeywords = copy.BaseKeywords,
            TechLevel = copy.TechLevel,
            Enchantments = dead.Spec.Enchantments,
        };
}

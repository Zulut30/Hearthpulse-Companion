using Hearthpulse.Core.Battlegrounds.Effects.Attack;

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// Timewarped Busker (BG34_Giant_001): "Battlecry and Deathrattle: Gain 1 Gold next turn." Golden: 2 Gold. The Gold is
/// for the Tavern; the deathrattle still triggers in combat, so it counts for "Deathrattles you've triggered" and for
/// "after a friendly Deathrattle" effects, and repeats with Titus Rivendare.
/// </summary>
[CardEffect("BG34_Giant_001")]
internal sealed class TimewarpedBusker : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        // Gold next turn: nothing changes in this fight.
    }
}

/// <summary>
/// Timewarped Caretaker (BG34_Giant_618): "Deathrattle: Summon five 1/1 Skeletons. Any that don't fit give your Undead +1
/// Attack this game (wherever they are)." Golden: ten Skeletons.
/// </summary>
/// <remarks>
/// The Skeletons go to the Caretaker's slot; for each one that finds no room the side's Undead get +1 Attack: the living
/// ones (the Skeletons that fit included) now, and Undead summoned later this fight when they arrive.
/// </remarks>
[CardEffect("BG34_Giant_618")]
internal sealed class TimewarpedCaretaker : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var count = self.Pick(5, 10);
        var missed = count - TimewarpedKit.Summon(fight, self, Tokens.Skeleton(), count);
        if (missed > 0)
        {
            TimewarpedKit.UndeadAttack(fight, self.Side, missed);
        }
    }
}

/// <summary>
/// Timewarped Icky Imp (BG34_Giant_674): "Deathrattle: Summon 2 Imps with this minion's maximum stats." Golden: 4 Imps.
/// </summary>
/// <remarks>
/// The Imps (BG_BRM_006t, 1/1 Demon) take the Icky Imp's Attack and maximum Health at its death and appear in its slot.
/// Assumption: the token is the Battlegrounds Imp; its card id has no effect of its own.
/// </remarks>
[CardEffect("BG34_Giant_674")]
internal sealed class TimewarpedIckyImp : CombatEffect
{
    private const string ImpId = "BG_BRM_006t";

    public override void Deathrattle(Fight fight, Unit self)
    {
        var imp = Tokens.Make(ImpId, Math.Max(0, self.Attack), Math.Max(1, self.MaxHealth), Tribe.Demon);
        TimewarpedKit.Summon(fight, self, imp, self.Pick(2, 4));
    }
}

/// <summary>
/// Timewarped Jazzer (BG34_Giant_306): "Deathrattle: Your Blood Gems give an extra +2 Health this game." Golden: +4.
/// Changes the fight only through Blood Gems played later in it.
/// </summary>
[CardEffect("BG34_Giant_306")]
internal sealed class TimewarpedJazzer : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TimewarpedKit.ImproveBloodGems(self.Side, 0, self.Pick(2, 4));
}

/// <summary>
/// Timewarped Lil' Quilboar (BG34_Giant_608): "Reborn. Deathrattle: This plays 3 Blood Gems on all your Quilboar."
/// Golden: 6 Blood Gems. Reborn is a keyword the engine handles; the deathrattle resolves before the reborn copy
/// arrives, so the copy gets no Gems.
/// </summary>
[CardEffect("BG34_Giant_608")]
internal sealed class TimewarpedLilQuilboar : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var side = self.Side;
        var gems = self.Pick(3, 6);
        for (var i = 0; i < side.Count; i++)
        {
            var unit = side.Minions[i];
            if (unit.IsAlive && unit.Is(Tribe.Quilboar))
            {
                BloodGems.Play(fight, self, unit, gems);
            }
        }
    }
}

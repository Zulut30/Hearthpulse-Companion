namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Timewarped Greenskeeper (BG34_Giant_041): "Rally: Trigger your right-most Battlecry and Deathrattle." Golden
/// (BG34_Giant_041_G): "... twice."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>When it attacks, before damage: first the Battlecry of the right-most living friendly minion that has one, then
/// the Deathrattle of the right-most living friendly minion that has one (looked up after the Battlecry resolved). The
/// golden card triggers each twice. Multipliers apply: Brann for the Battlecry, Titus Rivendare for the
/// Deathrattle.</item>
/// <item>A Battlecry with no combat effect (it gets cards or gold) still counts as triggered (Kalecgos, Dark Paradox);
/// summons of a triggered Deathrattle go to the right of the living minion.</item>
/// <item>No real log with this minion was available (2026-11-19): the order follows the card text.</item>
/// </list>
/// </remarks>
[CardEffect("BG34_Giant_041")]
internal sealed class TimewarpedGreenskeeper : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var times = self.Pick(1, 2);
        if (RightMost(self.Side, battlecry: true) is { } battlecry)
        {
            fight.TriggerBattlecry(battlecry, times);
        }

        if (RightMost(self.Side, battlecry: false) is { } deathrattle)
        {
            fight.TriggerDeathrattles(deathrattle, times);
        }
    }

    private static Unit? RightMost(Side side, bool battlecry)
    {
        for (var i = side.Count - 1; i >= 0; i--)
        {
            var unit = side.Minions[i];
            if (unit.IsAlive && (battlecry ? Fight.HasBattlecry(unit) : unit.HasDeathrattle))
            {
                return unit;
            }
        }

        return null;
    }
}

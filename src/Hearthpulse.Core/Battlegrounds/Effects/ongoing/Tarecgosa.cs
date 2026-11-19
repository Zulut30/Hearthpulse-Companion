namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Tarecgosa (BG21_015): "This permanently keeps Bonus Keywords and stats gained in combat." Golden (BG21_015_G): "…
/// and double stats gained in combat."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The normal card changes nothing inside a fight (what it keeps matters after it).</item>
/// <item>The golden card doubles a gain at once, in the same combat. Real log 15.11.2026 (game 5, fight 8, L1236859–
/// L1237030): Ship Master Eudora's deathrattle gives every friendly minion enchantment BG33_828e2 with 8/8; golden
/// Tarecgosa then triggers and sets that enchantment's script data to 16/16, so it ends 36/34 where the others got
/// +8/+8. Modelled as: after golden Tarecgosa gains stats from a buff (<see cref="Fight.Buff"/>), it gains the same
/// again. The second gain is the same enchantment growing, not a new buff, so it is not doubled again
/// (<see cref="Unit.AvengeCount"/> marks it while it is applied; Tarecgosa has no Avenge).</item>
/// <item>Assumption: auras and "minions summoned later have +X" bonuses are not gains (the hook does not report them);
/// only the Eudora case was in the logs (2026-11-19).</item>
/// </list>
/// </remarks>
[CardEffect("BG21_015")]
internal sealed class Tarecgosa : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (unit != self || !self.Golden || self.AvengeCount != 0)
        {
            return;
        }

        self.AvengeCount = 1;
        try
        {
            fight.Buff(self, attack, health);
        }
        finally
        {
            self.AvengeCount = 0;
        }
    }
}

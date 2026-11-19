namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Fishbait (BG36_205, 0/1): "This can't gain stats. Deathrattle: Give the minion that killed this +5/+5." Golden
/// (BG36_205_G, 0/2): "+10/+10".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>"The minion that killed this" is <see cref="Unit.Killer"/>: the enemy whose attack, damage or destroy effect
/// killed it. Nothing happens when that minion has left the board (it died in the same death phase) or when no minion
/// killed it. Each deathrattle trigger gives the buff again (Titus Rivendare: twice).</item>
/// <item>"Can't gain stats": every gain is undone (buffs, auras, summon buffs), so its stats never rise above what was
/// captured (a captured Fishbait cannot have gained either); losses still apply. Auras are kept out of its aura
/// bookkeeping, so an aura that leaves the board cannot take away stats it never gave.</item>
/// <item>No real log with a Fishbait in combat was available (2026-11-19): the buff follows the HearthstoneJSON latest
/// text (+5/+5, golden +10/+10); no captured tag is known to carry the number. The only trace in the logs is the buff it
/// left on a killer (enchantment BG36_205e, TAG_SCRIPT_DATA_NUM_1/2 = 15/15, a multiple of 5).</item>
/// </list>
/// </remarks>
[CardEffect("BG36_205")]
internal sealed class Fishbait : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        if (self.Killer is { IsOnBoard: true, IsExtra: false } killer)
        {
            var bonus = self.Pick(5, 10);
            fight.Buff(killer, bonus, bonus);
        }
    }

    public override void Refresh(Fight fight, Unit self)
    {
        self.AppliedAura = default;
        var attackCap = self.Spec.Attack;
        var healthCap = Math.Max(self.Spec.MaxHealth, self.Spec.Health);
        if (self.Attack > attackCap)
        {
            self.Attack = attackCap;
        }

        if (self.MaxHealth > healthCap)
        {
            // A gain raised Health and maximum Health together: take back the same amount from both.
            self.Health -= self.MaxHealth - healthCap;
            self.MaxHealth = healthCap;
        }
    }
}

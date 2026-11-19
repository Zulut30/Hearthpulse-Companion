namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Drifting Sacrifice (BG36_113): "Reborn. Deathrattle: Give your Deity +2/+1." Golden: "+4/+2". Reborn is a keyword the
/// engine handles, so the deathrattle runs on each of its two lives. See <see cref="Deity.Give"/>. The amount is the
/// captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026: 2/1), else the printed value (also for the reborn copy).
/// </summary>
[CardEffect("BG36_113")]
internal sealed class DriftingSacrifice : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = Deaths.DeathKit.Shown(self, (2, 1), (4, 2));
        Deity.Give(fight, self.Side, attack, health);
    }
}

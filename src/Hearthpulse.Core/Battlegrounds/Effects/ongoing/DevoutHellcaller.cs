namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Devout Hellcaller (BG33_155): "After another friendly Demon deals damage, gain +2/+2 permanently." Golden: "+4/+4".
/// Counts combat and effect damage that got through (a hit on Divine Shield deals no damage), once per damage event.
/// A mortally wounded Hellcaller does not grow. The amount is the captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026: 2/2),
/// else the printed value.
/// </summary>
[CardEffect("BG33_155")]
internal sealed class DevoutHellcaller : CombatEffect
{
    public override void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount)
    {
        if (dealer == self || amount <= 0 || !dealer.Is(Tribe.Demon) || !self.IsAlive)
        {
            return;
        }

        var (attack, health) = Deaths.DeathKit.Shown(self, (2, 2), (4, 4));
        fight.Buff(self, attack, health);
    }
}

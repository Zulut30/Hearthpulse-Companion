namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Mishmash (TB_BaconShop_HERO_33_Buddy, 4/4): "Whenever your Amalgam gains stats, this gains them too." Golden
/// (TB_BaconShop_HERO_33_Buddy_G, 8/8): "this gains them twice."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>"Your Amalgam" is a friendly Amalgam token (<see cref="Tokens.AmalgamId"/>, normal or golden) on the board. A
/// buff it gets in combat (<see cref="Fight.Buff"/>: Blood Gems, Rallies, deathrattle buffs...) is copied to a living
/// Mishmash: the gained part only (a debuff is not), once per buff, twice for a golden Mishmash.</item>
/// <item>Assumption: stats an Amalgam gets from an aura or from a "minions summoned later have +X" buff are not
/// "gained" and are not copied. No real log with this minion was available (2026-11-19).</item>
/// </list>
/// </remarks>
[CardEffect("TB_BaconShop_HERO_33_Buddy")]
internal sealed class Mishmash : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (unit == self || unit.CardId != Tokens.AmalgamId || !self.IsAlive)
        {
            return;
        }

        for (var n = self.Pick(1, 2); n > 0; n--)
        {
            fight.Buff(self, attack, health);
        }
    }
}

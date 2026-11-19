namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Ship Master Eudora (BG33_828): "Deathrattle: Give your minions +{0}/+{1}. Golden ones keep it permanently." (+8/+8.)
/// Golden: "Give your minions +{0}/+{1} twice." Two separate buffs of +8/+8, not one of +16/+16. Whether the buff is
/// kept after combat does not matter inside the fight.
/// </summary>
/// <remarks>
/// The amount is the captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026: 8/8, each minion +8/+8). The printed +8/+8 is
/// the fallback for a copy without script data (a Ghastcoiler summon); it was +6/+6 in patch 37.0.0.255130 and changed on
/// the server without a new client build.
/// </remarks>
[CardEffect("BG33_828")]
internal sealed class ShipMasterEudora : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = DeathKit.Shown(self, (8, 8), (8, 8));
        for (var i = self.Pick(1, 2); i > 0; i--)
        {
            DeathKit.BuffAll(fight, self.Side, Tribe.All, attack, health);
        }
    }
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Cage Gnawer (BG36_211): "Whenever a friendly Beast attacks, give your Beasts +2/+1." Golden: "+4/+2."
/// "A friendly Beast" includes this minion (it is a Beast); "your Beasts" are the living friendly Beasts, itself included.
/// The amount is the captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026: 2/1), else the printed value.
/// </summary>
[CardEffect("BG36_211")]
internal sealed class CageGnawer : CombatEffect
{
    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (!attacker.Is(Tribe.Beast))
        {
            return;
        }

        var (attack, health) = Deaths.DeathKit.Shown(self, (2, 1), (4, 2));
        foreach (var beast in fight.Alive(self.Side, Tribe.Beast))
        {
            fight.Buff(beast, attack, health);
        }
    }
}

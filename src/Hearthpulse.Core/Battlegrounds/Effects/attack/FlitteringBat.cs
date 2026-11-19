namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Flittering Bat (BG36_200): "Rally: Summon a 1/1 Beast." Golden: "Rally: Summon two 1/1 Beasts."
/// The Beast is the Foraging Bat token (BG36_200t, 1/1); the golden card summons two normal 1/1s, not golden ones.
/// They appear to the right of the bat, before damage. Their stats are the captured TAG_SCRIPT_DATA_NUM_1/2 (real log 15.11.2026:
/// 1/1), else the printed 1/1.
/// </summary>
[CardEffect("BG36_200")]
internal sealed class FlitteringBat : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var (attack, health) = Deaths.DeathKit.Shown(self, (1, 1), (1, 1));
        fight.SummonAt(self, Tokens.Make(Tokens.ForagingBatId, attack, Math.Max(1, health), Tribe.Beast), self.Pick(1, 2));
    }
}

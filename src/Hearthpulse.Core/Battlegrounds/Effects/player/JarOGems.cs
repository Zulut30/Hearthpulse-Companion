using Hearthpulse.Core.Battlegrounds.Effects.Attack;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Jar o' Gems (BG30_MagicItem_546): "After a friendly Quilboar attacks each combat, this plays a Blood Gem on all your
/// Quilboar."
/// <para>
/// Read as once per combat ("each combat"): after the first attack of a friendly Quilboar in the fight, one Blood Gem of
/// the owner's value (<see cref="BloodGems"/>) on every living friendly Quilboar, the attacker included. A Quilboar that
/// the attack left mortally wounded is not living and gets none. Assumption, no log has the trinket.
/// </para>
/// </summary>
[CardEffect("BG30_MagicItem_546")]
internal sealed class JarOGems : CombatEffect
{
    public override void AfterFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target, AttackOutcome outcome)
    {
        if (self.Counter != 0 || !attacker.Is(Tribe.Quilboar))
        {
            return;
        }

        self.Counter = 1;
        var board = self.Side.Minions;
        for (var i = 0; i < board.Count; i++)
        {
            if (board[i] is { IsAlive: true } unit && unit.Is(Tribe.Quilboar))
            {
                BloodGems.Play(fight, self, unit, 1);
            }
        }
    }
}

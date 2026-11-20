using Hearthpulse.Core.Battlegrounds.Effects.Player;

namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

// Minions out of today's pool that a rotation trinket portrait gives (package E6b: Kaboom Bot Portrait, Impulsive Portrait),
// from their English card text (HearthstoneJSON latest, checked 2026-11-19). Without the minion's own deathrattle the
// portrait would change nothing.

/// <summary>
/// Kaboom Bot (BG_BOT_606): "Deathrattle: Deal 4 damage to a random enemy minion." Golden: "…, twice." Each hit picks a
/// living enemy anew. Kaboom Bot Portrait (BG30_MagicItem_803) on its side adds 10 damage to each hit
/// (<see cref="KaboomBotPortrait.ExtraDamage"/>).
/// </summary>
[CardEffect("BG_BOT_606")]
internal sealed class KaboomBot : CombatEffect
{
    public const int Damage = 4;

    public override void Deathrattle(Fight fight, Unit self)
    {
        var damage = Damage + KaboomBotPortrait.ExtraDamage(self.Side.Info);
        for (var i = self.Pick(1, 2); i > 0; i--)
        {
            if (fight.RandomEnemy(self) is { } target)
            {
                fight.DealDamage(self, target, damage);
            }
        }
    }
}

/// <summary>
/// Impulsive Trickster (BG21_006): "Deathrattle: Give this minion's maximum Health to another friendly minion." Golden:
/// "…, twice." Without Impulsive Portrait the target is a random other living friendly minion (anew each time). With the
/// portrait (BG32_MagicItem_820, "Your Impulsive Tricksters' Deathrattles target adjacent minions") each trigger gives the
/// Health to the living minions next to where it stood: the first living one on each side of its slot. Assumption: both
/// neighbours, as "minions" is plural, and only Health (as the text says).
/// </summary>
[CardEffect("BG21_006")]
internal sealed class ImpulsiveTrickster : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var health = Math.Max(self.MaxHealth, 0);
        var adjacent = self.Side.Info.Extras.Contains(ImpulsivePortrait.Id);
        for (var i = self.Pick(1, 2); i > 0; i--)
        {
            if (!adjacent)
            {
                if (fight.RandomFriendly(self) is { } target)
                {
                    fight.Buff(target, 0, health);
                }

                continue;
            }

            var (left, right) = Adjacent(self);
            if (left is not null)
            {
                fight.Buff(left, 0, health);
            }

            if (right is not null)
            {
                fight.Buff(right, 0, health);
            }
        }
    }

    /// <summary>The living friendly minions next to <paramref name="self"/>'s slot (on the board or where it died).</summary>
    private static (Unit? Left, Unit? Right) Adjacent(Unit self)
    {
        var board = self.Side.Minions;
        int leftFrom;
        int rightFrom;
        if (self.IsOnBoard)
        {
            leftFrom = self.Position - 1;
            rightFrom = self.Position + 1;
        }
        else
        {
            // Dead: the first living minion right of it when it died stands in its slot now.
            var slot = self.DeathAnchor is { IsOnBoard: true } anchor && anchor.Side == self.Side
                ? anchor.Position
                : Math.Clamp(self.DeathIndex < 0 ? board.Count : self.DeathIndex, 0, board.Count);
            leftFrom = slot - 1;
            rightFrom = slot;
        }

        Unit? left = null;
        for (var i = leftFrom; i >= 0; i--)
        {
            if (board[i].IsAlive)
            {
                left = board[i];
                break;
            }
        }

        Unit? right = null;
        for (var i = rightFrom; i < board.Count; i++)
        {
            if (board[i].IsAlive)
            {
                right = board[i];
                break;
            }
        }

        return (left, right);
    }
}

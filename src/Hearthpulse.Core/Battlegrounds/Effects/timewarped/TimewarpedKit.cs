using Hearthpulse.Core.Battlegrounds.Effects.Attack;

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

// Timewarped minions (BG34_Giant_*, BG34_PreMadeChamp_*): the Timewarp Tavern's cards, in today's games through the
// anomalies that open it (Oathstone's Summoning, Unforeseen Portal) and the Timewarped Tavern. One class per card, from
// its English text and golden text (HearthstoneJSON enUS, build 256589); per-trigger code paths do not allocate.

/// <summary>Small helpers shared by the Timewarped effects.</summary>
internal static class TimewarpedKit
{
    private static readonly Func<Unit, bool> IsUndead = unit => unit.Is(Tribe.Undead);

    /// <summary>Buffs every living minion of <paramref name="side"/> of <paramref name="tribe"/> (<see cref="Tribe.All"/>: every one), left to right.</summary>
    public static void BuffAll(Fight fight, Side side, Tribe tribe, int attack, int health)
    {
        for (var i = 0; i < side.Count; i++)
        {
            var unit = side.Minions[i];
            if (unit.IsAlive && unit.Is(tribe))
            {
                fight.Buff(unit, attack, health);
            }
        }
    }

    /// <summary>
    /// "Your Undead have +N Attack this game (wherever they are)": living friendly Undead now, and Undead summoned later
    /// this fight when they arrive (as Plaguerunner).
    /// </summary>
    public static void UndeadAttack(Fight fight, Side side, int attack)
    {
        BuffAll(fight, side, Tribe.Undead, attack, 0);
        side.AddSummonBuff(IsUndead, attack, 0);
    }

    /// <summary>
    /// "Your Blood Gems give an extra +A/+H this game": raises the player's Blood Gem value that gems played later this
    /// fight read (<see cref="BloodGems.Value"/>).
    /// </summary>
    public static void ImproveBloodGems(Side side, int attack, int health)
    {
        if (attack != 0)
        {
            side.AddCounter(BloodGems.AttackTag, attack);
        }

        if (health != 0)
        {
            side.AddCounter(BloodGems.HealthTag, health);
        }
    }

    /// <summary>
    /// Summons <paramref name="count"/> copies of <paramref name="minion"/> where <paramref name="place"/> is (its old slot when
    /// it has died, else to its right), left to right, without allocating. Returns how many were summoned.
    /// </summary>
    public static int Summon(Fight fight, Unit place, SimMinion minion, int count)
    {
        var anchor = place;
        for (var i = 0; i < count; i++)
        {
            if (fight.SummonAt(place.IsOnBoard ? anchor : place, minion) is not { } summoned)
            {
                return i;
            }

            anchor = summoned;
        }

        return count;
    }
}

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Jim Raynor's Battlecruiser upgrades whose number lives on the enchantment the upgrade spell leaves on the Battlecruiser
// (TASK-2026-11-19, package E7b): the enchantment's text shows "+0" (HearthstoneJSON enUS, build 256589), the spell that
// gives it +2..+10 Attack (Advanced Ballistics, BG31_HERO_801ptd..ptd7) or +1/+1..+25/+25 (Caduceus Reactor,
// BG31_HERO_801pte..pte7). The capture keeps each enchantment's numbers (SimMinion.EnchantmentValues, capture format 2):
// TAG_SCRIPT_DATA_NUM_1/2, else its ATK/HEALTH tags, are read. No game of the owner has Jim Raynor, so which tag holds the
// number is the pattern of other enchantments (Beetle Army, Diremuck Forager), not checked on these. Without a number (a
// capture made before the values were kept, or a tag never sent) the lowest rank's number is used.

/// <summary>The number one Battlecruiser upgrade enchantment carries.</summary>
internal static class UpgradeNumber
{
    private sealed class Calls
    {
        public int Next;
    }

    /// <summary>
    /// The numbers of the enchantment <paramref name="enchantmentId"/> on <paramref name="self"/>, one instance per call: a
    /// Battlecruiser with the upgrade twice has the effect twice (EffectRegistry adds one per enchantment), and the calls
    /// of one trigger go through its instances in order. <paramref name="fallback"/> when the instance has no number.
    /// </summary>
    public static (int First, int Second) Next(Unit self, string enchantmentId, (int, int) fallback)
    {
        var values = self.Spec.EnchantmentValues;
        var count = 0;
        foreach (var value in values)
        {
            count += value.CardId == enchantmentId ? 1 : 0;
        }

        if (count == 0)
        {
            return fallback;
        }

        var calls = UnitState<Calls>.Of(self);
        var wanted = calls.Next++ % count;
        foreach (var value in values)
        {
            if (value.CardId != enchantmentId || wanted-- > 0)
            {
                continue;
            }

            var (first, second) = value.ScriptData1 > 0 || value.ScriptData2 > 0 ? (value.ScriptData1, value.ScriptData2) : (value.Attack, value.Health);
            return first > 0 || second > 0 ? (first, second) : fallback;
        }

        return fallback;
    }
}

/// <summary>
/// Advanced Ballistics (BG31_HERO_801ptde, on the Battlecruiser): "Rally: Give your other minions +X Attack." X: the
/// enchantment's number (<see cref="UpgradeNumber"/>), else 2 (the first rank).
/// </summary>
[CardEffect("BG31_HERO_801ptde")]
internal sealed class AdvancedBallistics : CombatEffect
{
    public const string Id = "BG31_HERO_801ptde";


    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var (attack, _) = UpgradeNumber.Next(self, Id, (2, 0));
        foreach (var unit in fight.Alive(self.Side, except: self))
        {
            fight.Buff(unit, attack, 0);
        }
    }
}

/// <summary>
/// Caduceus Reactor (BG31_HERO_801ptee, on the Battlecruiser): "Deathrattle: Give your left-most minion +X/+X." X: the
/// enchantment's numbers (<see cref="UpgradeNumber"/>; one number for both when only one is set), else +1/+1 (the first rank).
/// </summary>
[CardEffect("BG31_HERO_801ptee")]
internal sealed class CaduceusReactor : CombatEffect
{
    public const string Id = "BG31_HERO_801ptee";


    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = UpgradeNumber.Next(self, Id, (1, 1));
        if (health <= 0)
        {
            health = attack;
        }

        if (fight.Alive(self.Side) is [var left, ..])
        {
            fight.Buff(left, attack, health);
        }
    }
}

using Hearthpulse.Core.Battlegrounds.Effects.Attack;

namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Sanguine Champion (BG23_017): "Battlecry and Deathrattle: Your Blood Gems give an extra +2/+1 this game." Golden
/// (BG23_017_G): "an extra +4/+2".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Raises the owner's Blood Gem bonus (<see cref="BloodGems.AttackTag"/> / <see cref="BloodGems.HealthTag"/>) for the
/// rest of the fight, so Blood Gems played later in it (Bonker, Tusked Camper, Razorfen Vineweaver...) give more. A real
/// log (2026-11-15) shows it: the player tags of the side that lost a Champion in combat went from +9/+4 to +11/+5 right
/// after its deathrattle.</item>
/// <item>The Battlecry runs only when an effect triggers it in combat (<see cref="Fight.TriggerBattlecry"/>); the one
/// played in the Tavern is already in the captured tags.</item>
/// <item>The numbers come from the minion's captured TAG_SCRIPT_DATA_NUM_1/2 (the same log: 2 and 1 on a normal
/// Champion), so a server-side balance change is followed; the card text (+2/+1, golden +4/+2) is the fallback for a
/// minion summoned in combat.</item>
/// </list>
/// </remarks>
[CardEffect("BG23_017")]
internal sealed class SanguineChampion : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => Improve(fight, self);

    public override void Battlecry(Fight fight, Unit self) => Improve(fight, self);

    private static void Improve(Fight fight, Unit self) =>
        BloodGemBonus.Add(fight, self.Side, BloodGemBonus.Captured(self.Spec.ScriptData1, self.Pick(2, 4)),
                          BloodGemBonus.Captured(self.Spec.ScriptData2, self.Pick(1, 2)));
}

/// <summary>"Your Blood Gems give an extra +a/+h this game": the owner's Blood Gem bonus for the rest of the fight.</summary>
internal static class BloodGemBonus
{
    public static void Add(Fight fight, Side side, int attack, int health)
    {
        var a = side.AddCounter(BloodGems.AttackTag, attack);
        var h = side.AddCounter(BloodGems.HealthTag, health);
        if (fight.Tracing) fight.Trace($"{(side.IsOpponent ? "their" : "our")} Blood Gems now +{a}/+{h} extra");
    }

    /// <summary>A number the card shows in its captured script data, else the card text's number.</summary>
    public static int Captured(int scriptData, int fallback) => scriptData > 0 ? scriptData : fallback;
}

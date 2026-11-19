namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// The Last One Standing (BG34_320): "Rally: Give a friendly minion of each type +{0}/+{1} permanently and improve this."
/// Golden: "... +{0}/+{1} permanently and improve this, twice." ("Permanently" only matters after combat.) Printed +10/+10.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The current bonus is the captured TAG_SCRIPT_DATA_NUM_1/2 (what the text shows, earlier improvements included),
/// kept in <see cref="Unit.Counter"/>/<see cref="Unit.Counter2"/>; without it (a copy made in combat: Y'Shaarj, Reborn)
/// the printed +10/+10. Each run buffs the chosen minions by the current bonus and then improves it by +10/+10
/// (real log 15.11.2026, build 255790: script data 10 → 20 → 30, one Rally each; a fresh Y'Shaarj copy starts at 10).
/// Golden: two runs in a row, each with fresh picks and its own improvement (the +10 step of the golden card is from its
/// text; no golden one was in the logs).</item>
/// <item>Assumption (the exact selection rule is not on the card): for each minion type in a fixed order (Beast, Demon,
/// Dragon, Elemental, Mech, Murloc, Naga, Pirate, Quilboar, Undead, Aberration) one random living friendly minion of that
/// type is chosen, itself included; a minion is chosen at most once per trigger, and minions with fewer types are
/// preferred so that a single-type minion covers its type before a multi-type one (Amalgam-style, or this minion,
/// which has every type). The real log agrees: with this minion, five Mechs and an Aberration the buff went to this
/// minion once, one Mech and the Aberration.</item>
/// </list>
/// </remarks>
[CardEffect("BG34_320")]
internal sealed class TheLastOneStanding : CombatEffect
{
    private static readonly Tribe[] Types =
    [
        Tribe.Beast, Tribe.Demon, Tribe.Dragon, Tribe.Elemental, Tribe.Mech, Tribe.Murloc,
        Tribe.Naga, Tribe.Pirate, Tribe.Quilboar, Tribe.Undead, Tribe.Aberration,
    ];

    // Printed bonus, and the step of each improvement (normal and golden).
    private const int Printed = 10;

    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        for (var time = 0; time < self.Pick(1, 2); time++)
        {
            var attack = self.Counter > 0 ? self.Counter : Printed;
            var health = self.Counter2 > 0 ? self.Counter2 : Printed;
            foreach (var unit in Choose(fight, self))
            {
                fight.Buff(unit, attack, health);
            }

            self.Counter = attack + Printed;
            self.Counter2 = health + Printed;
        }
    }

    private static List<Unit> Choose(Fight fight, Unit self)
    {
        var chosen = new List<Unit>();
        foreach (var type in Types)
        {
            var candidates = fight.Alive(self.Side, type).Where(u => !chosen.Contains(u)).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var fewest = candidates.Min(TypeCount);
            if (fight.RandomOf(candidates.Where(u => TypeCount(u) == fewest).ToList(), self) is { } pick)
            {
                chosen.Add(pick);
            }
        }

        return chosen;
    }

    private static int TypeCount(Unit unit) => System.Numerics.BitOperations.PopCount((uint)(unit.Tribes & Tribe.All));
}

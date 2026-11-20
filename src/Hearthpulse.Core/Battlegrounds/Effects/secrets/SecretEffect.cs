namespace Hearthpulse.Core.Battlegrounds.Effects.Secrets;

/// <summary>
/// A Battlegrounds Secret in a player's SECRET zone (The Great Akazamzarak's Prestidigitation, the Eleventh Hour anomaly):
/// a player-level effect holder made from <see cref="SimSide.Secrets"/> (Simulation/Fight.cs), which triggers once and is
/// then gone, like every Secret.
/// </summary>
/// <remarks>
/// Rules taken from the cards' text and the general Secret rules, not from a log: the owner's kept logs (2026-11-15...19)
/// show only Ice Block triggering (rules-evidence 8b). Assumptions, recorded here once for every Secret:
/// <list type="bullet">
/// <item>In combat a Secret triggers for its owner whichever side attacks (combat is no one's turn of play).</item>
/// <item>Several Secrets on the same event trigger in the order they were put into play (the capture lists them by entity id),
/// each once.</item>
/// <item>A Secret that summons does not trigger on a full board and stays (the Constructed rule for summoning Secrets); one
/// that needs a target (Avenge) does not trigger without one.</item>
/// <item>"When one of your minions is attacked" runs with the "whenever a friendly minion is attacked" hooks, before damage;
/// the attack is not redirected to what the Secret summons. Summons without a place in the text go to the right end, copies
/// to the right of the original.</item>
/// <item>"When a friendly minion dies" runs with the "after a friendly minion dies" hooks: after its deathrattles, before its
/// Reborn.</item>
/// </list>
/// </remarks>
internal abstract class SecretEffect : CombatEffect
{
    /// <summary>The Secret has not triggered yet this fight (<see cref="Unit.Counter"/> of its holder: 1 = used).</summary>
    protected static bool Ready(Unit self) => self.Counter == 0;

    /// <summary>Marks the Secret as triggered (it leaves play) and traces it.</summary>
    protected static void Use(Fight fight, Unit self, string name)
    {
        self.Counter = 1;
        if (fight.Tracing) fight.Trace($"{(self.Side.IsOpponent ? "their" : "our")} Secret {name} triggers");
    }

    /// <summary>
    /// A copy of <paramref name="unit"/> as it is now (stats, damage, keywords, enchantments) made by <paramref name="creator"/>:
    /// without the auras and summon bonuses of its side, which the copy gets again when it is summoned.
    /// </summary>
    internal static SimMinion CopyNow(Unit unit, string creator)
    {
        var (summonAttack, summonHealth) = unit.Side.SummonBuffsFor(unit);
        var attack = Math.Max(0, unit.Attack - unit.AppliedAura.Attack - summonAttack);
        var maxHealth = Math.Max(1, unit.MaxHealth - unit.AppliedAura.Health - summonHealth);
        var health = Math.Clamp(unit.Health - unit.AppliedAura.Health - summonHealth, 1, maxHealth);
        return unit.ToSimMinion() with
        {
            EntityId = 0,
            Attack = attack,
            Health = health,
            MaxHealth = maxHealth,
            CreatorCardId = creator,
            SummonedInCombat = false,
            HasUnsimulatedEffect = false,
        };
    }

    /// <summary>
    /// A fresh copy of the card <paramref name="dead"/> was ("return it to life"): printed Attack, Health and keywords
    /// (Reborn included), golden if it was golden, without buffs or enchantments, at <paramref name="health"/> (0 = full).
    /// </summary>
    internal static SimMinion FreshCopy(Unit dead, string creator, int health = 0)
    {
        var spec = dead.Spec;
        var copy = spec.RebornCopy();
        var max = Math.Max(1, spec.BaseHealth);
        return copy with
        {
            Reborn = (spec.BaseKeywords & Keyword.Reborn) != 0,
            Health = health > 0 ? Math.Min(health, max) : max,
            MaxHealth = max,
            CreatorCardId = creator,
        };
    }
}

using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Minions the hero powers of today's heroes make, which the relevance audit (2026-11-19) found missing from the relevance
// tables and the coverage matrix: Kerrigan's Zerg (Larva morphs, BG31_HERO_811t*), the Protoss of Warp Gate
// (BG31_HERO_802pt*) and Jim Raynor's Battlecruiser upgrades (BG31_HERO_801pt*e). Numbers from the latest card text
// (HearthstoneJSON enUS, build 256589); a number the card shows from its script data is read from it when present
// (DeathKit.Shown). No game of the owner has one of these heroes (D:\Hearthstone\Logs, 2026-11-19).

/// <summary>
/// Hydralisk (BG31_HERO_811t4): "Windfury. Rally: Gain Attack equal to your Tier permanently." Golden: "twice your Tier".
/// The Tier is the owner's Tavern tier at capture.
/// </summary>
[CardEffect("BG31_HERO_811t4")]
internal sealed class Hydralisk : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) =>
        fight.Buff(self, self.Pick(1, 2) * Math.Max(1, self.Side.Info.TavernTier), 0);
}

/// <summary>
/// Baneling (BG31_HERO_811t5): "Deathrattle: Deal damage equal to this minion's Attack to a random enemy minion." Golden:
/// "…, twice." The Attack it died with.
/// </summary>
[CardEffect("BG31_HERO_811t5")]
internal sealed class Baneling : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        for (var n = self.Pick(1, 2); n > 0; n--)
        {
            if (fight.RandomEnemy(self) is { } enemy)
            {
                fight.DealDamage(self, enemy, self.Attack);
            }
        }
    }
}

/// <summary>Lurker (BG31_HERO_811t7): "Stealth. Avenge (1): Gain +1/+1 permanently." Golden: "+2/+2".</summary>
[CardEffect("BG31_HERO_811t7")]
internal sealed class Lurker : AvengeEffect
{
    protected override int Threshold(Unit self) => 1;

    protected override void Avenge(Fight fight, Unit self)
    {
        var bonus = self.Pick(1, 2);
        fight.Buff(self, bonus, bonus);
    }
}

/// <summary>
/// Mutalisk (BG31_HERO_811t6): "After a friendly minion kills an enemy, gain +3 Attack permanently." Golden: "+6". A kill:
/// the damage that leaves the enemy dying, credited once (as Glory of Combat).
/// </summary>
[CardEffect("BG31_HERO_811t6")]
internal sealed class Mutalisk : CombatEffect
{
    private sealed class Credited
    {
        public bool Done;
    }

    public override void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount)
    {
        if (self.IsAlive && victim.IsDying && victim.Killer == dealer && UnitState<Credited>.Of(victim) is { Done: false } credit)
        {
            credit.Done = true;
            fight.Buff(self, self.Pick(3, 6), 0);
        }
    }
}

/// <summary>
/// Ultralisk (BG31_HERO_811t10): "Also damages adjacent minions. Start of Combat: Double this minion's stats." The Start
/// of Combat resolved before the capture; the cleave is its attack: the target's neighbours take its Attack too.
/// </summary>
[CardEffect("BG31_HERO_811t10")]
internal sealed class Ultralisk : CombatEffect
{
    public override void OnAttackDamage(Fight fight, Unit self, Unit target)
    {
        var (left, right) = fight.Neighbours(target);
        if (left is not null) fight.DealDamage(self, left, self.Attack);
        if (right is not null) fight.DealDamage(self, right, self.Attack);
    }
}

/// <summary>Viper (BG31_HERO_811t8): "Venomous. Immune while attacking." Venomous is a captured keyword.</summary>
[CardEffect("BG31_HERO_811t8")]
internal sealed class Viper : CombatEffect
{
    public override bool ImmuneWhileAttacking => true;
}

/// <summary>
/// Carrier (BG31_HERO_802pt1): "Avenge (4): Summon a 7/7 Interceptor. Then improve this permanently." Golden: "14/14".
/// The Interceptor's size is the Carrier's shown numbers (script data) when captured, else the text's; how much one
/// trigger improves it is not on the card, so every Interceptor of the fight has that size (assumption).
/// </summary>
[CardEffect("BG31_HERO_802pt1")]
internal sealed class Carrier : AvengeEffect
{
    public const string InterceptorId = "BG31_HERO_802pt1t";

    public override string? CapturedInput => "размер Interceptor (TAG_SCRIPT_DATA_NUM_1/2 Carrier); прирост за срабатывание в бою не известен";

    protected override int Threshold(Unit self) => 4;

    protected override void Avenge(Fight fight, Unit self)
    {
        var (attack, health) = Deaths.DeathKit.Shown(self, (7, 7), (14, 14));
        TimewarpedKit.Summon(fight, self, Tokens.Make(InterceptorId, attack, health, Tribe.Mech), 1);
    }
}

/// <summary>
/// Colossus (BG31_HERO_802pt): "Rally: Deal 2 damage to the target's neighbors. (Improves when you spend Gold!)" Golden:
/// "4". The damage: its shown TAG_SCRIPT_DATA_NUM_1 when captured, else the text's. Before its attack hits.
/// </summary>
[CardEffect("BG31_HERO_802pt")]
internal sealed class Colossus : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var damage = Deaths.DeathKit.Shown1(self, 2, 4);
        var (left, right) = fight.Neighbours(target);
        if (left is not null) fight.DealDamage(self, left, damage);
        if (right is not null) fight.DealDamage(self, right, damage);
    }
}

/// <summary>
/// Void Ray (BG31_HERO_802pt5): "Divine Shield. Whenever a friendly minion attacks, give it and this minion +5 Attack
/// permanently." Golden: "+10". When the Void Ray itself attacks it gets the bonus once (assumption, no log).
/// </summary>
[CardEffect("BG31_HERO_802pt5")]
internal sealed class VoidRay : CombatEffect
{
    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (!self.IsAlive)
        {
            return;
        }

        var bonus = self.Pick(5, 10);
        fight.Buff(attacker, bonus, 0);
        if (attacker != self)
        {
            fight.Buff(self, bonus, 0);
        }
    }
}

/// <summary>
/// Ultra-Capacitor (Battlecruiser upgrade enchantment BG31_HERO_801ptje): "Reborn with full health and enchantments." As
/// the Timewarped Hag's grant (<see cref="RebornWithFullHealth"/>).
/// </summary>
[CardEffect("BG31_HERO_801ptje")]
internal sealed class UltraCapacitor : CombatEffect
{
    private static readonly RebornWithFullHealth Inner = new();

    public override SimMinion RebornAs(Fight fight, Unit dead, SimMinion copy) => Inner.RebornAs(fight, dead, copy);
}

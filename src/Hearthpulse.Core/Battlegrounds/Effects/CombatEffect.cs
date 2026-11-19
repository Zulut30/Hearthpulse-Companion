using System.Collections.Concurrent;
using System.Reflection;

namespace Hearthpulse.Core.Battlegrounds.Effects;

/// <summary>Which version of a card an effect class implements.</summary>
public enum GoldenFilter
{
    /// <summary>Normal and golden (read <see cref="Unit.Golden"/> to scale the numbers).</summary>
    Both,
    NormalOnly,
    GoldenOnly,
}

/// <summary>
/// Registers a <see cref="CombatEffect"/> class for a card (minion, token, enchantment, hero power, trinket...).
/// Use the normal card id; a golden card id is mapped to its normal id with <see cref="GoldenFilter.GoldenOnly"/>.
/// One class may carry several attributes when cards share a behaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CardEffectAttribute(string cardId) : Attribute
{
    public string CardId { get; } = cardId;

    public GoldenFilter Golden { get; set; } = GoldenFilter.Both;
}

/// <summary>Result of an attack's combat damage on its target, passed to after-attack hooks.</summary>
/// <param name="Damage">Damage that reached the target (0 when Divine Shield absorbed it).</param>
/// <param name="Killed">The target is mortally wounded or destroyed (it is still on the board until the death phase).</param>
/// <param name="Excess">Damage beyond the target's remaining Health ("excess damage"), 0 when it survived.</param>
public readonly record struct AttackOutcome(int Damage, bool Killed, int Excess);

/// <summary>Attack/Health granted by an aura.</summary>
public readonly record struct StatDelta(int Attack, int Health)
{
    public static StatDelta operator +(StatDelta a, StatDelta b) => new(a.Attack + b.Attack, a.Health + b.Health);

    public static StatDelta operator -(StatDelta a, StatDelta b) => new(a.Attack - b.Attack, a.Health - b.Health);

    public bool IsZero => Attack == 0 && Health == 0;
}

/// <summary>
/// Base class for a card's combat behaviour. Override only the hooks the card needs; the engine calls a hook only
/// on effects that override it. Instances are shared by every fight and thread, so an effect must not keep state in
/// fields: keep per-minion state on <see cref="Unit"/> (Counter, Counter2, AvengeCount) and per-player state on
/// <see cref="Side"/> (Counter(key)).
/// <para>
/// Order of one attack: <see cref="OnFriendlyAttack"/> → <see cref="OnAttack"/> (Rally) → <see cref="OnFriendlyAttacked"/> →
/// death phase (the attack is cancelled if the attacker or target died) → simultaneous combat damage and
/// <see cref="OnAttackDamage"/> (cleave) → damage hooks (<see cref="OnDamaged"/>, <see cref="AfterFriendlyDealtDamage"/>,
/// <see cref="AfterKill"/>, <see cref="OnFriendlyShieldLost"/>) → <see cref="AfterAttack"/> → <see cref="AfterFriendlyAttack"/>
/// → death phase. A death phase removes every dead minion (in order of play: the lower PLAYER_ID side's starting
/// minions, the other side's, then minions summoned in combat), then per dead minion runs its deathrattles (each one
/// × multiplier before the next), then <see cref="AfterFriendlyDeath"/> / <see cref="AfterEnemyDeath"/> for each death,
/// then each Reborn, refreshes auras, and repeats while minions keep dying.
/// </para>
/// </summary>
public abstract class CombatEffect
{
    private static readonly ConcurrentDictionary<Type, Hook> HookCache = new();

    protected CombatEffect()
    {
        Hooks = HookCache.GetOrAdd(GetType(), ScanHooks);
    }

    internal Hook Hooks { get; }

    /// <summary>
    /// True when the effect needs its owner's hand (Expert Aviator, Diremuck Forager...). The opponent's hand is hidden,
    /// so such a minion stays "not simulated" on the opponent's side.
    /// </summary>
    public virtual bool NeedsHand => false;

    /// <summary>"Immune while attacking": the engine prevents all damage to the unit while its own attack resolves.</summary>
    public virtual bool ImmuneWhileAttacking => false;

    /// <summary>
    /// What the effect reads from the capture besides the boards (a player-level card's value that grows over the game),
    /// for the coverage matrix; null when it needs nothing.
    /// </summary>
    public virtual string? CapturedInput => null;

    /// <summary>
    /// A value the effect needs that the capture never holds (e.g. Avenge progress made before the boards were
    /// captured): the fight is always "not simulated" with <see cref="UnsimulatedReason.MissingInput"/>. Null = none.
    /// </summary>
    public virtual string? UncapturedInput => null;

    /// <summary>
    /// The tags of the local player's player-level cards the capture keeps (<see cref="SimSide.ExtraTags"/>); nothing
    /// else is read, and nothing of the opponent's. SCORE_VALUE_1 / SCORE_VALUE_2 are Avenge (X) and its progress (real
    /// logs: 3 and 1, 2, 3 on an Avenge (3) minion, the progress back to 0 when Avenge fires).
    /// </summary>
    public static IReadOnlyList<string> CapturedTags { get; } = ["TAG_SCRIPT_DATA_NUM_1", "TAG_SCRIPT_DATA_NUM_2", "SCORE_VALUE_1", "SCORE_VALUE_2"];

    /// <summary>
    /// False when the value this effect needs is not in the capture of <paramref name="side"/>: the fight is then
    /// "not simulated" with the reason <see cref="UnsimulatedReason.MissingInput"/> instead of using a silent 0.
    /// </summary>
    public virtual bool HasInputs(SimSide side, string cardId) => UncapturedInput is null;

    // ---- Fight start -------------------------------------------------------------------------------------------

    /// <summary>
    /// Once per simulated fight, before the first attack. Not a Start of Combat trigger (those already resolved before
    /// capture): use it to set up what lasts "for the rest of this combat" (e.g. a buff for minions summoned later).
    /// </summary>
    public virtual void OnFightStart(Fight fight, Unit self) { }

    // ---- Attacks -----------------------------------------------------------------------------------------------

    /// <summary>This minion attacks <paramref name="target"/>, before damage: Rally and "whenever this attacks".</summary>
    public virtual void OnAttack(Fight fight, Unit self, Unit target) { }

    /// <summary>
    /// A friendly minion attacks, before damage and before the attacker's own <see cref="OnAttack"/> (Rally). Also called
    /// with <paramref name="attacker"/> == self; check it for "another".
    /// </summary>
    public virtual void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target) { }

    /// <summary>A friendly minion (possibly self) is attacked, before damage.</summary>
    public virtual void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker) { }

    /// <summary>During this minion's combat damage, after the main exchange: deal extra simultaneous damage here (cleave).</summary>
    public virtual void OnAttackDamage(Fight fight, Unit self, Unit target) { }

    /// <summary>After this minion attacked, before the death phase (self and target may be mortally wounded).</summary>
    public virtual void AfterAttack(Fight fight, Unit self, Unit target, AttackOutcome outcome) { }

    /// <summary>After a friendly minion (possibly self) attacked, before the death phase.</summary>
    public virtual void AfterFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target, AttackOutcome outcome) { }

    // ---- Damage ------------------------------------------------------------------------------------------------

    /// <summary>Whenever this minion takes damage (after Divine Shield; not called when the shield absorbed it).</summary>
    public virtual void OnDamaged(Fight fight, Unit self, Unit? source, int amount) { }

    /// <summary>After a friendly minion (possibly self) dealt damage to any minion.</summary>
    public virtual void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount) { }

    /// <summary>This minion's damage (attack or effect) or destroy effect killed <paramref name="victim"/>.</summary>
    public virtual void AfterKill(Fight fight, Unit self, Unit victim) { }

    /// <summary>A friendly minion (possibly self) lost Divine Shield.</summary>
    public virtual void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit) { }

    // ---- Deaths ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Deathrattle. <paramref name="self"/> is already off the board; <see cref="Fight.SummonAt(Unit, SimMinion)"/> summons
    /// at its old slot. Runs once per trigger; the engine repeats it for multipliers (<see cref="ExtraDeathrattles"/>).
    /// Also called by <see cref="Fight.TriggerDeathrattles"/> on a living minion (then summons go to its right).
    /// </summary>
    public virtual void Deathrattle(Fight fight, Unit self) { }

    /// <summary>Extra times friendly deathrattles trigger while this minion is alive (Titus Rivendare: 1, golden 2).</summary>
    public virtual int ExtraDeathrattles(Fight fight, Unit self) => 0;

    /// <summary>After one friendly deathrattle trigger resolved (per trigger, multipliers included).</summary>
    public virtual void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source) { }

    /// <summary>After a friendly minion died (its deathrattles have resolved, its Reborn comes after): Avenge, "after a friendly X dies".</summary>
    public virtual void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) { }

    /// <summary>After an enemy minion died.</summary>
    public virtual void AfterEnemyDeath(Fight fight, Unit self, Unit dead) { }

    /// <summary>
    /// Once per side after each round of a death phase, when its deathrattles, Avenge and Reborn have all resolved: a
    /// "when you have space" wait (Diremuck Forager) looks for room here, after the reborn copies took theirs.
    /// </summary>
    public virtual void AfterDeathPhase(Fight fight, Unit self) { }

    // ---- Summons -----------------------------------------------------------------------------------------------

    /// <summary>This minion was just summoned in combat (token, reborn copy, ...), before other minions react.</summary>
    public virtual void OnSummoned(Fight fight, Unit self) { }

    /// <summary>"Whenever you summon": a friendly minion other than self was summoned. Runs before the "after" hooks.</summary>
    public virtual void WheneverFriendlySummoned(Fight fight, Unit self, Unit summoned) { }

    /// <summary>"After you summon": a friendly minion other than self was summoned.</summary>
    public virtual void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned) { }

    /// <summary>After a friendly minion was Reborn (<paramref name="reborn"/> is the new copy; its origin is <see cref="Unit.RebornFrom"/>).</summary>
    public virtual void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn) { }

    /// <summary>
    /// What <paramref name="dead"/> (carrying this effect) comes back as with Reborn, before it is summoned: the default
    /// fresh copy, or another one ("Is Reborn with full stats"). Summon buffs and hooks then apply to what this returns.
    /// </summary>
    public virtual SimMinion RebornAs(Fight fight, Unit dead, SimMinion copy) => copy;

    /// <summary>After the owner cast a spell in combat (see <see cref="Fight.CastSpell"/>).</summary>
    public virtual void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target) { }

    // ---- Ongoing -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Static aura from self onto <paramref name="other"/> (a friendly minion, possibly self). Recomputed whenever the board
    /// changes; the captured stats already include the auras present at capture, and the engine applies only changes.
    /// </summary>
    public virtual StatDelta AuraFor(Fight fight, Unit self, Unit other) => default;

    /// <summary>Re-evaluates state-dependent keywords or thresholds (e.g. "has Taunt while it has Divine Shield").</summary>
    public virtual void Refresh(Fight fight, Unit self) { }

    // ---- Battlecries and spell multipliers ---------------------------------------------------------------------

    /// <summary>
    /// This minion's Battlecry, when an effect triggers it in combat (<see cref="Fight.TriggerBattlecry"/>, e.g. a Rally
    /// that triggers your right-most Battlecry). Runs once per trigger; the engine repeats it for multipliers.
    /// </summary>
    public virtual void Battlecry(Fight fight, Unit self) { }

    /// <summary>Extra times friendly Battlecries trigger while this minion is alive (Brann Bronzebeard: 1, golden 2).</summary>
    public virtual int ExtraBattlecries(Fight fight, Unit self) => 0;

    /// <summary>After one friendly Battlecry trigger in combat (per trigger, multipliers included): "after you trigger a Battlecry".</summary>
    public virtual void AfterFriendlyBattlecry(Fight fight, Unit self, Unit source) { }

    /// <summary>
    /// Extra casts of a spell the owner casts in combat while this minion is alive (Balinda Stonehearth: 1 for a spell
    /// that targets a friendly minion, golden 2). <paramref name="target"/> is the spell's target, null when it has none.
    /// </summary>
    public virtual int ExtraSpellCasts(Fight fight, Unit self, string spellCardId, Unit? target) => 0;

    /// <summary>
    /// After a friendly minion (possibly self) gained stats from <see cref="Fight.Buff"/>: the positive part of the buff
    /// (auras and summon buffs are not reported). "Whenever your Amalgam gains stats" (Mishmash).
    /// </summary>
    public virtual void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health) { }

    // ---- Spells, cards to hand, Blood Gems (Fight.Spells.cs) ------------------------------------------------------

    /// <summary>
    /// "Whenever you cast a (Tavern) spell (on X)": the owner is casting <paramref name="spell"/>, before it resolves
    /// (a real log shows such triggers ahead of the spell's own effect). <see cref="AfterFriendlySpellCast"/> runs after it.
    /// </summary>
    public virtual void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell) { }

    /// <summary>"Whenever a card is added to your hand" (see <see cref="Fight.AddToHand"/>); empty id = a random card the sim does not pick.</summary>
    public virtual void AfterCardAddedToHand(Fight fight, Unit self, string cardId) { }

    /// <summary>A Blood Gem was played on a friendly minion (possibly self) by <paramref name="source"/> (see <see cref="Fight.PlayBloodGem"/>).</summary>
    public virtual void AfterFriendlyBloodGem(Fight fight, Unit self, Unit target, Unit? source) { }

    private static Hook ScanHooks(Type type)
    {
        var hooks = Hook.None;
        foreach (var (name, hook) in HookNames)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (method is not null && method.DeclaringType != typeof(CombatEffect))
            {
                hooks |= hook;
            }
        }

        return hooks;
    }

    private static readonly (string Name, Hook Hook)[] HookNames =
    [
        (nameof(OnFightStart), Hook.FightStart),
        (nameof(OnAttack), Hook.Attack),
        (nameof(OnFriendlyAttack), Hook.FriendlyAttack),
        (nameof(OnFriendlyAttacked), Hook.FriendlyAttacked),
        (nameof(OnAttackDamage), Hook.AttackDamage),
        (nameof(AfterAttack), Hook.AfterAttack),
        (nameof(AfterFriendlyAttack), Hook.AfterFriendlyAttack),
        (nameof(OnDamaged), Hook.Damaged),
        (nameof(AfterFriendlyDealtDamage), Hook.FriendlyDealtDamage),
        (nameof(AfterKill), Hook.Kill),
        (nameof(OnFriendlyShieldLost), Hook.ShieldLost),
        (nameof(Deathrattle), Hook.Deathrattle),
        (nameof(ExtraDeathrattles), Hook.ExtraDeathrattles),
        (nameof(OnFriendlyDeathrattle), Hook.FriendlyDeathrattle),
        (nameof(AfterFriendlyDeath), Hook.FriendlyDeath),
        (nameof(AfterEnemyDeath), Hook.EnemyDeath),
        (nameof(AfterDeathPhase), Hook.DeathPhaseEnd),
        (nameof(OnSummoned), Hook.Summoned),
        (nameof(WheneverFriendlySummoned), Hook.WheneverSummon),
        (nameof(AfterFriendlySummoned), Hook.AfterSummon),
        (nameof(AfterFriendlyReborn), Hook.Reborn),
        (nameof(AfterFriendlySpellCast), Hook.SpellCast),
        (nameof(AuraFor), Hook.Aura),
        (nameof(Refresh), Hook.Refresh),
        (nameof(Battlecry), Hook.Battlecry),
        (nameof(ExtraBattlecries), Hook.ExtraBattlecries),
        (nameof(AfterFriendlyBattlecry), Hook.FriendlyBattlecry),
        (nameof(ExtraSpellCasts), Hook.ExtraSpellCasts),
        (nameof(AfterFriendlyGainedStats), Hook.FriendlyGainedStats),
        (nameof(OnFriendlySpellCast), Hook.SpellCasting),
        (nameof(AfterCardAddedToHand), Hook.CardToHand),
        (nameof(AfterFriendlyBloodGem), Hook.BloodGem),
    ];
}

/// <summary>Avenge (X): counts friendly deaths while this minion is alive and fires every X of them.</summary>
public abstract class AvengeEffect : CombatEffect
{
    /// <summary>The X of Avenge (X).</summary>
    protected abstract int Threshold(Unit self);

    protected abstract void Avenge(Fight fight, Unit self);

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        // The count starts at the friendly deaths of Start of Combat (SimMinion.FriendlyDeathsBeforeCapture); any full X of
        // them already fired before the capture, so only the remainder carries over.
        var threshold = Math.Max(1, Threshold(self));
        self.AvengeCount = self.AvengeCount % threshold + 1;
        if (self.AvengeCount >= threshold)
        {
            self.AvengeCount = 0;
            Avenge(fight, self);
        }
    }
}

/// <summary>Which hooks an effect overrides; the engine skips the rest.</summary>
[Flags]
internal enum Hook : long
{
    None = 0,
    FightStart = 1L << 0,
    Attack = 1L << 1,
    FriendlyAttack = 1L << 2,
    FriendlyAttacked = 1L << 3,
    AttackDamage = 1L << 4,
    AfterAttack = 1L << 5,
    AfterFriendlyAttack = 1L << 6,
    Damaged = 1L << 7,
    FriendlyDealtDamage = 1L << 8,
    Kill = 1L << 9,
    ShieldLost = 1L << 10,
    Deathrattle = 1L << 11,
    ExtraDeathrattles = 1L << 12,
    FriendlyDeathrattle = 1L << 13,
    FriendlyDeath = 1L << 14,
    EnemyDeath = 1L << 15,
    // Package R's bits are 50-53.
    DeathPhaseEnd = 1L << 50,
    Summoned = 1L << 16,
    WheneverSummon = 1L << 17,
    AfterSummon = 1L << 18,
    Reborn = 1L << 19,
    SpellCast = 1L << 20,
    Aura = 1L << 21,
    Refresh = 1L << 22,
    Battlecry = 1L << 23,
    ExtraBattlecries = 1L << 24,
    FriendlyBattlecry = 1L << 25,
    ExtraSpellCasts = 1L << 26,
    FriendlyGainedStats = 1L << 27,
    SpellCasting = 1L << 28,
    CardToHand = 1L << 29,
    BloodGem = 1L << 30,
}

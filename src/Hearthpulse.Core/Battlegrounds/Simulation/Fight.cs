using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// One simulated Battlegrounds fight and the API card effects use: board queries, decisions (<see cref="ICombatChooser"/>:
/// seeded random picks for the odds, the log's answers for a replay), summons, buffs, damage, destroy, extra attacks and
/// deathrattle triggers.
/// </summary>
/// <remarks>
/// Rules and the assumptions behind them (unverified against real logs where noted):
/// <list type="bullet">
/// <item>Sides alternate attacks. A side's next attacker is the first minion with Attack at or right of its "gap",
/// which sits just right of that side's last attacker (wrapping to the left-most minion). Minions summoned left of the
/// gap (for example in the slot of another minion that died) wait for the next round; minions summoned right of it,
/// and the deathrattle summons and reborn copy of the side's last attacker (in its slot), attack in turn (Power.log
/// 15.11, rules-evidence 2a/2b: 413 of 413 attacker choices).</item>
/// <item>An attack: "whenever a friendly minion attacks" hooks, then "when this attacks" hooks (Rally), then
/// "whenever a friendly minion is attacked" hooks run before damage (effects-audit E1), then a death phase. If the
/// attacker or the target died, the attack is cancelled. Otherwise damage is simultaneous; damage-triggered hooks and
/// after-attack hooks run while dead minions are still on the board (mortally wounded), then the death phase.</item>
/// <item>Death phase: all dead minions are removed at once, in order of play: the starting minions of the side with the
/// lower lobby PLAYER_ID, then the other side's, each left to right, then minions summoned during the combat in the
/// order summoned (rules-evidence 4a). Then, per dead minion in that order: its deathrattles, each one × (1 + extra
/// triggers from living minions such as Titus Rivendare and from the side) before the next (effects-audit E2); then
/// "after a friendly/enemy minion dies" hooks (Avenge) for each death; then each Reborn (rules-evidence 4d); auras are
/// refreshed, and the phase repeats while minions keep dying.</item>
/// <item>Summons from a dead minion appear in its slot, left to right in the order they are made, the reborn copy
/// after its deathrattle summons. A side holds at most 7 minions; summons beyond that are lost.</item>
/// <item>Reborn brings back the card's printed Attack and keywords with 1 Health of its printed maximum, golden if it
/// was golden, without buffs or enchantments.</item>
/// <item>"Attacks immediately" (<see cref="AttackSoon"/>) happens right after the attack in progress; Windfury's
/// second attack comes after it.</item>
/// </list>
/// </remarks>
public sealed partial class Fight
{
    private const int MaxAttacks = 400;
    private const int MaxDepth = 24;
    private const int MaxDeathRounds = 64;

    // Order of play (Unit.Entry): starting minions 0..MaxMinions-1 per side, Start of Combat summons from the capture
    // after them by entity id, minions summoned by the simulation after those in summon order.
    private const int CapturedSummonEntry = 16;
    private const int SimSummonEntry = 1 << 30;

    private const Hook DamageHooks = Hook.Damaged | Hook.FriendlyDealtDamage | Hook.Kill | Hook.ShieldLost | Hook.FriendlyDamaged | Hook.VenomLost;

    private EffectRegistry _registry;
    private List<string>? _log;
    private List<Unit> _dead;
    // One observer buffer per nesting depth, so hooks can be dispatched without allocating.
    private Unit[]?[] _buffers;
    private ICombatChooser _chooser;
    private PreparedEffects _prepared;
    // The thread's reusable objects when this fight is one of many on it (Batch.Work), else null.
    private Scratch? _scratch;
    private Queue<Unit>? _queuedAttacks;
    private List<DamageEvent>? _batch;
    private int _batchDepth;
    private bool _inDeathPhase;
    private int _depth;
    private int _attacks;
    private int _nextEntry;
    private int _attacksInProgress;

    internal Fight(SimSide ours, SimSide theirs, EffectRegistry registry, ICombatChooser chooser, bool trace, PreparedEffects? prepared = null,
                   Scratch? scratch = null) =>
        Init(ours, theirs, registry, chooser, trace, prepared, scratch);

    /// <summary>
    /// The thread's next fight of <paramref name="ours"/> against <paramref name="theirs"/>: the fight object, its two sides
    /// and its units are the ones <paramref name="scratch"/> kept from the previous fight, set up again (<see cref="Init"/>),
    /// so a run of many fights does not allocate them again. The previous fight of the scratch is gone after this call.
    /// </summary>
    internal static Fight Next(Scratch scratch, SimSide ours, SimSide theirs, EffectRegistry registry, ICombatChooser chooser, PreparedEffects prepared)
    {
        if (scratch.Fight is { } fight)
        {
            fight.Init(ours, theirs, registry, chooser, trace: false, prepared, scratch);
            return fight;
        }

        return scratch.Fight = new Fight(ours, theirs, registry, chooser, trace: false, prepared, scratch);
    }

    /// <summary>
    /// Sets every field as a new fight has it, reusing its sides and (with a scratch) the scratch's units. Assigns each
    /// field, defaults included: <c>SimPoolResetTests</c> checks that no field of the fight, its sides or its units is
    /// left out.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_registry), nameof(_dead), nameof(_buffers), nameof(_chooser), nameof(_prepared), nameof(Ours),
        nameof(Theirs), nameof(Attacking))]
    private void Init(SimSide ours, SimSide theirs, EffectRegistry registry, ICombatChooser chooser, bool trace, PreparedEffects? prepared,
                      Scratch? scratch)
    {
        _registry = registry;
        _scratch = scratch;
        _dead = scratch?.Dead ?? new List<Unit>();
        _dead.Clear();
        _buffers = scratch?.Buffers ?? new Unit[MaxDepth + 8][];
        _targets = scratch?.Targets;
        _targets?.Clear();
        if (scratch is not null)
        {
            scratch.UnitsUsed = 0;
        }

        _chooser = chooser;
        _log = trace ? new List<string>() : null;
        _queuedAttacks = null;
        _batch = null;
        _batchDepth = 0;
        _inDeathPhase = false;
        _depth = 0;
        _attacks = 0;
        _nextEntry = SimSummonEntry;
        _attacksInProgress = 0;
        _spell = default;
        _casting = false;
        Truncated = false;
        EnteredFirst = null;

        // A new fight has no sides yet (null here despite the type); a reused one sets its own up again.
        if (Ours is null || Theirs is null)
        {
            Ours = new Side(this, 0, ours);
            Theirs = new Side(this, 1, theirs);
        }
        else
        {
            Ours.Init(this, 0, ours);
            Theirs.Init(this, 1, theirs);
        }

        Ours.Enemy = Theirs;
        Theirs.Enemy = Ours;
        Attacking = Ours;
        prepared ??= PreparedEffects.For(ours, theirs, registry);
        _prepared = prepared;
        Populate(Ours, ours, prepared.Ours, prepared.OurExtras);
        Populate(Theirs, theirs, prepared.Theirs, prepared.TheirExtras);
        if (ours.PlayerId > 0 && theirs.PlayerId > 0 && ours.PlayerId != theirs.PlayerId)
        {
            EnteredFirst = ours.PlayerId < theirs.PlayerId ? Ours : Theirs;
        }
    }

    /// <summary>
    /// The side whose starting minions entered play first: the lower lobby PLAYER_ID (rules-evidence 4a). Null when
    /// unknown: a death phase then takes the attacking side's starting minions first (the old rule; one traced fight
    /// must pick an order, while <see cref="CombatSimulator.Run(SimSide, SimSide, bool?, SimOptions?)"/> alternates).
    /// </summary>
    internal Side? EnteredFirst { get; set; }

    /// <summary>An engine limit (nesting depth, death phases) dropped an effect: the result is "not simulated".</summary>
    internal bool Truncated { get; private set; }

    /// <summary>
    /// Effects of the captured minions and player-level cards, looked up once for many fights. A player-level card with an
    /// effect gets its holder's spec and effect array here too (both immutable: <see cref="Unit.AddEffect"/> replaces the
    /// array), so a fight does not build them again; null for a card without an effect. For many fights
    /// (<paramref name="RebornCopies"/>) also the <see cref="SimMinion.RebornCopy"/> of every captured minion, teammates
    /// included, by spec reference: specs are immutable, so the fights share them.
    /// </summary>
    internal sealed record PreparedEffects(CombatEffect[][] Ours, CombatEffect[][] Theirs, ExtraHolder?[] OurExtras, ExtraHolder?[] TheirExtras,
                                           IReadOnlyDictionary<SimMinion, SimMinion>? RebornCopies = null)
    {
        public static PreparedEffects For(SimSide ours, SimSide theirs, EffectRegistry registry, bool manyFights = false) => new(
            ours.Minions.Select(registry.EffectsFor).ToArray(),
            theirs.Minions.Select(registry.EffectsFor).ToArray(),
            HoldersOf(ours, registry),
            HoldersOf(theirs, registry),
            manyFights ? RebornCopiesOf(ours, theirs) : null);

        /// <summary>The side's player-level cards, then its Secrets (in the order they were put into play).</summary>
        private static ExtraHolder?[] HoldersOf(SimSide side, EffectRegistry registry) =>
            side.Extras.Concat(side.Secrets).Select(id => ExtraHolder.For(id, registry)).ToArray();

        private static Dictionary<SimMinion, SimMinion> RebornCopiesOf(SimSide ours, SimSide theirs)
        {
            var copies = new Dictionary<SimMinion, SimMinion>(ReferenceEqualityComparer.Instance);
            foreach (var side in new[] { ours, ours.Teammate, theirs, theirs.Teammate })
            {
                foreach (var spec in side?.Minions ?? [])
                {
                    copies.TryAdd(spec, spec.RebornCopy());
                }
            }

            return copies;
        }
    }

    /// <summary>What a player-level effect holder is made from: a 0/0 spec of the card and its one effect.</summary>
    internal sealed record ExtraHolder(SimMinion Spec, CombatEffect[] Effects)
    {
        public static ExtraHolder? For(string cardId, EffectRegistry registry) =>
            registry.Get(cardId, golden: false) is { } effect ? new ExtraHolder(SimMinion.Create(cardId, 0, 0), [effect]) : null;
    }

    /// <summary>
    /// Working memory one thread reuses for its fights one after another (observer buffers, the death list), so a fight
    /// does not allocate it again. Never shared by two fights at once; the death list is empty between fights.
    /// </summary>
    internal sealed class Scratch
    {
        internal List<Unit> Dead { get; } = new();

        internal Unit[]?[] Buffers { get; } = new Unit[MaxDepth + 8][];

        /// <summary>The candidates of one attack's target pick (<c>PickTarget</c>), empty between picks.</summary>
        internal List<Unit> Targets { get; } = new(Side.MaxMinions);

        /// <summary>The thread's fight object, set up again for each fight (<see cref="Next"/>).</summary>
        internal Fight? Fight { get; set; }

        /// <summary>Units made by this thread's fights; the first <see cref="UnitsUsed"/> belong to the current fight.</summary>
        internal List<Unit> Units { get; } = new();

        internal int UnitsUsed { get; set; }
    }

    /// <summary>The local player's side.</summary>
    public Side Ours { get; private set; }

    /// <summary>The opponent's side.</summary>
    public Side Theirs { get; private set; }

    /// <summary>
    /// Attacks being resolved right now: 0 between attacks, 1 during an attack, more for an attack started inside another
    /// (a replay tells a regular attack from a nested one by it).
    /// </summary>
    internal int AttacksInProgress => _attacksInProgress;

    /// <summary>The side whose attack is being resolved (its deaths resolve first when <see cref="EnteredFirst"/> is unknown).</summary>
    public Side Attacking { get; private set; }

    public bool Tracing => _log is not null;

    public IReadOnlyList<string> Log => (IReadOnlyList<string>?)_log ?? Array.Empty<string>();

    /// <summary>Adds a line to the fight log (tests); free when tracing is off. Guard costly messages with <see cref="Tracing"/>.</summary>
    public void Trace(string message) => _log?.Add(message);

    // ================================================================================================ fight loop

    /// <returns>+1 we win, -1 we lose, 0 tie.</returns>
    internal int Run(bool? firstAttackerIsOurs, int firstAttackerEntityId)
    {
        var side = firstAttackerIsOurs switch
        {
            true => Ours,
            false => Theirs,
            // The side with more minions attacks first; a coin flip on equal counts.
            null => Ours.Count > Theirs.Count ? Ours : Theirs.Count > Ours.Count ? Theirs
                : Pick(new CombatChoice(ChoiceKind.FirstAttacker, null, 2, null, null)) == 0 ? Ours : Theirs,
        };

        if (firstAttackerEntityId != 0)
        {
            for (var i = 0; i < side.Count; i++)
            {
                if (side.Minions[i].EntityId == firstAttackerEntityId)
                {
                    side.Gap = i;
                    break;
                }
            }
        }

        Attacking = side;
        StartFight();

        // BoardsStanding: both sides have minions; in Duos it first brings in the teammate of a side left without any.
        while (BoardsStanding() && _attacks < MaxAttacks)
        {
            Attacking = side;
            var attacker = side.NextAttacker();
            if (attacker is null)
            {
                // This side cannot attack; if neither can, the fight stalls into a tie.
                if (side.Enemy.NextAttacker() is null)
                {
                    break;
                }

                side = side.Enemy;
                continue;
            }

            side.Gap = side.IndexOf(attacker) + 1;
            side.LastAttacker = attacker;
            for (var swing = 0; swing < attacker.AttacksPerTurn && attacker.IsAlive && side.Enemy.Count > 0 && _attacks < MaxAttacks; swing++)
            {
                Attack(attacker, null);
                RunQueuedAttacks();
            }

            side = side.Enemy;
        }

        var outcome = Ours.Count > 0 && Theirs.Count == 0 ? 1 : Theirs.Count > 0 && Ours.Count == 0 ? -1 : 0;
        Trace(outcome switch { > 0 => "result: win", < 0 => "result: loss", _ => "result: tie" });
        return outcome;
    }

    private void Populate(Side side, SimSide info, CombatEffect[][] effects, ExtraHolder?[] extras)
    {
        for (var i = 0; i < info.Minions.Count; i++)
        {
            var spec = info.Minions[i];
            var unit = NewUnit(spec, side, effects[i], extra: false);
            // Order of play: starting minions left to right; Start of Combat summons after them, in creation order.
            unit.Entry = spec.SummonedInCombat ? CapturedSummonEntry + Math.Clamp(spec.EntityId, 0, 1 << 29) : i;
            // Avenge progress brought from Start of Combat; a copy summoned later starts from 0.
            unit.AvengeCount = Math.Max(0, spec.FriendlyDeathsBeforeCapture);
            side.AddInitial(unit);
        }

        // Player-level cards, then Secrets (PreparedEffects.HoldersOf).
        for (var i = 0; i < extras.Length; i++)
        {
            if (extras[i] is { } holder)
            {
                side.AddExtra(NewUnit(holder.Spec, side, holder.Effects, extra: true));
            }
        }
    }

    /// <summary>A unit set up from <paramref name="spec"/>: one the scratch kept from an earlier fight when it has one, else new.</summary>
    private Unit NewUnit(SimMinion spec, Side side, CombatEffect[] effects, bool extra)
    {
        if (_scratch is not { } scratch)
        {
            return new Unit(spec, side, effects, extra);
        }

        var units = scratch.Units;
        if (scratch.UnitsUsed < units.Count)
        {
            var unit = units[scratch.UnitsUsed++];
            unit.Init(spec, side, effects, extra);
            return unit;
        }

        var made = new Unit(spec, side, effects, extra);
        units.Add(made);
        scratch.UnitsUsed++;
        return made;
    }

    private Unit CreateUnit(SimMinion spec, Side side)
    {
        var unit = NewUnit(spec, side, _registry.EffectsFor(spec), extra: false);
        unit.Entry = _nextEntry++;
        return unit;
    }

    private void StartFight()
    {
        UndeadArmy(Ours);
        UndeadArmy(Theirs);
        StartSide(Ours);
        StartSide(Theirs);
        BaselineAuras(Ours);
        BaselineAuras(Theirs);

        RefreshAuras();
        ResolveDeaths();
        RunQueuedAttacks();
    }

    /// <summary>
    /// "Your Undead have +X Attack this game (wherever they are)", made in the Tavern: Undead summoned in this fight get it
    /// too (a captured Undead already shows it). <see cref="SimInputs.UndeadAttackBonus"/>.
    /// </summary>
    private static void UndeadArmy(Side side)
    {
        if (SimInputs.UndeadAttackBonus(side.Info) is > 0 and var bonus)
        {
            side.AddSummonBuff(Tribe.Undead, bonus, 0);
        }
    }

    private void StartSide(Side side)
    {
        if ((side.Hooks & Hook.FightStart) == 0)
        {
            return;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.FightStart) != 0)
                    {
                        effect.OnFightStart(this, unit);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Captured stats already include the auras present at capture: record them without applying.</summary>
    private void BaselineAuras(Side side)
    {
        if ((side.Hooks & Hook.Aura) == 0)
        {
            return;
        }

        foreach (var unit in side.Minions)
        {
            unit.AppliedAura = AuraTotal(side, unit);
            side.HasAppliedAuras |= !unit.AppliedAura.IsZero;
        }

        side.AuraDirty = false;
    }

    /// <summary>
    /// Nested triggers reached the depth the engine follows: the caller drops what it was about to do (a summon, an attack,
    /// deathrattles, hooks) and the fight counts as "not simulated" (<see cref="Truncated"/>) instead of silently differing.
    /// </summary>
    private bool TooDeep()
    {
        if (_depth < MaxDepth)
        {
            return false;
        }

        Truncated = true;
        return true;
    }

    /// <summary>Board and extra units of a side at this moment, in a buffer owned by the current nesting depth.</summary>
    private Unit[] Snapshot(Side side, out int count)
    {
        count = side.Count + side.Extras.Count;
        if (_depth >= _buffers.Length)
        {
            var fresh = new Unit[count];
            side.CopyObservers(fresh);
            return fresh;
        }

        var buffer = _buffers[_depth];
        if (buffer is null || buffer.Length < count)
        {
            buffer = _buffers[_depth] = new Unit[Math.Max(16, count)];
        }

        side.CopyObservers(buffer);
        return buffer;
    }
}

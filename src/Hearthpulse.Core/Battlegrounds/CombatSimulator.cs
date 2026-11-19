using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>One minion as it stands when the first attack of a combat starts.</summary>
/// <param name="RebornAttack">The card's printed Attack (golden-aware), which a reborn copy comes back with.</param>
/// <param name="HasUnsimulatedEffect">The card (or an enchantment on it) changes the fight in a way no registered effect models.</param>
public sealed partial record SimMinion(
    int Attack,
    int Health,
    bool Taunt,
    bool DivineShield,
    bool Poisonous,
    bool Venomous,
    bool Reborn,
    int AttacksPerTurn,
    bool Stealth,
    int RebornAttack,
    bool HasUnsimulatedEffect)
{
    /// <summary>Normal (non-golden) card id; effects are looked up by it with <see cref="Golden"/>. Empty when unknown.</summary>
    public string CardId { get; init; } = string.Empty;

    public bool Golden { get; init; }

    /// <summary>Power.log entity id at capture (0 for minions created by the simulator).</summary>
    public int EntityId { get; init; }

    public Tribe Tribes { get; init; }

    /// <summary>Maximum Health (the HEALTH tag); 0 means "same as <see cref="Health"/>".</summary>
    public int MaxHealth { get; init; }

    /// <summary>The card's printed Health (golden-aware); 0 when unknown.</summary>
    public int BaseHealth { get; init; }

    /// <summary>
    /// Keywords printed on the card (golden-aware). A reborn copy comes back with these (minus Reborn),
    /// not with keywords the minion gained from buffs.
    /// </summary>
    public Keyword BaseKeywords { get; init; }

    public CardMechanic Mechanics { get; init; }

    public int TechLevel { get; init; }

    /// <summary>TAG_SCRIPT_DATA_NUM_1/2 at capture: card-specific counters (e.g. the current bonus of a scaling card).</summary>
    public int ScriptData1 { get; init; }

    public int ScriptData2 { get; init; }

    /// <summary>
    /// Card id of the entity that created this minion (its CREATOR tag), when known: e.g. a hero power that summoned it at
    /// the start of this combat. Empty when unknown.
    /// </summary>
    public string CreatorCardId { get; init; } = string.Empty;

    /// <summary>Card ids of enchantments attached to the minion (Dark Gifts, granted deathrattles, ...).</summary>
    public IReadOnlyList<string> Enchantments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Minions this one's start-of-combat trigger removed from the board before capture (e.g. the minion Stitched
    /// Salvager destroyed), as they were when removed.
    /// </summary>
    public IReadOnlyList<SimMinion> Remembered { get; init; } = Array.Empty<SimMinion>();

    /// <summary>
    /// Attack this minion has gained from Blood Gems ("its Blood Gems"): Power.log tag 4235 on the minion, the total its
    /// visible "Blood Gems" enchantment shows (seen in real logs of 2026-11-05 and 2026-11-15). 0 when it has none.
    /// </summary>
    public int BloodGemAttack { get; init; }

    /// <summary>Health gained from Blood Gems (tag 4236), see <see cref="BloodGemAttack"/>.</summary>
    public int BloodGemHealth { get; init; }

    /// <summary>Current keywords as flags.</summary>
    public Keyword Keywords =>
        (Taunt ? Keyword.Taunt : 0) | (DivineShield ? Keyword.DivineShield : 0) | (Poisonous ? Keyword.Poisonous : 0)
        | (Venomous ? Keyword.Venomous : 0) | (Reborn ? Keyword.Reborn : 0) | (Stealth ? Keyword.Stealth : 0)
        | (AttacksPerTurn >= 4 ? Keyword.MegaWindfury : AttacksPerTurn >= 2 ? Keyword.Windfury : 0);

    /// <summary>
    /// A minion with the given printed stats and keywords: a token an effect summons, or a test minion.
    /// Base stats and keywords are the given ones.
    /// </summary>
    public static SimMinion Create(string cardId, int attack, int health, Tribe tribes = Tribe.None, Keyword keywords = Keyword.None,
                                   bool golden = false, CardMechanic mechanics = CardMechanic.None) =>
        new(attack, health,
            (keywords & Keyword.Taunt) != 0,
            (keywords & Keyword.DivineShield) != 0,
            (keywords & Keyword.Poisonous) != 0,
            (keywords & Keyword.Venomous) != 0,
            (keywords & Keyword.Reborn) != 0,
            Traits.AttacksPerTurn(keywords),
            (keywords & Keyword.Stealth) != 0,
            attack,
            false)
        {
            CardId = cardId,
            Golden = golden,
            Tribes = tribes,
            MaxHealth = health,
            BaseHealth = health,
            BaseKeywords = keywords,
            Mechanics = mechanics,
        };

    /// <summary>
    /// What Reborn brings back: a fresh copy of the card with its printed Attack, 1 Health and its printed keywords
    /// (not buffs, not enchantments), golden if the minion was golden, without Reborn. Its maximum Health is the printed
    /// Health, so it is a damaged minion (Power.log: HEALTH = printed, DAMAGE = printed - 1; rules-evidence 4e, 12 copies).
    /// </summary>
    public SimMinion RebornCopy() =>
        Create(CardId, RebornAttack, 1, Tribes, BaseKeywords & ~Keyword.Reborn, Golden, Mechanics) with
        {
            MaxHealth = Math.Max(1, BaseHealth),
            BaseHealth = BaseHealth,
            TechLevel = TechLevel,
            // A fresh copy shows the card's base script data: the captured one where it never changes (FixedScriptDataCards).
            ScriptData1 = FixedScriptDataCards.Contains(CardId) ? ScriptData1 : 0,
            ScriptData2 = FixedScriptDataCards.Contains(CardId) ? ScriptData2 : 0,
        };
}

/// <summary>
/// One side of a combat: the minions plus what the player can see about that player. Only public information:
/// the opponent's hand and secrets are never filled in.
/// </summary>
public sealed partial record SimSide(IReadOnlyList<SimMinion> Minions)
{
    public string HeroCardId { get; init; } = string.Empty;

    public string HeroPowerCardId { get; init; } = string.Empty;

    /// <summary>
    /// Player-level cards (hero powers, trinkets, quest rewards, the anomaly, the Deity) whose registered effects take part
    /// in the fight. Cards without a registered effect are ignored.
    /// </summary>
    public IReadOnlyList<string> Extras { get; init; } = Array.Empty<string>();

    /// <summary>Minions in hand: the local player's only. Effects that need a hand do nothing for the opponent.</summary>
    public IReadOnlyList<SimMinion> Hand { get; init; } = Array.Empty<SimMinion>();

    /// <summary>Numeric tags of the player entity at capture, by the name Power.log prints (player-wide counters).</summary>
    public IReadOnlyDictionary<string, int> Tags { get; init; } = EmptyTags;

    /// <summary>
    /// Numeric tags of the player-level cards in <see cref="Extras"/> at capture, by card id (a hero power's or trinket's
    /// value that grows over the game). A card missing here has no captured value: effects that need one report
    /// <see cref="UnsimulatedReason.MissingInput"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ExtraTags { get; init; } = EmptyExtraTags;

    /// <summary>A numeric tag of the player-level card <paramref name="cardId"/> at capture, or null when it was not seen.</summary>
    public int? ExtraTag(string cardId, string tag) =>
        ExtraTags.TryGetValue(cardId, out var tags) && tags.TryGetValue(tag, out var value) ? value : null;

    /// <summary>
    /// Minions a player-level card of this player (hero power, trinket) removed at Start of Combat, before the capture, as
    /// they were when removed, by that card's id: Rapid Reanimation and Soul Fermenter summon them again later. Public
    /// information: they stood on the board the player saw.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SimMinion>> RemovedByExtras { get; init; } = EmptyRemovedByExtras;

    public int TavernTier { get; init; }

    /// <summary>The hero's health plus armor when the fight starts (public on the leaderboard); 0 when unknown.</summary>
    public int Health { get; init; }

    /// <summary>The Battlegrounds turn of the fight (hero powers that unlock on a turn); 0 when unknown.</summary>
    public int Turn { get; init; }

    internal static readonly IReadOnlyDictionary<string, int> EmptyTags = new Dictionary<string, int>();

    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> EmptyExtraTags =
        new Dictionary<string, IReadOnlyDictionary<string, int>>();

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<SimMinion>> EmptyRemovedByExtras =
        new Dictionary<string, IReadOnlyList<SimMinion>>();
}

public sealed record SimOptions
{
    public static SimOptions Default { get; } = new();

    public int Runs { get; init; } = 4000;

    /// <summary>
    /// Stop early once this much time has passed (checked before each chunk of <see cref="CombatSimulator.ChunkRuns"/>
    /// runs, after <see cref="MinRuns"/>).
    /// </summary>
    public TimeSpan? TimeBudget { get; init; }

    /// <summary>Neither <see cref="TimeBudget"/> nor <see cref="TargetHalfWidth"/> stops the runs before this many.</summary>
    public int MinRuns { get; init; } = 500;

    /// <summary>
    /// Stop early, after <see cref="MinRuns"/>, once the 95 % interval (Wilson) of each of win, tie and loss reaches at
    /// most this far either side of the share: a lopsided fight needs fewer runs than a close one for the same
    /// precision. Checked after each chunk in chunk order, so the result still depends on the seed only. 0 = no such stop.
    /// </summary>
    public double TargetHalfWidth { get; init; }

    /// <summary>
    /// Most threads the runs are spread over, the calling thread included; 1 (or less) = the calling thread only. The
    /// odds do not depend on it: runs are made in chunks with seeds of their own and counted in chunk order.
    /// </summary>
    public int Parallelism { get; init; } = 1;

    /// <summary>
    /// Threads a background simulation may take while the game runs: half the logical processors, at most four (the
    /// gain above four is small, docs/bg-combat-sim.md "Скорость"), at least one.
    /// </summary>
    public static int BackgroundParallelism { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>Card effects to apply; null uses <see cref="EffectRegistry.Default"/>.</summary>
    public EffectRegistry? Effects { get; init; }

    /// <summary>Entity id of the minion that made the first attack, when known; attack order starts from it.</summary>
    public int FirstAttackerEntityId { get; init; }

    /// <summary>Ends the runs early (checked before each chunk after the first); the odds then cover the chunks done so far.</summary>
    public CancellationToken Cancel { get; init; }

    /// <summary>The lobby's combat damage cap (<see cref="DamageRules.Cap"/>); 0 = none.</summary>
    public int DamageCap { get; init; }

    /// <summary>
    /// Estimate hero damage and lethal. Off for Duos, where the loser of one board is not the one who takes the damage.
    /// </summary>
    public bool CountDamage { get; init; } = true;

    /// <summary>
    /// Simulate with the opponent's values the player could not see during the game (<see cref="SimSide.HiddenTags"/>, see
    /// <see cref="SimInputs.PostGame"/>). Only for consumers after the game (saved captures, Bench); the live tracker never
    /// sets it.
    /// </summary>
    public bool PostGame { get; init; }
}

/// <summary>
/// Estimated outcome of a combat. <see cref="Unsimulated"/> counts minions and player-level cards (the Deity, anomaly...)
/// whose combat-relevant effects are ignored.
/// </summary>
public sealed record CombatOdds(double Win, double Tie, double Loss, int Unsimulated)
{
    public bool Approximate => Unsimulated > 0;

    /// <summary>Number of simulated fights behind the numbers (0 when the result needed no simulation).</summary>
    public int Runs { get; init; }

    /// <summary>
    /// Sampling error of the numbers: the 95 % half-width (Wilson) of the least certain of win, tie and loss at
    /// <see cref="Runs"/>; 0 when no fight was simulated.
    /// </summary>
    public double HalfWidth { get; init; }

    /// <summary>Damage our hero takes in the fights we lose; null when we never lose (or damage is not counted).</summary>
    public DamageSpread? DamageTaken { get; init; }

    /// <summary>Damage the opponent's hero takes in the fights we win; null when we never win.</summary>
    public DamageSpread? DamageDealt { get; init; }

    /// <summary>Share of all fights that kill our hero; null when our health is unknown or damage is not counted.</summary>
    public double? LethalTaken { get; init; }

    /// <summary>Share of all fights that kill the opponent's hero; null when their health is unknown.</summary>
    public double? LethalDealt { get; init; }

    /// <summary>The combat damage cap the damage was capped at; 0 = none.</summary>
    public int DamageCap { get; init; }

    /// <summary>
    /// Fights cut short by an engine limit (nested triggers deeper than the engine follows, or endless death phases):
    /// effects were dropped there, so <see cref="Unsimulated"/> counts one more "not simulated" item when this is above 0.
    /// </summary>
    public int Truncated { get; init; }

    /// <summary>
    /// Inputs the fight was simulated with an estimate for (<see cref="SimSide.Estimates"/> of both sides): values of the
    /// opponent the player cannot see. Separate from <see cref="Unsimulated"/>: those effects did run.
    /// </summary>
    public int Estimated { get; init; }
}

/// <summary>Why something in a captured fight is "not simulated" (the coverage matrix uses the same words).</summary>
public enum UnsimulatedReason
{
    /// <summary>The card acts in combat and no effect is registered for it.</summary>
    MissingEffect,

    /// <summary>The effect exists but a value it needs is not in the capture (a growing value, the opponent's hidden hand).</summary>
    MissingInput,

    /// <summary>No table knows the card (newer than the data): its effect cannot be classified.</summary>
    Unknown,
}

/// <summary>One "not simulated" minion or player-level card of a side.</summary>
public sealed record UnsimulatedItem(string CardId, bool Golden, bool IsOpponent, UnsimulatedReason Reason, bool Extra)
{
    /// <summary>The reason as the matrix and simcover write it: missing_effect, missing_input, unknown.</summary>
    public string Code => SimCoverage.Code(Reason);
}

/// <summary>One simulated fight: +1 we won, -1 we lost, 0 tie; the surviving minions and (when traced) a log.</summary>
public sealed record FightResult(int Outcome, IReadOnlyList<SimMinion> Ours, IReadOnlyList<SimMinion> Theirs, IReadOnlyList<string> Log)
{
    /// <summary>An engine limit cut the fight short and dropped effects (see <see cref="CombatOdds.Truncated"/>).</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Monte Carlo Battlegrounds combat. Keywords (taunt, divine shield, poisonous, venomous, reborn, windfury, stealth) are
/// built in; card effects plug in through <see cref="CombatEffect"/> classes found by <see cref="EffectRegistry"/>.
/// Minions and player-level cards whose effects are not implemented are reported through <see cref="CombatOdds.Unsimulated"/>.
/// Start-of-combat effects have already resolved when the boards are captured (at the first attack), so they are
/// reflected in the stats.
/// </summary>
/// <remarks>
/// Runs are made in chunks of <see cref="ChunkRuns"/>, each with its own random source seeded from the boards' seed
/// and the chunk's index (<see cref="ChunkSeed"/>), and counted in chunk order. The odds for a pair of boards are
/// therefore the same however many threads made the chunks and in whatever order they finished; only how many chunks
/// a time budget or a cancellation leaves can differ between two calls.
/// </remarks>
public static partial class CombatSimulator
{
    /// <summary>Runs per chunk: the unit of seeding, of the time, cancellation and precision checks, and of work per thread.</summary>
    public const int ChunkRuns = 50;

    /// <param name="firstAttackerIsOurs">Who attacks first if known (read from the first attack), else null.</param>
    public static CombatOdds Run(IReadOnlyList<SimMinion> ours, IReadOnlyList<SimMinion> theirs, bool? firstAttackerIsOurs, int runs = 4000) =>
        Run(new SimSide(ours), new SimSide(theirs), firstAttackerIsOurs, new SimOptions { Runs = runs });

    public static CombatOdds Run(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs, SimOptions? options = null)
    {
        if (options?.PostGame == true)
        {
            (ours, theirs) = (PostGameTeam(ours), PostGameTeam(theirs));
        }

        var estimated = EstimateCount(ours) + EstimateCount(theirs);
        (ours, theirs) = (StartingBoard(ours), StartingBoard(theirs));
        return RunFights(ours, theirs, firstAttackerIsOurs, options) with { Estimated = estimated };
    }

    /// <summary>The inputs of a side the fight was simulated with an estimate for, and what each estimate rests on.</summary>
    public static IReadOnlyList<EstimatedItem> ExplainEstimates(SimSide side, bool isOpponent) =>
        side.Estimates.Concat(side.Teammate?.Estimates ?? [])
            .Select(e => new EstimatedItem(e.Input, e.Value1, e.Value2, e.Basis, isOpponent)).ToList();

    private static CombatOdds RunFights(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs, SimOptions? options)
    {
        options ??= SimOptions.Default;
        var effects = options.Effects ?? EffectRegistry.Default;
        var unsimulated = Unsimulated(ours, isOpponent: false, effects) + Unsimulated(theirs, isOpponent: true, effects);
        var rules = new DamageRule(ours, theirs, options.DamageCap, options.CountDamage);
        if (ours.Minions.Count == 0 || theirs.Minions.Count == 0)
        {
            var win = ours.Minions.Count > 0 ? 1.0 : 0;
            var loss = theirs.Minions.Count > 0 ? 1.0 : 0;
            var certain = new CombatOdds(win, 1 - win - loss, loss, unsimulated);
            if (!rules.Count)
            {
                return certain;
            }

            var tally = new Tally();
            rules.Add(tally, win > 0 ? 1 : loss > 0 ? -1 : 0, Tiers(ours.Minions), Tiers(theirs.Minions));
            return rules.With(certain, tally);
        }

        // Stable seed: the same boards always show the same numbers, in every session and on any number of threads.
        var total = new Batch(ours, theirs, firstAttackerIsOurs, options, effects, rules, TeamSeed(ours, theirs, firstAttackerIsOurs)).Run();
        int done = total.Done, wins = total.Wins, losses = total.Losses;
        // A fight cut short by an engine limit dropped effects: one more "not simulated" item, not silence.
        unsimulated += total.Truncated > 0 ? 1 : 0;
        if (done == 0)
        {
            return new CombatOdds(0, 1.0, 0, unsimulated) { Runs = 0 };
        }

        var winRate = Math.Clamp(wins / (double)done, 0.0, 1.0);
        var lossRate = Math.Clamp(losses / (double)done, 0.0, 1.0);
        var tieRate = Math.Clamp((done - wins - losses) / (double)done, 0.0, 1.0);

        if (wins == done)
        {
            winRate = 1.0;
            lossRate = 0.0;
            tieRate = 0.0;
        }
        else if (losses == done)
        {
            winRate = 0.0;
            lossRate = 1.0;
            tieRate = 0.0;
        }
        else if (wins + losses == 0)
        {
            winRate = 0.0;
            lossRate = 0.0;
            tieRate = 1.0;
        }
        else
        {
            tieRate = Math.Max(0.0, 1.0 - winRate - lossRate);
        }

        var odds = new CombatOdds(winRate, tieRate, lossRate, unsimulated)
        {
            Runs = done,
            HalfWidth = HalfWidth(wins, losses, done),
            Truncated = total.Truncated,
        };
        return rules.Count ? rules.With(odds, total) : odds;
    }

    /// <summary>
    /// Minions with unknown effects, plus player-level extras that change a fight without a registered effect (those the
    /// tables mark as acting in combat, and those no table knows: every hero power, trinket, quest reward and anomaly of
    /// the card data has a row, so an unknown one is newer than the data) or whose effect lacks a captured value.
    /// </summary>
    private static int Unsimulated(SimSide side, bool isOpponent, EffectRegistry effects) =>
        side.Minions.Count(m => m.HasUnsimulatedEffect) + side.Extras.Count(id => ExtraReason(side, id, isOpponent, effects) is not null)
        + (side.Teammate is { } teammate ? Unsimulated(teammate, isOpponent, effects) : 0);

    /// <summary>
    /// Why a side is "not simulated": one entry per flagged minion and per player-level card, with the reason. The count
    /// equals what <see cref="CombatOdds.Unsimulated"/> counts for this side.
    /// </summary>
    public static IReadOnlyList<UnsimulatedItem> Explain(SimSide side, bool isOpponent, EffectRegistry? effects = null)
    {
        effects ??= EffectRegistry.Default;
        var items = new List<UnsimulatedItem>();
        foreach (var m in side.Minions.Where(m => m.HasUnsimulatedEffect))
        {
            items.Add(new UnsimulatedItem(m.CardId, m.Golden, isOpponent, MinionReason(m, isOpponent, effects), Extra: false));
        }

        foreach (var id in side.Extras)
        {
            if (ExtraReason(side, id, isOpponent, effects) is { } reason)
            {
                items.Add(new UnsimulatedItem(id, Golden: false, isOpponent, reason, Extra: true));
            }
        }

        if (side.Teammate is { } teammate)
        {
            items.AddRange(Explain(teammate, isOpponent, effects));
        }

        return items;
    }

    private static UnsimulatedReason? ExtraReason(SimSide side, string id, bool isOpponent, EffectRegistry effects)
    {
        var row = CombatRelevance.Find(id);
        if (row is not null && row.Relevance != Relevance.Relevant)
        {
            return null;
        }

        if (effects.Get(id, golden: false) is { } effect)
        {
            return isOpponent && effect.NeedsHand ? UnsimulatedReason.MissingInput
                : effect.HasInputs(side, id) ? null
                : UnsimulatedReason.MissingInput;
        }

        return row is null ? UnsimulatedReason.Unknown
            : SimCoverage.InputGap(id) is not null ? UnsimulatedReason.MissingInput
            : UnsimulatedReason.MissingEffect;
    }

    private static UnsimulatedReason MinionReason(SimMinion m, bool isOpponent, EffectRegistry effects)
    {
        if (effects.Get(m.CardId, m.Golden) is { NeedsHand: true } && isOpponent)
        {
            return UnsimulatedReason.MissingInput;
        }

        if (CombatRelevance.Find(m.CardId) is null && !effects.Implements(m.CardId, m.Golden, isOpponent))
        {
            return UnsimulatedReason.Unknown;
        }

        return SimCoverage.InputGap(m.CardId) is not null || m.Enchantments.Any(e => SimCoverage.InputGap(e) is not null)
            ? UnsimulatedReason.MissingInput
            : UnsimulatedReason.MissingEffect;
    }

    /// <summary>Runs one fight with the given random source and returns the survivors (and a log when <paramref name="trace"/>).</summary>
    /// <remarks>Tests can pass a <see cref="Random"/> subclass overriding <see cref="Random.Next(int)"/> to script every random pick.</remarks>
    public static FightResult RunSingle(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs, Random random,
                                        EffectRegistry? effects = null, bool trace = true, int firstAttackerEntityId = 0) =>
        RunSingle(ours, theirs, firstAttackerIsOurs, new RandomChooser(random), effects, trace, firstAttackerEntityId);

    /// <summary>Runs one fight whose every decision <paramref name="chooser"/> makes (a replay of a real fight answers from its log).</summary>
    public static FightResult RunSingle(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs, ICombatChooser chooser,
                                        EffectRegistry? effects = null, bool trace = true, int firstAttackerEntityId = 0)
    {
        var fight = new Fight(ours, theirs, effects ?? EffectRegistry.Default, chooser, trace);
        var outcome = fight.Run(firstAttackerIsOurs, firstAttackerEntityId);
        return new FightResult(
            outcome,
            fight.Ours.Minions.Select(u => u.ToSimMinion()).ToList(),
            fight.Theirs.Minions.Select(u => u.ToSimMinion()).ToList(),
            fight.Log)
        {
            Truncated = fight.Truncated,
        };
    }

    /// <summary>FNV-1a over the boards: stable across processes and machines (unlike string/record hash codes).</summary>
    internal static int Seed(SimSide ours, SimSide theirs, bool? firstAttackerIsOurs)
    {
        var hash = 14695981039346656037UL;
        void Add(long value)
        {
            for (var i = 0; i < 8; i++)
            {
                hash ^= (byte)(value >> (8 * i));
                hash *= 1099511628211UL;
            }
        }

        void AddText(string text)
        {
            Add(text.Length);
            foreach (var c in text)
            {
                Add(c);
            }
        }

        void AddSide(SimSide side)
        {
            Add(side.Minions.Count);
            foreach (var m in side.Minions)
            {
                Add(m.Attack);
                Add(m.Health);
                Add((long)m.Keywords);
                Add(m.RebornAttack);
                Add(m.HasUnsimulatedEffect ? 1 : 0);
                AddText(m.CardId);
                Add(m.Golden ? 1 : 0);
                Add((long)m.Tribes);
                Add(m.MaxHealth);
                Add((long)m.BaseKeywords);
                Add(m.ScriptData1);
                Add(m.ScriptData2);
                foreach (var enchantment in m.Enchantments)
                {
                    AddText(enchantment);
                }

                // Only when known, so boards without it keep the seeds they had.
                if (m.CreatorCardId.Length > 0)
                {
                    AddText(m.CreatorCardId);
                }

                if (m.ScriptData3 != 0 || m.ScriptData4 != 0 || m.Crew.Count > 0)
                {
                    AddText($"{m.ScriptData3}/{m.ScriptData4}/{string.Join(',', m.Crew.Select(c => c.CardId))}");
                }

                foreach (var remembered in m.Remembered)
                {
                    AddText(remembered.CardId);
                    Add(remembered.Attack);
                    Add(remembered.Health);
                }
            }

            AddText(side.HeroCardId);
            AddText(side.HeroPowerCardId);
            foreach (var extra in side.Extras)
            {
                AddText(extra);
            }

            Add(side.Hand.Count);
        }

        AddSide(ours);
        Add(-1);
        AddSide(theirs);
        Add(firstAttackerIsOurs switch { true => 1, false => 2, null => 3 });
        return (int)(hash ^ (hash >> 32));
    }
}

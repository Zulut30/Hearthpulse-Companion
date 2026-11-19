namespace Hearthpulse.Core.Battlegrounds.Effects;

/// <summary>Where a card stands for the combat simulator (the six statuses of the coverage matrix).</summary>
public enum CoverageStatus
{
    /// <summary>The text does not change a fight after capture (Tavern text, keywords the engine applies itself).</summary>
    NoEffect,

    /// <summary>A Start of Combat effect: it resolved before the boards were captured, the captured stats show it.</summary>
    Resolved,

    /// <summary>A registered effect simulates it.</summary>
    Implemented,

    /// <summary>It changes a fight and no effect is registered.</summary>
    MissingEffect,

    /// <summary>An effect would need a value the capture does not hold.</summary>
    MissingInput,

    /// <summary>No relevance table knows the card: its effect cannot be classified.</summary>
    Unknown,
}

/// <summary>What kind of card a matrix row is; player-level cards are judged like the simulator judges extras.</summary>
public enum CoverageKind
{
    Minion,
    HeroPower,
    Trinket,
    DarkGift,
    Enchantment,

    /// <summary>A Battlegrounds quest (Sire Denathrius's hero power offers them): its task may progress in a fight.</summary>
    Quest,

    /// <summary>A quest reward: a player-level card like a trinket.</summary>
    QuestReward,

    /// <summary>The lobby's anomaly.</summary>
    Anomaly,

    /// <summary>A Tavern spell: in the pool, or cast during a fight by another card's combat effect.</summary>
    Spell,

    /// <summary>A Tavern card (minion or spell) that only Duos offers.</summary>
    DuosOnly,
}

/// <summary>A card's status and a short note (Russian, for docs/bg-sim-matrix.md).</summary>
public sealed record Coverage(CoverageStatus Status, string Note);

/// <summary>
/// Classifies cards into the coverage statuses with the same rules the simulator uses to flag a fight "not simulated"
/// (<see cref="CombatRelevance"/>, <see cref="EffectRegistry"/>), so the matrix (Hearthpulse.Bench simmatrix →
/// docs/bg-sim-matrix.md) and the reasons of <see cref="CombatSimulator.Explain"/> say the same thing.
/// </summary>
public static class SimCoverage
{
    /// <summary>The status codes, in the order of <see cref="CoverageStatus"/>.</summary>
    public static IReadOnlyList<string> Codes { get; } = ["no_effect", "resolved", "implemented", "missing_effect", "missing_input", "unknown"];

    /// <summary>
    /// Combat cards whose effect cannot be written yet because a value it needs is not captured, with what is missing.
    /// They make a fight "not simulated" with <see cref="UnsimulatedReason.MissingInput"/>.
    /// </summary>
    private static readonly Dictionary<string, string> InputGaps = new(StringComparer.OrdinalIgnoreCase)
    {
        // Empty for now. Blood Golem Sticker was here until E3 (2026-11-19) found "its Blood Gems" in the dying Quilboar's
        // tags 4235/4236 (Effects/player/BloodGolemSticker.cs).
    };

    /// <summary>
    /// Spells a combat effect resolves inside its own code instead of a registered spell effect: the spell is simulated
    /// when one of these casters has an effect. Checked against the effects' source (2026-11-19).
    /// </summary>
    private static readonly Dictionary<string, (string[] Casters, string Note)> ResolvedInline = new(StringComparer.OrdinalIgnoreCase)
    {
        // Effects/spells/TavernSpells.cs: the spell's effect, cast through the spell pipeline (Fight.CastSpell) by its caster.
        ["BG36_246"] = (["BG36_241"], "разрешается в TavernSpells через CastSpell: каст Crimson Vindicator (BG36_241)"),
        ["BG28_518"] = (["BG34_925"], "разрешается в TavernSpells через CastSpell: каст Seafloor Recruiter (BG34_925)"),
        ["BG28_888"] = (["BG36_640"], "разрешается в TavernSpells через CastSpell: каст Gatekeeper Amalgam (BG36_640)"),
        // Effects/attack/BloodGems.cs → Fight.PlayBloodGem, called by Bonker, Prodigious Tusker, Razorfen Vineweaver, Tusked
        // Camper and Geomagus Roogug. In a real log a gem a minion plays is BG20_GEM_No_Impact, not a cast.
        ["BG20_GEM"] = (["BG20_104", "BG33_430", "BG33_883", "BG33_886", "BG28_583"],
            "разыгрывается в эффектах существ (Fight.PlayBloodGem): не заклинание (в логе BG20_GEM_No_Impact), его видит Geomagus Roogug"),
    };

    public static string Code(CoverageStatus status) => Codes[(int)status];

    /// <summary>How a spell resolved inside another card's effect is simulated, or null (see <see cref="ResolvedInline"/>).</summary>
    public static string? InlineResolution(string? cardId) =>
        cardId is { Length: > 0 } && ResolvedInline.TryGetValue(cardId, out var inline) ? inline.Note : null;

    public static string Code(UnsimulatedReason reason) => reason switch
    {
        UnsimulatedReason.MissingInput => Code(CoverageStatus.MissingInput),
        UnsimulatedReason.Unknown => Code(CoverageStatus.Unknown),
        _ => Code(CoverageStatus.MissingEffect),
    };

    /// <summary>What a combat card without an effect is missing from the capture, or null.</summary>
    public static string? InputGap(string? cardId) =>
        cardId is { Length: > 0 } && InputGaps.TryGetValue(cardId, out var gap) ? gap : null;

    /// <summary>
    /// The card's status. A spell another card's effect resolves itself (<see cref="InlineResolution"/>) is implemented
    /// while that effect is registered. Cards no table knows: an effect makes them implemented; a player-level card (hero
    /// power, trinket, quest, quest reward, anomaly, Tavern spell) is unknown, as the simulator counts it; a minion or
    /// enchantment falls back to its English text as the simulator does
    /// (<see cref="CombatRelevance.TextSuggestsCombatEffect"/>): combat-like text = unknown, a Start of Combat text =
    /// resolved, other text = no effect.
    /// </summary>
    /// <param name="cardId">Normal or golden card id.</param>
    /// <param name="kind">What the card is.</param>
    /// <param name="englishText">The card's English text, used only when no table knows the card.</param>
    /// <param name="effects">Registry to check (default: every effect in Hearthpulse.Core).</param>
    public static Coverage Classify(string cardId, CoverageKind kind, string? englishText = null, EffectRegistry? effects = null)
    {
        effects ??= EffectRegistry.Default;
        var (_, golden) = CombatRelevance.Normalize(cardId);
        var effect = effects.Get(cardId, golden);
        if (effect is null && InputGap(cardId) is { } gap)
        {
            return new Coverage(CoverageStatus.MissingInput, gap);
        }

        if (effect is null && ResolvedInline.TryGetValue(cardId, out var inline) && inline.Casters.Any(c => effects.Get(c, golden: false) is not null))
        {
            return new Coverage(CoverageStatus.Implemented, inline.Note);
        }

        if (CombatRelevance.Find(cardId) is not { } row)
        {
            return effect is not null ? Implemented(effect, "нет в таблице релевантности")
                : IsPlayerLevel(kind) ? new Coverage(CoverageStatus.Unknown, "нет в таблице: новее данных")
                : FromText(englishText);
        }

        return row.Relevance switch
        {
            Relevance.Resolved => new Coverage(CoverageStatus.Resolved, "Start of Combat: разрешилось до снимка"),
            Relevance.Relevant when effect is null => new Coverage(CoverageStatus.MissingEffect, row.Category),
            Relevance.Conditional when effect is null =>
                new Coverage(CoverageStatus.MissingEffect, $"условный ({row.Category}): бой не помечается"),
            Relevance.None or Relevance.Keywords when effect is null => new Coverage(CoverageStatus.NoEffect, row.Category),
            _ => Implemented(effect!, row.Category),
        };
    }

    /// <summary>
    /// Kinds every card of which has a table row (tools/effects/gen_player_relevance.py labels each one of the card
    /// data): a card no table knows is newer than the data, so it is unknown rather than judged by its text.
    /// </summary>
    private static bool IsPlayerLevel(CoverageKind kind) =>
        kind is CoverageKind.HeroPower or CoverageKind.Trinket or CoverageKind.Quest or CoverageKind.QuestReward
            or CoverageKind.Anomaly or CoverageKind.Spell;

    private static Coverage Implemented(CombatEffect effect, string note) =>
        effect.UncapturedInput is { } missing ? new Coverage(CoverageStatus.MissingInput, $"{note}; эффект есть, но нет в снимке: {missing}")
        : effect.NeedsHand ? new Coverage(CoverageStatus.Implemented, $"{note}; у соперника missing_input (рука скрыта)")
        : effect.CapturedInput is { } input ? new Coverage(CoverageStatus.Implemented, $"{note}; величина: {input}, без неё missing_input")
        : new Coverage(CoverageStatus.Implemented, note);

    private static Coverage FromText(string? englishText)
    {
        if (string.IsNullOrWhiteSpace(englishText))
        {
            return new Coverage(CoverageStatus.Unknown, "нет в таблице и нет текста");
        }

        if (CombatRelevance.TextSuggestsCombatEffect(englishText))
        {
            return new Coverage(CoverageStatus.Unknown, "нет в таблице; текст похож на боевой");
        }

        return CombatRelevance.NormalizeText(englishText).Contains("Start of Combat", StringComparison.Ordinal)
            ? new Coverage(CoverageStatus.Resolved, "нет в таблице; по тексту Start of Combat")
            : new Coverage(CoverageStatus.NoEffect, "нет в таблице; по тексту");
    }
}

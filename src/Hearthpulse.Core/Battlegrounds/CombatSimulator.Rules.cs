using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Hearthpulse.Core.Battlegrounds;

// Inputs the engine's combat rules read (TASK-2026-11-19 package R, rules from real Power.log fights in
// rules-evidence.md). Not saved with captures yet: the capture format is package P1's to change.

public sealed partial record SimMinion
{
    /// <summary>
    /// Summoned during this combat before capture (Start of Combat), not brought to the fight: in a death phase it dies
    /// after every starting minion of both sides, in the order summoned (Power.log, rules-evidence 4a: 17 of 17 phases).
    /// </summary>
    [JsonIgnore]
    public bool SummonedInCombat { get; init; }

    /// <summary>
    /// Cards (normal ids) whose TAG_SCRIPT_DATA_NUM_1/2 is a balance value that does not change during a game. A fresh
    /// copy of a card (Reborn, Y'Shaarj) carries the card's base script data: for these it is what the captured minion
    /// shows, so <see cref="RebornCopy"/> keeps it. Cards whose value grows (The Last One Standing BG34_320 10/20/30, Faceless
    /// Converter BG36_318, BG31_148 3/6/9...) come back with their base value, which the effects take from the card text.
    /// Power.log 15.11, every value of the five games: one non-zero value per card (and golden card) for these; Y'Shaarj's
    /// copy of a The Last One Standing improved to 20/30 carries 10/10 (line 751156), of Sha of Fear 3/3 (751229).
    /// </summary>
    internal static readonly FrozenSet<string> FixedScriptDataCards = FrozenSet.ToFrozenSet(
    [
        "BG23_017", "BG27_005", "BG28_741", "BG33_828", "BG33_885", "BG36_108", "BG36_109", "BG36_110", "BG36_111", "BG36_113",
        "BG36_116", "BG36_703", "BG36_704", "BG36_853", "BGS_008", "BGS_018", "BGS_041",
    ], StringComparer.Ordinal);
}

public sealed partial record SimSide
{
    /// <summary>
    /// The hero's lobby PLAYER_ID; 0 when unknown. The side with the lower one entered play first, so its starting
    /// minions die first in a death phase (Power.log, rules-evidence 4a: 83 of 83 phases).
    /// </summary>
    [JsonIgnore]
    public int PlayerId { get; init; }

    /// <summary>
    /// The local player's own secrets in play (card ids), which are on their screen; always empty for the opponent,
    /// whose secrets are hidden. Read for hero damage: Ice Block (<see cref="DamageRules.IceBlock"/>).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Secrets { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Post-game only: the opponent's Secrets (card ids) as the log showed them by the end of the game, for
    /// <see cref="SimOptions.PostGame"/> (<see cref="SimInputs.PostGame"/> puts them into <see cref="Secrets"/>). During the
    /// game this is empty and the opponent's Secrets are an estimate (<see cref="SimInputs.OpponentSecrets"/>): the player sees
    /// how many there are, not which (docs/adr/0005-hidden-information.md). Kept in the local capture file only, never in the
    /// pool's records.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> HiddenSecrets { get; init; } = Array.Empty<string>();
}

public sealed partial record SimMinion
{
    /// <summary>
    /// What Reborn brings back: a fresh copy of the card with its printed Attack, 1 Health and its printed keywords
    /// (not buffs, not enchantments), golden if the minion was golden, without Reborn. Its maximum Health is the printed
    /// Health, so it is a damaged minion (Power.log: HEALTH = printed, DAMAGE = printed - 1; rules-evidence 4e, 12 copies).
    /// </summary>
    public SimMinion RebornCopy()
    {
        // One object: what Create(CardId, RebornAttack, 1, Tribes, keywords, Golden, Mechanics) makes, with the copy's
        // maximum Health, base Health, tier and script data set at once (Create then `with` made two).
        var keywords = BaseKeywords & ~Keyword.Reborn;
        // A fresh copy shows the card's base script data: the captured one where it never changes (FixedScriptDataCards).
        var fixedScriptData = FixedScriptDataCards.Contains(CardId);
        return new SimMinion(RebornAttack, 1,
            (keywords & Keyword.Taunt) != 0,
            (keywords & Keyword.DivineShield) != 0,
            (keywords & Keyword.Poisonous) != 0,
            (keywords & Keyword.Venomous) != 0,
            Reborn: false,
            Traits.AttacksPerTurn(keywords),
            (keywords & Keyword.Stealth) != 0,
            RebornAttack,
            HasUnsimulatedEffect: false)
        {
            CardId = CardId,
            Golden = Golden,
            Tribes = Tribes,
            MaxHealth = Math.Max(1, BaseHealth),
            BaseHealth = BaseHealth,
            BaseKeywords = keywords,
            Mechanics = Mechanics,
            TechLevel = TechLevel,
            ScriptData1 = fixedScriptData ? ScriptData1 : 0,
            ScriptData2 = fixedScriptData ? ScriptData2 : 0,
        };
    }
}

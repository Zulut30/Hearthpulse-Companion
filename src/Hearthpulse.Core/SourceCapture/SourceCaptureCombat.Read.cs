using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.SourceCapture;

// A bg_combat record read back as simulator input (Bench simcover/simreplay --pool), and the record's wire types.
internal static partial class SourceCaptureCombat
{
    /// <summary>
    /// A checked record as simulator input again, with its transcript (the measurement and the replay read the pool's
    /// export this way); null when it breaks a rule. The hand is empty: the record never holds one.
    /// </summary>
    internal static SourceCombatInput? Read(JsonElement data)
    {
        try
        {
            Validate(data);
        }
        catch (SourceCaptureException)
        {
            return null;
        }

        var fight = data.Deserialize<SourceCombat>(SourceCaptureRecord.Json)!;
        CombatOdds? odds = fight.Odds is { } o
            ? new CombatOdds(o.Win, o.Tie, o.Loss, o.Unsimulated) { Runs = o.Runs, LethalTaken = o.LethalTaken, LethalDealt = o.LethalDealt }
            : null;
        return new SourceCombatInput(
            fight.Fight,
            fight.Turn,
            new BattlegroundsTracker.CombatCapture(ToSim(fight.Ours), ToSim(fight.Theirs), fight.OursFirst, fight.FirstAttacker),
            fight.DamageCap,
            fight.CountDamage,
            fight.Facts.Select(f => new BattlegroundsTracker.MinionFacts(f.Entity, f.Card, f.Markers.ToArray())).ToArray(),
            fight.Result switch { "W" => "Win", "L" => "Loss", _ => "Tie" },
            fight.DamageTaken,
            fight.DamageDealt,
            odds,
            fight.Transcript is { } transcript ? ReadTranscript(transcript) : null);
    }

    private static SimSide ToSim(SourceCombatSide side) => new(side.Minions.Select(ToSim).ToArray())
    {
        HeroCardId = side.Hero ?? string.Empty,
        HeroPowerCardId = side.HeroPower ?? string.Empty,
        Extras = side.Extras.ToArray(),
        Tags = side.Tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
        ExtraTags = side.CardTags.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, int>)t.Value.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        TavernTier = side.Tier,
        Health = side.Health,
        Turn = side.Turn,
        PlayerEnchantments = side.PlayerEnchantments.Select(ToSim).ToArray(),
        LobbyTribes = (Tribe)side.LobbyTribes,
        DeathsBeforeCapture = side.Deaths.Select(ToSim).ToArray(),
    };

    private static SimEnchantment ToSim(SourceCombatEnchantment e) => new(e.Card)
    {
        ScriptData1 = e.Script1,
        ScriptData2 = e.Script2,
        Attack = e.Attack,
        Health = e.Health,
        CreatorEntityId = e.CreatorEntity,
    };

    private static SimMinion ToSim(SourceCombatMinion m)
    {
        var keywords = (Keyword)m.Keywords;
        bool Has(Keyword keyword) => (keywords & keyword) != 0;
        return new SimMinion(m.Attack, m.Health, Has(Keyword.Taunt), Has(Keyword.DivineShield), Has(Keyword.Poisonous), Has(Keyword.Venomous),
            Has(Keyword.Reborn), m.Attacks, Has(Keyword.Stealth), m.RebornAttack, m.Unsimulated)
        {
            CardId = m.Card ?? string.Empty,
            Golden = m.Golden,
            EntityId = m.Entity,
            Tribes = (Tribe)m.Tribes,
            MaxHealth = m.MaxHealth,
            BaseHealth = m.BaseHealth,
            BaseKeywords = (Keyword)m.BaseKeywords,
            Mechanics = (CardMechanic)m.Mechanics,
            TechLevel = m.Tier,
            ScriptData1 = m.Script1,
            ScriptData2 = m.Script2,
            CreatorCardId = m.Creator ?? string.Empty,
            Enchantments = m.Enchantments.ToArray(),
            EnchantmentValues = m.EnchantmentValues.Select(ToSim).ToArray(),
            FriendlyDeathsBeforeCapture = m.FriendlyDeaths,
            Remembered = m.Remembered.Select(ToSim).ToArray(),
        };
    }
}

/// <summary>Data of one <c>bg_combat</c> record (camelCase on the wire); see <see cref="SourceCaptureCombat"/>.</summary>
/// <param name="Fight">The combat's number in the game (0 = the first fight), growing within a capture.</param>
/// <param name="FirstAttacker">Reference of the minion that attacked first; 0 when unknown.</param>
/// <param name="Result">W, T or L from the player's side.</param>
/// <param name="Transcript">
/// The fight step by step (<c>SourceCaptureCombat.Transcript.cs</c>), written field by field from its allowlist; null when
/// the fight has none or what of it may leave is outside the rules.
/// </param>
internal sealed record SourceCombat(
    int Fight, int Turn, bool? OursFirst, int FirstAttacker, int DamageCap, bool CountDamage, SourceCombatSide Ours, SourceCombatSide Theirs,
    IReadOnlyList<SourceCombatFact> Facts, string Result, int DamageTaken, int DamageDealt, SourceCombatOdds? Odds, JsonElement? Transcript);

/// <summary>
/// One side: hero, hero power, player-level cards, tier, hero health with armor, allowlisted values, the player's own
/// enchantments (always empty for the opponent), the lobby's tribes, the minions that died at Start of Combat, the board.
/// </summary>
internal sealed record SourceCombatSide(
    string? Hero, string? HeroPower, IReadOnlyList<string> Extras, int Tier, int Health, int Turn, IReadOnlyDictionary<string, int> Tags,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CardTags, IReadOnlyList<SourceCombatEnchantment> PlayerEnchantments, int LobbyTribes,
    IReadOnlyList<SourceCombatMinion> Deaths, IReadOnlyList<SourceCombatMinion> Minions);

/// <summary>
/// A minion as the simulator gets it; <paramref name="Keywords"/> are <see cref="Keyword"/> bits without Windfury,
/// <paramref name="EnchantmentValues"/> the numbers its enchantments carry, <paramref name="FriendlyDeaths"/> the friendly
/// minions that died at Start of Combat while it was on the board.
/// </summary>
internal sealed record SourceCombatMinion(
    int Entity, string? Card, bool Golden, int Attack, int Health, int MaxHealth, int BaseHealth, int RebornAttack, int Keywords, int Attacks,
    int BaseKeywords, int Tribes, int Mechanics, int Tier, int Script1, int Script2, string? Creator, IReadOnlyList<string> Enchantments,
    IReadOnlyList<SourceCombatEnchantment> EnchantmentValues, int FriendlyDeaths, IReadOnlyList<SourceCombatMinion> Remembered, bool Unsimulated);

/// <summary>
/// An enchantment with the numbers it carries at capture (<see cref="SimEnchantment"/>); <paramref name="CreatorEntity"/> is
/// the reference of what made it when the record holds that entity, else 0.
/// </summary>
internal sealed record SourceCombatEnchantment(string Card, int Script1, int Script2, int Attack, int Health, int CreatorEntity);

/// <summary>What the log said about a board minion: its card id as logged and its effect markers.</summary>
internal sealed record SourceCombatFact(int Entity, string Card, IReadOnlyList<string> Markers);

/// <summary>The odds the app showed for the fight when the game ended, rounded to 4 decimals.</summary>
internal sealed record SourceCombatOdds(double Win, double Tie, double Loss, double? LethalTaken, double? LethalDealt, int Unsimulated, int Runs);

/// <summary>A <c>bg_combat</c> record read back as the simulator's input, what happened, the odds shown and the transcript.</summary>
internal sealed record SourceCombatInput(
    int Fight, int Turn, BattlegroundsTracker.CombatCapture Capture, int DamageCap, bool CountDamage,
    IReadOnlyList<BattlegroundsTracker.MinionFacts> Facts, string Result, int DamageTaken, int DamageDealt, CombatOdds? Odds,
    BattlegroundsTracker.CombatTranscript? Transcript);

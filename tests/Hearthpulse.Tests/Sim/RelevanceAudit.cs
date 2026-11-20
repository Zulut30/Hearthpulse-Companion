using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// An independent reading of card text for combat signs, to check the relevance classification (task
/// sim-all-mechanics, AUDIT2). It shares no code with <see cref="CombatRelevance"/>, the generators in tools/effects or the
/// hand tables: its patterns are written from the Battlegrounds rules (what happens after the boards are captured at the
/// first attack) and over-report on purpose. Every card the matrix calls no_effect or resolved that shows a sign must have
/// a written reason in <see cref="RelevanceAuditReasons"/>.
/// </summary>
internal static partial class RelevanceAudit
{
    /// <summary>One matrix row as tools/Hearthpulse.Bench simmatrix wrote it into the audit fixture.</summary>
    internal sealed record Row(CoverageKind Kind, string Id, string ClassifyId, string Name, bool InPool, string Status, string Text,
                               string GoldenText, IReadOnlyList<string> Mechanics);

    /// <summary>A hero of the data with its starting hero power first, then every variant.</summary>
    internal sealed record Hero(string Id, string Name, bool InPool, IReadOnlyList<string> Powers);

    private static readonly Lazy<(IReadOnlyList<Row> Rows, IReadOnlyList<Hero> Heroes)> Data = new(Load);

    internal static IReadOnlyList<Row> Rows => Data.Value.Rows;

    internal static IReadOnlyList<Hero> Heroes => Data.Value.Heroes;

    /// <summary>Combat-time triggers a sentence can carry; the first match names the sign.</summary>
    private static readonly (string Sign, Regex Pattern)[] Triggers =
    [
        ("deathrattle", Rx(@"\bDeathrattle\b[^:]*:")),
        ("avenge", Rx(@"\bAvenge \(\d+\)")),
        ("rally", Rx(@"\bRally\b[^:]*:")),
        ("gains_stats", Rx(@"\b(whenever|after|when)\b[^.]*\bgains? (Attack|Health|stats|\+)")),
        ("attacks", Rx(@"\b(whenever|after|when|each time|the first time)\b[^.]*(\b(attacks|is attacked)\b|\battack(,|\.|$| immediately))")),
        ("dies", Rx(@"\b(whenever|after|when|each time)\b[^.]*\b(dies|die|is destroyed|are destroyed)\b")),
        ("summoned", Rx(@"\b(whenever|after|when)\b[^.]*\b(is summoned|are summoned|you summon|summons)\b")),
        ("damage", Rx(@"\b(whenever|after|when)\b[^.]*\b(takes? damage|deals? damage|is damaged)\b")),
        ("divine_shield_lost", Rx(@"\bloses? (a |their |its )?Divine Shield\b")),
        ("kills", Rx(@"\b(kills?|overkill|excess damage)\b")),
        ("reborn", Rx(@"\bis Reborn\b|\bReborn minions?\b")),
        ("triggers_effects", Rx(@"\b(trigger|triggers|triggered) (a |an |your )?(friendly )?(Deathrattle|Rally|Avenge)")),
        ("card_to_hand", Rx(@"\b(whenever|after|when)\b[^.]*\b(a card is|cards are) added to your hand\b")),
        ("combat", Rx(@"\bcombats?\b")),
        ("space", Rx(@"\bwhen you have space\b")),
    ];

    /// <summary>Lasting rules a sentence can set (auras, doubled triggers, damage to heroes).</summary>
    private static readonly (string Sign, Regex Pattern)[] Rules =
    [
        ("aura", Rx(@"^(Your|Adjacent|Other|All|Friendly)\b[^.]*\b(have|has)\b\s*(\+|Venomous|Poisonous|Cleave|Windfury|Taunt|Divine Shield|Stealth|Reborn)")),
        ("extra_triggers", Rx(@"\b(Deathrattles?|Rall(y|ies)|Avenges?|Battlecries|Start of Combat effects?)\b[^.]*\b(trigger|triggers) (an extra time|twice)")),
        ("keyword_rule", Rx(@"^Your\b[^.]*\b(Divine Shields?|Reborn|Venomous|Windfury|Taunt)\b")),
        ("hero_damage", Rx(@"\bdamage to (the |your |their )?(enemy |opposing )?heroe?s?\b|\b(your|enemy) hero (takes|would take)\b")),
    ];

    /// <summary>What a Start of Combat sentence gives that acts later in the fight (a granted trigger, a lasting rule).</summary>
    private static readonly Regex StartOfCombatGrant =
        Rx(@"\b(Deathrattle|Avenge|Rally)\b|rest of (this|the) combat|\b(whenever|after|when)\b|\bthis combat\b");

    private static readonly Regex StartOfCombatHead = Rx(@"^.*?Start of Combat[^:]*:");

    /// <summary>Mechanics tags that mean a trigger acting in combat whatever the text says.</summary>
    private static readonly string[] CombatMechanics = ["DEATHRATTLE", "AVENGE", "BACON_RALLY", "OVERKILL"];

    /// <summary>
    /// The combat signs of a row: from the normal and golden text sentence by sentence, from the card's mechanics, and a
    /// Start of Combat text on a row the matrix does not call resolved. Each sign is "name: sentence".
    /// </summary>
    internal static IReadOnlyList<string> Signs(Row row)
    {
        var signs = new List<string>();
        signs.AddRange(TextSigns(row.Text));
        signs.AddRange(TextSigns(row.GoldenText).Select(s => "golden " + s));
        signs.AddRange(row.Mechanics.Where(m => CombatMechanics.Contains(m)).Select(m => "mechanic: " + m));
        if (row.Status == "no_effect" && Normalize(row.Text).Contains("Start of Combat", StringComparison.OrdinalIgnoreCase))
        {
            signs.Add("start_of_combat on no_effect");
        }

        return signs.Distinct(StringComparer.Ordinal).ToList();
    }

    internal static IReadOnlyList<string> TextSigns(string? text)
    {
        var signs = new List<string>();
        foreach (var sentence in Sentences(Normalize(text)))
        {
            if (sentence.Contains("Start of Combat", StringComparison.OrdinalIgnoreCase))
            {
                if (StartOfCombatGrant.IsMatch(StartOfCombatHead.Replace(sentence, string.Empty)))
                {
                    signs.Add("start_of_combat_grant: " + sentence);
                }

                continue;
            }

            var sign = Triggers.Concat(Rules).FirstOrDefault(t => t.Pattern.IsMatch(sentence)).Sign;
            if (sign is not null)
            {
                signs.Add($"{sign}: {sentence}");
            }
        }

        return signs;
    }

    internal static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var at = text.IndexOf('@', StringComparison.Ordinal);
        var t = at >= 0 ? text[..at] : text;
        t = TagRx().Replace(t, string.Empty).Replace("[x]", string.Empty, StringComparison.Ordinal).Replace('_', ' ').Replace('\n', ' ');
        t = LeftRx().Replace(t, string.Empty);
        return SpaceRx().Replace(t, " ").Trim();
    }

    private static IEnumerable<string> Sentences(string text) =>
        SentenceRx().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static (IReadOnlyList<Row>, IReadOnlyList<Hero>) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SimAudit", "matrix-cards.json.gz");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Select(e => new Row(
            Enum.Parse<CoverageKind>(e.GetProperty("kind").GetString()!),
            e.GetProperty("id").GetString()!,
            e.GetProperty("classifyId").GetString()!,
            e.GetProperty("name").GetString()!,
            e.GetProperty("inPool").GetBoolean(),
            e.GetProperty("status").GetString()!,
            e.GetProperty("text").GetString()!,
            e.GetProperty("goldenText").GetString()!,
            e.GetProperty("mechanics").EnumerateArray().Select(m => m.GetString()!).ToList())).ToList();
        var heroes = doc.RootElement.GetProperty("heroes").EnumerateArray().Select(e => new Hero(
            e.GetProperty("id").GetString()!,
            e.GetProperty("name").GetString()!,
            e.GetProperty("inPool").GetBoolean(),
            e.GetProperty("powers").EnumerateArray().Select(p => p.GetString()!).ToList())).ToList();
        return (rows, heroes);
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"\((\d+ left!|[^)]*left!\)?)\)")]
    private static partial Regex LeftRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    [GeneratedRegex(@"(?<=[.!])\s+")]
    private static partial Regex SentenceRx();
}

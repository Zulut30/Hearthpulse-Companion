using Hearthpulse.Core.Arena;

namespace Hearthpulse.Core.Api;

/// <summary>
/// A made-up data summary for the render check (<c>HEARTHPULSE_RENDER_ONLY=datahealth</c>): every section fresh, the
/// Arena class numbers late by days and the matchups waiting for their origin, so one render shows each wording.
/// </summary>
public static class DataHealthScene
{
    /// <summary>When the scene's summary was "read".</summary>
    public static DateTime CheckedAt { get; } = new(2026, 11, 15, 21, 30, 0, DateTimeKind.Utc);

    private static DateTime Collected => CheckedAt.AddHours(-4.5);

    /// <summary>When the late Arena numbers were collected.</summary>
    public static DateTime Late => CheckedAt.AddHours(-90);

    public static DataHealthReport Report() => new(
        DataSections.All.SelectMany(DataSections.Sources).Distinct(StringComparer.Ordinal).Select(id => id switch
        {
            "hsreplay_arena_class_pages_firecrawl" => new DataSourceHealth(id, DataFreshness.Stale, 90, 30, Late, CheckedAt.AddHours(1)),
            "hsguru_matchups" => new DataSourceHealth(id, DataFreshness.Pending, 20, 6, CheckedAt.AddHours(-20), CheckedAt.AddHours(1)),
            _ => new DataSourceHealth(id, DataFreshness.Fresh, 4.5, 30, Collected, CheckedAt.AddHours(1)),
        }),
        CheckedAt);

    /// <summary>Class results for the Arena page's community card, collected at <see cref="Late"/>.</summary>
    public static ArenaMeta ArenaMeta() => new(
        [],
        [
            new ArenaClassStat("DEATHKNIGHT", 0.534, 0.14, 0.071, 48200),
            new ArenaClassStat("MAGE", 0.521, 0.12, 0.062, 41100),
            new ArenaClassStat("PALADIN", 0.508, 0.11, 0.055, 37900),
            new ArenaClassStat("HUNTER", 0.497, 0.09, 0.049, 30400),
            new ArenaClassStat("PRIEST", 0.481, 0.07, 0.041, 24800),
        ],
        Late);
}

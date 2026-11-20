using Hearthpulse.Core;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

// What both UIs show of the player's month or year (docs/wrapped-page.md), made from Core's WrappedReport in the
// current language. Nothing here names an opponent.

/// <summary>
/// A worked-out recap: the hero card (one big number and the three under it) and the slides. The slides about games
/// are left out when the period has none (<see cref="HasGames"/>); achievements show either way.
/// </summary>
/// <param name="Number">The hero number: games, or completed achievements in a period without games.</param>
/// <param name="Caption">"games in November" / "achievements in 2026".</param>
/// <param name="Delta">"+34 vs last month", "Same as last month", "No games last month"; empty without games.</param>
/// <param name="DeltaGlyph">An arrow up or down, or a dash.</param>
/// <param name="DeltaSign">1 more games, -1 fewer, 0 the same or no comparison (colours the line).</param>
/// <param name="DeltaTip">The previous period and its games.</param>
public sealed record RecapView(
    string Number, string Caption,
    string Delta, string DeltaGlyph, int DeltaSign, string DeltaTip,
    bool HasGames,
    string Hours, string HoursTip,
    string WinRate, string WinRateDetail,
    string Streak, string StreakLabel,
    IReadOnlyList<RecapTile> Favourites,
    string TopMode, IReadOnlyList<RecapModeRow> Modes,
    string Day, IReadOnlyList<RecapBar> Days, string DaysName,
    string Hour, IReadOnlyList<RecapBar> HoursOfDay, string HoursName,
    string Longest, string LongestDetail,
    IReadOnlyList<StatTile> Battlegrounds,
    string AchievementsCount, string AchievementsPoints, IReadOnlyList<AchievementRecentRow> Achievements,
    string AchievementsMore, string AchievementsNote) : IImmutableView
{
    public bool HasDelta => Delta.Length > 0;

    public bool HasFavourites => Favourites.Count > 0;

    public bool HasLongest => Longest.Length > 0;

    public bool HasBattlegrounds => Battlegrounds.Count > 0;

    public bool HasAchievements => Achievements.Count > 0;

    public bool HasAchievementsMore => AchievementsMore.Length > 0;

    public bool HasAchievementsNote => AchievementsNote.Length > 0;
}

/// <summary>A tile of "Favourites": a portrait, what it is, the name and how many games.</summary>
/// <param name="CardId">The portrait's card (the class's hero, the deck's class hero, the Battlegrounds hero).</param>
public sealed record RecapTile(string Label, string Value, string Detail, string CardId) : IImmutableView;

/// <summary>A mode of the period: its name, its share of the most played mode, games, time and wins–losses ("—" without).</summary>
/// <param name="Name">The row as one sentence, for a screen reader and the tooltip.</param>
public sealed record RecapModeRow(string Title, double Share, string Games, string Time, string Record, string Name) : IImmutableView;

/// <summary>
/// A bar of "When you play": its label under it ("Mon", "21"; empty for most hours), the count over it (days only, not
/// zero; the views keep the count's line either way, so every bar stands on the same base), its height out of
/// <see cref="MaxHeight"/>, the busiest one marked, and the line its tooltip says.
/// </summary>
public sealed record RecapBar(string Label, string Count, double Height, bool Top, string Tip) : IImmutableView
{
    /// <summary>The tallest bar's height in DIP: the chart's bar area.</summary>
    public const double MaxHeight = 96;
}

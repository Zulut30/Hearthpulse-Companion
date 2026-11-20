using System.Globalization;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Share;

namespace Hearthpulse.Core.Stats;

/// <summary>The length of a "Wrapped" period.</summary>
public enum WrappedSpan
{
    Month,
    Year,
}

/// <summary>
/// A calendar month or year in local time. <see cref="Month"/> is 1..12 for a month and 0 for a year; values out of
/// range are clamped when the bounds are worked out.
/// </summary>
public readonly record struct WrappedPeriod(WrappedSpan Span, int Year, int Month)
{
    /// <summary>Local midnight of the period's first day.</summary>
    public DateTime From => Span == WrappedSpan.Year
        ? new DateTime(SafeYear, 1, 1, 0, 0, 0, DateTimeKind.Local)
        : new DateTime(SafeYear, SafeMonth, 1, 0, 0, 0, DateTimeKind.Local);

    /// <summary>Local midnight of the first day after the period (not included).</summary>
    public DateTime To => Span == WrappedSpan.Year
        ? (SafeYear < 9999 ? new DateTime(SafeYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Local) : DateTime.MaxValue)
        : (SafeYear < 9999 || SafeMonth < 12 ? From.AddMonths(1) : DateTime.MaxValue);

    /// <summary>The period of the same length just before.</summary>
    public WrappedPeriod Previous => Span == WrappedSpan.Year
        ? new WrappedPeriod(WrappedSpan.Year, Math.Max(1, SafeYear - 1), 0)
        : SafeMonth == 1 ? new WrappedPeriod(WrappedSpan.Month, Math.Max(1, SafeYear - 1), 12) : new WrappedPeriod(WrappedSpan.Month, SafeYear, SafeMonth - 1);

    /// <summary>The period of the same length just after.</summary>
    public WrappedPeriod Next => Span == WrappedSpan.Year
        ? new WrappedPeriod(WrappedSpan.Year, Math.Min(9999, SafeYear + 1), 0)
        : SafeMonth == 12 ? new WrappedPeriod(WrappedSpan.Month, Math.Min(9999, SafeYear + 1), 1) : new WrappedPeriod(WrappedSpan.Month, SafeYear, SafeMonth + 1);

    private int SafeYear => Math.Clamp(Year, 1, 9999);

    private int SafeMonth => Math.Clamp(Month, 1, 12);

    /// <summary>The month <paramref name="local"/> falls in.</summary>
    public static WrappedPeriod MonthOf(DateTime local) => new(WrappedSpan.Month, local.Year, local.Month);

    /// <summary>The year <paramref name="local"/> falls in.</summary>
    public static WrappedPeriod YearOf(DateTime local) => new(WrappedSpan.Year, local.Year, 0);

    /// <summary>The period of <paramref name="span"/> that <paramref name="local"/> falls in.</summary>
    public static WrappedPeriod Of(WrappedSpan span, DateTime local) => span == WrappedSpan.Year ? YearOf(local) : MonthOf(local);

    /// <summary>The period contains the local time.</summary>
    public bool Contains(DateTime local) => local >= From && local < To;

    /// <summary>
    /// "2026-11" for a month, "2026" for a year: a machine form, not for the interface. Written by hand: the record's own
    /// would print <see cref="Previous"/> and <see cref="Next"/>, periods themselves, without end.
    /// </summary>
    public override string ToString() => Span == WrappedSpan.Year
        ? SafeYear.ToString("D4", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{SafeYear:D4}-{SafeMonth:D2}");
}

/// <summary>Games, time and wins/losses of a period. Time counts at most three hours a game; wins and losses only outside Battlegrounds and Mercenaries.</summary>
public sealed record WrappedTotals(int Games, TimeSpan TimePlayed, WinLoss Record);

/// <summary>One mode of a period (<see cref="GameRecord.Mode"/>); Battlegrounds, Duos and Mercenaries have no wins or losses.</summary>
public sealed record WrappedMode(string Mode, int Games, TimeSpan TimePlayed, WinLoss Record);

/// <summary>One game, without anything about the opponent.</summary>
public sealed record WrappedGame(DateTime StartedAt, TimeSpan Duration, string Mode, string HeroCardId, string PlayerClass, string Result, int Placement);

/// <summary>Battlegrounds solo games of a period.</summary>
/// <param name="Placed">Games with a known final place.</param>
/// <param name="AveragePlacement">The mean final place of the placed games; null without any.</param>
public sealed record WrappedBattlegrounds(int Games, int Placed, int Firsts, int Top4, double? AveragePlacement);

/// <summary>
/// The player's month or year (docs/collection-cosmetics.md, "Wrapped"), from their own history and achievements. No
/// opponent names anywhere.
/// </summary>
/// <param name="Previous">The totals of <see cref="WrappedPeriod.Previous"/>, for "+N against last month".</param>
/// <param name="FavouriteClass">The class with the most games outside Battlegrounds and Mercenaries.</param>
/// <param name="FavouriteDeck">The deck name with the most of those games (<see cref="DeckName.Display"/>).</param>
/// <param name="FavouriteHero">The Battlegrounds solo base hero picked most.</param>
/// <param name="BestWinStreak">The most wins in a row outside Battlegrounds and Mercenaries.</param>
/// <param name="LongestGame">The longest game of at most three hours (a longer one is a client left open).</param>
/// <param name="GamesByWeekday">Games by local weekday, Monday first.</param>
/// <param name="GamesByHour">Games by local hour of their start.</param>
/// <param name="Achievements">Tiers completed in the period (local time), newest first.</param>
public sealed record WrappedReport(
    WrappedPeriod Period, WrappedTotals Totals, WrappedTotals Previous,
    IReadOnlyList<WrappedMode> Modes,
    string? FavouriteClass, int FavouriteClassGames,
    string? FavouriteDeck, string? FavouriteDeckClass, int FavouriteDeckGames,
    string? FavouriteHero, int FavouriteHeroGames,
    int BestWinStreak, WrappedGame? LongestGame,
    IReadOnlyList<int> GamesByWeekday,
    IReadOnlyList<int> GamesByHour,
    WrappedBattlegrounds? Battlegrounds,
    IReadOnlyList<CompletedAchievement> Achievements, int AchievementPoints)
{
    /// <summary>Nothing to show: no game and no completed achievement in the period.</summary>
    public bool IsEmpty => Totals.Games == 0 && Achievements.Count == 0;
}

/// <summary>
/// Builds <see cref="WrappedReport"/>: pure. Counts as <see cref="YearInReview"/> does (abandoned games left out, three
/// hours at most a game, wins and losses without Battlegrounds and Mercenaries), so a year here and the year in review
/// agree. Times are local: a game's start as the history keeps it (a UTC time is turned into local first), an
/// achievement's completion from UTC.
/// </summary>
public static class Wrapped
{
    /// <summary>The most time one game counts for.</summary>
    public static readonly TimeSpan MaxGame = TimeSpan.FromHours(3);

    /// <param name="games">Games of any time and order (the view reads <c>HistoryStore.Since(period.Previous.From)</c>); the period's and the previous period's are counted.</param>
    /// <param name="achievements">The book whose completions of the period are listed; null for none.</param>
    /// <param name="baseHero">Maps a Battlegrounds hero skin to its hero; identity when null.</param>
    public static WrappedReport Build(IEnumerable<GameRecord> games, WrappedPeriod period,
                                      AchievementBook? achievements = null, Func<string, string>? baseHero = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        var counted = games.Where(g => !g.IsAbandoned).ToList();
        var all = counted.Where(g => period.Contains(Local(g.StartedAt))).OrderBy(g => g.StartedAt).ToList();
        var previous = counted.Where(g => period.Previous.Contains(Local(g.StartedAt))).ToList();
        var constructed = all.Where(Decided).ToList();
        var bgSolo = all.Where(g => g.Mode == "Battlegrounds").ToList();
        var hero = baseHero ?? (h => h);

        var modes = all.GroupBy(g => g.Mode, StringComparer.Ordinal)
            .Select(g => new WrappedMode(g.Key, g.Count(), Time(g), Decided(g.First()) ? WinLoss.Of(g) : new WinLoss(g.Count(), 0, 0)))
            .OrderByDescending(m => m.Games)
            .ThenBy(m => m.Mode, StringComparer.Ordinal)
            .ToList();
        var favouriteClass = Top(constructed.Where(g => !string.IsNullOrEmpty(g.PlayerClass) && g.PlayerClass != "NEUTRAL"), g => g.PlayerClass);
        var favouriteDeck = constructed.Where(g => !string.IsNullOrWhiteSpace(g.DeckName))
            .GroupBy(g => (Name: g.DeckName!.Trim(), g.PlayerClass))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name, StringComparer.Ordinal)
            .Select(g => ((string Name, string Class, int Games)?)(g.Key.Name, g.Key.PlayerClass, g.Count()))
            .FirstOrDefault();
        var favouriteHero = Top(bgSolo.Where(g => !string.IsNullOrEmpty(g.HeroCardId)), g => hero(g.HeroCardId));
        var longest = all.Where(g => g.Duration > TimeSpan.Zero && g.Duration <= MaxGame)
            .OrderByDescending(g => g.Duration)
            .ThenBy(g => g.StartedAt)
            .Select(g => new WrappedGame(Local(g.StartedAt), g.Duration, g.Mode, g.HeroCardId, g.PlayerClass, g.Result, g.Placement))
            .FirstOrDefault();

        var weekdays = new int[7];
        var hours = new int[24];
        foreach (var game in all)
        {
            var start = Local(game.StartedAt);
            weekdays[((int)start.DayOfWeek + 6) % 7]++;
            hours[start.Hour]++;
        }

        var placed = bgSolo.Where(g => g.Placement > 0).ToList();
        var battlegrounds = bgSolo.Count == 0 ? null : new WrappedBattlegrounds(
            bgSolo.Count, placed.Count, placed.Count(g => g.Placement == 1), placed.Count(g => g.Placement <= 4),
            placed.Count == 0 ? null : placed.Average(g => g.Placement));

        var completed = achievements?.CompletedBetween(period.From.ToUniversalTime(), period.To == DateTime.MaxValue ? DateTime.MaxValue : period.To.ToUniversalTime())
            ?? [];

        return new WrappedReport(
            period,
            Totals(all),
            Totals(previous),
            modes,
            favouriteClass?.Key, favouriteClass?.Count ?? 0,
            favouriteDeck is { } d ? DeckName.Display(d.Name) : null, favouriteDeck?.Class, favouriteDeck?.Games ?? 0,
            favouriteHero?.Key, favouriteHero?.Count ?? 0,
            SessionRecap.LongestWinStreak(constructed),
            longest,
            weekdays,
            hours,
            battlegrounds,
            completed,
            completed.Sum(c => c.Achievement.Points));
    }

    /// <summary>
    /// The first and last periods there is anything for: from the period of the first game or the first dated completed
    /// achievement, whichever is earlier, to the period of <paramref name="today"/>. With nothing, both are today's.
    /// </summary>
    public static (WrappedPeriod First, WrappedPeriod Last) Range(IEnumerable<GameRecord> games, AchievementBook? achievements,
                                                                  WrappedSpan span, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(games);
        var last = WrappedPeriod.Of(span, Local(today));
        var firstGame = games.Where(g => !g.IsAbandoned).Select(g => (DateTime?)Local(g.StartedAt)).Min();
        var firstAchievement = achievements?.States.Values
            .Where(s => s.Status == AchievementStatus.Completed && s.CompletedAt is not null && achievements.Achievements.ContainsKey(s.Id))
            .Select(s => (DateTime?)s.CompletedAt!.Value.ToLocalTime())
            .Min();
        var first = (firstGame, firstAchievement) switch
        {
            ({ } g, { } a) => g < a ? g : a,
            ({ } g, null) => g,
            (null, { } a) => a,
            _ => (DateTime?)null,
        };
        var firstPeriod = first is { } f ? WrappedPeriod.Of(span, f) : last;
        return (firstPeriod.From > last.From ? last : firstPeriod, last);
    }

    /// <summary>A game's start as local time: the history keeps local times; one marked UTC is turned into local.</summary>
    internal static DateTime Local(DateTime time) => time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;

    /// <summary>A game whose result counts as a win or a loss (not Battlegrounds or Mercenaries).</summary>
    private static bool Decided(GameRecord game) => !game.IsBattlegrounds && game.Mode != "Mercenaries";

    private static WrappedTotals Totals(IReadOnlyCollection<GameRecord> games) => new(games.Count, Time(games), WinLoss.Of(games.Where(Decided)));

    private static TimeSpan Time(IEnumerable<GameRecord> games) =>
        TimeSpan.FromTicks(games.Sum(g => Math.Max(0, Math.Min(g.Duration.Ticks, MaxGame.Ticks))));

    private static (string Key, int Count)? Top(IEnumerable<GameRecord> games, Func<GameRecord, string> key) =>
        games.GroupBy(key, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => ((string, int)?)(g.Key, g.Count()))
            .FirstOrDefault();
}

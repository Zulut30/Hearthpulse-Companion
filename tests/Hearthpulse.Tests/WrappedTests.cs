using System.Globalization;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Share;
using Hearthpulse.Core.Stats;

namespace Hearthpulse.Tests;

/// <summary>
/// "Wrapped" for a month and a year (<see cref="Wrapped"/>, docs/collection-cosmetics.md): the counts follow the year
/// in review's rules, weekdays and hours are local with Monday first, Battlegrounds solo has its own block, the previous
/// period of the same length is summed, and achievements fall into a period by their local completion date. Made-up
/// history in local time, so the cases hold in any time zone.
/// </summary>
public sealed class WrappedTests
{
    private static readonly WrappedPeriod November = new(WrappedSpan.Month, 2026, 11);

    private static int _key;

    private static DateTime At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Local);

    private static GameRecord Game(string mode, DateTime start, double minutes, string result = "Win", string cls = "MAGE",
                                   string? deck = null, string hero = "", int placement = 0) => new()
    {
        Key = "wrapped-" + Interlocked.Increment(ref _key).ToString(CultureInfo.InvariantCulture),
        StartedAt = start,
        EndedAt = start.AddMinutes(minutes),
        Mode = mode,
        Result = result,
        PlayerClass = cls,
        DeckName = deck,
        HeroCardId = hero,
        Placement = placement,
        OpponentName = "Opponent#1234",
    };

    private static string BaseHero(string hero) => hero == "BG_SKIN_A" ? "BG_HERO_A" : hero;

    /// <summary>November 2026 (Sunday the 1st), the October before it and a December game.</summary>
    private static readonly GameRecord[] History =
    [
        Game("Ranked", At(11, 2, 10), 20, "Win", deck: "Test Mage"),                     // Monday
        Game("Ranked", At(11, 3, 21), 25, "Win", deck: "Test Mage"),                     // Tuesday
        Game("Ranked", At(11, 3, 22), 15, "Loss", deck: "Test Mage"),
        Game("Casual", At(11, 4, 9), 10, "Win", cls: "ROGUE"),                          // Wednesday
        Game("Arena", At(11, 5, 23), 12, "Loss", cls: "PRIEST"),                         // Thursday
        Game("Mercenaries", At(11, 6, 18), 10, "Win", cls: string.Empty),                // Friday
        Game("Battlegrounds", At(11, 7, 14), 40, "Win", cls: string.Empty, hero: "BG_SKIN_A", placement: 1),   // Saturday
        Game("Battlegrounds", At(11, 7, 15), 30, "Win", cls: string.Empty, hero: "BG_HERO_A", placement: 3),
        Game("Battlegrounds", At(11, 8, 20), 20, "Loss", cls: string.Empty, hero: "BG_HERO_B", placement: 6),  // Sunday
        Game("Duos", At(11, 8, 21), 25, "Win", cls: string.Empty, hero: "BG_HERO_B", placement: 2),
        Game("Ranked", At(11, 9, 12), 5, GameRecord.AbandonedResult, deck: "Test Mage"),
        Game("Ranked", At(11, 10, 8), 300, "Win", cls: "WARRIOR", deck: "Big Warrior"),  // Tuesday, a client left open
        Game("Ranked", At(10, 15, 12), 30, "Win", deck: "Test Mage"),
        Game("Battlegrounds", At(10, 20, 12), 50, "Loss", cls: string.Empty, hero: "BG_HERO_B", placement: 4),
        Game("Ranked", At(12, 1, 12), 30, "Win", cls: "DRUID"),
    ];

    [Fact]
    public void A_month_counts_games_time_and_results_by_the_year_in_review_rules()
    {
        var report = Wrapped.Build(History, November, baseHero: BaseHero);

        Assert.Equal(11, report.Totals.Games);
        // Every game up to three hours; the five-hour one counts three.
        Assert.Equal(TimeSpan.FromMinutes(20 + 25 + 15 + 10 + 12 + 10 + 40 + 30 + 20 + 25 + 180), report.Totals.TimePlayed);
        // Wins and losses outside Battlegrounds, Duos and Mercenaries.
        Assert.Equal(new WinLoss(6, 4, 2), report.Totals.Record);
        Assert.False(report.IsEmpty);
    }

    [Fact]
    public void Modes_have_games_time_and_their_own_results()
    {
        var modes = Wrapped.Build(History, November).Modes;

        Assert.Equal(["Ranked", "Battlegrounds", "Arena", "Casual", "Duos", "Mercenaries"], modes.Select(m => m.Mode));
        Assert.Equal(new WrappedMode("Ranked", 4, TimeSpan.FromMinutes(240), new WinLoss(4, 3, 1)), modes[0]);
        Assert.Equal(new WrappedMode("Battlegrounds", 3, TimeSpan.FromMinutes(90), new WinLoss(3, 0, 0)), modes[1]);
        Assert.Equal(new WinLoss(1, 0, 0), modes.Single(m => m.Mode == "Mercenaries").Record);
        Assert.Equal(new WinLoss(1, 0, 0), modes.Single(m => m.Mode == "Duos").Record);
    }

    [Fact]
    public void Favourites_streak_and_the_longest_game_of_at_most_three_hours()
    {
        var report = Wrapped.Build(History, November, baseHero: BaseHero);

        Assert.Equal(("MAGE", 3), (report.FavouriteClass, report.FavouriteClassGames));
        Assert.Equal(("Test Mage", "MAGE", 3), (report.FavouriteDeck, report.FavouriteDeckClass, report.FavouriteDeckGames));
        Assert.Equal(("BG_HERO_A", 2), (report.FavouriteHero, report.FavouriteHeroGames));
        Assert.Equal(2, report.BestWinStreak);
        Assert.NotNull(report.LongestGame);
        Assert.Equal(new WrappedGame(At(11, 7, 14), TimeSpan.FromMinutes(40), "Battlegrounds", "BG_SKIN_A", string.Empty, "Win", 1), report.LongestGame);
    }

    [Fact]
    public void A_deck_name_is_written_as_the_interface_writes_it()
    {
        GameRecord[] games = [Game("Ranked", At(11, 2, 10), 20, deck: "Mage2"), Game("Ranked", At(11, 2, 11), 20, deck: "Mage2")];

        Assert.Equal(DeckName.Display("Mage2"), Wrapped.Build(games, November).FavouriteDeck);
    }

    [Fact]
    public void Weekdays_start_on_monday_and_hours_are_local()
    {
        var report = Wrapped.Build(History, November);

        Assert.Equal([1, 3, 1, 1, 1, 2, 2], report.GamesByWeekday);
        Assert.Equal(24, report.GamesByHour.Count);
        Assert.Equal(2, report.GamesByHour[21]);
        Assert.Equal(1, report.GamesByHour[8]);
        Assert.Equal(0, report.GamesByHour[12]);
        Assert.Equal(11, report.GamesByHour.Sum());
    }

    [Fact]
    public void A_game_kept_in_utc_is_counted_by_its_local_time()
    {
        var local = At(11, 2, 10);
        var report = Wrapped.Build([Game("Ranked", local.ToUniversalTime(), 20)], November);

        Assert.Equal(1, report.GamesByHour[10]);
        Assert.Equal(1, report.GamesByWeekday[0]);
    }

    [Fact]
    public void Battlegrounds_solo_has_firsts_top_four_and_average_place()
    {
        var bg = Wrapped.Build(History, November).Battlegrounds;

        Assert.NotNull(bg);
        Assert.Equal((3, 3, 1, 2), (bg.Games, bg.Placed, bg.Firsts, bg.Top4));
        Assert.Equal(10 / 3.0, bg.AveragePlacement!.Value, 6);
        Assert.Null(Wrapped.Build([Game("Duos", At(11, 2, 10), 20, placement: 1)], November).Battlegrounds);
        var unplaced = Wrapped.Build([Game("Battlegrounds", At(11, 2, 10), 20)], November).Battlegrounds!;
        Assert.Equal((1, 0, null), (unplaced.Games, unplaced.Placed, unplaced.AveragePlacement));
    }

    [Fact]
    public void The_previous_period_of_the_same_length_is_summed()
    {
        var month = Wrapped.Build(History, November);
        var year = Wrapped.Build(History, WrappedPeriod.YearOf(At(6, 1, 12)));

        Assert.Equal(new WrappedTotals(2, TimeSpan.FromMinutes(80), new WinLoss(1, 1, 0)), month.Previous);
        Assert.Equal(new WrappedTotals(0, TimeSpan.Zero, WinLoss.Empty), year.Previous);
        Assert.Equal(14, year.Totals.Games);
    }

    [Fact]
    public void A_year_agrees_with_the_year_in_review()
    {
        var wrapped = Wrapped.Build(History, WrappedPeriod.YearOf(At(3, 1, 12)), baseHero: BaseHero);
        var review = YearInReview.Build(History, 2026, baseHero: BaseHero);

        Assert.Equal(review.Games, wrapped.Totals.Games);
        Assert.Equal(review.TimePlayed, wrapped.Totals.TimePlayed);
        Assert.Equal(review.Record, wrapped.Totals.Record);
        Assert.Equal((review.FavouriteClass, review.FavouriteClassGames), (wrapped.FavouriteClass, wrapped.FavouriteClassGames));
        Assert.Equal((review.FavouriteDeck, review.FavouriteDeckClass, review.FavouriteDeckGames),
            (wrapped.FavouriteDeck, wrapped.FavouriteDeckClass, wrapped.FavouriteDeckGames));
        Assert.Equal((review.FavouriteHero, review.FavouriteHeroGames), (wrapped.FavouriteHero, wrapped.FavouriteHeroGames));
        Assert.Equal(review.BestWinStreak, wrapped.BestWinStreak);
    }

    [Fact]
    public void A_period_without_games_or_achievements_is_empty()
    {
        var report = Wrapped.Build(History, new WrappedPeriod(WrappedSpan.Month, 2026, 3));

        Assert.True(report.IsEmpty);
        Assert.Null(report.LongestGame);
        Assert.Null(report.FavouriteClass);
        Assert.Equal(new int[7], report.GamesByWeekday);
    }

    [Fact]
    public void Nothing_in_the_report_names_the_opponent()
    {
        var report = Wrapped.Build(History, November, baseHero: BaseHero);

        Assert.DoesNotContain("Opponent", report.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Opponent", report.LongestGame!.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------- achievements

    private static AchievementBook Book(params (int Id, DateTime? Local, int Points)[] completed)
    {
        var achievements = completed.ToDictionary(c => c.Id, c => new AchievementDef(c.Id, 100, c.Id, $"A{c.Id}", string.Empty, false, 1, c.Points, 0));
        var states = completed.ToDictionary(c => c.Id, c => new AchievementState(c.Id, AchievementStatus.Completed, 1, c.Local?.ToUniversalTime()));
        return new AchievementBook(
            [new AchievementCategory(1, "Общие", "general", 1)],
            [new AchievementSubcategory(10, 1, "Прогресс", 1)],
            [new AchievementSection(100, 10, string.Empty, 1)],
            achievements, states, new Dictionary<int, AchievementGain>(), DateTime.UtcNow);
    }

    [Fact]
    public void An_achievement_falls_into_the_period_of_its_local_completion_date()
    {
        var book = Book((1, At(10, 31, 23, 30), 10), (2, At(11, 1, 0, 30), 20), (3, null, 40));

        var november = Wrapped.Build([], November, book);
        var october = Wrapped.Build([], November.Previous, book);
        var year = Wrapped.Build([], WrappedPeriod.YearOf(At(1, 1, 12)), book);

        Assert.Equal([2], november.Achievements.Select(a => a.Achievement.Id));
        Assert.Equal(20, november.AchievementPoints);
        Assert.False(november.IsEmpty);
        Assert.Equal([1], october.Achievements.Select(a => a.Achievement.Id));
        // Without a date it is in no period.
        Assert.Equal([2, 1], year.Achievements.Select(a => a.Achievement.Id));
        Assert.Equal(30, year.AchievementPoints);
    }

    // ------------------------------------------------------------------------------------------------ periods

    [Fact]
    public void Periods_step_over_year_ends_and_bound_local_days()
    {
        var january = new WrappedPeriod(WrappedSpan.Month, 2026, 1);

        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2025, 12), january.Previous);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2027, 1), new WrappedPeriod(WrappedSpan.Month, 2026, 12).Next);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Year, 2025, 0), WrappedPeriod.YearOf(At(5, 5, 5)).Previous.Previous.Next);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), january.From);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Local), january.To);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local), WrappedPeriod.YearOf(At(5, 5, 5)).To);
        Assert.Equal(November, WrappedPeriod.MonthOf(At(11, 30, 23, 59)));
        Assert.False(November.Contains(At(12, 1, 0)));
        Assert.Equal("2026-11", November.ToString());
        Assert.Equal("2026", WrappedPeriod.YearOf(At(5, 5, 5)).ToString());
        Assert.True(November.Contains(At(11, 1, 0)));
    }

    [Fact]
    public void The_range_runs_from_the_first_game_or_achievement_to_today()
    {
        var today = At(11, 19, 12);

        var games = Wrapped.Range(History, null, WrappedSpan.Month, today);
        var earlierAchievement = Wrapped.Range(History, Book((1, new DateTime(2025, 3, 5, 12, 0, 0, DateTimeKind.Local), 10)), WrappedSpan.Month, today);
        var nothing = Wrapped.Range([], null, WrappedSpan.Year, today);

        Assert.Equal((new WrappedPeriod(WrappedSpan.Month, 2026, 10), November), games);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2025, 3), earlierAchievement.First);
        Assert.Equal((WrappedPeriod.YearOf(today), WrappedPeriod.YearOf(today)), nothing);
    }
}

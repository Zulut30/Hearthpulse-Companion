using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The Recap page's view model (docs/wrapped-page.md, AC28) on a made-up history: the period it opens on, month and
/// year, the arrows within the range, the hero numbers and the slides, the empty states, a new game, a newer request
/// winning, a failure and "Try again", the achievements of the period, no opponent anywhere, and a language switch.
/// The recap is worked out on the caller (<see cref="BackgroundWork.Inline"/>) unless a test holds it.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class WrappedViewModelTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);

    private const string Opponent = "Соперник#1234";

    public WrappedViewModelTests() => Loc.Instance.SetLanguage(Loc.Russian);

    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static int _key;

    private static GameRecord Game(DateTime start, string mode = "Ranked", string result = "Win", int minutes = 10, int placement = 0, string hero = "HERO_08",
                                   string playerClass = "MAGE", string? deck = "Пробный маг") => new()
    {
        Key = "w" + Interlocked.Increment(ref _key).ToString(CultureInfo.InvariantCulture),
        StartedAt = start,
        EndedAt = start.AddMinutes(minutes),
        Mode = mode,
        Result = result,
        Placement = placement,
        HeroCardId = hero,
        PlayerClass = playerClass,
        DeckName = mode is "Battlegrounds" or "Duos" ? null : deck,
        OpponentName = Opponent,
        OpponentClass = "WARLOCK",
        OpponentHeroCardId = "HERO_07",
    };

    /// <summary>
    /// November 2026, 11 games counted: four Ranked games (three wins in a row), a Casual loss, an Arena win, three
    /// Battlegrounds solo games (places 1, 3 and 6), one Duos game and a Casual game of five hours, plus an abandoned game;
    /// October: two games; January: one.
    /// </summary>
    private static List<GameRecord> History() =>
    [
        Game(new DateTime(2026, 1, 5, 20, 0, 0, DateTimeKind.Local)),
        Game(new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Local)),
        Game(new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Local), result: "Loss"),
        Game(new DateTime(2026, 11, 2, 21, 0, 0, DateTimeKind.Local), minutes: 12),
        Game(new DateTime(2026, 11, 2, 21, 30, 0, DateTimeKind.Local), minutes: 14),
        Game(new DateTime(2026, 11, 3, 21, 0, 0, DateTimeKind.Local), minutes: 9),
        Game(new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Local), result: "Loss", minutes: 40),
        Game(new DateTime(2026, 11, 7, 15, 0, 0, DateTimeKind.Local), mode: "Casual", result: "Loss"),
        Game(new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Local), mode: "Arena"),
        Game(new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Local), mode: "Battlegrounds", result: "Win", minutes: 25, placement: 1, hero: "TB_BaconShop_HERO_01"),
        Game(new DateTime(2026, 11, 8, 22, 0, 0, DateTimeKind.Local), mode: "Battlegrounds", result: "Win", minutes: 20, placement: 3, hero: "TB_BaconShop_HERO_01"),
        Game(new DateTime(2026, 11, 9, 21, 0, 0, DateTimeKind.Local), mode: "Battlegrounds", result: "Loss", minutes: 18, placement: 6, hero: "TB_BaconShop_HERO_02"),
        Game(new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Local), mode: "Duos", result: "Win", minutes: 22, placement: 2, hero: "TB_BaconShop_HERO_02"),
        Game(new DateTime(2026, 11, 11, 21, 0, 0, DateTimeKind.Local), result: GameRecord.AbandonedResult),
        Game(new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Local), mode: "Casual", minutes: 300),
    ];

    private static HistoryStore Store(IEnumerable<GameRecord> games)
    {
        var history = HistoryStore.InMemory();
        foreach (var game in games)
        {
            history.Add(game);
        }

        return history;
    }

    private static MainViewModel Model(HistoryStore history, BackgroundWork? background = null, DateTime? today = null) =>
        new(history, new AppSettings(), TestDataFolder.App) { Background = background ?? BackgroundWork.Inline, WrappedToday = () => today ?? Today };

    private static MainViewModel Shown(IEnumerable<GameRecord> games, AchievementBook? book = null, DateTime? today = null)
    {
        var vm = Model(Store(games), today: today);
        vm.SetAchievements(book);
        vm.PageShown("Wrapped");
        return vm;
    }

    private static WrappedReport Report(IEnumerable<GameRecord> games, WrappedPeriod period, AchievementBook? book = null) => Wrapped.Build(games, period, book);

    // ------------------------------------------------------------------------------------------ the period it opens on

    [Fact]
    public void The_page_opens_on_this_month_with_its_games_and_the_comparison_with_last_month()
    {
        var games = History();
        var vm = Shown(games);
        var report = Report(games, WrappedPeriod.MonthOf(Today));
        var recap = vm.Recap!;

        Assert.Equal(WrappedPeriod.MonthOf(Today), vm.ShownWrappedPeriod);
        Assert.Equal("Ноябрь 2026", vm.WrappedTitle);
        Assert.Equal(report.Totals.Games.ToString("N0", CultureInfo.CurrentCulture), recap.Number);
        Assert.Equal(11, report.Totals.Games);
        Assert.Equal(Loc.P("Wrapped.Hero.GamesMonth", 11, "ноябрь"), recap.Caption);
        Assert.Equal("игр за ноябрь", recap.Caption);
        Assert.Equal((Loc.F("Wrapped.Delta.MoreMonth", 9), 1), (recap.Delta, recap.DeltaSign));
        Assert.Equal(Loc.F("Wrapped.Delta.Tip", "Октябрь 2026", Loc.P("Stats.Games", 2)), recap.DeltaTip);
        Assert.True(recap.HasGames);

        // Three numbers: time (a game counts at most three hours), win rate outside Battlegrounds, the best streak.
        Assert.Equal(Loc.F("Wrapped.Hours.Value", Math.Round(report.Totals.TimePlayed.TotalHours)), recap.Hours);
        Assert.Equal(report.Totals.Record.WinRate!.Value.ToString("P0", CultureInfo.CurrentCulture), recap.WinRate);
        Assert.Equal(Loc.F("Wrapped.WinRate.Record", report.Totals.Record.Wins, report.Totals.Record.Losses), recap.WinRateDetail);
        Assert.Equal(("3", Loc.P("Wrapped.Streak.Label", 3)), (recap.Streak, recap.StreakLabel));
    }

    [Fact]
    public void An_empty_current_month_opens_the_last_month_with_games()
    {
        var vm = Shown(History(), today: new DateTime(2026, 12, 15, 12, 0, 0, DateTimeKind.Local));

        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, 11), vm.ShownWrappedPeriod);
        Assert.True(vm.WrappedCanGoForward);
    }

    // ------------------------------------------------------------------------------------------ month, year, arrows

    [Fact]
    public void Year_shows_the_shown_months_year_and_month_goes_back_to_the_years_last_month_with_games()
    {
        var vm = Shown(History());

        vm.SetWrappedSpan.Execute("Year");
        Assert.Equal(new WrappedPeriod(WrappedSpan.Year, 2026, 0), vm.ShownWrappedPeriod);
        Assert.Equal("2026", vm.WrappedTitle);
        Assert.Equal(Loc.P("Wrapped.Hero.GamesYear", 14, "2026"), vm.Recap!.Caption);
        Assert.Equal(Loc.T("Wrapped.Delta.NoneYear"), vm.Recap.Delta);
        Assert.True(vm.WrappedIsYear);
        Assert.False(vm.WrappedCanGoBack);
        Assert.False(vm.WrappedCanGoForward);
        Assert.Equal(Loc.T("Wrapped.Nav.NoEarlier"), vm.WrappedBackTip);
        Assert.Equal(Loc.T("Wrapped.Nav.Current"), vm.WrappedForwardTip);

        vm.SetWrappedSpan.Execute("Month");
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, 11), vm.ShownWrappedPeriod);
        Assert.Equal(Loc.T("Wrapped.Nav.PrevMonth"), vm.WrappedBackName);
    }

    [Fact]
    public void The_arrows_go_month_by_month_from_the_first_game_to_this_month()
    {
        var vm = Shown(History());
        Assert.False(vm.WrappedCanGoForward);
        Assert.True(vm.WrappedCanGoBack);

        for (var month = 10; month >= 1; month--)
        {
            vm.WrappedBack.Execute(null);
            Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, month), vm.ShownWrappedPeriod);
        }

        Assert.False(vm.WrappedCanGoBack);
        vm.WrappedBack.Execute(null);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, 1), vm.ShownWrappedPeriod);
        vm.WrappedForward.Execute(null);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, 2), vm.ShownWrappedPeriod);

        // February has nothing: its empty state, no slides.
        Assert.Null(vm.Recap);
        Assert.False(vm.HasRecap);
        Assert.Equal(Loc.T("Wrapped.Empty.Period"), vm.WrappedEmpty);
    }

    [Fact]
    public void Without_any_game_or_achievement_the_page_says_so_and_both_arrows_are_off()
    {
        var vm = Shown([]);

        Assert.Null(vm.Recap);
        Assert.Equal(Loc.T("Wrapped.Empty.None"), vm.WrappedEmpty);
        Assert.False(vm.WrappedCanGoBack);
        Assert.False(vm.WrappedCanGoForward);
        Assert.False(vm.WrappedComputing);

        // Month or year still works.
        vm.SetWrappedSpan.Execute("Year");
        Assert.Equal("2026", vm.WrappedTitle);
    }

    // ------------------------------------------------------------------------------------------ the slides

    [Fact]
    public void The_slides_show_favourites_modes_days_hours_the_longest_game_and_battlegrounds()
    {
        var games = History();
        var vm = Shown(games);
        var recap = vm.Recap!;

        Assert.Equal([Loc.T("Wrapped.Fav.Class"), Loc.T("Wrapped.Fav.Deck"), Loc.T("Wrapped.Fav.Hero")], recap.Favourites.Select(f => f.Label));
        Assert.Equal("HERO_08", recap.Favourites[0].CardId);
        Assert.Equal("Пробный маг", recap.Favourites[1].Value);
        Assert.Equal("TB_BaconShop_HERO_01", recap.Favourites[2].CardId);

        Assert.Equal(Loc.T("Main.Mode.Ranked"), recap.TopMode);
        Assert.Equal(Loc.T("Main.Mode.Ranked"), recap.Modes[0].Title);
        Assert.Equal(1.0, recap.Modes[0].Share);
        Assert.Equal(Loc.F("Wrapped.Modes.Record", 3, 1), recap.Modes[0].Record);
        Assert.Equal("—", recap.Modes.Single(m => m.Title == Loc.T("Main.Mode.Battlegrounds")).Record);
        Assert.Equal("—", recap.Modes.Single(m => m.Title == Loc.T("Main.Mode.Duos")).Record);

        // Days Monday first; the busiest day is marked and named.
        Assert.Equal(7, recap.Days.Count);
        Assert.Equal("Пн", recap.Days[0].Label);
        Assert.Single(recap.Days, d => d.Top);
        Assert.Equal(RecapBar.MaxHeight, recap.Days.Max(d => d.Height));
        Assert.Equal(24, recap.HoursOfDay.Count);
        Assert.Equal(["0", "3", "6", "9", "12", "15", "18", "21"], recap.HoursOfDay.Where(h => h.Label.Length > 0).Select(h => h.Label));
        Assert.True(recap.HoursOfDay[21].Top);
        Assert.Equal(Loc.F("Wrapped.When.HourValue", Today.Date.AddHours(21), Today.Date.AddHours(22)), recap.Hour);

        // The five-hour game is a client left open: the longest is the 40-minute one.
        Assert.Equal(Loc.F("Stats.Minutes", 40), recap.Longest);
        Assert.StartsWith(Loc.T("Main.Mode.Ranked") + " · ", recap.LongestDetail, StringComparison.Ordinal);

        // Battlegrounds solo only: three games, one first place, two in the top 4.
        Assert.Equal(["3", "1"], recap.Battlegrounds.Take(2).Select(t => t.Value));
        Assert.Equal(Loc.F("Wrapped.Bg.Top4Detail", 2, 3), recap.Battlegrounds[2].Detail);
        Assert.Equal((10.0 / 3).ToString("0.0", CultureInfo.CurrentCulture), recap.Battlegrounds[3].Value);
    }

    [Fact]
    public void Nothing_on_the_page_names_an_opponent()
    {
        var vm = Shown(History());

        foreach (var text in Texts(vm.Recap!))
        {
            Assert.DoesNotContain(Opponent, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Чернокнижник", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HERO_07", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Every string of the recap and of its rows.</summary>
    private static IEnumerable<string> Texts(object value)
    {
        foreach (var property in value.GetType().GetProperties())
        {
            var item = property.GetValue(value);
            if (item is string text)
            {
                yield return text;
            }
            else if (item is System.Collections.IEnumerable list)
            {
                foreach (var row in list)
                {
                    foreach (var inner in Texts(row!))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------ achievements

    [Fact]
    public void The_achievements_of_the_period_are_listed_with_their_points_and_the_rest_counted()
    {
        var book = AchievementsScene.Build(Today.ToUniversalTime(), confirmed: true);
        var vm = Shown(History(), book);
        var month = WrappedPeriod.MonthOf(Today);
        var done = book.CompletedBetween(month.From.ToUniversalTime(), month.To.ToUniversalTime());

        Assert.Equal(Loc.P("Wrapped.Ach.Count", done.Count), vm.Recap!.AchievementsCount);
        Assert.Equal(Loc.P("Wrapped.Ach.Points", done.Sum(d => d.Achievement.Points)), vm.Recap.AchievementsPoints);
        Assert.Equal(done.Take(10).Select(d => d.Achievement.Name.Length > 0 ? d.Achievement.Name : Loc.T("Achievements.Hidden")), vm.Recap.Achievements.Select(a => a.Title));

        vm.SetWrappedSpan.Execute("Year");
        var year = book.CompletedBetween(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(), new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime());
        Assert.True(year.Count > 10);
        Assert.Equal(10, vm.Recap!.Achievements.Count);
        Assert.Equal(Loc.F("Wrapped.Ach.More", year.Count - 10), vm.Recap.AchievementsMore);
    }

    [Fact]
    public void Without_a_book_the_achievements_slide_says_they_come_once_read_and_a_period_without_games_shows_achievements()
    {
        var vm = Shown(History());
        Assert.Equal(Loc.T("Wrapped.Ach.NoBook"), vm.Recap!.AchievementsNote);

        // A month with achievements and no games: the hero number is the achievements, the game slides are off.
        var book = AchievementsScene.Build(Today.ToUniversalTime(), confirmed: true);
        var onlyAchievements = Shown([], book);
        var month = WrappedPeriod.MonthOf(Today);
        var count = book.CompletedBetween(month.From.ToUniversalTime(), month.To.ToUniversalTime()).Count;
        Assert.False(onlyAchievements.Recap!.HasGames);
        Assert.Equal(count.ToString("N0", CultureInfo.CurrentCulture), onlyAchievements.Recap.Number);
        Assert.Equal(Loc.P("Wrapped.Hero.AchMonth", count, "ноябрь"), onlyAchievements.Recap.Caption);
        Assert.False(onlyAchievements.Recap.HasDelta);
    }

    // ------------------------------------------------------------------------------------------ freshness

    [Fact]
    public void A_new_game_counts_once_the_page_is_shown_again()
    {
        var history = Store(History());
        var vm = Model(history);
        vm.PageShown("Wrapped");
        Assert.Equal("11", vm.Recap!.Number);

        vm.PageShown("History");
        history.Add(Game(new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Local)));
        Assert.True(vm.RefreshAsync().IsCompletedSuccessfully);
        Assert.Equal("11", vm.Recap!.Number);

        vm.PageShown("Wrapped");
        Assert.Equal("12", vm.Recap!.Number);
    }

    [Fact]
    public void A_newer_request_wins_over_one_still_being_worked_out() => LocBindingTests.Headless.Run(() =>
    {
        var held = new HeldWork("WrappedData");
        var vm = Model(Store(History()), held);
        vm.PageShown("Wrapped");
        held.Release(0);
        var november = new WrappedPeriod(WrappedSpan.Month, 2026, 11);
        Assert.Equal(november, vm.ShownWrappedPeriod);

        // October, then the year: November stays on screen while both are worked out; the year, asked last, comes in first
        // and stays when October comes in after it.
        vm.WrappedBack.Execute(null);
        vm.SetWrappedSpan.Execute("Year");
        Assert.Equal(3, held.Count);
        Assert.True(vm.WrappedComputing);
        Assert.Equal(november, vm.ShownWrappedPeriod);

        held.Release(2);
        var year = new WrappedPeriod(WrappedSpan.Year, 2026, 0);
        Assert.Equal(year, vm.ShownWrappedPeriod);
        Assert.False(vm.WrappedComputing);
        held.Release(1);
        Assert.Equal(year, vm.ShownWrappedPeriod);
        Assert.Equal("2026", vm.WrappedTitle);
        Assert.Equal(Loc.P("Wrapped.Hero.GamesYear", 14, "2026"), vm.Recap!.Caption);
        Assert.False(vm.WrappedComputing);
    });

    [Fact]
    public void A_history_refresh_repeats_the_newest_request_and_keeps_the_period_asked_for() => LocBindingTests.Headless.Run(() =>
    {
        var held = new HeldWork("WrappedData");
        var history = Store(History());
        var vm = Model(history, held);
        vm.PageShown("Wrapped");
        held.Release(0);

        // A period outside the range, asked for outright, then a new game while it is still on its way: the recap is
        // worked out again for that period, not for the one shown before it, nor pulled into the range.
        var asked = new WrappedPeriod(WrappedSpan.Month, 2001, 1);
        vm.ShowWrappedPeriod(asked, clamp: false);
        history.Add(Game(new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Local)));
        Assert.True(vm.RefreshAsync().IsCompletedSuccessfully);
        Assert.Equal(3, held.Count);

        held.Release(2);
        Assert.Equal(asked, vm.ShownWrappedPeriod);
        held.Release(1);
        Assert.Equal(asked, vm.ShownWrappedPeriod);
        Assert.Null(vm.Recap);
        Assert.Equal(Loc.T("Wrapped.Empty.Period"), vm.WrappedEmpty);
    });

    [Fact]
    public void A_failed_recap_says_so_and_try_again_repeats_the_request_that_failed()
    {
        var work = new FailingWork("WrappedData") { Fail = true };
        var vm = Model(Store(History()), work);
        vm.PageShown("Wrapped");

        // The first result failed: the error and its button, not "working out" for ever, not an empty period.
        Assert.True(vm.WrappedFailed);
        Assert.False(vm.WrappedComputing);
        Assert.False(vm.WrappedFirstComputing);
        Assert.False(vm.HasRecap);
        Assert.False(vm.WrappedEmptyShown);

        work.Fail = false;
        vm.RetryWrapped.Execute(null);
        Assert.False(vm.WrappedFailed);
        Assert.Equal(WrappedPeriod.MonthOf(Today), vm.ShownWrappedPeriod);
        Assert.Equal("11", vm.Recap!.Number);

        // October fails: trying again works out October, not the month shown before it.
        work.Fail = true;
        vm.WrappedBack.Execute(null);
        Assert.True(vm.WrappedFailed);
        Assert.False(vm.WrappedComputing);
        work.Fail = false;
        vm.RetryWrapped.Execute(null);
        Assert.False(vm.WrappedFailed);
        Assert.Equal(new WrappedPeriod(WrappedSpan.Month, 2026, 10), vm.ShownWrappedPeriod);
        Assert.Equal("2", vm.Recap!.Number);
    }

    // ------------------------------------------------------------------------------------------ language

    [Fact]
    public void A_language_switch_rebuilds_the_period_and_the_texts()
    {
        var vm = Shown(History());

        Loc.Instance.SetLanguage(Loc.English);

        Assert.Equal("November 2026", vm.WrappedTitle);
        Assert.Equal("games in November", vm.Recap!.Caption);
        Assert.Equal("Mon", vm.Recap.Days[0].Label);
        Assert.Equal(Loc.T("Main.Mode.Ranked"), vm.Recap.TopMode);
    }
}

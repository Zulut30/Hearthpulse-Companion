#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;
using Hearthpulse.Core.Stats;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The recap page, "Wrapped" (docs/wrapped-page.md): the player's month or year from their own history and completed
/// achievements. Worked out off the UI thread with a build number (a newer request wins); the history is read once and
/// read again after a new game, the period is one of <see cref="Wrapped.Range"/>. Nothing here names an opponent.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The recap lists at most this many completed achievements; the rest are on the achievements page.</summary>
    internal const int RecapAchievementRows = 10;

    private const string GlyphUp = "\U000F0360";
    private const string GlyphDown = "\U000F035D";
    private const string GlyphFlat = "\U000F0374";

    private WrappedSpan _wrappedSpan = WrappedSpan.Month;
    private WrappedPeriod? _wrappedPeriod;
    /// <summary>
    /// The newest request: the one being worked out, or, once it is in, the shown period as it was asked for. Working
    /// the recap out again (a new game, a new book, a retry) repeats it, so a request still on its way is not replaced by
    /// the period shown before it.
    /// </summary>
    private RecapRequest _wrappedRequest = RecapRequest.Default;
    private IReadOnlyList<GameRecord>? _wrappedGames;
    private bool _wrappedWanted;
    private bool _wrappedDirty = true;
    private int _wrappedBuild;
    private bool _wrappedComputing;
    private bool _wrappedFailed;
    private bool _wrappedNothing;
    private WrappedReport? _wrappedReport;
    private (WrappedPeriod First, WrappedPeriod Last) _wrappedRange;
    private RecapView? _recap;
    private RelayCommand? _setWrappedSpan;
    private RelayCommand? _wrappedBack;
    private RelayCommand? _wrappedForward;
    private RelayCommand? _retryWrapped;

    /// <summary>The work of the last request; a render scene waits for it.</summary>
    internal Task WrappedWork { get; private set; } = Task.CompletedTask;

    /// <summary>Today for the recap (the current period and the newest one there is); a test or a render scene sets it.</summary>
    internal Func<DateTime> WrappedToday { get; set; } = () => DateTime.Now;

    /// <summary>"Month" or "Year": the span the page shows.</summary>
    public string WrappedSpanKey => _wrappedSpan == WrappedSpan.Year ? "Year" : "Month";

    public bool WrappedIsMonth => _wrappedSpan == WrappedSpan.Month;

    public bool WrappedIsYear => _wrappedSpan == WrappedSpan.Year;

    /// <summary>The shown period: "November 2026" or "2026"; empty before the first result.</summary>
    public string WrappedTitle => _wrappedPeriod is { } period ? PeriodTitle(period) : string.Empty;

    /// <summary>The shown period (null before the first result).</summary>
    internal WrappedPeriod? ShownWrappedPeriod => _wrappedPeriod;

    /// <summary>The worked-out recap of the shown period; null before the first result or for a period with nothing in it.</summary>
    public RecapView? Recap { get => _recap; private set => Set(ref _recap, value); }

    public bool HasRecap => _recap is not null;

    /// <summary>A request is being worked out: the "Working out your recap…" line lights up; what is shown stays until it is replaced.</summary>
    public bool WrappedComputing
    {
        get => _wrappedComputing;
        private set
        {
            if (Set(ref _wrappedComputing, value))
            {
                RaiseWrappedState();
            }
        }
    }

    /// <summary>The first result is still to come: the hero card says so instead of numbers.</summary>
    public bool WrappedFirstComputing => _wrappedComputing && _wrappedReport is null && !_wrappedFailed;

    /// <summary>Working the recap out failed: the hero card says so and offers to try again.</summary>
    public bool WrappedFailed
    {
        get => _wrappedFailed;
        private set
        {
            if (Set(ref _wrappedFailed, value))
            {
                RaiseWrappedState();
            }
        }
    }

    /// <summary>The empty state in the hero card: nothing at all yet, or nothing in this period; empty when there is a recap.</summary>
    public string WrappedEmpty => _wrappedReport is not { IsEmpty: true } || _wrappedFailed ? string.Empty
        : Loc.T(_wrappedNothing ? "Wrapped.Empty.None" : "Wrapped.Empty.Period");

    public bool WrappedEmptyShown => WrappedEmpty.Length > 0;

    /// <summary>An earlier period has games or achievements.</summary>
    public bool WrappedCanGoBack => _wrappedPeriod is { } period && period.From > _wrappedRange.First.From;

    /// <summary>The shown period is not the current one.</summary>
    public bool WrappedCanGoForward => _wrappedPeriod is { } period && period.From < _wrappedRange.Last.From;

    /// <summary>The back arrow's name: "Previous month" / "Previous year".</summary>
    public string WrappedBackName => Loc.T(WrappedIsYear ? "Wrapped.Nav.PrevYear" : "Wrapped.Nav.PrevMonth");

    public string WrappedForwardName => Loc.T(WrappedIsYear ? "Wrapped.Nav.NextYear" : "Wrapped.Nav.NextMonth");

    /// <summary>The back arrow's help text: its name, or why it is off ("Nothing earlier"; shown on the disabled arrow).</summary>
    public string WrappedBackTip => WrappedCanGoBack ? WrappedBackName : Loc.T("Wrapped.Nav.NoEarlier");

    public string WrappedForwardTip => WrappedCanGoForward ? WrappedForwardName : Loc.T("Wrapped.Nav.Current");

    /// <summary>Month or year ("Month"/"Year"): a year shows the shown month's year, a month the year's last month with games.</summary>
    public RelayCommand SetWrappedSpan => _setWrappedSpan ??= new RelayCommand(p =>
    {
        var span = p as string == "Year" ? WrappedSpan.Year : WrappedSpan.Month;
        if (span == _wrappedSpan)
        {
            return;
        }

        var year = (_wrappedPeriod ?? WrappedPeriod.MonthOf(WrappedToday())).Year;
        _wrappedSpan = span;
        RaiseWrappedSpan();
        RequestWrapped(span == WrappedSpan.Year ? RecapRequest.Of(new WrappedPeriod(WrappedSpan.Year, year, 0)) : RecapRequest.MonthIn(year));
    });

    /// <summary>The period before the shown one, when there is anything earlier.</summary>
    public RelayCommand WrappedBack => _wrappedBack ??= new RelayCommand(() =>
    {
        if (WrappedCanGoBack && _wrappedPeriod is { } period)
        {
            RequestWrapped(RecapRequest.Of(period.Previous));
        }
    });

    /// <summary>The period after the shown one, up to the current one.</summary>
    public RelayCommand WrappedForward => _wrappedForward ??= new RelayCommand(() =>
    {
        if (WrappedCanGoForward && _wrappedPeriod is { } period)
        {
            RequestWrapped(RecapRequest.Of(period.Next));
        }
    });

    /// <summary>Works the recap out again after a failure.</summary>
    public RelayCommand RetryWrapped => _retryWrapped ??= new RelayCommand(() =>
    {
        _wrappedGames = null;
        RequestWrapped(_wrappedRequest);
    });

    /// <summary>Shows <paramref name="period"/> (its span too); <paramref name="clamp"/> false takes it even outside the range (render scenes).</summary>
    internal void ShowWrappedPeriod(WrappedPeriod period, bool clamp = true)
    {
        _wrappedWanted = true;
        _wrappedSpan = period.Span;
        RaiseWrappedSpan();
        RequestWrapped(RecapRequest.Of(period) with { Exact = !clamp });
    }

    // ---- hooks --------------------------------------------------------------------------------------------------------

    partial void WrappedPageShown()
    {
        _wrappedWanted = true;
        if (_wrappedDirty)
        {
            RequestWrapped(_wrappedRequest);
        }
    }

    partial void RebuildWrapped()
    {
        if (_wrappedReport is not null)
        {
            ShowRecap();
        }

        RaiseWrappedSpan();
        RaiseWrappedState();
    }

    /// <summary>A game was added or the history read again: the recap reads it anew, now when it is on screen, else when it is shown.</summary>
    partial void OnRecentChanged()
    {
        _wrappedGames = null;
        WrappedStale();
    }

    /// <summary>The achievement book changed (<see cref="SetAchievements"/>): its completions are part of the recap.</summary>
    private void WrappedBookChanged() => WrappedStale();

    private void WrappedStale()
    {
        _wrappedDirty = true;
        if (_wrappedWanted && _shownPage == "Wrapped")
        {
            RequestWrapped(_wrappedRequest);
        }
    }

    // ---- working out --------------------------------------------------------------------------------------------------

    private void RequestWrapped(RecapRequest request)
    {
        _wrappedRequest = request;
        _wrappedDirty = false;
        var build = ++_wrappedBuild;
        WrappedFailed = false;
        WrappedComputing = true;
        WrappedWork = WorkOutWrappedAsync(request, _wrappedSpan, _wrappedGames, _achievementBook, build);
    }

    private async Task WorkOutWrappedAsync(RecapRequest request, WrappedSpan span, IReadOnlyList<GameRecord>? known, AchievementBook? book, int build)
    {
        var history = _history;
        var cards = _cards;
        var today = WrappedToday();
        try
        {
            var data = await Background.RunAsync(() =>
            {
                var games = known ?? history.Since(DateTime.MinValue);
                var range = Wrapped.Range(games, book, span, today);
                var chosen = request.Choose(games, book, span, today);
                var period = request.Exact ? chosen : Clamp(chosen, range);
                var nothing = games.All(g => g.IsAbandoned) && !HasDatedAchievements(book);
                return new WrappedData(games, range, Wrapped.Build(games, period, book, cards.BaseHeroId), nothing);
            }).ConfigureAwait(true);
            if (build != _wrappedBuild)
            {
                return;
            }

            _wrappedRequest = RecapRequest.Of(data.Report.Period) with { Exact = request.Exact };
            _wrappedGames = data.Games;
            _wrappedNothing = data.Nothing;
            _wrappedRange = data.Range;
            _wrappedPeriod = data.Report.Period;
            _wrappedReport = data.Report;
            ShowRecap();
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
        {
            if (build == _wrappedBuild)
            {
                Diag.Error("Recap not worked out", ex);
                WrappedFailed = true;
            }
        }
        finally
        {
            if (build == _wrappedBuild)
            {
                WrappedComputing = false;
            }
        }
    }

    private static WrappedPeriod Clamp(WrappedPeriod period, (WrappedPeriod First, WrappedPeriod Last) range) =>
        period.From < range.First.From ? range.First : period.From > range.Last.From ? range.Last : period;

    /// <summary>The book has a completion with a date: the recap has something to show.</summary>
    private static bool HasDatedAchievements(AchievementBook? book) => book?.RecentlyCompleted(1).Count > 0;

    // ---- the texts ----------------------------------------------------------------------------------------------------

    /// <summary>The page from the worked-out report in the current language.</summary>
    private void ShowRecap()
    {
        if (_wrappedReport is not { } report)
        {
            Recap = null;
            RaiseWrappedSpan();
            RaiseWrappedState();
            return;
        }

        Recap = report.IsEmpty ? null : RecapOf(report, _achievementBook is not null);
        RaiseWrappedSpan();
        RaiseWrappedState();
    }

    /// <summary>The recap of a report: hero numbers, favourites, modes, days and hours, Battlegrounds, achievements.</summary>
    internal RecapView RecapOf(WrappedReport report, bool hasBook)
    {
        var period = report.Period;
        var year = period.Span == WrappedSpan.Year;
        var games = report.Totals.Games;
        var monthName = Loc.DateCulture.DateTimeFormat.GetMonthName(Math.Clamp(period.Month, 1, 12));
        var when = year ? period.Year.ToString(CultureInfo.InvariantCulture) : monthName;
        var number = games > 0 ? games : report.Achievements.Count;
        var caption = games > 0
            ? Loc.P(year ? "Wrapped.Hero.GamesYear" : "Wrapped.Hero.GamesMonth", games, when)
            : Loc.P(year ? "Wrapped.Hero.AchYear" : "Wrapped.Hero.AchMonth", number, when);

        var (delta, glyph, sign) = games == 0 ? (string.Empty, GlyphFlat, 0)
            : report.Previous.Games == 0 ? (Loc.T(year ? "Wrapped.Delta.NoneYear" : "Wrapped.Delta.NoneMonth"), GlyphFlat, 0)
            : games > report.Previous.Games ? (Loc.F(year ? "Wrapped.Delta.MoreYear" : "Wrapped.Delta.MoreMonth", games - report.Previous.Games), GlyphUp, 1)
            : games < report.Previous.Games ? (Loc.F(year ? "Wrapped.Delta.LessYear" : "Wrapped.Delta.LessMonth", report.Previous.Games - games), GlyphDown, -1)
            : (Loc.T(year ? "Wrapped.Delta.SameYear" : "Wrapped.Delta.SameMonth"), GlyphFlat, 0);
        var deltaTip = Loc.F("Wrapped.Delta.Tip", PeriodTitle(period.Previous), Loc.P("Stats.Games", report.Previous.Games));

        var time = report.Totals.TimePlayed;
        var hours = time.TotalHours >= 1 ? Loc.F("Wrapped.Hours.Value", Math.Round(time.TotalHours)) : Loc.F("Wrapped.Hours.Minutes", (int)time.TotalMinutes);
        var record = report.Totals.Record;
        var winRate = record.WinRate is { } rate ? rate.ToString("P0", CultureInfo.CurrentCulture) : "—";
        var winDetail = record.Wins + record.Losses > 0 ? Loc.F("Wrapped.WinRate.Record", record.Wins, record.Losses) : Loc.T("Wrapped.WinRate.BgOnly");

        return new RecapView(
            number.ToString("N0", CultureInfo.CurrentCulture), caption,
            delta, glyph, sign, deltaTip,
            games > 0,
            hours, Loc.F("Wrapped.Hours.Tip", Hours(time)),
            winRate, winDetail,
            report.BestWinStreak.ToString("N0", CultureInfo.CurrentCulture), Loc.P("Wrapped.Streak.Label", report.BestWinStreak),
            Favourites(report),
            report.Modes.Count > 0 ? Mode(report.Modes[0].Mode) : string.Empty, ModeRows(report),
            DayOf(report), DayBars(report), DaysName(report),
            HourOf(report), HourBars(report), HoursName(report),
            report.LongestGame is { } longest ? Hours(longest.Duration) : string.Empty,
            report.LongestGame is { } game ? LongestDetail(game) : string.Empty,
            BattlegroundsTiles(report.Battlegrounds),
            Loc.P("Wrapped.Ach.Count", report.Achievements.Count), Loc.P("Wrapped.Ach.Points", report.AchievementPoints),
            [.. report.Achievements.Take(RecapAchievementRows).Select(RecapAchievement)],
            report.Achievements.Count > RecapAchievementRows ? Loc.F("Wrapped.Ach.More", report.Achievements.Count - RecapAchievementRows) : string.Empty,
            !hasBook ? Loc.T("Wrapped.Ach.NoBook") : report.Achievements.Count == 0 ? Loc.T("Wrapped.Ach.None") : string.Empty);
    }

    private List<RecapTile> Favourites(WrappedReport report)
    {
        var tiles = new List<RecapTile>();
        if (report.FavouriteClass is { } heroClass)
        {
            tiles.Add(new RecapTile(Loc.T("Wrapped.Fav.Class"), ConstructedMeta.ClassName(heroClass), Loc.P("Stats.Games", report.FavouriteClassGames), ClassHero(heroClass)));
        }

        if (report.FavouriteDeck is { } deck)
        {
            var deckClass = report.FavouriteDeckClass ?? string.Empty;
            var games = Loc.P("Stats.Games", report.FavouriteDeckGames);
            tiles.Add(new RecapTile(Loc.T("Wrapped.Fav.Deck"), deck,
                deckClass.Length > 0 ? Loc.F("Wrapped.Fav.DeckDetail", games, ConstructedMeta.ClassName(deckClass)) : games, ClassHero(deckClass)));
        }

        if (report.FavouriteHero is { } hero)
        {
            tiles.Add(new RecapTile(Loc.T("Wrapped.Fav.Hero"), _cards.NameOf(hero), Loc.P("Stats.Games", report.FavouriteHeroGames), hero));
        }

        return tiles;
    }

    private static List<RecapModeRow> ModeRows(WrappedReport report)
    {
        var most = report.Modes.Count > 0 ? Math.Max(1, report.Modes[0].Games) : 1;
        return [.. report.Modes.Select(m =>
        {
            var title = Mode(m.Mode);
            var games = Loc.P("Stats.Games", m.Games);
            var time = Hours(m.TimePlayed);
            var record = NoRecord(m.Mode) ? "—" : Loc.F("Wrapped.Modes.Record", m.Record.Wins, m.Record.Losses);
            return new RecapModeRow(title, (double)m.Games / most, games, time, record, Loc.F("Wrapped.Modes.A11y", title, games, time, record));
        })];
    }

    /// <summary>Modes whose games end in a place, not a win or a loss.</summary>
    private static bool NoRecord(string mode) => mode is "Battlegrounds" or "Duos" or "Mercenaries";

    private static string DayName(int mondayFirst)
    {
        var name = Loc.DateCulture.DateTimeFormat.GetDayName((DayOfWeek)((mondayFirst + 1) % 7));
        return Capital(name);
    }

    private static string DayOf(WrappedReport report) => Top(report.GamesByWeekday) is { } day ? DayName(day) : string.Empty;

    private static List<RecapBar> DayBars(WrappedReport report)
    {
        var top = Top(report.GamesByWeekday);
        var most = Math.Max(1, report.GamesByWeekday.DefaultIfEmpty(0).Max());
        return [.. report.GamesByWeekday.Select((count, day) => new RecapBar(
            Capital(Loc.DateCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((day + 1) % 7))),
            count > 0 ? count.ToString("N0", CultureInfo.CurrentCulture) : string.Empty,
            BarHeight(count, most),
            day == top,
            Loc.F("Wrapped.When.DayTip", DayName(day), Loc.P("Stats.Games", count))))];
    }

    private static string DaysName(WrappedReport report) => Top(report.GamesByWeekday) is { } day
        ? Loc.F("Wrapped.When.DaysA11y", DayName(day), Loc.P("Stats.Games", report.GamesByWeekday[day]))
        : string.Empty;

    private static string HourOf(WrappedReport report) => Top(report.GamesByHour) is { } hour ? HourRange(hour) : string.Empty;

    private static string HourRange(int hour) => Loc.F("Wrapped.When.HourValue", DateTime.Today.AddHours(hour), DateTime.Today.AddHours(hour + 1));

    private static List<RecapBar> HourBars(WrappedReport report)
    {
        var top = Top(report.GamesByHour);
        var most = Math.Max(1, report.GamesByHour.DefaultIfEmpty(0).Max());
        return [.. report.GamesByHour.Select((count, hour) => new RecapBar(
            hour % 3 == 0 ? hour.ToString(CultureInfo.CurrentCulture) : string.Empty,
            string.Empty,
            BarHeight(count, most),
            hour == top,
            Loc.F("Wrapped.When.HourTip", DateTime.Today.AddHours(hour), DateTime.Today.AddHours(hour + 1), Loc.P("Stats.Games", count))))];
    }

    private static string HoursName(WrappedReport report) => Top(report.GamesByHour) is { } hour
        ? Loc.F("Wrapped.When.HoursA11y", HourRange(hour), Loc.P("Stats.Games", report.GamesByHour[hour]))
        : string.Empty;

    /// <summary>The index of the largest count (the first of equal ones), null when every count is 0.</summary>
    private static int? Top(IReadOnlyList<int> counts)
    {
        int? top = null;
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i] > 0 && (top is null || counts[i] > counts[top.Value]))
            {
                top = i;
            }
        }

        return top;
    }

    private static double BarHeight(int count, int most) => count <= 0 ? 0 : Math.Max(2, RecapBar.MaxHeight * count / most);

    private string LongestDetail(WrappedGame game)
    {
        var who = NoRecord(game.Mode) ? _cards.NameOf(_cards.BaseHeroId(game.HeroCardId))
            : game.PlayerClass.Length > 0 ? ConstructedMeta.ClassName(game.PlayerClass)
            : string.Empty;
        return who.Length > 0
            ? Loc.F("Wrapped.When.LongestDetail", Mode(game.Mode), who, game.StartedAt)
            : Loc.F("Achievements.Gain.Line", Mode(game.Mode), Loc.F("Wrapped.Ach.Date", game.StartedAt));
    }

    private static List<StatTile> BattlegroundsTiles(WrappedBattlegrounds? bg)
    {
        if (bg is null)
        {
            return [];
        }

        var top4 = bg.Placed > 0 ? ((double)bg.Top4 / bg.Placed).ToString("P0", CultureInfo.CurrentCulture) : "—";
        return
        [
            new(Loc.T("Wrapped.Bg.Games"), bg.Games.ToString("N0", CultureInfo.CurrentCulture), string.Empty),
            new(Loc.T("Wrapped.Bg.Firsts"), bg.Firsts.ToString("N0", CultureInfo.CurrentCulture), string.Empty),
            new(Loc.T("Wrapped.Bg.Top4"), top4, bg.Placed > 0 ? Loc.F("Wrapped.Bg.Top4Detail", bg.Top4, bg.Placed) : string.Empty),
            new(Loc.T("Wrapped.Bg.Average"), bg.AveragePlacement is { } average ? average.ToString("0.0", CultureInfo.CurrentCulture) : "—", string.Empty),
        ];
    }

    private static AchievementRecentRow RecapAchievement(CompletedAchievement done)
    {
        var muted = done.Achievement.Name.Length == 0;
        var title = muted ? Loc.T("Achievements.Hidden") : done.Achievement.Name;
        var local = done.CompletedAt.ToLocalTime();
        var date = Loc.F("Wrapped.Ach.Date", local);
        var points = Loc.P("Wrapped.Ach.Points", done.Achievement.Points);
        return new AchievementRecentRow(title, muted, string.Empty, date, points, Loc.F("Achievements.Gain.Line", title, Loc.F("Achievements.CompletedOn", local)));
    }

    /// <summary>"November 2026" (the month with a capital, as a heading) or "2026".</summary>
    private static string PeriodTitle(WrappedPeriod period) => period.Span == WrappedSpan.Year
        ? period.Year.ToString(CultureInfo.InvariantCulture)
        : Capital(Loc.F("Wrapped.Period.Month", period.From));

    private static string Capital(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Loc.DateCulture) + text[1..];

    /// <summary>A game's start in local time, as Core's recap counts it (the history keeps local times; one marked UTC is turned).</summary>
    private static DateTime LocalStart(GameRecord game) => game.StartedAt.Kind == DateTimeKind.Utc ? game.StartedAt.ToLocalTime() : game.StartedAt;

    private void RaiseWrappedSpan()
    {
        Raise(nameof(WrappedSpanKey));
        Raise(nameof(WrappedIsMonth));
        Raise(nameof(WrappedIsYear));
        Raise(nameof(WrappedTitle));
        Raise(nameof(WrappedCanGoBack));
        Raise(nameof(WrappedCanGoForward));
        Raise(nameof(WrappedBackName));
        Raise(nameof(WrappedForwardName));
        Raise(nameof(WrappedBackTip));
        Raise(nameof(WrappedForwardTip));
    }

    private void RaiseWrappedState()
    {
        Raise(nameof(HasRecap));
        Raise(nameof(WrappedFirstComputing));
        Raise(nameof(WrappedEmpty));
        Raise(nameof(WrappedEmptyShown));
    }

    /// <summary>What a request works out off the UI thread: the games it read, the range of periods with anything in them, the report, and whether there is nothing at all.</summary>
    private sealed record WrappedData(IReadOnlyList<GameRecord> Games, (WrappedPeriod First, WrappedPeriod Last) Range, WrappedReport Report, bool Nothing);

    /// <summary>What a request asks for: a period, the default (this month, else the last one with anything), or a month of a year.</summary>
    private sealed record RecapRequest(WrappedPeriod? Period, int? MonthOfYear)
    {
        /// <summary>The period is taken as asked, even outside the range (a render scene's empty period).</summary>
        public bool Exact { get; init; }

        public static RecapRequest Default { get; } = new(null, null);

        public static RecapRequest Of(WrappedPeriod period) => new(period, null);

        public static RecapRequest MonthIn(int year) => new(null, year);

        /// <summary>The period to work out, off the UI thread with the games at hand.</summary>
        public WrappedPeriod Choose(IReadOnlyList<GameRecord> games, AchievementBook? book, WrappedSpan span, DateTime today)
        {
            if (Period is { } period)
            {
                return period;
            }

            // The newest month with a game or a dated achievement: of the year asked for, or of all.
            if (MonthOfYear is { } year)
            {
                var whole = new WrappedPeriod(WrappedSpan.Year, year, 0);
                var latest = Latest(games.Where(g => whole.Contains(LocalStart(g))), book?.CompletedBetween(whole.From.ToUniversalTime(), whole.To.ToUniversalTime()));
                return latest is { } at && at <= today ? WrappedPeriod.MonthOf(at)
                    : year == today.Year ? WrappedPeriod.MonthOf(today) : new WrappedPeriod(WrappedSpan.Month, year, 12);
            }

            var current = WrappedPeriod.Of(span, today);
            return Wrapped.Build(games, current, book).IsEmpty && Latest(games, book?.RecentlyCompleted(1)) is { } last
                ? WrappedPeriod.Of(span, last)
                : current;
        }

        /// <summary>The newest start of a counted game or completion of an achievement, local; null with neither.</summary>
        private static DateTime? Latest(IEnumerable<GameRecord> games, IReadOnlyList<CompletedAchievement>? completed)
        {
            var game = games.Where(g => !g.IsAbandoned).Select(g => (DateTime?)LocalStart(g)).Max();
            var achievement = completed is { Count: > 0 } ? completed.Max(c => c.CompletedAt).ToLocalTime() : (DateTime?)null;
            return game is { } g && (achievement is null || g >= achievement) ? g : achievement;
        }
    }
}

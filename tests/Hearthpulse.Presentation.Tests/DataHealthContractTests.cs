using System.Windows;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The data summary in both apps' view models (roadmap 2026-11-15, 2.5): the "Data status" card of Settings → Data,
/// the "update is delayed" line on the page whose source is late and on no other, the date under the overlay's Arena
/// numbers, and the card's place in the WPF Data tab.
/// </summary>
public sealed class DataHealthContractTests
{
    private static readonly DateTime Checked = new(2026, 11, 15, 21, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Collected = new(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Late = new(2026, 11, 12, 3, 35, 0, DateTimeKind.Utc);

    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    private static dynamic Model(bool wpf) =>
        wpf ? new WpfMain(History(), new AppSettings(), TestApp.Data) : new AvaMain(History(), new AppSettings(), TestApp.Data);

    /// <summary>Every mapped source fresh, except those given another state (collected at <see cref="Late"/>).</summary>
    private static DataHealthReport Report(bool offline = false, params (string Id, DataFreshness State)[] others)
    {
        var sources = DataSections.All.SelectMany(DataSections.Sources).Distinct(StringComparer.Ordinal).Select(id =>
        {
            var other = others.FirstOrDefault(o => o.Id == id);
            return other.Id is null
                ? new DataSourceHealth(id, DataFreshness.Fresh, 4.5, 30, Collected, Checked.AddHours(2))
                : new DataSourceHealth(id, other.State, 90, 30, Late, null);
        });
        return new DataHealthReport(sources, Checked, offline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_card_lists_the_sections_with_date_and_state_and_marks_only_the_late_one(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);

        // Before the first summary: every row is there, nothing is known, the note says a check is running.
        var before = Rows((object)model);
        Assert.Equal(13, before.Count);
        Assert.All(before, r => Assert.Equal(("—", Loc.T("Source.Health.State.Unknown"), false), (r.Date, r.State, r.Attention)));
        Assert.Equal(Loc.T("Source.Health.Checking"), (string)model.DataHealthNote);

        model.SetDataHealth(Report(false, ("hsguru_brawl", DataFreshness.Stale), ("hsguru_matchups", DataFreshness.Pending)));

        var rows = Rows((object)model);
        Assert.Equal(13, rows.Count);
        Assert.DoesNotContain(rows, r => r.Name.Contains("CardPool", StringComparison.Ordinal)); // no source in the summary: no row
        var brawl = Assert.Single(rows, r => r.Attention);
        Assert.Equal((Loc.T("Source.Health.Section.Brawl"), Loc.F("Source.Health.Date", Late.ToLocalTime()), "Update delayed"), (brawl.Name, brawl.Date, brawl.State));
        var matchups = Assert.Single(rows, r => r.Name == Loc.T("Source.Health.Section.ConstructedMatchups"));
        Assert.Equal(("Waiting for the source to publish", false), (matchups.State, matchups.Attention));
        var arena = Assert.Single(rows, r => r.Name == "Arena");
        Assert.Equal((Loc.F("Source.Health.Date", Collected.ToLocalTime()), "Up to date"), (arena.Date, arena.State));
        Assert.Equal(Loc.F("Source.Health.Checked", Checked.ToLocalTime()), (string)model.DataHealthNote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_late_source_gives_the_delay_line_on_its_own_page_only(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);
        model.SetDataHealth(Report());
        Assert.Equal((string.Empty, string.Empty, string.Empty), ((string)model.MetaDataDelay, (string)model.ArenaDataDelay, (string)model.BrawlDataDelay));

        model.SetDataHealth(Report(false, ("hsreplay_arena_class_pages_firecrawl", DataFreshness.Stale)));

        Assert.Equal(Loc.F("Source.Health.Delay", Late.ToLocalTime()), (string)model.ArenaDataDelay);
        Assert.StartsWith("Data from 12 Nov", (string)model.ArenaDataDelay, StringComparison.Ordinal);
        Assert.Equal((string.Empty, string.Empty), ((string)model.MetaDataDelay, (string)model.BrawlDataDelay));

        // A failed collection reads the same on the page; waiting for the origin and an unknown state give no line.
        model.SetDataHealth(Report(false, ("hsguru_meta_matrix", DataFreshness.Failed), ("hsguru_brawl", DataFreshness.Pending), ("hsreplay_arena_cards_advanced", DataFreshness.Unknown)));
        Assert.Equal(Loc.F("Source.Health.Delay", Late.ToLocalTime()), (string)model.MetaDataDelay);
        Assert.Equal((string.Empty, string.Empty), ((string)model.ArenaDataDelay, (string)model.BrawlDataDelay));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Offline_shows_no_delay_line_and_no_state_and_says_so(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);

        model.SetDataHealth(Report(true, ("hsguru_brawl", DataFreshness.Stale)));

        Assert.Equal(string.Empty, (string)model.BrawlDataDelay);
        var rows = Rows((object)model);
        Assert.All(rows, r => Assert.Equal((Loc.T("Source.Health.State.Unknown"), false), (r.State, r.Attention)));
        Assert.Contains(rows, r => r.Date != "—"); // the dates of the last check stay
        Assert.Equal(Loc.F("Source.Health.Offline", Checked.ToLocalTime()), (string)model.DataHealthNote);

        model.SetDataHealth(DataHealthReport.None);
        Assert.Equal(Loc.T("Source.Health.OfflineNever"), (string)model.DataHealthNote);
        Assert.All(Rows((object)model), r => Assert.Equal("—", r.Date));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Check_now_asks_the_host_once_until_the_summary_arrives(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);
        model.SetDataHealth(Report());
        var asked = 0;
        model.DataHealthCheckRequested += new Action(() => asked++);

        model.CheckDataHealth.Execute(null);
        model.CheckDataHealth.Execute(null);

        Assert.Equal(1, asked);
        Assert.True((bool)model.DataHealthChecking);
        Assert.Equal(Loc.T("Source.Health.Checking"), (string)model.DataHealthNote);

        model.SetDataHealth(Report());
        Assert.False((bool)model.DataHealthChecking);
        model.CheckDataHealth.Execute(null);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void The_battlegrounds_tabs_carry_the_line_under_the_late_lists_source_only_in_both_apps()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data) { BgClock = () => Checked };
        var ava = new AvaMain(History(), new AppSettings(), TestApp.Data) { BgClock = () => Checked };
        var stats = BgSiteTabsContractTests.Stats();
        wpf.SetBgSiteStats(stats);
        ava.SetBgSiteStats(stats);
        var plain = wpf.HeroTab.Source;
        Assert.DoesNotContain('\n', plain);

        var late = Report(false, ("hsreplay_battlegrounds_hero_details", DataFreshness.Stale));
        wpf.SetDataHealth(late);
        ava.SetDataHealth(late);

        Assert.Equal(plain + "\n" + Loc.F("Source.Health.Delay", Late.ToLocalTime()), wpf.HeroTab.Source);
        Assert.Equal(wpf.HeroTab.Source, ava.HeroTab.Source);
        Assert.Equal((wpf.TrinketTab.Source, wpf.CardTab.Source, wpf.CompTab.Source), (ava.TrinketTab.Source, ava.CardTab.Source, ava.CompTab.Source));
        Assert.All(new[] { wpf.TrinketTab.Source, wpf.CardTab.Source, wpf.CompTab.Source }, s => Assert.DoesNotContain('\n', s));

        // Back in time: the line goes.
        wpf.SetDataHealth(Report());
        Assert.Equal(plain, wpf.HeroTab.Source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_arena_page_and_the_draft_panel_date_their_numbers(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);
        Assert.Equal(string.Empty, (string)model.ArenaDataDate);
        model.SetArenaMeta(new ArenaMeta([], [new ArenaClassStat("MAGE", 0.52, 0.1, 0.05, 1000)], Collected));
        Assert.Equal(Loc.F("Source.Health.Updated", Collected.ToLocalTime()), (string)model.ArenaDataDate);
        Assert.StartsWith("Hearthpulse data · updated 15 Nov", (string)model.ArenaDataDate, StringComparison.Ordinal);

        dynamic overlay = wpf ? new WpfOverlay() : new AvaOverlay();
        Assert.Equal((string.Empty, string.Empty), ((string)overlay.ArenaDataNote, (string)overlay.BgHeroDataNote));

        overlay.SetArenaDataDate((DateTime?)Collected);
        Assert.Equal(Loc.F("Source.Health.Overlay", Collected.ToLocalTime()), (string)overlay.ArenaDataNote);

        overlay.SetDataHealth(Report(false, ("hsreplay_arena_cards_advanced", DataFreshness.Stale)));
        Assert.Equal(Loc.F("Source.Health.OverlayDelayed", Collected.ToLocalTime()), (string)overlay.ArenaDataNote);
        Assert.EndsWith("update delayed", (string)overlay.ArenaDataNote, StringComparison.Ordinal);

        // No hero badges on screen: no line under them, whatever date the stats have.
        overlay.SetBgData((DateTime?)Collected, true);
        Assert.Equal(string.Empty, (string)overlay.BgHeroDataNote);
    }

    private static LiveSnapshot Pick(IReadOnlyList<BgHeroOption> heroes, IReadOnlyList<BgTrinketOption> trinkets) => LiveSnapshot.Empty with
    {
        InGame = true,
        HearthstoneRunning = true,
        Battlegrounds = new BattlegroundsView(heroes.Count > 0 ? 0 : 6, 3, 6, heroes.Count > 0 ? null : "HERO_ME", 30, 0, 2, false, false, heroes.Count > 0, heroes,
            ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "MECHANICAL"], true, [], [], [], [], null, 0, [], [])
        {
            TrinketOptions = trinkets,
        },
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_hero_pick_has_one_date_line_and_it_says_when_heroes_or_their_comps_are_late(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic overlay = wpf ? new WpfOverlay() : new AvaOverlay();
        overlay.SetBgData((DateTime?)Collected, true);
        overlay.SetDataHealth(Report());
        var heroes = new[]
        {
            new BgHeroOption("HERO_A", "Hero A", 0, null, new HeroMeta(4.0, 3.9, 0.3, 0, "A"), true, [new BgHeroComp("Demons", "A", "easy", null)]),
            new BgHeroOption("HERO_B", "Hero B", 0, null, new HeroMeta(4.2, 4.2, 0.2, 0, "B")),
        };

        overlay.Update(Pick(heroes, []), CardDb.Empty, new AppSettings(), null, null);

        var day = Collected.ToLocalTime().ToString("d MMM", Loc.DateCulture);
        Assert.Equal(2, ((IEnumerable<dynamic>)overlay.HeroPicks).Count());
        Assert.Equal($"data from {day}", (string)overlay.BgHeroDataNote);

        // The comps on the badges come from their own collector: its delay marks the same line.
        overlay.SetDataHealth(Report(false, ("hsreplay_battlegrounds_compositions", DataFreshness.Stale)));
        Assert.Equal($"data from {day} · update delayed", (string)overlay.BgHeroDataNote);
        overlay.SetDataHealth(Report(false, ("hsreplay_battlegrounds_heroes", DataFreshness.Failed)));
        Assert.Equal($"data from {day} · update delayed", (string)overlay.BgHeroDataNote);
        // Stats that are the stats pool's alone: the summary's collectors are not theirs, so the date stays plain.
        overlay.SetBgData((DateTime?)Collected, false);
        Assert.Equal($"data from {day}", (string)overlay.BgHeroDataNote);
        overlay.SetDataHealth(Report(false, ("hsreplay_battlegrounds_hero_details", DataFreshness.Stale), ("hsreplay_battlegrounds_compositions", DataFreshness.Failed)));
        Assert.Equal($"data from {day}", (string)overlay.BgHeroDataNote);
        overlay.SetBgData((DateTime?)Collected, true);
        Assert.Equal($"data from {day} · update delayed", (string)overlay.BgHeroDataNote);

        // Another section's delay is not the heroes'.
        overlay.SetDataHealth(Report(false, ("hsguru_brawl", DataFreshness.Stale), ("hsreplay_arena_cards_advanced", DataFreshness.Stale)));
        Assert.Equal($"data from {day}", (string)overlay.BgHeroDataNote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_trinket_badge_keeps_its_one_date_and_marks_only_external_numbers_as_delayed(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic overlay = wpf ? new WpfOverlay() : new AvaOverlay();
        var trinkets = new[]
        {
            new BgTrinketOption("TRK_EXTERNAL", "External", new TrinketMeta(3.47, 0.277, 2528, "S", External: true, GamesAreMinimum: true, Updated: Collected), true),
            new BgTrinketOption("TRK_POOL", "Pool", new TrinketMeta(3.9, 0.5, 240, "A", Updated: Collected)),
            new BgTrinketOption("TRK_NONE", "None"),
        };
        var snapshot = Pick([], trinkets);
        var day = Collected.ToLocalTime().ToString("d MMM", Loc.DateCulture);
        string[] Dates()
        {
            var dates = new List<string>();
            foreach (var badge in overlay.TrinketPicks)
            {
                dates.Add((string)badge.Updated);
            }

            return [.. dates];
        }

        overlay.SetBgData((DateTime?)Late, true); // the meta's own date is not a second date on the badges
        overlay.SetDataHealth(Report());
        overlay.Update(snapshot, CardDb.Empty, new AppSettings(), null, null);
        Assert.Equal([$"data from {day}", $"data from {day}", string.Empty], Dates());

        // The trinket slices' collector is late: the data API's badge says so, the pool's own numbers do not.
        overlay.SetDataHealth(Report(false, ("hsreplay_battlegrounds_trinkets_top_20_percent_last_7_days", DataFreshness.Stale)));
        overlay.Update(snapshot, CardDb.Empty, new AppSettings(), null, null);
        Assert.Equal([$"data from {day} · update delayed", $"data from {day}", string.Empty], Dates());

        // Heroes late, trinkets fine: the badges are plain again.
        overlay.SetDataHealth(Report(false, ("hsreplay_battlegrounds_heroes", DataFreshness.Stale)));
        overlay.Update(snapshot, CardDb.Empty, new AppSettings(), null, null);
        Assert.Equal([$"data from {day}", $"data from {day}", string.Empty], Dates());
    }

    [Fact]
    public void Russian_names_the_source_as_hearthpulse_data()
    {
        Loc.Instance.SetLanguage("ru");
        try
        {
            var model = new AvaMain(History(), new AppSettings(), TestApp.Data);
            model.SetDataHealth(Report(false, ("hsguru_brawl", DataFreshness.Stale)));

            Assert.EndsWith("обновление задерживается.", model.BrawlDataDelay, StringComparison.Ordinal);
            Assert.Contains(model.DataHealthRows, r => r is { Name: "Потасовка", State: "Обновление задерживается", Attention: true });
            Assert.StartsWith("данные от ", Loc.F("Source.Health.Overlay", Collected), StringComparison.Ordinal);
            Assert.StartsWith("Данные Hearthpulse · обновлено ", Loc.F("Source.Health.Updated", Collected), StringComparison.Ordinal);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    [Fact]
    public void The_wpf_data_tab_has_the_card_between_your_data_and_the_settings_file()
    {
        var failure = default(Exception);
        var thread = new Thread(() =>
        {
            try
            {
                Loc.Instance.SetLanguage(Loc.English);
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
                var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
                model.SetDataHealth(Report(false, ("hsguru_brawl", DataFreshness.Stale)));
                var tab = new Hearthpulse.App.Main.SettingsTabs.DataSettings { DataContext = model };
                tab.Measure(new Size(780, double.PositiveInfinity));
                tab.Arrange(new Rect(tab.DesiredSize));
                tab.UpdateLayout();

                var card = Assert.IsAssignableFrom<FrameworkElement>(LogicalTreeHelper.FindLogicalNode(tab, "DataHealthCard"));
                var file = Assert.IsAssignableFrom<FrameworkElement>(LogicalTreeHelper.FindLogicalNode(tab, "SettingsFileCard"));
                Assert.True(card.TranslatePoint(default, tab).Y < file.TranslatePoint(default, tab).Y);
                var list = Assert.IsType<System.Windows.Controls.ItemsControl>(LogicalTreeHelper.FindLogicalNode(tab, "DataHealthList"));
                Assert.Equal(13, list.Items.Count);
                var button = Assert.IsType<System.Windows.Controls.Button>(LogicalTreeHelper.FindLogicalNode(tab, "DataHealthCheck"));
                Assert.Equal("Check now", button.Content);
                Assert.Same(model.CheckDataHealth, button.Command);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Data settings check failed", failure);
        }
    }

    private static List<(string Name, string Date, string State, bool Attention)> Rows(object source)
    {
        dynamic model = source;
        var rows = new List<(string, string, string, bool)>();
        foreach (var row in model.DataHealthRows)
        {
            rows.Add(((string)row.Name, (string)row.Date, (string)row.State, (bool)row.Attention));
        }

        return rows;
    }
}

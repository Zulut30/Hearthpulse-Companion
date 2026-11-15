using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Pool;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The block "Hearthpulse statistics (own games)" of the Battlegrounds Heroes tab in both hosts (roadmap
/// 2026-11-15-hsreplay-remediation, C1): the WPF and the Avalonia view model show the same state, rows, lines and
/// date for the same pool answer, and the WPF page shows the block only on the Heroes tab and only when a pool
/// answered. The Avalonia page and the two markups side by side: <c>Hearthpulse.Tests/BgOwnTests</c>.
/// </summary>
public sealed class BgOwnContractTests
{
    private static readonly DateTime Fetched = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The C5 response of docs/analytics-api.md: one complete hero, one with counts only, one hidden.</summary>
    private const string Answer =
        """
        {
          "v": 2, "metric": "bg.hero.offer_pick_placement", "rulesVersion": "bg-hero-1",
          "mode": "solo", "available": true, "reason": null,
          "published": 1762646400000, "stale": true, "source": "source_capture",
          "period": { "fromDay": 20371, "toDay": 20401, "days": 30 },
          "groups": [
            { "build": 223456, "captures": 120, "sources": 14, "suppressed": 3,
              "heroes": [
                { "hero": "TB_BaconShop_HERO_xx", "offered": 40, "picked": 12,
                  "pickRate":  { "num": 12, "den": 40, "value": 0.3,   "low": 0.181, "high": 0.454 },
                  "placement": { "n": 12, "mean": 3.92, "low": 3.05, "high": 4.79, "dist": [2,1,2,2,1,2,1,1] },
                  "top4":      { "num": 7,  "den": 12, "value": 0.583, "low": 0.320, "high": 0.807 },
                  "first":     { "num": 2,  "den": 12, "value": 0.167, "low": 0.047, "high": 0.448 },
                  "quality": "ok" },
                { "hero": "TB_BaconShop_HERO_yy", "offered": 9, "picked": 5,
                  "pickRate":  { "num": 5, "den": 9, "value": null, "low": null, "high": null },
                  "placement": { "n": 5, "mean": null, "low": null, "high": null, "dist": null },
                  "top4":      { "num": 4, "den": 5, "value": null, "low": null, "high": null },
                  "first":     { "num": 0, "den": 5, "value": null, "low": null, "high": null },
                  "quality": "small_sample" },
                { "hero": "TB_BaconShop_HERO_zz", "offered": 6, "picked": null,
                  "pickRate": null, "placement": null, "top4": null, "first": null,
                  "quality": "small_sample" } ] },
            { "build": 220000, "captures": 900, "sources": 60, "suppressed": 0, "heroes": [] }
          ]
        }
        """;

    private static OwnBgHeroesResult Published(bool oldCopy = false) =>
        OwnBgHeroesResult.FromJson(Answer, Fetched, oldCopy) ?? throw new InvalidOperationException("answer");

    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void Both_view_models_show_the_same_state_rows_and_date_for_the_same_answer()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var ava = new AvaMain(History(), new AppSettings(), TestApp.Data);

        void Same(string what, bool shown)
        {
            Assert.Equal((shown, what), (wpf.ShowBgOwn, what));
            Assert.Equal((wpf.ShowBgOwn, wpf.BgOwn.ToString(), what), (ava.ShowBgOwn, ava.BgOwn.ToString(), what));
            Assert.Equal(wpf.BgOwnRows.Select(r => r.ToString()), ava.BgOwnRows.Select(r => r.ToString()));
        }

        void Set(OwnBgHeroesResult result)
        {
            wpf.SetBgOwn(result);
            ava.SetBgOwn(result);
        }

        // My stats is open and nothing is loaded.
        Same("start", shown: false);
        wpf.BgTabShown("Heroes");
        ava.BgTabShown("Heroes");
        Same("heroes, not configured", shown: false);

        Set(Published());
        Same("published", shown: true);
        Assert.Equal(3, wpf.BgOwnRows.Count);
        Assert.StartsWith("game build 223456 · 120 games from 14 installations · ", wpf.BgOwn.Summary, StringComparison.Ordinal);
        Assert.EndsWith(Loc.T("Main.BgOwn.Stale"), wpf.BgOwn.Summary, StringComparison.Ordinal);
        Assert.Equal(Loc.T("Main.BgOwn.Small"), wpf.BgOwnRows[2].Place);

        Set(Published(oldCopy: true));
        Same("old copy", shown: true);
        Assert.EndsWith(Loc.F("Main.BgOwn.OldCopy", Fetched.ToLocalTime()), wpf.BgOwn.Summary, StringComparison.Ordinal);

        foreach (var (state, key) in new[]
                 {
                     (OwnBgHeroesState.NotPublished, "Main.BgOwn.NotPublished"), (OwnBgHeroesState.Rebuilding, "Main.BgOwn.Rebuilding"),
                     (OwnBgHeroesState.Offline, "Main.BgOwn.Offline"),
                 })
        {
            Set(OwnBgHeroesResult.Of(state));
            Same(state.ToString(), shown: true);
            Assert.Empty(wpf.BgOwnRows);
            Assert.Equal(Loc.T(key), wpf.BgOwn.EmptyText);
        }

        foreach (var hidden in new[] { OwnBgHeroesState.NotConfigured, OwnBgHeroesState.Unsupported })
        {
            Set(OwnBgHeroesResult.Of(hidden));
            Same(hidden.ToString(), shown: false);
        }

        Set(Published());
        wpf.BgTabShown("Comps");
        ava.BgTabShown("Comps");
        Same("another tab", shown: false);
    }

    [Fact]
    public void The_WPF_page_shows_the_block_on_the_Heroes_tab_with_the_models_texts() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump();
        try
        {
            window.ShowPage("Battlegrounds");
            model.SetBgOwn(Published());
            Pump();
            var page = window.PageBattlegrounds;

            // My stats is open: the block belongs to the Heroes tab.
            Assert.False(page.BgOwnCard.IsVisible);
            window.BattlegroundsTab = "Heroes";
            Pump();
            Assert.True(page.BgOwnCard.IsVisible);
            Assert.Equal(model.BgOwn.Summary, page.BgOwnSummary.Text);
            Assert.Equal(model.BgOwn.Note, page.BgOwnNote.Text);
            Assert.Equal(3, page.BgOwnList.Items.Count);
            var texts = VisualChildren(page.BgOwnList).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(model.BgOwnRows[0].PlaceDetail, texts);
            Assert.Equal(7, texts.Count(t => t == Loc.T("Main.BgOwn.Small")));
            Assert.False(page.BgOwnEmpty.IsVisible);
            Assert.Contains(VisualChildren(page.BgOwnCard).OfType<TextBlock>(), t => t.Text == Loc.T("Main.BgOwn.Offered") && t.IsVisible);

            // Nothing published: the text in place of the table.
            model.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotPublished));
            Pump();
            Assert.True(page.BgOwnEmpty.IsVisible);
            Assert.Equal(Loc.T("Main.BgOwn.NotPublished"), page.BgOwnEmpty.Content);
            Assert.False(page.BgOwnList.IsVisible);

            // Another tab, and no pool: no block.
            window.BattlegroundsTab = "Trinkets";
            Pump();
            Assert.False(page.BgOwnCard.IsVisible);
            window.BattlegroundsTab = "Heroes";
            Pump();
            Assert.True(page.BgOwnCard.IsVisible);
            model.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
            Pump();
            Assert.False(page.BgOwnCard.IsVisible);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in VisualChildren(child))
            {
                yield return nested;
            }
        }
    }
}

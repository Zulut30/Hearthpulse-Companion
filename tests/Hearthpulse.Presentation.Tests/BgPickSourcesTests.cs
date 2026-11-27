using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The hero and trinket badges on external data (docs/bg-meta-sources.md, section 4), in both compiled overlays: a
/// hero's own comps with their tier, difficulty and advice in the tooltip (comps every hero shares go to the tier
/// badge's tooltip, O-21); the pick rate in the tooltip; a trinket's source by name and its sample as a lower bound,
/// never as a count of games, and its date on the badge only while the source is late (O-22).
/// </summary>
public sealed class BgPickSourcesTests
{
    private static readonly DateTime Fetched = new(2026, 11, 15, 17, 1, 0, DateTimeKind.Utc);

    private static BattlegroundsView View(IReadOnlyList<BgHeroOption> heroes, IReadOnlyList<BgTrinketOption> trinkets) =>
        new(heroes.Count > 0 ? 0 : 6, 3, 6, heroes.Count > 0 ? null : "HERO_ME", 30, 0, 2, false, false, heroes.Count > 0, heroes,
            ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "MECHANICAL"], true, [], [], [], [], null, 0, [], [])
        {
            TrinketOptions = trinkets,
        };

    private static LiveSnapshot Live(BattlegroundsView view) => LiveSnapshot.Empty with { Battlegrounds = view, InGame = true, HearthstoneRunning = true };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_hero_badge_names_its_own_comps_and_the_tooltip_gives_tier_difficulty_and_advice(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        var heroes = new[]
        {
            new BgHeroOption("HERO_A", "Hero A", 0, null, new HeroMeta(4.0, 3.9, 0.3, 0, "A"), true,
                [new BgHeroComp("Demons - Self damage", "A", "easy", "Hurt your own hero.", ByHero: true), new BgHeroComp("Pool comp", string.Empty, string.Empty, null)]),
            new BgHeroOption("HERO_B", "Hero B", 0, null, new HeroMeta(4.2, 4.2, 0.2, 0, "B")),
            new BgHeroOption("HERO_C", "Hero C", 0, null, new HeroMeta(4.1, 4.1, 0.25, 0, "B"), false,
                [new BgHeroComp("Demons - Self damage", "A", "easy", "Hurt your own hero."), new BgHeroComp("Pool comp", string.Empty, string.Empty, null)]),
        };

        model.Update(Live(View(heroes, [])), CardDb.Empty, new AppSettings(), null, null);

        var picks = ((IEnumerable<dynamic>)model.HeroPicks).ToList();
        var tiers = Loc.T("Overlay.Tier.Heroes");
        var comps = "Best comps available\n\nDemons - Self damage · tier A · Difficulty: easy\nHurt your own hero.\n\nPool comp";
        Assert.Equal("Demons - Self damage · Pool comp", (string)picks[0].Comps);
        Assert.Equal(comps, (string)picks[0].CompsTip);
        // How often players pick the hero: under the tier scale in the tier badge's tooltip, not a line of the badge.
        Assert.Equal(tiers + "\n" + Loc.F("Overlay.Badge.PickRate", 0.3.ToString("P0", System.Globalization.CultureInfo.CurrentCulture)), (string)picks[0].TierTip);
        // No comps: no line and no tooltip.
        Assert.Equal((string.Empty, string.Empty), ((string)picks[1].Comps, (string)picks[1].CompsTip));
        // Comps in the order every hero shares (none of the hero's own games behind it): no line, the tier tooltip has them.
        Assert.Equal((string.Empty, string.Empty), ((string)picks[2].Comps, (string)picks[2].CompsTip));
        Assert.EndsWith("\n\n" + comps, (string)picks[2].TierTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_trinket_badge_dates_its_numbers_and_calls_an_external_sample_a_lower_bound(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        var trinkets = new[]
        {
            new BgTrinketOption("TRK_EXTERNAL", "External", new TrinketMeta(3.47, 0.277, 2528, "S", External: true, GamesAreMinimum: true, Updated: Fetched), true),
            new BgTrinketOption("TRK_POOL", "Pool", new TrinketMeta(3.9, 0.5, 240, "A", Updated: Fetched)),
            new BgTrinketOption("TRK_NONE", "None"),
        };

        model.Update(Live(View([], trinkets)), CardDb.Empty, new AppSettings(), null, null);

        var badges = ((IEnumerable<dynamic>)model.TrinketPicks).ToList();
        var local = Fetched.ToLocalTime();
        var day = local.ToString("d MMM", Loc.DateCulture);
        var time = local.ToString("d MMM HH:mm", Loc.DateCulture);
        var tiers = Loc.T("Overlay.Tier.Heroes");

        string Pick(double rate) => Loc.F("Overlay.Badge.PickRate", rate.ToString("P0", System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal(3.47.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture), (string)badges[0].Place);
        // Fresh numbers: no date on the badge (DataHealthContractTests has the late ones); the pick rate is in the tooltip.
        Assert.Equal((string.Empty, string.Empty), ((string)badges[0].Updated, (string)badges[0].Note));
        Assert.Equal($"{tiers}\n{Pick(0.277)}\nHearthpulse data · updated {time} · at least {2528:N0} games (a lower bound)", (string)badges[0].TierTip);
        // The pool's own numbers keep the pool's name and are a count.
        Assert.Equal($"{tiers}\n{Pick(0.5)}\nHearthpulse stats pool · updated {time} · {240:N0} games", (string)badges[1].TierTip);
        // Nothing known: no date, the badge says so, and the tooltip explains the tiers alone.
        Assert.Equal((string.Empty, Loc.T("Overlay.Badge.NoData"), tiers), ((string)badges[2].Updated, (string)badges[2].Note, (string)badges[2].TierTip));
        Assert.NotEmpty(day);

        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            model.Update(Live(View([], trinkets)), CardDb.Empty, new AppSettings(), null, null);
            var russian = ((IEnumerable<dynamic>)model.TrinketPicks).First();
            Assert.Equal(string.Empty, (string)russian.Updated);
            Assert.Contains("выбор ", (string)russian.TierTip, StringComparison.Ordinal);
            Assert.Contains("Данные Hearthpulse · обновлено ", (string)russian.TierTip, StringComparison.Ordinal);
            Assert.EndsWith($"не меньше {2528:N0} игр (оценка снизу)", (string)russian.TierTip, StringComparison.Ordinal);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    /// <summary>
    /// The WPF badge templates built with data (a XAML mistake shows only when the template is loaded): the hero badge
    /// has its comps line with the tooltip, the trinket badge its date line and its "no data" note; without them those
    /// lines are absent, and neither badge has a pick rate line.
    /// </summary>
    [Fact]
    public void Wpf_badges_show_the_comps_line_and_the_date_line() => WpfThread.RunThemed(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        var panels = new System.Windows.ResourceDictionary { Source = new Uri("/Hearthpulse;component/Overlay/OverlayPanels.xaml", UriKind.Relative) };

        var hero = Shown(panels, "HeroPickTemplate", new Hearthpulse.App.Overlay.HeroPickCard("HERO_A", "Hero A", "A", "3.90", "avg place", string.Empty, 0,
            string.Empty, "Demons - Self damage", true, true) { CompsTip = "tip" });
        var comps = Assert.Single(hero, t => t.Text == "Demons - Self damage");
        Assert.Equal("tip", comps.ToolTip);
        Assert.True(Hearthpulse.App.Overlay.OverlayHit.GetInteractive(comps));

        var bare = Shown(panels, "HeroPickTemplate", new Hearthpulse.App.Overlay.HeroPickCard("HERO_B", "Hero B", "B", "4.20", "avg place", string.Empty, 0,
            string.Empty, string.Empty, false, true));
        Assert.Equal(["B", "4.20", "avg place"], bare.Select(t => t.Text).Where(t => t.Length > 0 && !t.Any(char.IsSurrogate) && t != Loc.T("Overlay.Badge.BestPick")));

        var trinket = Shown(panels, "TrinketBadgeTemplate", new Hearthpulse.App.Overlay.TrinketBadge("TRK", "Trinket", "S", "3.47", string.Empty, false, true) { Updated = "data from 15 Nov" });
        Assert.Contains(trinket, t => t.Text == "data from 15 Nov");
        var undated = Shown(panels, "TrinketBadgeTemplate", new Hearthpulse.App.Overlay.TrinketBadge("TRK", "Trinket", "S", "3.47", string.Empty, false, true));
        Assert.Equal(["S", "3.47", "avg place"], undated.Select(t => t.Text).Where(t => t.Length > 0 && !t.Any(char.IsSurrogate) && t != Loc.T("Overlay.Badge.BestPick")));
        var unknown = Shown(panels, "TrinketBadgeTemplate", new Hearthpulse.App.Overlay.TrinketBadge("TRK", "Trinket", "–", "—", "no data", false, false));
        Assert.Contains(unknown, t => t.Text == "no data");
    });

    /// <summary>The visible text blocks of a template of the overlay's panels, laid out with <paramref name="item"/>.</summary>
    private static List<System.Windows.Controls.TextBlock> Shown(System.Windows.ResourceDictionary panels, string template, object item)
    {
        var host = new System.Windows.Controls.ContentControl
        {
            Resources = panels,
            Content = item,
            ContentTemplate = (System.Windows.DataTemplate)panels[template],
        };
        host.Measure(new System.Windows.Size(400, 300));
        host.Arrange(new System.Windows.Rect(0, 0, 400, 300));
        // Bindings attach at DataBind priority: let them, then lay out again.
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        host.UpdateLayout();
        return [.. Texts(host).Where(t => Visible(t, host))];

        static IEnumerable<System.Windows.Controls.TextBlock> Texts(System.Windows.DependencyObject root)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.TextBlock text)
                {
                    yield return text;
                }

                foreach (var nested in Texts(child))
                {
                    yield return nested;
                }
            }
        }

        static bool Visible(System.Windows.DependencyObject element, System.Windows.DependencyObject root)
        {
            for (var e = element; e is not null && !ReferenceEquals(e, root); e = System.Windows.Media.VisualTreeHelper.GetParent(e))
            {
                if (e is System.Windows.UIElement { Visibility: not System.Windows.Visibility.Visible })
                {
                    return false;
                }
            }

            return true;
        }
    }
}

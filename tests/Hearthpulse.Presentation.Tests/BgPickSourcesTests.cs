using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The hero and trinket badges on external data (docs/bg-meta-sources.md, section 4), in both compiled overlays: a
/// hero's comps with their tier, difficulty and advice in the tooltip; a trinket's date, its source by name and its
/// sample as a lower bound, never as a count of games.
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
    public void A_hero_badge_names_its_comps_and_the_tooltip_gives_tier_difficulty_and_advice(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        var heroes = new[]
        {
            new BgHeroOption("HERO_A", "Hero A", 0, null, new HeroMeta(4.0, 3.9, 0.3, 0, "A"), true,
                [new BgHeroComp("Demons - Self damage", "A", "easy", "Hurt your own hero."), new BgHeroComp("Pool comp", string.Empty, string.Empty, null)]),
            new BgHeroOption("HERO_B", "Hero B", 0, null, new HeroMeta(4.2, 4.2, 0.2, 0, "B")),
        };

        model.Update(Live(View(heroes, [])), CardDb.Empty, new AppSettings(), null, null);

        var picks = ((IEnumerable<dynamic>)model.HeroPicks).ToList();
        Assert.Equal("Demons - Self damage · Pool comp", (string)picks[0].Comps);
        Assert.Equal("Best comps available\n\nDemons - Self damage · tier A · Difficulty: easy\nHurt your own hero.\n\nPool comp", (string)picks[0].CompsTip);
        // No comps: no line and no tooltip.
        Assert.Equal((string.Empty, string.Empty), ((string)picks[1].Comps, (string)picks[1].CompsTip));
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

        Assert.Equal(3.47.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture), (string)badges[0].Place);
        Assert.Equal($"data from {day}", (string)badges[0].Updated);
        Assert.Equal($"{tiers}\nHearthpulse data · updated {time} · at least {2528:N0} games (a lower bound)", (string)badges[0].TierTip);
        // The pool's own numbers keep the pool's name and are a count.
        Assert.Equal($"{tiers}\nHearthpulse stats pool · updated {time} · {240:N0} games", (string)badges[1].TierTip);
        // Nothing known: no date, and the tooltip explains the tiers alone.
        Assert.Equal((string.Empty, tiers), ((string)badges[2].Updated, (string)badges[2].TierTip));

        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            model.Update(Live(View([], trinkets)), CardDb.Empty, new AppSettings(), null, null);
            var russian = ((IEnumerable<dynamic>)model.TrinketPicks).First();
            Assert.StartsWith("данные от ", (string)russian.Updated, StringComparison.Ordinal);
            Assert.Contains("Данные Hearthpulse · обновлено ", (string)russian.TierTip, StringComparison.Ordinal);
            Assert.EndsWith($"не меньше {2528:N0} игр (оценка снизу)", (string)russian.TierTip, StringComparison.Ordinal);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }
}

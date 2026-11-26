using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The Battlegrounds bar ("bgbar") and the combat odds ("odds") in both hosts: the lobby's tribes as a line of text,
/// the tribe filter in the tier list, the bar waiting for the first tavern turn, and the odds cells with damage ranges,
/// unsimulated effects in the "≈" tooltip and a bottom line only while the damage cap cuts the range (O-19, O-24).
/// </summary>
public sealed class BgBarTests : IDisposable
{
    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static BattlegroundsView View(IReadOnlyList<string> races, CombatOdds? odds = null) =>
        new(11, 5, 6, "TB_BaconShop_HERO_72", 5, 2, 6, false, true, false, [], races, true, [], [], [], [], null, 0, [], [])
        { CombatOdds = odds };

    private static dynamic Model(bool wpf, BattlegroundsView view)
    {
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        model.Update(LiveSnapshot.Empty with { Battlegrounds = view, InGame = true }, CardDb.Empty,
            new AppSettings { ShowTurnTimer = false, ShowCombatOdds = true }, null, null);
        return model;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_bar_lists_the_lobby_tribes_as_text_and_says_when_they_are_still_read(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("Tribes: Beast, Dragon", (string)Model(wpf, View(["BEAST", "DRAGON"])).TribesLine);
        Assert.Equal("Tribes: reading the lobby…", (string)Model(wpf, View([])).TribesLine);
        // The view styles the label apart from the values: split where the key puts its value.
        var known = Model(wpf, View(["BEAST", "DRAGON"]));
        Assert.Equal(("Tribes:", "Beast, Dragon"), ((string)known.TribesLabel, (string)known.TribesNames));
        var reading = Model(wpf, View([]));
        Assert.Equal(("Tribes:", "reading the lobby…"), ((string)reading.TribesLabel, (string)reading.TribesNames));

        Loc.Instance.SetLanguage(Loc.Russian);
        Assert.StartsWith("Расы: ", (string)Model(wpf, View(["BEAST", "DRAGON"])).TribesLine, StringComparison.Ordinal);
        Assert.Equal("Расы: лобби считывается…", (string)Model(wpf, View([])).TribesLine);
        Assert.Equal(("Расы:", "лобби считывается…"), ((string)Model(wpf, View([])).TribesLabel, (string)Model(wpf, View([])).TribesNames));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tier_button_opens_the_list_whose_tribe_chips_filter_it(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf, View(["BEAST", "DRAGON"]));

        model.SelectTier.Execute(3);
        Assert.Equal(3, (int)model.SelectedTier);
        Assert.Equal("Tier 3", (string)model.BrowserTitle);
        Assert.Equal(new[] { "BEAST", "DRAGON" }, ((IEnumerable<dynamic>)model.TribeChips).Select(c => (string)c.Key));

        model.SelectTribe.Execute("DRAGON");
        Assert.Equal("Dragon", (string)model.BrowserScope);
        Assert.Equal(3, (int)model.SelectedTier);
        // The same chip again lifts the filter.
        model.SelectTribe.Execute("DRAGON");
        Assert.DoesNotContain((IEnumerable<dynamic>)model.TribeChips, c => (bool)c.Selected);
        Assert.Equal("minions in this lobby", (string)model.BrowserScope);
    }

    private static CombatOdds Odds(int unsimulated = 1, double lethal = 0.311, double kill = 0.197, int cap = 0) =>
        new(0.355, 0.324, 0.321, unsimulated)
        {
            Runs = 4000,
            DamageDealt = new DamageSpread(12, 8, 16, 20, [1]),
            DamageTaken = new DamageSpread(13, 9, 17, 21, [1]),
            LethalTaken = lethal,
            LethalDealt = kill,
            DamageCap = cap,
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Odds_show_damage_ranges_both_ways_and_unsimulated_effects_in_the_mark_tooltip(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = Model(wpf, View(["BEAST"], Odds()));

        Assert.Equal("Нанесёте 8–16", (string)model.OddsDamageWin);
        Assert.Equal("Получите 9–17", (string)model.OddsDamageLoss);
        // Not a line of its own any more: the "≈" mark says the odds are not exact, its tooltip says why.
        Assert.Equal(string.Empty, (string)model.OddsNote);
        Assert.True((bool)model.OddsEstimated);
        Assert.Equal("1 эффект не учтён", (string)model.OddsEstimatedTip);
        // The cells carry the label, the value is just the share.
        Assert.Equal(Hearthpulse.App.Overlay.OverlayFormat.Percent(0.311, 1), (string)model.OddsLethal);
        Assert.Equal(Hearthpulse.App.Overlay.OverlayFormat.Percent(0.197, 1), (string)model.OddsKill);
        Assert.True((bool)model.LethalDanger);

        Loc.Instance.SetLanguage(Loc.English);
        model = Model(wpf, View(["BEAST"], Odds()));
        Assert.Equal(("You deal 8–16", "You take 9–17"), ((string)model.OddsDamageWin, (string)model.OddsDamageLoss));
        Assert.Equal("1 effect not simulated", (string)model.OddsEstimatedTip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Odds_drop_empty_lethal_cells_and_show_the_cap_only_where_the_damage_reaches_it(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf, View(["BEAST"], Odds(unsimulated: 0, lethal: 0, kill: 0)));
        Assert.Equal(string.Empty, (string)model.OddsLethal);
        Assert.Equal(string.Empty, (string)model.OddsKill);
        Assert.Equal(string.Empty, (string)model.OddsNote);
        Assert.False((bool)model.LethalDanger);
        Assert.False((bool)model.OddsEstimated);

        // The ranges reach 16 and 17: a cap of 15 cuts them, a cap of 30 says nothing.
        Assert.Equal("Hero damage capped at 15", (string)Model(wpf, View(["BEAST"], Odds(unsimulated: 0, cap: 15))).OddsNote);
        Assert.Equal(string.Empty, (string)Model(wpf, View(["BEAST"], Odds(unsimulated: 0, cap: 30))).OddsNote);
        var both = Model(wpf, View(["BEAST"], Odds(unsimulated: 2, cap: 15)));
        Assert.Equal("Hero damage capped at 15", (string)both.OddsNote);
        Assert.Equal("2 effects not simulated", (string)both.OddsEstimatedTip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_bar_waits_for_the_first_tavern_turn(bool wpf)
    {
        // The hero pick: no turn, no tavern, the lobby still read - the bar is not shown (O-19).
        var pick = View(["BEAST"]) with { IsHeroSelection = true, HeroCardId = null, Turn = 0 };
        Assert.False((bool)Model(wpf, pick).ShowBattlegrounds);
        Assert.True((bool)Model(wpf, View(["BEAST"])).ShowBattlegrounds);
    }
}

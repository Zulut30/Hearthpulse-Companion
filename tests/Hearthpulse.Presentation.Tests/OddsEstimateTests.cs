using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The combat odds' estimate mark in both hosts: "≈" at the share bar's end when the simulator estimated values the player
/// cannot see (<see cref="CombatOdds.Estimated"/>) or left effects out (<see cref="CombatOdds.Unsimulated"/>), a tooltip
/// saying how many effects and naming the estimates without their values (O-24: the effects were a bottom line of their
/// own); in WPF the mark takes the pointer for its tooltip and leaves the card's size alone.
/// </summary>
public sealed class OddsEstimateTests : IDisposable
{
    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static BattlegroundsView View(CombatOdds odds, IReadOnlyList<EstimatedItem> estimates) =>
        new(11, 5, 6, "TB_BaconShop_HERO_72", 5, 2, 6, false, true, false, [], ["BEAST"], true, [], [], [], [], null, 0, [], [])
        { CombatOdds = odds, CombatEstimates = estimates };

    private static CombatOdds Odds(int estimated, int unsimulated = 0) =>
        new(0.355, 0.324, 0.321, unsimulated) { Runs = 4000, Estimated = estimated };

    private static EstimatedItem Item(string input, bool opponent = true) => new(input, 11, 5, "hidden from the player: our own value", opponent);

    /// <summary>The estimates of a fight: the opponent's Deity and Blood Gems, two of their trinkets, the lobby's tribes on both sides.</summary>
    private static readonly EstimatedItem[] Estimates =
    [
        Item(SimInputs.DeityStats), Item(SimInputs.BloodGemBonus), Item(SimInputs.TrinketValue), Item(SimInputs.TrinketValue),
        Item(SimInputs.LobbyTribes, opponent: false), Item(SimInputs.LobbyTribes),
    ];

    private static dynamic Model(bool wpf, BattlegroundsView view)
    {
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        Update(model, view);
        return model;
    }

    private static void Update(dynamic model, BattlegroundsView view) =>
        model.Update(LiveSnapshot.Empty with { Battlegrounds = view, InGame = true }, CardDb.Empty,
            new AppSettings { ShowTurnTimer = false, ShowCombatOdds = true }, null, null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_mark_names_what_was_estimated_once_each_and_never_a_value(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = Model(wpf, View(Odds(estimated: Estimates.Length), Estimates));

        Assert.True((bool)model.ShowOdds);
        Assert.True((bool)model.OddsEstimated);
        Assert.Equal(
            "Шансы с оценкой — эти значения неизвестны точно:\n• Характеристики Божества соперника\n• Кровавые самоцветы соперника\n"
            + "• Число на безделушке соперника\n• Расы лобби (ещё не считаны)",
            (string)model.OddsEstimatedTip);
        // The estimate used (11/5 here) is the opponent's hidden value's stand-in: only the names reach the player.
        Assert.DoesNotContain(((string)model.OddsEstimatedTip).ToCharArray(), char.IsDigit);

        Loc.Instance.SetLanguage(Loc.English);
        model = Model(wpf, View(Odds(estimated: 3), [Item(SimInputs.BeetleStats), Item(SimInputs.TavernSpellBonus), Item("some_future_input")]));
        Assert.Equal(
            "Odds include estimates — these values aren't known exactly:\n• The opponent's Beetle stats\n• The opponent's Tavern spells\n• Other values",
            (string)model.OddsEstimatedTip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_opponents_secrets_are_named_without_how_many(bool wpf)
    {
        // E7a: the player sees how many Secrets the opponent has, not which; the line names the estimate, not the count.
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = Model(wpf, View(Odds(estimated: 1), [Item(SimInputs.OpponentSecrets)]));
        Assert.Equal("Шансы с оценкой — эти значения неизвестны точно:\n• Секреты соперника (известно только, сколько их)", (string)model.OddsEstimatedTip);

        Loc.Instance.SetLanguage(Loc.English);
        model = Model(wpf, View(Odds(estimated: 1), [Item(SimInputs.OpponentSecrets)]));
        Assert.Equal("Odds include estimates — these values aren't known exactly:\n• The opponent's Secrets (only how many is known)", (string)model.OddsEstimatedTip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_duos_second_board_never_seen_is_named_as_left_out_and_a_seen_one_as_last_seen(bool wpf)
    {
        // TASK-2026-11-26 P3 Q1: a second board never seen is not in the odds (its estimate holds no minions,
        // SimInputs.IsUnseenBoard); one seen in an earlier fight is, as last seen. Our side's and the opponent's, ru and en.
        static EstimatedItem Unseen(string input, bool opponent) => new(input, 0, 0, "not seen yet: the fight has no second board", opponent);
        static EstimatedItem Seen(string input, bool opponent) => new(input, 4, 3, "the board as last seen on turn 4", opponent);

        Loc.Instance.SetLanguage(Loc.Russian);
        var model = Model(wpf, View(Odds(estimated: 2), [Unseen(SimInputs.DuosPartnerBoard, false), Unseen(SimInputs.DuosOpponentTeammate, true)]));
        Assert.Equal(
            "Шансы с оценкой — эти значения неизвестны точно:\n• Второй борт вашей команды (ещё не виден — шансы без него)\n"
            + "• Борт напарника соперника (ещё не виден — шансы без него)",
            (string)model.OddsEstimatedTip);

        model = Model(wpf, View(Odds(estimated: 2), [Seen(SimInputs.DuosPartnerBoard, false), Seen(SimInputs.DuosOpponentTeammate, true)]));
        Assert.Equal(
            "Шансы с оценкой — эти значения неизвестны точно:\n• Второй борт вашей команды (последний увиденный)\n• Борт напарника соперника (последний увиденный)",
            (string)model.OddsEstimatedTip);

        Loc.Instance.SetLanguage(Loc.English);
        model = Model(wpf, View(Odds(estimated: 2), [Unseen(SimInputs.DuosPartnerBoard, false), Seen(SimInputs.DuosOpponentTeammate, true)]));
        Assert.Equal(
            "Odds include estimates — these values aren't known exactly:\n• Your team's second board (not seen yet — odds without it)\n"
            + "• The opponent teammate's board (last seen)",
            (string)model.OddsEstimatedTip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Estimates_and_unsimulated_effects_share_the_mark(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = Model(wpf, View(Odds(estimated: 1, unsimulated: 2), [Item(SimInputs.BloodGemBonus)]));
        Assert.True((bool)model.OddsEstimated);
        Assert.Equal("2 эффекта не учтены\nШансы с оценкой — эти значения неизвестны точно:\n• Кровавые самоцветы соперника", (string)model.OddsEstimatedTip);
        Assert.Equal(string.Empty, (string)model.OddsNote);

        // Only unsimulated effects: the mark, its tooltip says how many; no bottom line.
        Update(model, View(Odds(estimated: 0, unsimulated: 1), []));
        Assert.True((bool)model.OddsEstimated);
        Assert.Equal("1 эффект не учтён", (string)model.OddsEstimatedTip);
        Assert.Equal(string.Empty, (string)model.OddsNote);

        // Only estimates: the mark without a bottom line.
        Update(model, View(Odds(estimated: 1), [Item(SimInputs.DeityStats)]));
        Assert.True((bool)model.OddsEstimated);
        Assert.Equal(string.Empty, (string)model.OddsNote);

        // The next fight estimates nothing: the mark goes.
        Update(model, View(Odds(estimated: 0), []));
        Assert.False((bool)model.OddsEstimated);
        Assert.Equal(string.Empty, (string)model.OddsEstimatedTip);
    }

    /// <summary>
    /// The WPF panel: the mark shows with the tooltip and an accessible name, takes the pointer over the click-through
    /// overlay (its tooltip needs it), and the card measures the same with and without it.
    /// </summary>
    [Fact]
    public void Wpf_mark_shows_its_tooltip_and_keeps_the_card_size() => WpfThread.RunThemed(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var model = new WpfOverlay();
        Update(model, View(Odds(estimated: 0), []));
        var panel = new Hearthpulse.App.Overlay.OddsPanel { DataContext = model };
        var card = (FrameworkElement)panel.FindName("OddsCard");
        var mark = (Border)panel.FindName("EstimateMark");
        var plain = Layout(panel, card);
        Assert.Equal(Visibility.Collapsed, mark.Visibility);

        Update(model, View(Odds(estimated: 2, unsimulated: 1), [Item(SimInputs.BloodGemBonus), Item(SimInputs.DeityStats)]));
        var marked = Layout(panel, card);

        Assert.Equal(Visibility.Visible, mark.Visibility);
        Assert.Equal(model.OddsEstimatedTip, mark.ToolTip);
        Assert.Equal("Шансы приблизительные", AutomationProperties.GetName(mark));
        Assert.True(Hearthpulse.App.Overlay.OverlayHit.GetInteractive(mark));
        Assert.True(mark.ActualWidth > 0);
        Assert.Equal(plain, marked);
    });

    private static Size Layout(UIElement panel, FrameworkElement card)
    {
        panel.Measure(new Size(600, 400));
        panel.Arrange(new Rect(0, 0, 600, 400));
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        panel.UpdateLayout();
        return new Size(card.ActualWidth, card.ActualHeight);
    }
}

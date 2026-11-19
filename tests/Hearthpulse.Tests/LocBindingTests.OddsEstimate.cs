using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// The Avalonia odds panel's estimate mark: "≈" at the share bar's end with the names of what was estimated in its
    /// tooltip and an accessible name, taking the pointer over the click-through overlay; the card measures the same with
    /// and without it, and the bottom line for unsimulated effects stays beside it.
    /// </summary>
    [Fact]
    public void Avalonia_odds_estimate_mark_binds_its_tooltip_and_keeps_the_card_size() => Headless.Run(OddsEstimateMark);

    private static void OddsEstimateMark()
    {
        var app = Application.Current!;
        var theme = Theme();
        var window = new Window { Width = 800, Height = 400 };
        window.Resources["UiFont"] = Avalonia.Media.FontFamily.Default;
        try
        {
            Loc.Instance.SetLanguage(Loc.Russian);
            var model = new OverlayViewModel();
            var panel = new OddsPanel { DataContext = model, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            window.Content = panel;
            window.Show();
            var card = panel.FindControl<Border>("OddsCard")!;
            var mark = panel.FindControl<Border>("EstimateMark")!;

            OddsUpdate(model, new CombatOdds(0.4, 0.2, 0.4, 1) { Runs = 1000 }, []);
            var plain = OddsLayout(window, card);
            Assert.False(mark.IsVisible);
            Assert.Equal("1 эффект не учтён", model.OddsNote);

            OddsUpdate(model, new CombatOdds(0.4, 0.2, 0.4, 1) { Runs = 1000, Estimated = 2 },
                [new EstimatedItem(SimInputs.BloodGemBonus, 2, 3, "hidden from the player: our own value", true), new EstimatedItem(SimInputs.DeityStats, 10, 12, "hidden", true)]);
            var marked = OddsLayout(window, card);

            Assert.True(mark.IsVisible);
            Assert.Equal("Шансы с оценкой — эти значения неизвестны точно:\n• Кровавые самоцветы соперника\n• Характеристики Божества соперника",
                ToolTip.GetTip(mark));
            Assert.Equal("Шансы с оценкой", AutomationProperties.GetName(mark));
            Assert.True(OverlayHit.GetInteractive(mark));
            Assert.True(mark.Bounds.Width > 0);
            Assert.Equal("1 эффект не учтён", model.OddsNote);
            Assert.Equal(plain, marked);
        }
        finally
        {
            window.Close();
            Loc.Instance.SetLanguage(Loc.English);
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void OddsUpdate(OverlayViewModel model, CombatOdds odds, IReadOnlyList<EstimatedItem> estimates)
    {
        var view = new BattlegroundsView(11, 5, 6, "TB_BaconShop_HERO_72", 5, 2, 6, false, true, false, [], ["BEAST"], true, [], [], [], [], null, 0, [], [])
        {
            CombatOdds = odds,
            CombatEstimates = estimates,
        };
        model.Update(LiveSnapshot.Empty with { Battlegrounds = view, InGame = true }, CardDb.Empty, new AppSettings { ShowTurnTimer = false, ShowCombatOdds = true }, null, null);
    }

    private static Size OddsLayout(Window window, Control card)
    {
        for (var round = 0; round < 3; round++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        return card.Bounds.Size;
    }
}

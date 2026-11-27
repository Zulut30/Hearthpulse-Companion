using System.Text;
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

public sealed class ArenaFitDisplayTests
{
    [Theory]
    [InlineData(1.2, "+1", "+1")]
    [InlineData(1.3, "+1.5", "+1,5")]
    [InlineData(-1.2, "−1", "−1")]
    [InlineData(-1.3, "−1.5", "−1,5")]
    [InlineData(0.24, "", "")]
    [InlineData(0.25, "+0.5", "+0,5")]
    [InlineData(-0.24, "", "")]
    [InlineData(-0.25, "−0.5", "−0,5")]
    [InlineData(0.0, "", "")]
    public void The_heuristic_uses_half_point_steps_without_signed_zero_in_both_hosts(double points, string english, string russian)
    {
        var cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
            [{"id":"CARD_A","dbfId":1,"name":"A","cost":4,"type":"MINION","collectible":true},
             {"id":"CARD_B","dbfId":2,"name":"B","cost":3,"type":"MINION","collectible":true}]
            """)));
        var view = ArenaDraft.Build(new("HERO_07", 0, ["CARD_A"], "DRAFTING"), ["CARD_A", "CARD_B"],
            new ArenaMeta([new("CARD_A", 1, 50, null, null, 0), new("CARD_B", 2, 40, null, null, 0)], []), cards);
        // The display rounds the net heuristic, which can be small when its reasons offset one another.
        view = view with { Options = [view.Options[0] with { Fit = new ArenaFit(points, [new(ArenaFitReason.FewMinions, points)]) }, view.Options[1]] };
        var snapshot = LiveSnapshot.Empty with { Scene = "DRAFT", HearthstoneRunning = true, ArenaDraft = view };
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var (language, amount, prefix) in new[] { (Loc.English, english, "Deck fit: "), (Loc.Russian, russian, "Под колоду: ") })
            {
                Loc.Instance.SetLanguage(language);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language == Loc.English ? "en-US" : "ru-RU");
                var wpf = new WpfOverlay();
                var ava = new AvaOverlay();
                var settings = new AppSettings { ShowArenaDraft = true };
                wpf.Update(snapshot, cards, settings, null);
                ava.Update(snapshot, cards, settings, null);
                Assert.Equal(2, wpf.ArenaPicks.Count);
                Assert.Equal(2, ava.ArenaPicks.Count);
                var wb = wpf.ArenaPicks[0];
                var ab = ava.ArenaPicks[0];
                foreach (var (number, fit, why, best) in new[]
                         { (wb.Number, wb.DeckFit, wb.DeckFitWhy, wb.IsBest), (ab.Number, ab.DeckFit, ab.DeckFitWhy, ab.IsBest) })
                {
                    Assert.Equal(amount.Length == 0 ? string.Empty : prefix + amount, fit);
                    Assert.Equal(amount.Length == 0, why.Length == 0);
                    Assert.Equal("50", number);
                    Assert.True(best);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Loc.Instance.SetLanguage(Loc.English);
        }
    }
}

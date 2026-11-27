using System.Runtime.CompilerServices;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Plan 2.4.2: a pair synergy from Hearthpulse's own statistics reaches the mulligan panel of both hosts the same way -
/// the flag on both cards, the line in each card's tooltip and in the summary's details - with its interval, its games
/// and its source named as our statistics, never as the external "Hearthpulse data".
/// </summary>
public sealed class MulliganSynergyContractTests
{
    [Fact]
    public void A_synergy_flags_both_cards_and_names_its_source_in_both_hosts()
    {
        var constructed = MulliganReplay.DeckSample(CardDb.Empty, near: false)!;
        var view = constructed.Mulligan!;
        var (a, b) = (view.Cards[0], view.Cards.First(c => c.CardId != view.Cards[0].CardId));
        var paired = view with
        {
            HasPairData = true,
            Recommended = [a.EntityId, b.EntityId],
            Synergies = [new MulliganSynergy(a.EntityId, b.EntityId, a.Name, b.Name, 0.032, 412, 0.014)],
        };

        foreach (var (cards, details) in Shown(constructed with { Mulligan = paired }))
        {
            Assert.Equal(new[] { a.EntityId, b.EntityId }.Order(), cards.Where(c => c.Synergy).Select(c => c.EntityId).Order());
            var points = (3.2).ToString("+0.0", System.Globalization.CultureInfo.CurrentCulture);
            var margin = (1.4).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
            var withB = Loc.F("Mulligan.SynergyWith", b.Name, points, margin, 412);
            var withA = Loc.F("Mulligan.SynergyWith", a.Name, points, margin, 412);
            Assert.Contains(withB, cards.Single(c => c.EntityId == a.EntityId).Tooltip, StringComparison.Ordinal);
            Assert.Contains(withA, cards.Single(c => c.EntityId == b.EntityId).Tooltip, StringComparison.Ordinal);
            var kept = Loc.F("Mulligan.SynergyKept", a.Name, b.Name, points, margin, 412);
            Assert.Contains(kept, details, StringComparison.Ordinal);
            Assert.DoesNotContain(Loc.T("Mulligan.NoPairData"), details, StringComparison.Ordinal);
            // The value travels with its interval and its sample, and the line says whose number it is.
            foreach (var line in new[] { withB, kept })
            {
                Assert.Contains("±", line, StringComparison.Ordinal);
                Assert.Contains("412", line, StringComparison.Ordinal);
                Assert.DoesNotContain(HearthpulseData.Attribution, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Without_a_synergy_no_card_is_flagged_and_the_note_shows_only_while_there_is_no_pair_data()
    {
        var constructed = MulliganReplay.DeckSample(CardDb.Empty, near: false)!;

        foreach (var (cards, details) in Shown(constructed))
        {
            Assert.DoesNotContain(cards, c => c.Synergy);
            Assert.Contains(Loc.T("Mulligan.NoPairData"), details, StringComparison.Ordinal);
        }

        foreach (var (cards, details) in Shown(constructed with { Mulligan = constructed.Mulligan! with { HasPairData = true } }))
        {
            Assert.DoesNotContain(cards, c => c.Synergy);
            Assert.DoesNotContain(Loc.T("Mulligan.NoPairData"), details, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en", "Hearthpulse statistics", "Hearthpulse data")]
    [InlineData("ru", "статистика Hearthpulse", "данные Hearthpulse")]
    public void The_synergy_texts_call_the_numbers_our_statistics_in_both_languages(string language, string own, string external)
    {
        var strings = File.ReadAllText(Path.Combine(Root(), "src", "Hearthpulse.Core", "Localization", "Strings", language, "mulligan.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(strings);

        foreach (var key in new[] { "Mulligan.SynergyKept", "Mulligan.SynergyWith" })
        {
            var text = doc.RootElement.GetProperty(key).GetString()!;
            Assert.Contains(own, text, StringComparison.Ordinal);
            Assert.DoesNotContain(external, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Both_panels_bind_the_flag_the_tooltip_and_the_details()
    {
        var wpf = File.ReadAllText(Path.Combine(Root(), "src", "Hearthpulse.App", "Overlay", "MulliganPanel.xaml"));
        var ava = File.ReadAllText(Path.Combine(Root(), "src", "Hearthpulse.UI", "Overlay", "MulliganPanel.axaml"));
        Assert.Contains("Visibility=\"{Binding Synergy, Converter={StaticResource Vis}}\"", wpf, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding Tooltip}\"", wpf, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding Synergy}\"", ava, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding Tooltip}\"", ava, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding MulliganDetails}\"", File.ReadAllText(Path.Combine(Root(), "src", "Hearthpulse.App", "Overlay", "MulliganSummary.xaml")), StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding MulliganDetails}\"", File.ReadAllText(Path.Combine(Root(), "src", "Hearthpulse.UI", "Overlay", "MulliganSummary.axaml")), StringComparison.Ordinal);
    }

    /// <summary>What each host's overlay shows for the mulligan: (entity, flag, tooltip) per card and the summary's details.</summary>
    private static IEnumerable<(IReadOnlyList<(int EntityId, bool Synergy, string Tooltip)> Cards, string Details)> Shown(ConstructedView constructed)
    {
        var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
        var wpf = new WpfOverlay();
        var ava = new AvaOverlay();
        wpf.Update(snapshot, CardDb.Empty, new AppSettings(), null);
        ava.Update(snapshot, CardDb.Empty, new AppSettings(), null);
        yield return (wpf.MulliganCards.Select(c => (c.EntityId, c.Synergy, c.Tooltip)).ToList(), wpf.MulliganDetails);
        yield return (ava.MulliganCards.Select(c => (c.EntityId, c.Synergy, c.Tooltip)).ToList(), ava.MulliganDetails);
    }

    /// <summary>The folder with <c>Hearthpulse.slnx</c>, found above this source file.</summary>
    private static string Root([CallerFilePath] string source = "")
    {
        var dir = Path.GetDirectoryName(source);
        while (dir is not null && !File.Exists(Path.Combine(dir, "Hearthpulse.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Hearthpulse.slnx not found above " + source);
    }
}

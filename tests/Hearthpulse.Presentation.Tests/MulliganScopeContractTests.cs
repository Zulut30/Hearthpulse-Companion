using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Plan 2.4.1: the mulligan summary names whose numbers it shows - the player's deck or a close one, with the deck's
/// games and the date of the data - the same in both hosts; an archetype's numbers add no line.
/// </summary>
public sealed class MulliganScopeContractTests
{
    private static readonly DateTime AsOf = new(2026, 11, 14, 1, 5, 27, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, "Mulligan.Scope.Deck")]
    [InlineData(true, "Mulligan.Scope.NearDeck")]
    public void A_decks_numbers_are_named_with_their_games_and_date(bool near, string key)
    {
        var view = MulliganReplay.DeckSample(CardDb.Empty, near)!.Mulligan!;
        var culture = CultureInfo.CurrentCulture;
        var expected = Loc.P(key, 456569, AsOf.ToLocalTime().ToString("d", culture));

        Assert.Equal(near ? MulliganScope.NearDeck : MulliganScope.Deck, view.Scope);
        Assert.Equal(expected, WpfOverlay.ScopeLine(view, culture));
        Assert.Equal(expected, AvaOverlay.ScopeLine(view, culture));
        Assert.Contains((456569).ToString("N0", culture), expected, StringComparison.Ordinal);

        // Without a date the line says only whose games they are.
        var undated = view with { DataAsOf = null };
        Assert.Equal(Loc.P(key + "NoDate", 456569), WpfOverlay.ScopeLine(undated, culture));
        Assert.Equal(Loc.P(key + "NoDate", 456569), AvaOverlay.ScopeLine(undated, culture));
    }

    [Fact]
    public void The_overlay_shows_the_line_for_a_deck_and_none_for_an_archetype()
    {
        var constructed = MulliganReplay.DeckSample(CardDb.Empty, near: true)!;
        var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
        var wpf = new WpfOverlay();
        var ava = new AvaOverlay();

        wpf.Update(snapshot, CardDb.Empty, new AppSettings(), null);
        ava.Update(snapshot, CardDb.Empty, new AppSettings(), null);

        var line = WpfOverlay.ScopeLine(constructed.Mulligan!, CultureInfo.CurrentCulture);
        var note = Loc.P("Mulligan.Scope.NearNote", 2);
        foreach (var (scope, source, details) in new[] { (wpf.MulliganScopeLine, wpf.MulliganSource, wpf.MulliganDetails), (ava.MulliganScopeLine, ava.MulliganSource, ava.MulliganDetails) })
        {
            Assert.Equal(line, scope);
            Assert.NotEmpty(scope);
            // The source is "Hearthpulse data", never the collector's upstream.
            Assert.Contains(HearthpulseData.Attribution, source, StringComparison.Ordinal);
            Assert.DoesNotContain("HSReplay", source + details, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(line, details, StringComparison.Ordinal);
            Assert.Contains(note, details, StringComparison.Ordinal);
        }

        // The same hand with an archetype's numbers: no line, as before.
        var archetype = constructed with { Mulligan = constructed.Mulligan! with { Scope = MulliganScope.Archetype, DeckGames = null, DataAsOf = null, DeckDifference = 0 } };
        wpf.Update(snapshot with { Constructed = archetype }, CardDb.Empty, new AppSettings(), null);
        ava.Update(snapshot with { Constructed = archetype }, CardDb.Empty, new AppSettings(), null);

        Assert.Equal((string.Empty, string.Empty), (wpf.MulliganScopeLine, ava.MulliganScopeLine));
        Assert.DoesNotContain(note, wpf.MulliganDetails, StringComparison.Ordinal);
    }
}

using System.Windows;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Overlay;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Plan 2.4.1: a decklist's numbers name their source in the WPF mulligan summary's tooltip (O-15), so the summary is
/// one line for a deck as for an archetype. The panel is anchored to the game at
/// <see cref="OverlayViewModel.MulliganSummaryTop"/> and grows downwards with the overlay scale; measured with the real
/// fonts and the theme, in both languages, it ends above the Confirm button's zone at 100 % and at 160 %.
/// </summary>
public sealed class MulliganSummaryLayoutTests
{
    [Fact]
    public void Wpf_mulligan_summary_is_one_line_with_the_deck_source_in_its_tooltip()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Check();
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
            throw new InvalidOperationException("The mulligan summary does not fit: " + failure.Message, failure);
        }
    }

    private static void Check()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
        var language = Loc.Instance.Language;
        try
        {
            foreach (var tested in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(tested);
                var deck = MulliganReplay.DeckSample(CardDb.Empty, near: true)!;
                var archetype = deck with { Mulligan = deck.Mulligan! with { Scope = MulliganScope.Archetype, DeckGames = null, DataAsOf = null, DeckDifference = 0 } };
                var (archetypeHeight, _, _) = SummarySize(archetype);
                var (deckHeight, line, tip) = SummarySize(deck);

                Assert.True(line.Length > 0 && tip.Contains(line, StringComparison.Ordinal), $"{tested}: the deck line is in the tooltip");
                Assert.Equal(archetypeHeight, deckHeight);
                foreach (var scale in new[] { 1.0, 1.6 })
                {
                    var bottom = OverlayViewModel.MulliganSummaryTop + deckHeight * scale;
                    Assert.True(bottom <= OverlayViewModel.MulliganConfirmTop,
                        $"{tested}, overlay scale {scale}: the summary ends at {bottom:0.#} of 1080, the Confirm button's zone starts at {OverlayViewModel.MulliganConfirmTop}");
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(language);
        }
    }

    /// <summary>The summary's measured height in overlay units for this view, its deck line and its tooltip.</summary>
    private static (double Height, string Line, string Tip) SummarySize(ConstructedView view)
    {
        var model = new OverlayViewModel();
        model.Update(LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = view }, CardDb.Empty, new AppSettings(), null);
        var summary = new MulliganSummary { DataContext = model };
        summary.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        summary.Arrange(new Rect(summary.DesiredSize));
        // Bindings attach at DataBind priority: let them, then lay out again.
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        summary.InvalidateMeasure();
        summary.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        summary.Arrange(new Rect(summary.DesiredSize));
        summary.UpdateLayout();
        return (summary.DesiredSize.Height, model.MulliganScopeLine, model.MulliganDetails);
    }
}

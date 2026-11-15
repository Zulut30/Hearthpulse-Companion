using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// A decklist's numbers add a second line to the mulligan summary (plan 2.4.1). The panel is anchored to the game at
    /// <see cref="OverlayViewModel.MulliganSummaryTop"/> and grows downwards with the overlay scale, towards the Confirm
    /// button: measured in the Avalonia overlay, in both languages, it ends above the button's zone at 100 % and at the
    /// largest overlay scale, 160 %.
    /// </summary>
    [Fact]
    public void Avalonia_mulligan_summary_with_the_deck_line_ends_above_the_confirm_button() => Headless.Run(MulliganSummaryHeight);

    [Theory]
    [InlineData("src/Hearthpulse.App/Overlay/OverlayWindow.xaml")]
    [InlineData("src/Hearthpulse.UI/Overlay/OverlayView.axaml")]
    public void The_mulligan_summary_is_anchored_to_the_game_at_the_top_the_layout_rule_counts_from(string file)
    {
        var panel = Regex.Match(File.ReadAllText(Path.Combine(RepoFiles.Root, file)), "<o:MovablePanel Id=\"mulligan-summary\"[^>]*>", RegexOptions.CultureInvariant).Value;

        Assert.Contains("GameAnchored=\"True\"", panel, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Top\"", panel, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"Margin=\"12,{OverlayViewModel.MulliganSummaryTop},0,0\""), panel, StringComparison.Ordinal);
        // A game-anchored default margin undoes the overlay scale, so the top stays put and only the height scales.
        Assert.Equal(1.6, PanelLayout.DefaultMarginDivisor(gameAnchored: true, overlayScale: 1.6));
    }

    private static void MulliganSummaryHeight()
    {
        var app = Application.Current!;
        var theme = Theme();
        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                var deck = MulliganReplay.DeckSample(CardDb.Empty, near: true)!;
                var archetype = deck with { Mulligan = deck.Mulligan! with { Scope = MulliganScope.Archetype, DeckGames = null, DataAsOf = null, DeckDifference = 0 } };
                var (oneLine, _) = SummarySize(archetype);
                var (twoLines, line) = SummarySize(deck);

                Assert.True(line.Length > 0 && twoLines > oneLine, $"{language}: the deck line is shown ({oneLine} -> {twoLines})");
                foreach (var scale in new[] { 1.0, 1.6 })
                {
                    var bottom = OverlayViewModel.MulliganSummaryTop + twoLines * scale;
                    Assert.True(bottom <= OverlayViewModel.MulliganConfirmTop, $"{language}, overlay scale {scale}: the summary ends at {bottom:0.#} of 1080, the Confirm button's zone starts at {OverlayViewModel.MulliganConfirmTop}");
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The summary's measured height in overlay units for this view, and the deck line it shows.</summary>
    private static (double Height, string Line) SummarySize(ConstructedView view)
    {
        var window = new Window { Width = 1920, Height = 1080 };
        window.Resources["UiFont"] = Avalonia.Media.FontFamily.Default;
        try
        {
            var model = new OverlayViewModel();
            model.Update(LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = view }, CardDb.Empty, new AppSettings(), null, null);
            var summary = new MulliganSummary { DataContext = model, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            window.Content = summary;
            window.Show();
            for (var round = 0; round < 3; round++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            return (summary.Bounds.Height, model.MulliganScopeLine);
        }
        finally
        {
            window.Close();
        }
    }
}

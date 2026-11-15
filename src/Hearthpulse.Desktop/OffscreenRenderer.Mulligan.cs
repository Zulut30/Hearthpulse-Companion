using Avalonia.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Desktop;

/// <summary><c>--render-ui</c>: the mulligan panels with a collected deck's numbers.</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// Renders <c>overlay-mulligan-deck.png</c> (the player's exact list) and <c>overlay-mulligan-deck-near.png</c> (a
    /// list two cards away) from <see cref="MulliganReplay.DeckSample"/>: the source line under the summary.
    /// </summary>
    private static async Task RenderMulliganDeckAsync(AppHost host, AppSettings settings, OverlayView overlayView, Window overlay, string folder)
    {
        var shown = settings.ShowMulligan;
        try
        {
            settings.ShowMulligan = true;
            foreach (var (near, name) in new[] { (false, "overlay-mulligan-deck"), (true, "overlay-mulligan-deck-near") })
            {
                if (MulliganReplay.DeckSample(host.Cards, near) is not { } view)
                {
                    Console.WriteLine(name + ": the sample hand has no numbers");
                    continue;
                }

                var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = view };
                host.Overlay.Update(snapshot, host.Cards, settings, null, null);
                overlayView.Fit(1920, 1080);
                await Save(overlay, Path.Combine(folder, name + ".png"));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            // The renders after this one (overlay-layout.png) must still be written.
            Console.WriteLine("overlay-mulligan-deck: " + ex.Message);
        }
        finally
        {
            settings.ShowMulligan = shown;
        }
    }
}

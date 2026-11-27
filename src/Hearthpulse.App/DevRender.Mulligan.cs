using System.IO;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.App;

/// <summary><c>--render-ui</c>: the mulligan panels with a collected deck's numbers.</summary>
public partial class App
{
    /// <summary>
    /// Renders <c>overlay-mulligan-deck.png</c> (the player's exact list) and <c>overlay-mulligan-deck-near.png</c> (a
    /// list two cards away) from <see cref="MulliganReplay.DeckSample"/>: the source line under the summary.
    /// </summary>
    private async Task RenderMulliganDeckAsync(string folder)
    {
        var shown = _settings.ShowMulligan;
        try
        {
            _settings.ShowMulligan = true;
            foreach (var (near, name) in new[] { (false, "overlay-mulligan-deck"), (true, "overlay-mulligan-deck-near") })
            {
                if (MulliganReplay.DeckSample(_cards, near) is not { } view)
                {
                    Console.WriteLine(name + ": the sample hand has no numbers");
                    continue;
                }

                var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = view };
                _overlayViewModel.Update(snapshot, _cards, _settings, null);
                await _overlay.RenderToFileAsync(Path.Combine(folder, name + ".png"), 1920, 1080, 0);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            // The renders after this one (overlay-layout.png) must still be written.
            Console.WriteLine("overlay-mulligan-deck: " + ex.Message);
        }
        finally
        {
            _settings.ShowMulligan = shown;
        }
    }
}

using System.Globalization;
using System.IO;
using Hearthpulse.App.Overlay;
using Hearthpulse.Core;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.App;

/// <summary>
/// Render group <c>picks</c>: the hero and trinket pick badges on made-up offers (<see cref="PickScene"/>) at the data
/// copy's overlay scale: <c>overlay-pick-2</c>, <c>-pick-4</c>, <c>overlay-trinket-2</c>, <c>-3</c>, <c>-4</c>, and
/// <c>overlay-picks-scale.txt</c> with the scale the frames were drawn at.
/// </summary>
public partial class App
{
    private async Task RenderPicksAsync(string folder)
    {
        try
        {
            foreach (var (name, view) in new[]
                     {
                         ("overlay-pick-2", PickScene.HeroPick(2)), ("overlay-pick-4", PickScene.HeroPick(4)),
                         ("overlay-trinket-2", PickScene.Trinkets(2)), ("overlay-trinket-3", PickScene.Trinkets(3)), ("overlay-trinket-4", PickScene.Trinkets(4)),
                     })
            {
                var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_BATTLEGROUNDS", Battlegrounds = view };
                _overlayViewModel.Update(snapshot, _cards, _settings, null, null);
                await _overlay.RenderToFileAsync(Path.Combine(folder, name + ".png"), 1920, 1080, 0);
            }

            // What the frames were drawn at: the saved scale as the settings hold it now, and the surface's own.
            var drawn = _overlay.LayOutFor(1920, 1080);
            var line = string.Create(CultureInfo.InvariantCulture, $"wpf settings.OverlayScale={_settings.OverlayScale} surface={drawn} language={_settings.InterfaceLanguage()}");
            Diag.Info("Render picks: " + line);
            await File.WriteAllTextAsync(Path.Combine(folder, "overlay-picks-scale.txt"), line + Environment.NewLine);
        }
        catch (IOException ex)
        {
            Diag.Error("Render of the pick badges failed", ex);
        }
    }
}

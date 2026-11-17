using System.Globalization;
using Avalonia.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Desktop;

/// <summary>
/// Render group <c>picks</c>: the hero and trinket pick badges on made-up offers (<see cref="PickScene"/>) at the data
/// copy's overlay scale: <c>overlay-pick-2</c>, <c>-pick-4</c>, <c>overlay-trinket-2</c>, <c>-3</c>, <c>-4</c>,
/// <c>-late</c> (the trinket numbers' update delayed), and <c>overlay-picks-scale.txt</c> with the scale the frames were drawn at.
/// </summary>
internal static partial class OffscreenRenderer
{
    private static async Task RenderPicksAsync(string folder, AppHost host, AppSettings settings, Window overlay, OverlayView overlayView)
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
                host.Overlay.Update(snapshot, host.Cards, settings, null, null);
                overlayView.Fit(1920, 1080);
                await Save(overlay, Path.Combine(folder, name + ".png"));
            }

            // Last: the trinket numbers' collector late, so each badge shows its "update delayed" line whole.
            host.Overlay.SetDataHealth(PickScene.TrinketsLate());
            host.Overlay.Update(LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_BATTLEGROUNDS", Battlegrounds = PickScene.Trinkets(3) },
                host.Cards, settings, null, null);
            overlayView.Fit(1920, 1080);
            await Save(overlay, Path.Combine(folder, "overlay-trinket-late.png"));

            // What the frames were drawn at: the saved scale as the settings hold it now, and the surface's own.
            var line = string.Create(CultureInfo.InvariantCulture, $"ava settings.OverlayScale={settings.OverlayScale} surface={overlayView.SurfaceScaleNow} language={settings.InterfaceLanguage()}");
            Diag.Info("Render picks: " + line);
            Console.WriteLine(line);
            await File.WriteAllTextAsync(Path.Combine(folder, "overlay-picks-scale.txt"), line + Environment.NewLine);
        }
        catch (IOException ex)
        {
            Diag.Error("Render of the pick badges failed", ex);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Pool;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the own hero statistics on the Battlegrounds Heroes tab (docs/analytics-api.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=bgown</c>: the block "Hearthpulse statistics (own games)" in each of its states, on
    /// the pool answers of the folder <c>HEARTHPULSE_RENDER_BGOWN</c> (tests/Hearthpulse.Tests/Fixtures/Analytics:
    /// <c>bg-heroes-render.json</c>, <c>bg-heroes-render-small.json</c>). Files <c>main-bg-own-*.png</c>: <c>ok</c>,
    /// <c>small</c>, <c>old-copy</c>, <c>not-published</c>, <c>rebuilding</c>, <c>offline</c>, and the compact layout
    /// at 420 (<c>ok-compact</c>). No pool is asked.
    /// </summary>
    private static async Task RenderBgOwnAsync(Window main, MainView view, AppHost host, string folder)
    {
        await host.LoadBgSiteAsync();
        view.ShowPage("Battlegrounds");

        async Task Shot(string name, OwnBgHeroesResult result)
        {
            try
            {
                host.Main.SetBgOwn(result);
                view.ShowBattlegroundsTab("Heroes");
                // A tab shown for the first time is laid out on the next pass: scroll again once it has a size.
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                view.ShowBattlegroundsTab("Heroes");
                await Save(main, Path.Combine(folder, $"main-bg-own-{name}.png"));
            }
            catch (IOException ex)
            {
                Diag.Error($"Render of the own hero statistics ({name}) failed", ex);
            }
        }

        var scenes = OwnBgHeroesScenes.All(Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_BGOWN"), DateTime.UtcNow);
        foreach (var (name, result) in scenes)
        {
            await Shot(name, result);
        }

        if (scenes.FirstOrDefault(s => s.Name == "ok").Result is { } ok)
        {
            var (width, height) = (main.Width, main.Height);
            main.Width = 420;
            main.Height = 1200;
            await Shot("ok-compact", ok);
            main.Width = width;
            main.Height = height;
        }

        host.Main.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
    }
}

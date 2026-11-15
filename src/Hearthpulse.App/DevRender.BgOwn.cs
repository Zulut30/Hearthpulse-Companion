using System.IO;
using Hearthpulse.Core;
using Hearthpulse.Core.Pool;

namespace Hearthpulse.App;

/// <summary>Render-check scenes of the own hero statistics on the Battlegrounds Heroes tab (docs/analytics-api.md).</summary>
public partial class App
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=bgown</c>: the block "Hearthpulse statistics (own games)" in each of its states, on
    /// the pool answers of the folder <c>HEARTHPULSE_RENDER_BGOWN</c> (tests/Hearthpulse.Tests/Fixtures/Analytics:
    /// <c>bg-heroes-render.json</c>, <c>bg-heroes-render-small.json</c>). Files <c>main-bg-own-*.png</c>: <c>ok</c>,
    /// <c>small</c>, <c>old-copy</c>, <c>not-published</c>, <c>rebuilding</c>, <c>offline</c>. No pool is asked.
    /// </summary>
    private async Task RenderBgOwnAsync(string folder)
    {
        await LoadBgSiteAsync();
        _main.ShowPage("Battlegrounds");
        _main.ShowBattlegroundsTab("Heroes");
        await Task.Delay(1500);

        var now = DateTime.UtcNow;
        foreach (var (name, result) in OwnBgHeroesScenes.All(Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_BGOWN"), now))
        {
            try
            {
                _mainViewModel.SetBgOwn(result);
                await Task.Delay(400);
                _main.UpdateLayout();
                Save(_main, Path.Combine(folder, $"main-bg-own-{name}.png"));
            }
            catch (IOException ex)
            {
                Diag.Error($"Render of the own hero statistics ({name}) failed", ex);
            }
        }

        _mainViewModel.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
    }
}

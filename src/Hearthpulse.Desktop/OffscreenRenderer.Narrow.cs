using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Hearthpulse.Core.Arena;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>
/// Render-check scene of the narrow main window (bug-hunt #3, #4, #6, #7, #23-#25, #29, #30 of TASK-2026-11-25-polish):
/// <c>HEARTHPULSE_RENDER_ONLY=narrow</c> draws Home, Standard (My stats with its "More" part open), Battlegrounds (My
/// stats), Arena and Brawl whole at 1040 (the WPF minimum), 900 (the narrowest wide layout of Avalonia), 860 and 420
/// (compact), or only at <c>HEARTHPULSE_RENDER_WIDTH</c> when that is set: <c>main-narrow-&lt;page&gt;-&lt;width&gt;.png</c>.
/// In a data copy (HEARTHPULSE_DATA) the made-up runs of <see cref="ArenaRunsScene"/> go into its history first, as the
/// <c>arenapage</c> group does, so the Arena page has rows of its own.
/// </summary>
internal static partial class OffscreenRenderer
{
    private static readonly (string Page, double Height)[] NarrowPages =
        [("Overview", 2400), ("Standard", 3000), ("Battlegrounds", 2600), ("Arena", 2400), ("Brawl", 1800)];

    private static async Task RenderNarrowPagesAsync(Window main, MainView view, AppHost host, string folder)
    {
        await host.LoadArenaMetaAsync();
        await host.LoadBrawlMetaAsync();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEARTHPULSE_DATA")))
        {
            foreach (var game in ArenaRunsScene.Games())
            {
                host.History.Add(game);
            }

            // The history's refresh after a change is debounced.
            await Task.Delay(2500);
        }

        var (width, height) = (main.Width, main.Height);
        double[] widths = RenderWidth is { } only ? [only] : [1040, 900, 860, 420];
        foreach (var narrow in widths)
        {
            main.Width = narrow;
            foreach (var (page, tall) in NarrowPages)
            {
                main.Height = tall;
                view.ShowPage(page);
                // A page is built on its first showing: its "More" part (the rows of classes, opponents and archetypes of
                // Standard and Arena) can be opened once it is laid out.
                await Task.Delay(800);
                var more = page switch { "Standard" => "CtMore", "Arena" => "ArenaMore", _ => null };
                if (more is not null && view.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(t => t.Name == more) is { } toggle)
                {
                    toggle.IsChecked = true;
                    await Task.Delay(800);
                }

                await Save(main, Path.Combine(folder, $"main-narrow-{page.ToLowerInvariant()}-{narrow:0}.png"));
            }
        }

        main.Width = width;
        main.Height = height;
    }
}

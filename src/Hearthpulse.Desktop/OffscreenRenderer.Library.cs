using Avalonia.Controls;
using Avalonia.VisualTree;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Hosting;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Match;

namespace Hearthpulse.Desktop;

/// <summary>
/// <c>HEARTHPULSE_RENDER_ONLY=library</c>: the replay library (History) with Arena and Underground Arena cards
/// (<see cref="ArenaRunsScene"/>) beside Constructed and Battlegrounds ones (<see cref="VisualBaseline.Games"/>), a card
/// shown as uploaded (a made-up link, nothing is sent), the list scrolled, the minimum width and compact, and the match
/// page header of a game with a replay and of an Arena game.
/// </summary>
internal static partial class OffscreenRenderer
{
    /// <summary>The made-up link of the "uploaded" card: the site's form, never a real upload.</summary>
    private const string RenderSiteUrl = "https://hearthpulse.net/replay/render0001";

    private static async Task RenderLibraryAsync(Window main, MainView view, AppHost host, string folder)
    {
        // The made-up games go into the history only when it is a copy (HEARTHPULSE_DATA), never the player's own.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEARTHPULSE_DATA")))
        {
            Console.WriteLine("main-library: skipped, HEARTHPULSE_DATA is not set (the scene writes to the history)");
            return;
        }

        foreach (var game in ArenaRunsScene.Games().Concat(ArenaRunsScene.UndergroundGames()).Concat(VisualBaseline.Games))
        {
            host.History.Add(game);
        }

        await host.Main.RefreshAsync();
        view.ShowPage("History");
        await Save(main, Path.Combine(folder, "main-library.png"));

        if (host.Main.Library.AllCards.FirstOrDefault(c => c.CanUpload) is { } uploaded)
        {
            uploaded.SiteUrl = RenderSiteUrl;
            await Save(main, Path.Combine(folder, "main-library-onsite.png"));
            uploaded.SiteUrl = string.Empty;
        }

        if (view.GetVisualDescendants().OfType<ReplayLibraryView>().FirstOrDefault()?.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(l => l.Name == "List")?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
        {
            // A card cut by the list's top edge: the rows under the tabs, the search and the status line.
            scroll.Offset = new Avalonia.Vector(0, 40);
            await Save(main, Path.Combine(folder, "main-library-scrolled.png"));
            // The oldest games: the made-up Battlegrounds and Ranked cards without files.
            scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
            await Save(main, Path.Combine(folder, "main-library-end.png"));
            scroll.Offset = default;
        }

        var width = main.Width;
        foreach (var narrow in new[] { 1040, 420 })
        {
            main.Width = narrow;
            await Save(main, Path.Combine(folder, narrow == 420 ? "main-library-compact.png" : "main-library-1040.png"));
        }

        main.Width = width;

        if (host.History.Recent(400).FirstOrDefault(g => g.Replay is not null && !g.IsBattlegrounds) is { } withReplay
            && await host.Main.OpenMatchPageAsync(withReplay.Key))
        {
            view.ShowPage("History");
            await Save(main, Path.Combine(folder, "main-match-browser.png"));
            // "View in browser" on a game not on the site asks first; the question only, never an upload or a browser here.
            if (host.Main.MatchPage.HeaderCard is { FileName: { } file } card)
            {
                host.Main.AskSiteUpload(card, file);
                await Save(main, Path.Combine(folder, "main-match-browser-confirm.png"));
                host.Main.MatchPage.CancelHeader.Execute(null);
            }

            host.Main.MatchPage.Back.Execute(null);
        }

        if (ArenaRunsScene.UndergroundGames()[^1] is { } arena && await host.Main.OpenMatchPageAsync(arena.Key))
        {
            view.ShowPage("History");
            await Save(main, Path.Combine(folder, "main-match-arena.png"));
            host.Main.MatchPage.Back.Execute(null);
        }
    }
}

using Avalonia.Controls;
using Hearthpulse.Core.Constructed;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the collection in the top decks, the side card and the deck page (docs/collection.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=collection</c>: on a made-up collection and made-up lists (<see cref="CollectionScene"/>,
    /// never the player's own collection) the top decks with every label (<c>main-collection-top</c>), by a saved
    /// collection (<c>-top-saved</c>), with "can build" on and the order by dust (<c>-top-filter</c>), without a
    /// collection (<c>-top-none</c>), the side card of the tier list (<c>-side</c>), the deck page with its missing
    /// cards (<c>-deck-full</c>), and the compact layout at 420 (<c>-top-compact</c>, <c>-deck-compact</c>). The
    /// page's own stats and the window's size are put back afterwards.
    /// </summary>
    private static async Task RenderCollectionAsync(Window main, MainView view, AppHost host, string folder)
    {
        if (CollectionScene.Build(host.Cards, confirmed: true) is not { } scene || CollectionScene.Build(host.Cards, confirmed: false) is not { } saved)
        {
            Console.WriteLine("collection: the card data lacks the cards for the scene, skipped");
            return;
        }

        var (width, height) = (main.Width, main.Height);
        view.ShowPage("Standard");
        view.ShowConstructedTab("Top");

        // The page asks for its stats when it opens: they come first, so they do not replace the made-up ones later.
        for (var waited = 0; waited < 40 && host.Main.MetaLoading; waited++)
        {
            await Task.Delay(250);
        }

        var own = host.Main.ShownConstructedMeta;
        host.Main.SetConstructedMeta(scene.Meta);
        async Task Shot(string name)
        {
            // The stats come in two steps (the tier list, then the mulligan numbers): a late one must not stand in the
            // frame. So the made-up stats are set again until they are the ones shown after the wait.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                host.Main.SetConstructedMeta(scene.Meta);
                await Task.Delay(600);
                if (ReferenceEquals(host.Main.ShownConstructedMeta, scene.Meta))
                {
                    break;
                }
            }

            await Save(main, Path.Combine(folder, $"main-collection-{name}.png"));
        }

        host.Main.SetCollection(scene.Collection);
        await Shot("top");
        host.Main.CanBuildOnly = true;
        host.Main.SetMetaDeckSort.Execute("Dust");
        await Shot("top-filter");
        host.Main.CanBuildOnly = false;
        host.Main.SetMetaDeckSort.Execute("WinRate");
        host.Main.SetCollection(saved.Collection);
        await Shot("top-saved");
        main.Width = 420;
        main.Height = 1500;
        await Shot("top-compact");
        main.Width = width;
        main.Height = height;
        host.Main.SetCollection(null);
        await Shot("top-none");

        host.Main.SetCollection(saved.Collection);
        view.ShowConstructedTab("Tiers");
        await Shot("side");

        host.Main.SetCollection(scene.Collection);
        host.Main.OpenSampleDeck(scene.Deck);
        main.Height = 2200;
        await Shot("deck-full");
        main.Width = 420;
        main.Height = 4200;
        await Shot("deck-compact");
        main.Width = width;
        main.Height = height;

        // The session's own collection and the page's own stats again.
        host.Main.SetCollection(host.Session.Collection);
        if (own is not null)
        {
            host.Main.SetConstructedMeta(own);
        }
    }
}

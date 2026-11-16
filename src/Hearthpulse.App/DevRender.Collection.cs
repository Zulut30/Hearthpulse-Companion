using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core.Constructed;

namespace Hearthpulse.App;

/// <summary>Render-check scenes of the collection in the top decks, the side card and the deck page (docs/collection.md).</summary>
public partial class App
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=collection</c>: on a made-up collection and made-up lists (<see cref="CollectionScene"/>,
    /// never the player's own collection) the top decks with every label (<c>main-collection-top</c>), by a saved
    /// collection (<c>-top-saved</c>), with "can build" on and the order by dust (<c>-top-filter</c>), without a
    /// collection (<c>-top-none</c>), the side card of the tier list (<c>-side</c>) and the deck page with its missing
    /// cards (<c>-deck</c>, <c>-deck-full</c>). The page's own stats are put back afterwards.
    /// </summary>
    private async Task RenderCollectionAsync(string folder)
    {
        if (CollectionScene.Build(_cards, confirmed: true) is not { } scene || CollectionScene.Build(_cards, confirmed: false) is not { } saved)
        {
            Console.WriteLine("collection: the card data lacks the cards for the scene, skipped");
            return;
        }

        _main.ShowPage("Standard");
        _main.ShowConstructedTab("Top");

        // The page asks for its stats when it opens: they come first, so they do not replace the made-up ones later.
        for (var waited = 0; waited < 40 && _mainViewModel.MetaLoading; waited++)
        {
            await Task.Delay(250);
        }

        var own = _mainViewModel.ShownConstructedMeta;
        _mainViewModel.SetConstructedMeta(scene.Meta);

        async Task Shot(string name)
        {
            // The stats come in two steps (the tier list, then the mulligan numbers): a late one must not stand in the frame.
            // So the made-up stats are set again until they are the ones shown after the wait.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                _mainViewModel.SetConstructedMeta(scene.Meta);
                await Task.Delay(1200);
                if (ReferenceEquals(_mainViewModel.ShownConstructedMeta, scene.Meta))
                {
                    break;
                }
            }

            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, $"main-collection-{name}.png"));
        }

        _mainViewModel.SetCollection(scene.Collection);
        await Shot("top");
        _mainViewModel.CanBuildOnly = true;
        _mainViewModel.SetMetaDeckSort.Execute("Dust");
        await Shot("top-filter");
        _mainViewModel.CanBuildOnly = false;
        _mainViewModel.SetMetaDeckSort.Execute("WinRate");
        _mainViewModel.SetCollection(saved.Collection);
        await Shot("top-saved");
        _mainViewModel.SetCollection(null);
        await Shot("top-none");

        _mainViewModel.SetCollection(saved.Collection);
        _main.ShowConstructedTab("Tiers");
        await Shot("side");

        _mainViewModel.SetCollection(scene.Collection);
        _mainViewModel.OpenSampleDeck(scene.Deck);
        await Task.Delay(1500);
        await Shot("deck");
        if (_main.FindName("PageDeck") is ScrollViewer { Content: FrameworkElement content })
        {
            SaveFull(content, Path.Combine(folder, "main-collection-deck-full.png"));
        }

        // The session's own collection and the page's own stats again.
        _mainViewModel.SetCollection(_session.Collection);
        if (own is not null)
        {
            _mainViewModel.SetConstructedMeta(own);
        }
    }
}

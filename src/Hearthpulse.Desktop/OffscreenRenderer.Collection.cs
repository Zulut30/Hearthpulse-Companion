using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core.Constructed;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Decks;
using Hearthpulse.UI.Main.Meta;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the collection in the top decks, the side card and the deck page (docs/collection.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=collection</c>: on a made-up collection and made-up lists (<see cref="CollectionScene"/>,
    /// never the player's own collection) the top decks with every label (<c>main-collection-top</c>), by a saved
    /// collection (<c>-top-saved</c>), with "can build" on and the order by dust (<c>-top-filter</c>), without a
    /// collection (<c>-top-none</c>), the missing cards of a label opened (<c>-missing</c>: the flyout itself), the side
    /// card of the tier list (<c>-side</c>), the deck page with its missing cards (<c>-deck-full</c>), and the compact
    /// layout at 420 (<c>-top-compact</c>, <c>-deck-compact</c>). A deck frame is taken only with the deck page open
    /// and its collection block on screen: otherwise the render fails. The page's own stats and the window's size are
    /// put back afterwards.
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

        async Task Hold()
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
        }

        async Task Shot(string name)
        {
            await Hold();
            await Save(main, Path.Combine(folder, $"main-collection-{name}.png"));
        }

        host.Main.SetCollection(scene.Collection);
        await Shot("top");

        // The missing cards of the first label that has any, as a press of the label opens them.
        await Hold();
        Dispatcher.UIThread.RunJobs();
        var meta = view.GetVisualDescendants().OfType<ConstructedMetaView>().First();
        if (meta.OpenFirstDeckBuild() is not { } flyout)
        {
            throw new InvalidOperationException("collection: no build label with missing cards to open");
        }

        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        SavePopup(main, (flyout.Content as Control)?.FindAncestorOfType<FlyoutPresenter>(), Path.Combine(folder, "main-collection-missing.png"));
        flyout.Hide();

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

        // The deck page: the made-up deck stays open through the stats set again by Hold (they rebuild the deck list).
        host.Main.SetCollection(scene.Collection);
        host.Main.OpenSampleDeck(scene.Deck);
        main.Height = 2200;
        await Hold();
        await Task.Delay(600);
        Dispatcher.UIThread.RunJobs();
        void DeckPageShown()
        {
            var page = view.GetVisualDescendants().OfType<DeckDetailsView>().FirstOrDefault();
            var block = page?.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == "DeckCollection");
            if (!host.Main.DeckOpen || page is not { IsEffectivelyVisible: true } || block is not { IsEffectivelyVisible: true } || host.Main.DeckPage.Collection.Build is null)
            {
                throw new InvalidOperationException("collection: the deck page with its collection block is not on screen");
            }
        }

        DeckPageShown();
        await Save(main, Path.Combine(folder, "main-collection-deck-full.png"));
        main.Width = 420;
        main.Height = 4200;
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        DeckPageShown();
        await Save(main, Path.Combine(folder, "main-collection-deck-compact.png"));
        main.Width = width;
        main.Height = height;

        // The session's own collection and the page's own stats again.
        host.Main.CloseDeck();
        host.Main.SetCollection(host.Session.Collection);
        if (own is not null)
        {
            host.Main.SetConstructedMeta(own);
        }
    }
}

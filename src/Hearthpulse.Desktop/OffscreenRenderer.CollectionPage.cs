using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>Render-check scenes of the Collection page (docs/collection-page.md).</summary>
internal static partial class OffscreenRenderer
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=collectionpage</c>: the Collection page on the data copy's own <c>collection.json</c> and
    /// <c>cosmetics.json</c> (the owner asked for frames of their data; the PNGs stay out of the repository): each tab
    /// (<c>main-collection-sets</c>, <c>-skins</c>, <c>-coins</c>, <c>-backs</c>), the golden copies turned on by the
    /// scene's own switch (<c>-sets-golden</c>), the sets in English (<c>-sets-en</c>), the compact layout at 420
    /// (<c>-sets-compact</c>, <c>-backs-compact</c>); then the states on the made-up data of <see cref="CosmeticsScene"/>:
    /// <c>-none</c>, <c>-saved</c>, <c>-notread</c>, <c>-nocosmetics</c>, <c>-unsupported</c>, <c>-nocards</c>. Tall frames
    /// hold the whole page; the card backs show their largest group; the window's size and the session's data come back
    /// afterwards.
    /// </summary>
    private static async Task RenderCollectionPageAsync(Window main, MainView view, AppHost host, string folder)
    {
        var collection = new CollectionStore(AppPaths.Collection).Load();
        var cosmetics = new CosmeticsStore(AppPaths.Cosmetics).Load();
        var model = host.Main;
        var (width, height) = (main.Width, main.Height);
        view.ShowPage("Collection");

        async Task Shot(string name, double tall = 2600)
        {
            main.Height = tall;
            await Task.Delay(1500);
            Dispatcher.UIThread.RunJobs();
            await Save(main, Path.Combine(folder, $"main-collection-{name}.png"));
        }

        if (collection is null && cosmetics is null)
        {
            Console.WriteLine("collectionpage: no collection.json or cosmetics.json in the data folder, own-data frames skipped");
        }
        else
        {
            model.SetCollection(collection);
            model.SetCosmetics(cosmetics);
            foreach (var tab in MainViewModel.CollectionTabs)
            {
                model.ShowCollectionTab(tab);
                if (tab == "Backs" && model.CollectionBackGroups.Count > 0)
                {
                    // A frame of one back says little: the group with the most backs.
                    model.SetCollectionBackGroup.Execute(model.CollectionBackGroups
                        .OrderByDescending(g => int.TryParse(g.Label[(g.Label.LastIndexOf('/') + 1)..], NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) ? n : 0).First().Key);
                }

                await Shot(tab.ToLowerInvariant(), tab switch { "Sets" => 1700, "Coins" => 1800, _ => 2600 });
            }

            model.ShowCollectionTab("Sets");
            model.CollectionGoldenShown = true;
            await Shot("sets-golden", 1700);
            model.CollectionGoldenShown = CollectionFinish.GoldenVerified;
            var language = Loc.Instance.Language;
            Loc.Instance.SetLanguage(Loc.English);
            await Shot("sets-en", 1700);
            Loc.Instance.SetLanguage(language);

            main.Width = 420;
            await Shot("sets-compact", 3900);
            model.ShowCollectionTab("Backs");
            await Shot("backs-compact", 3600);
            main.Width = width;
        }

        // The states, on made-up data.
        if (CosmeticsScene.Build(host.Cards, confirmed: false) is { } saved)
        {
            model.ShowCollectionTab("Sets");
            model.SetCollection(null);
            model.SetCosmetics(null);
            await Shot("none", 900);
            model.SetCollection(saved.Collection);
            await Shot("saved", 1700);
            model.SetCollection(null);
            model.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true });
            await Shot("notread", 900);
            model.SetCollection(saved.Collection);
            model.ShowCollectionTab("Coins");
            await Shot("nocosmetics", 900);
            model.Apply(LiveSnapshot.Empty);
            model.ShowCollectionTab("Sets");
            model.CollectionReadable = false;
            model.SetCollection(null);
            await Shot("unsupported", 900);
            model.CollectionReadable = true;
            model.SetCollection(saved.Collection);
            var cards = host.Cards;
            model.SetCards(CardDb.Empty);
            await Shot("nocards", 900);
            model.SetCards(cards);
        }
        else
        {
            Console.WriteLine("collectionpage: the card data lacks the cards for the made-up states, skipped");
        }

        main.Width = width;
        main.Height = height;
        model.SetCollection(host.Session.Collection);
        model.SetCosmetics(host.Session.Cosmetics);
        model.ShowCollectionTab("Sets");
    }
}

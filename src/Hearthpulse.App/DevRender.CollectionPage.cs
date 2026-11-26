using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hearthpulse.Core;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.App;

/// <summary>Render-check scenes of the Collection page (docs/collection-page.md).</summary>
public partial class App
{
    /// <summary>
    /// <c>HEARTHPULSE_RENDER_ONLY=collectionpage</c>: the Collection page on the data copy's own <c>collection.json</c> and
    /// <c>cosmetics.json</c> (the owner asked for frames of their data; the PNGs stay out of the repository): each tab
    /// (<c>main-collection-sets</c>, <c>-skins</c>, <c>-coins</c>, <c>-backs</c>), the golden copies turned on by the
    /// scene's own switch (<c>-sets-golden</c>), the sets in English (<c>-sets-en</c>); then the states on the made-up
    /// data of <see cref="CosmeticsScene"/>: <c>-none</c>, <c>-notread</c>, <c>-saved</c>, <c>-unsupported</c>,
    /// <c>-readingoff</c> (reading the client turned off), <c>-nocards</c>, <c>-nocosmetics</c>. Full-length frames are the page's content; <c>-window</c> is the whole window on
    /// the sets (the sidebar with the library pages). The card backs show their largest group. The session's data comes
    /// back afterwards. Without the data copy's files the own-data frames are skipped (said on the console).
    /// </summary>
    private async Task RenderCollectionPageAsync(string folder)
    {
        var collection = new CollectionStore(AppPaths.Collection).Load();
        var cosmetics = new CosmeticsStore(AppPaths.Cosmetics).Load();
        var page = _main.PageCollection;
        _main.ShowPage("Collection");

        async Task Shot(string name)
        {
            await Task.Delay(2500);
            _main.UpdateLayout();
            if (page.Content is ScrollViewer { Content: FrameworkElement content })
            {
                SavePage(content, Path.Combine(folder, $"main-collection-{name}.png"));
            }
        }

        async Task Tabs(string suffix)
        {
            foreach (var tab in Hearthpulse.App.Main.MainViewModel.CollectionTabs)
            {
                _mainViewModel.ShowCollectionTab(tab);
                if (tab == "Backs" && _mainViewModel.CollectionBackGroups.Count > 0)
                {
                    _mainViewModel.SetCollectionBackGroup.Execute(LargestGroup());
                }

                await Shot(tab.ToLowerInvariant() + suffix);
            }
        }

        if (collection is null && cosmetics is null)
        {
            Console.WriteLine("collectionpage: no collection.json or cosmetics.json in the data folder, own-data frames skipped");
        }
        else
        {
            _mainViewModel.SetCollection(collection);
            _mainViewModel.SetCosmetics(cosmetics);
            await Tabs(string.Empty);
            _mainViewModel.ShowCollectionTab("Sets");
            await Task.Delay(1500);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-collection-window.png"));
            _mainViewModel.CollectionGoldenShown = true;
            await Shot("sets-golden");
            _mainViewModel.CollectionGoldenShown = CollectionFinish.GoldenVerified;
            var language = Loc.Instance.Language;
            Loc.Instance.SetLanguage(Loc.English);
            await Shot("sets-en");
            Loc.Instance.SetLanguage(language);
        }

        // The states, on made-up data.
        if (CosmeticsScene.Build(_cards, confirmed: false) is { } saved)
        {
            _mainViewModel.ShowCollectionTab("Sets");
            _mainViewModel.SetCollection(null);
            _mainViewModel.SetCosmetics(null);
            await Shot("none");
            _mainViewModel.SetCollection(saved.Collection);
            await Shot("saved");
            _mainViewModel.SetCollection(null);
            _mainViewModel.Apply(LiveSnapshot.Empty with { HearthstoneRunning = true });
            await Shot("notread");
            _mainViewModel.SetCollection(saved.Collection);
            _mainViewModel.ShowCollectionTab("Coins");
            await Shot("nocosmetics");
            _mainViewModel.Apply(LiveSnapshot.Empty);
            _mainViewModel.ShowCollectionTab("Sets");
            _mainViewModel.CollectionReadable = false;
            _mainViewModel.SetCollection(null);
            await Shot("unsupported");
            _mainViewModel.CollectionReadable = true;
            // Reading the game client turned off (Settings → Data): the page says so and where to turn it back on.
            _mainViewModel.ReadGameMemory = false;
            await Shot("readingoff");
            _mainViewModel.ReadGameMemory = true;
            _mainViewModel.SetCollection(saved.Collection);
            var cards = _cards;
            _mainViewModel.SetCards(Hearthpulse.Core.Cards.CardDb.Empty);
            await Shot("nocards");
            _mainViewModel.SetCards(cards);
        }
        else
        {
            Console.WriteLine("collectionpage: the card data lacks the cards for the made-up states, skipped");
        }

        _mainViewModel.SetCollection(_session.Collection);
        _mainViewModel.SetCosmetics(_session.Cosmetics);
        _mainViewModel.ShowCollectionTab("Sets");
    }

    /// <summary>The key of the card back group with the most backs (a frame of one back says little).</summary>
    private string LargestGroup()
    {
        var counts = _mainViewModel.CollectionBackGroups.Select(g => (g.Key, Count: int.TryParse(g.Label[(g.Label.LastIndexOf('/') + 1)..], NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) ? n : 0));
        return counts.OrderByDescending(g => g.Count).First().Key;
    }

    /// <summary>
    /// The page's content in full with its own margins, drawn straight (no brush): a shadowed tile past the content's box
    /// shifts the shared <c>SaveFull</c>'s picture, and a brush of a very tall page is drawn blurred.
    /// </summary>
    private static void SavePage(FrameworkElement content, string path)
    {
        var margin = content.Margin;
        var width = Math.Ceiling(content.ActualWidth + margin.Left + margin.Right);
        var height = Math.Ceiling(content.ActualHeight + margin.Top + Math.Min(margin.Bottom, 40));
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen())
        {
            dc.DrawRectangle(content.TryFindResource("Bg") as Brush ?? Brushes.Black, null, new Rect(0, 0, width, height));
        }

        bitmap.Render(back);
        // The content draws at its offset in its parent: its left and top margins.
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Tests;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The Collection page in both hosts (docs/collection-page.md): the WPF and the Avalonia view model show the same
/// status line, empty state and content for the same data in every state (AC20), and the WPF page binds them - the
/// tabs, the status line with its mark, one tile per set, one class's skins. The Avalonia page:
/// <c>Hearthpulse.Tests/CollectionPageLayoutTests</c>.
/// </summary>
public sealed class CollectionPageContractTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void Both_view_models_show_the_same_page_in_every_state()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline, CollectionReadable = true };
        var ava = new AvaMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.UI.Infrastructure.BackgroundWork.Inline, CollectionReadable = true };
        var cards = CollectionPageFixture.Cards;
        Both(m => m.SetCards(cards), m => m.SetCards(cards));
        wpf.PageShown("Collection");
        ava.PageShown("Collection");

        void Both(Action<WpfMain> w, Action<AvaMain> a)
        {
            w(wpf);
            a(ava);
        }

        void Same(string what)
        {
            foreach (var tab in WpfMain.CollectionTabs)
            {
                Both(m => m.ShowCollectionTab(tab), m => m.ShowCollectionTab(tab));
                Assert.Equal((Status(wpf.CollectionPageStatus), wpf.CollectionPageEmpty, wpf.CollectionPageEmptyGlyph, wpf.CollectionArtOpacity, what, tab),
                    (Status(ava.CollectionPageStatus), ava.CollectionPageEmpty, ava.CollectionPageEmptyGlyph, ava.CollectionArtOpacity, what, tab));
                Assert.Equal((wpf.CollectionSetsShown, wpf.CollectionSkinsShown, wpf.CollectionCoinsShown, wpf.CollectionBacksShown, wpf.CollectionFilterShown),
                    (ava.CollectionSetsShown, ava.CollectionSkinsShown, ava.CollectionCoinsShown, ava.CollectionBacksShown, ava.CollectionFilterShown));
                Assert.Equal(wpf.CollectionSum.Select(t => t.ToString()), ava.CollectionSum.Select(t => t.ToString()));
                Assert.Equal(wpf.CollectionSets.Select(s => (s.A11y, s.Percent, s.Group, s.Golden, s.ArtOpacity)), ava.CollectionSets.Select(s => (s.A11y, s.Percent, s.Group, s.Golden, s.ArtOpacity)));
                Assert.Equal(wpf.CollectionSkinClasses.Select(c => c.ToString()), ava.CollectionSkinClasses.Select(c => c.ToString()));
                Assert.Equal(wpf.CollectionSkins.Select(s => s.ToString()), ava.CollectionSkins.Select(s => s.ToString()));
                Assert.Equal(wpf.CollectionCoins.Select(s => s.ToString()), ava.CollectionCoins.Select(s => s.ToString()));
                Assert.Equal(wpf.CollectionBackGroups.Select(c => c.ToString()), ava.CollectionBackGroups.Select(c => c.ToString()));
                Assert.Equal(wpf.CollectionBacks.Select(b => b.ToString()), ava.CollectionBacks.Select(b => b.ToString()));
            }

            Both(m => m.ShowCollectionTab("Sets"), m => m.ShowCollectionTab("Sets"));
        }

        Same("no data");
        Assert.Equal(Loc.T("CollectionPage.Empty.None"), wpf.CollectionPageEmpty);

        var running = LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" };
        Both(m => m.Apply(running), m => m.Apply(running));
        Same("not read, the game running");
        Assert.Equal(Loc.T("CollectionPage.Empty.NotRead"), wpf.CollectionPageEmpty);

        Both(m => m.SetCollection(CollectionPageFixture.Collection(confirmed: false)), m => m.SetCollection(CollectionPageFixture.Collection(confirmed: false)));
        Same("saved collection, no cosmetics, the game running");
        Assert.True(wpf.CollectionPageStatus!.Saved);

        Both(m => m.Apply(LiveSnapshot.Empty), m => m.Apply(LiveSnapshot.Empty));
        Both(m => m.SetCosmetics(CollectionPageFixture.Cosmetics(confirmed: false)), m => m.SetCosmetics(CollectionPageFixture.Cosmetics(confirmed: false)));
        Same("saved");

        Both(m => m.CollectionReadable = false, m => m.CollectionReadable = false);
        Same("saved, a system without the read");
        Both(m => m.CollectionReadable = true, m => m.CollectionReadable = true);

        Both(m => m.SetCollection(CollectionPageFixture.Collection()), m => m.SetCollection(CollectionPageFixture.Collection()));
        Both(m => m.SetCosmetics(CollectionPageFixture.Cosmetics()), m => m.SetCosmetics(CollectionPageFixture.Cosmetics()));
        Same("confirmed");
        Assert.NotEmpty(wpf.CollectionSets);

        Both(m => m.ShowCollectionFilter("All"), m => m.ShowCollectionFilter("All"));
        Both(m => m.CollectionGoldenShown = true, m => m.CollectionGoldenShown = true);
        Same("all sets, golden shown");

        Both(m => m.SetCards(CardDb.Empty), m => m.SetCards(CardDb.Empty));
        Same("no card data");
        Assert.Equal(Loc.T("CollectionPage.Empty.NoCards"), wpf.CollectionPageEmpty);

        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            Both(m => m.SetCards(cards), m => m.SetCards(cards));
            Same("Russian");
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }

        static string Status(object? status) => status?.ToString() ?? "none";
    }

    [Fact]
    public void The_wpf_page_shows_its_tabs_status_and_one_tile_per_set() => WpfThread.RunFresh(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline, CollectionReadable = true };
        model.SetCards(CollectionPageFixture.Cards);
        model.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        model.SetCosmetics(CollectionPageFixture.Cosmetics(confirmed: false));
        var window = OffScreen(new WpfWindow(model));
        window.Show();
        window.ShowPage("Collection");
        Pump();
        try
        {
            var page = window.PageCollection;
            Assert.Equal(Visibility.Visible, page.Visibility);
            Assert.True(((RadioButton)page.FindName("TabSets")).IsChecked);

            // The status line with its mark: a saved collection says so, in words.
            var status = (ContentControl)page.FindName("Status");
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Contains(Texts(status), t => t == Loc.T("CollectionPage.Mark.Saved"));
            Assert.Contains(Texts(status), t => t == model.CollectionPageStatus!.Text);

            // One tile per set of the filter, named for a screen reader.
            var sets = (FrameworkElement)page.FindName("SetsView");
            Assert.Equal(Visibility.Visible, sets.Visibility);
            var tiles = Descendants<Border>(sets).Where(b => AutomationProperties.GetName(b) is { Length: > 0 } name && model.CollectionSets.Any(s => s.A11y == name)).ToList();
            Assert.Equal(model.CollectionSets.Count, tiles.Count);

            // The skins tab: the tab button switches the view; one class's tiles are built.
            ((RadioButton)page.FindName("TabSkins")).IsChecked = true;
            Pump();
            Assert.True(model.CollectionTabSkins);
            Assert.Equal(Visibility.Collapsed, sets.Visibility);
            var skins = (FrameworkElement)page.FindName("SkinsView");
            Assert.Equal(Visibility.Visible, skins.Visibility);
            Assert.Equal(model.CollectionSkins.Count, Descendants<StackPanel>(skins).Count(p => p.DataContext is Hearthpulse.App.Main.CollectionItemView && p.Width == 112));

            // The card backs: tiles carry their name and state for a screen reader.
            ((RadioButton)page.FindName("TabBacks")).IsChecked = true;
            Pump();
            var backs = (FrameworkElement)page.FindName("BacksView");
            Assert.Equal(model.CollectionBacks.Select(b => b.A11y), Descendants<StackPanel>(backs).Where(p => p.DataContext is Hearthpulse.App.Main.CardBackView && p.Width == 112).Select(AutomationProperties.GetName));
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    private static IEnumerable<string> Texts(DependencyObject root) => Descendants<TextBlock>(root).Select(t => t.Text);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    private static WpfWindow OffScreen(WpfWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1260;
        window.Height = 840;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

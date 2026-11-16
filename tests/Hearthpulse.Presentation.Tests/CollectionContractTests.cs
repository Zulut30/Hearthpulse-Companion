using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Tests;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The collection in both hosts (docs/collection.md, "In the interface"): the WPF and the Avalonia view model show the
/// same labels, marks, missing cards, filter state, order and status line for the same collection, in every state;
/// and the WPF page shows them - the status line, the filter that a saved collection puts off limits, a named
/// keyboard-reachable label per list, and the block on the deck page (the popup of a label's missing cards is opened
/// in <see cref="CollectionPopupTests"/>, which runs last: <see cref="RealPopups"/>). The Avalonia
/// page: <c>Hearthpulse.Tests/CollectionLayoutTests</c>; the two markups side by side: <c>CollectionMarkupTests</c>.
/// </summary>
public sealed partial class CollectionContractTests
{
    internal static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void Both_view_models_show_the_same_for_the_same_collection_in_every_state()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data) { CollectionReadable = true };
        var ava = new AvaMain(History(), new AppSettings(), TestApp.Data) { CollectionReadable = true };
        wpf.SetCards(CollectionFixture.Cards);
        ava.SetCards(CollectionFixture.Cards);
        wpf.ShowConstructedFormat("FT_STANDARD");
        ava.ShowConstructedFormat("FT_STANDARD");
        wpf.SetConstructedMeta(CollectionFixture.Meta());
        ava.SetConstructedMeta(CollectionFixture.Meta());

        void Same(string what)
        {
            Assert.Equal((wpf.CollectionStatus, what), (ava.CollectionStatus, what));
            Assert.Equal((wpf.HasCollection, wpf.CollectionSaved, wpf.CanBuildAvailable, wpf.CanBuildOnly, wpf.CanBuildNote, wpf.MetaCanBuildEmpty, what),
                (ava.HasCollection, ava.CollectionSaved, ava.CanBuildAvailable, ava.CanBuildOnly, ava.CanBuildNote, ava.MetaCanBuildEmpty, what));
            Assert.Equal(wpf.MetaDeckSorts.Select(s => s.ToString()), ava.MetaDeckSorts.Select(s => s.ToString()));
            Assert.Equal(wpf.MetaDecks.Select(d => Shown(d.Code, d.Build)), ava.MetaDecks.Select(d => Shown(d.Code, d.Build)));
            Assert.Equal(wpf.ArchetypeDecks.Select(d => Shown(d.Code, d.Build)), ava.ArchetypeDecks.Select(d => Shown(d.Code, d.Build)));
            foreach (var code in new[] { CollectionFixture.NeedsDust, CollectionFixture.Owned, null })
            {
                var (w, a) = (wpf.DeckCollectionFor(code), ava.DeckCollectionFor(code));
                Assert.Equal((w.Status, w.Note, Shown(code, w.Build), what), (a.Status, a.Note, Shown(code, a.Build), what));
            }
        }

        void Set(CardCollection? collection)
        {
            wpf.SetCollection(collection);
            ava.SetCollection(collection);
        }

        Same("no collection");
        Assert.All(wpf.MetaDecks, d => Assert.Null(d.Build));

        Set(CollectionFixture.Collection());
        Same("confirmed");
        Assert.Equal(6, wpf.MetaDecks.Count(d => d.Build is not null));

        wpf.CanBuildOnly = true;
        ava.CanBuildOnly = true;
        Same("can build");
        Assert.Equal(3, wpf.MetaDecks.Count);

        wpf.SetMetaDeckSort.Execute("Dust");
        ava.SetMetaDeckSort.Execute("Dust");
        Same("by dust");
        Assert.Equal(CollectionFixture.Owned, wpf.MetaDecks[0].Code);

        Set(CollectionFixture.Collection(confirmed: false));
        Same("saved");
        Assert.False(wpf.CanBuildOnly);
        Assert.All(wpf.MetaDecks.Where(d => d.Build is not null), d => Assert.True(d.Build!.Saved));

        var running = LiveSnapshot.Empty with { HearthstoneRunning = true, Scene = "HUB" };
        wpf.Apply(running);
        ava.Apply(running);
        Same("saved, the game running");

        Set(null);
        Same("not read, the game running");
        wpf.CollectionReadable = false;
        ava.CollectionReadable = false;
        Same("a system without the read");
        Assert.Equal(Loc.T("Collection.Status.Unsupported"), wpf.CollectionStatus);

        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            wpf.CollectionReadable = true;
            ava.CollectionReadable = true;
            Set(CollectionFixture.Collection(confirmed: false));
            Same("saved, in Russian");
            Assert.StartsWith("Сохранённая коллекция от ", wpf.CollectionStatus, StringComparison.Ordinal);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }

        static string Shown(string? code, object? build) =>
            build is null ? code + ": none" : string.Join('|', code, ((dynamic)build).Kind, ((dynamic)build).Glyph, ((dynamic)build).Name, ((dynamic)build).Details, ((dynamic)build).Opacity);
    }

    [Fact]
    public void The_WPF_page_shows_the_status_the_filter_and_a_label_per_list_with_its_missing_cards() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { CollectionReadable = true };
        model.SetCards(CollectionFixture.Cards);
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump();
        try
        {
            window.ShowPage("Standard");
            window.ShowConstructedTab("Top");
            model.SetConstructedMeta(CollectionFixture.Meta());
            model.SetCollection(CollectionFixture.Collection(confirmed: false));
            Pump();
            var view = Descendants(window).OfType<Hearthpulse.App.Main.Meta.ConstructedMetaView>().Single();
            T Named<T>(string name) where T : FrameworkElement => (T)view.FindName(name);
            var card = Named<Border>("MetaDecksPanel");
            Assert.True(card.IsVisible);

            // By a saved collection: the status says so with its date, the filter is off limits and says why.
            var status = Named<TextBlock>("CollectionStatusLine");
            Assert.Equal(model.CollectionStatus, status.Text);
            Assert.True(status.IsVisible);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            var filter = Named<CheckBox>("CanBuildFilter");
            Assert.Equal(Loc.T("Collection.Filter.CanBuild"), filter.Content);
            Assert.False(filter.IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(filter));
            var note = Named<TextBlock>("CanBuildNote");
            Assert.Equal((true, model.CanBuildNote), (note.IsVisible, note.Text));
            var sort = Named<Border>("DeckSortPicker");
            Assert.True(sort.IsVisible);
            Assert.Equal(Loc.T("Collection.Sort.Label"), AutomationProperties.GetName(sort));
            Assert.Equal(new object[] { Loc.T("Collection.Sort.WinRate"), Loc.T("Collection.Sort.Dust") },
                Descendants(sort).OfType<RadioButton>().Select(r => r.Content));

            // A label per list with an answer: a focusable button named by its words, dimmed and marked as saved.
            List<Button> Labels() => Descendants(card).OfType<Button>().Where(b => b.Content is Hearthpulse.App.Main.DeckBuildView && b.IsVisible).ToList();
            var labels = Labels();
            Assert.Equal(6, labels.Count);
            foreach (var label in labels)
            {
                var build = (Hearthpulse.App.Main.DeckBuildView)label.Content;
                Assert.True(label.Focusable && label.IsTabStop);
                Assert.Equal(build.Name, AutomationProperties.GetName(label));
                Assert.Equal(build.Details, AutomationProperties.GetHelpText(label));
                Assert.True(label.ActualHeight >= 24, $"label height {label.ActualHeight}");
                Assert.Contains(Loc.T("Collection.Mark.Saved"), build.Name, StringComparison.Ordinal);
                var texts = Descendants(label).OfType<TextBlock>().ToList();
                Assert.Equal(new[] { build.Glyph, build.Name }, texts.Select(t => t.Text));
                Assert.Equal(TextWrapping.Wrap, texts[1].TextWrapping);
                Assert.Equal(Hearthpulse.App.Main.DeckBuildView.SavedOpacity, Descendants(label).OfType<DockPanel>().First().Opacity);
            }

            // Confirmed: the filter works, and leaves the lists that can be built.
            model.SetCollection(CollectionFixture.Collection());
            Pump();
            Assert.True(filter.IsEnabled);
            Assert.False(note.IsVisible);
            filter.IsChecked = true;
            Pump();
            Assert.True(model.CanBuildOnly);
            Assert.Equal(3, Labels().Count);
            Assert.All(Labels(), b => Assert.Equal(1.0, Descendants(b).OfType<DockPanel>().First().Opacity));

            // No collection: the status says what to do, no label, no order to choose.
            model.SetCollection(null);
            Pump();
            Assert.Equal(Loc.T("Collection.Status.None"), status.Text);
            Assert.Empty(Labels());
            Assert.False(sort.IsVisible);
            Assert.False(filter.IsEnabled);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    [Fact]
    public void The_WPF_deck_page_shows_the_label_the_missing_cards_and_the_status() => WpfThread.RunFresh(() =>
    {
        var history = History();
        var games = new[]
        {
            new GameRecord
            {
                Key = "collection-deck-game-1",
                StartedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local),
                EndedAt = new DateTime(2026, 11, 14, 10, 8, 0, DateTimeKind.Local),
                Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
                PlayerClass = "MAGE", OpponentClass = "WARRIOR", DeckName = "Test Mage", DeckCode = CollectionFixture.NeedsDust,
            },
        };
        Assert.True(history.Add(games[0]));
        var model = new WpfMain(history, new AppSettings(), TestApp.Data) { CollectionReadable = true };
        model.SetCards(CollectionFixture.Cards);
        model.SetCollection(CollectionFixture.Collection());
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump();
        try
        {
            // As the render scene does: a deck opened by the view model (here one the history has), then new stats, which
            // rebuild the deck list. The window follows the view model to the deck page: nothing is made visible by hand.
            window.ShowPage("Standard");
            Pump();
            var built = model.DeckListBuilds;
            model.SetConstructedMeta(CollectionFixture.Meta());
            PumpUntil(() => model.DeckListBuilds > built);
            var family = Assert.Single(model.DeckFamiliesShown);
            Assert.True(model.OpenDeckPage(family.Key));
            built = model.DeckListBuilds;
            model.SetConstructedMeta(CollectionFixture.Meta());
            PumpUntil(() => model.DeckListBuilds > built && model.DeckPage.DeckList.Count > 0);
            Assert.True(model.DeckOpen);
            Assert.True(window.PageDeck.IsVisible);
            var page = window.DeckDetailsPage;
            Assert.True(page.IsVisible);
            Assert.Same(model.DeckPage, page.DataContext);

            var block = (StackPanel)page.FindName("DeckCollection");
            Assert.True(block.IsVisible);
            var build = model.DeckPage.Collection.Build!;
            Assert.Equal("NeedsDust", build.Kind);
            var texts = Descendants(block).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(build.Name, texts);
            Assert.Contains(CollectionFixture.LongName, texts);
            Assert.All(build.Missing, m => Assert.Contains(m.Price, texts));
            Assert.Contains(build.Total, texts);
            Assert.Contains(model.CollectionStatus, texts);
            Assert.Equal(build.Name, AutomationProperties.GetName((ContentControl)page.FindName("DeckBuild")));

            // A new collection reaches the open page: with dust for everything the label changes.
            model.SetCollection(CollectionFixture.Collection(dust: 5000));
            Pump();
            Assert.Equal("CraftableNow", model.DeckPage.Collection.Build!.Kind);
            Assert.Contains(model.DeckPage.Collection.Build.Name, Descendants(block).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));

            // None: the status line alone.
            model.SetCollection(null);
            Pump();
            Assert.False(((ContentControl)page.FindName("DeckBuild")).IsVisible);
            Assert.False(((ContentControl)page.FindName("DeckMissing")).IsVisible);
            Assert.Equal(Loc.T("Collection.Status.None"), ((TextBlock)page.FindName("DeckCollectionStatus")).Text);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    /// <summary>Runs the dispatcher until <paramref name="done"/>: the view model's background work comes back through it.</summary>
    internal static void PumpUntil(Func<bool> done)
    {
        var hang = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(hang.Elapsed < TimeSpan.FromSeconds(30), "the awaited change did not come");
            Pump();
        }

        Pump();
    }

    internal static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}

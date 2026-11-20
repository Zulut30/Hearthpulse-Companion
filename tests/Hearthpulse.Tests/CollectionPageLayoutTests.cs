using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Pages;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia Collection page laid out headless in the main view (docs/collection-page.md): the bindings of every tab
/// reach their controls (compiled bindings, the status line's template, the tiles), one class of hero skins is built at a
/// time, the tiles name themselves for a screen reader, and the set tiles stack in one column in the compact layout.
/// The WPF twin: <c>Hearthpulse.Presentation.Tests/CollectionPageContractTests</c>.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class CollectionPageLayoutTests
{
    public CollectionPageLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Theory]
    [InlineData(1260)]
    [InlineData(420)]
    public void Every_tab_binds_its_content_and_the_sets_stack_in_compact(double width) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-collection-page-layout") + ".db"), new AppSettings(), TestDataFolder.App)
        {
            Background = BackgroundWork.Inline,
            CollectionReadable = true,
        };
        model.SetCards(CollectionPageFixture.Cards);
        model.SetCollection(CollectionPageFixture.Collection(confirmed: false));
        model.SetCosmetics(CollectionPageFixture.Cosmetics());
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1600 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Collection");
            PageRowsPanelAvaloniaTests.Settle(window);
            var page = view.GetVisualDescendants().OfType<CollectionPage>().Single();
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

            // The status line of a saved collection: its text and the mark, in words.
            var status = Named<ContentControl>("Status");
            Assert.True(status.IsEffectivelyVisible);
            var texts = status.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains(model.CollectionPageStatus!.Text, texts);
            Assert.Contains(Loc.T("CollectionPage.Mark.Saved"), texts);

            // A tile per set, named; two columns, one in compact.
            var sets = Named<StackPanel>("SetsView");
            Assert.True(sets.IsEffectivelyVisible);
            var tiles = sets.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("setTile")).ToList();
            Assert.Equal(model.CollectionSets.Select(s => s.A11y), tiles.Select(AutomationProperties.GetName));
            var grid = sets.GetVisualDescendants().OfType<UniformGrid>().Single(g => g.Classes.Contains("setGrid"));
            Assert.Equal(width < 900 ? 1 : 2, grid.Columns);

            // Hero skins: one class's tiles, owned first; a missing one says so under its name.
            Named<RadioButton>("TabSkins").IsChecked = true;
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(model.CollectionTabSkins);
            var skins = Named<StackPanel>("SkinsView");
            Assert.True(skins.IsEffectivelyVisible);
            Assert.False(sets.IsEffectivelyVisible);
            var skinTiles = skins.GetVisualDescendants().OfType<StackPanel>().Where(p => p.DataContext is CollectionItemView && p.Width == 112).ToList();
            Assert.Equal(model.CollectionSkins.Count, skinTiles.Count);
            Assert.Equal(model.CollectionSkins.Select(s => s.A11y), skinTiles.Select(AutomationProperties.GetName));

            // A class chip shows the other class.
            var chip = skins.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Content as string == model.CollectionSkinClasses[1].Label);
            chip.Command!.Execute(chip.CommandParameter);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Equal(model.CollectionSkinClasses[1].Key, model.CollectionSkinClasses.Single(c => c.Selected).Key);

            // Coins: favourites first with the heart; the one without card data has no art but its name.
            Named<RadioButton>("TabCoins").IsChecked = true;
            PageRowsPanelAvaloniaTests.Settle(window);
            var coins = Named<StackPanel>("CoinsView");
            Assert.True(coins.IsEffectivelyVisible);
            Assert.Equal(model.CollectionCoins.Select(c => c.A11y),
                coins.GetVisualDescendants().OfType<StackPanel>().Where(p => p.DataContext is CollectionItemView && p.Width == 112).Select(AutomationProperties.GetName));

            // Card backs: one group's tiles, each with its medallion and its name for a screen reader.
            Named<RadioButton>("TabBacks").IsChecked = true;
            PageRowsPanelAvaloniaTests.Settle(window);
            var backs = Named<StackPanel>("BacksView");
            Assert.True(backs.IsEffectivelyVisible);
            var backTiles = backs.GetVisualDescendants().OfType<StackPanel>().Where(p => p.DataContext is CardBackView && p.Width == 112).ToList();
            Assert.Equal(model.CollectionBacks.Select(b => b.A11y), backTiles.Select(AutomationProperties.GetName));
            Assert.All(backTiles, t => Assert.Single(t.GetVisualDescendants().OfType<Hearthpulse.UI.Controls.Scallop>()));

            // No data at all: the empty state, no status line, nothing else.
            model.SetCosmetics(null);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(status.IsEffectivelyVisible);
            Assert.False(backs.IsEffectivelyVisible);
            var empty = Named<ContentControl>("Empty");
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Contains(Loc.T("CollectionPage.Empty.NoCosmetics"), empty.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    });
}

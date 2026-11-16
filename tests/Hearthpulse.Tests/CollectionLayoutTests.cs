using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The collection in the Avalonia page, laid out headless: the status line, the "can build" filter, the order and the
/// build labels of the top decks, and the label with its missing cards as the deck page and the label's flyout show
/// them. At the compact window's narrowest (420) and at the usual width, in Russian and English, with the longest
/// texts (a saved collection, an estimate, a long card name): every text is whole, wrapped where it has to be and
/// never cut, the controls are named for a screen reader and reachable from the keyboard.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed partial class CollectionLayoutTests
{
    /// <summary>As in <see cref="BgOwnLayoutTests"/>: the headless platform gives every character its font size as width.</summary>
    private const double GlyphScale = 0.6;

    public CollectionLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Theory]
    [InlineData(420, "ru", false)]
    [InlineData(420, "en", false)]
    [InlineData(1280, "ru", false)]
    [InlineData(1280, "en", false)]
    [InlineData(420, "ru", true)]
    [InlineData(1280, "en", true)]
    public void The_status_the_filter_and_the_labels_of_the_top_decks_are_whole(double width, string language, bool confirmed) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-collection-layout") + ".db"), new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        model.SetCards(CollectionFixture.Cards);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1600 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Standard");
            view.ShowConstructedTab("Top");
            model.SetConstructedMeta(CollectionFixture.Meta());
            model.SetCollection(CollectionFixture.Collection(confirmed));
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.Equal(width < 900, view.Classes.Contains("compact"));

            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var card = Named<Border>("MetaDecksPanel");
            Assert.True(card.IsEffectivelyVisible);
            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right;
            Assert.True(room > 200, $"room {room}");

            // The status line and, by a saved collection, the reason the filter is off limits: on screen, wrapped, whole.
            var bar = Named<StackPanel>("CollectionBar");
            var status = Named<TextBlock>("CollectionStatusLine");
            Assert.Equal(model.CollectionStatus, status.Text);
            Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            var note = Named<TextBlock>("CanBuildNote");
            Assert.Equal((!confirmed, TextWrapping.Wrap), (note.IsEffectivelyVisible, note.TextWrapping));
            foreach (var block in bar.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                Whole(block, card, room, block.Text!);
            }

            // The filter: a switch with its words as content (its name), in the tab order, off limits by a saved collection.
            var filter = Named<CheckBox>("CanBuildFilter");
            Assert.Equal(Loc.T("Collection.Filter.CanBuild"), filter.Content);
            Assert.True(filter.Focusable && filter.IsTabStop && filter.IsEffectivelyVisible);
            Assert.Equal(confirmed, filter.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(filter));
            Assert.True(filter.Bounds.Height >= 24, $"filter height {filter.Bounds.Height}");
            InCard(filter, card, room, "filter");

            // The order: two choices, named, inside the card (they wrap under the filter when the row is narrow).
            var sort = Named<Border>("DeckSortPicker");
            Assert.Equal(Loc.T("Collection.Sort.Label"), AutomationProperties.GetName(sort));
            var choices = sort.GetVisualDescendants().OfType<RadioButton>().ToList();
            Assert.Equal(new object?[] { Loc.T("Collection.Sort.WinRate"), Loc.T("Collection.Sort.Dust") }, choices.Select(c => c.Content));
            Assert.All(choices, c => Assert.True(c.Focusable && c.IsEffectivelyVisible));
            InCard(sort, card, room, "order");

            // Every list with an answer has its label: a button (keyboard, a name, the missing cards as help text and in
            // a flyout), a glyph beside the words, dimmed and marked by a saved collection. The unknown list has none.
            var labels = card.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("deckBuild")).ToList();
            Assert.Equal(model.MetaDecks.Count, labels.Count);
            var shown = labels.Where(b => b.IsEffectivelyVisible).ToList();
            Assert.Equal(model.MetaDecks.Count(d => d.Build is not null), shown.Count);
            Assert.Equal(6, shown.Count);
            foreach (var label in shown)
            {
                var build = Assert.IsType<MetaDeckRow>(label.DataContext).Build!;
                Assert.True(label.Focusable && label.IsTabStop);
                Assert.Equal(build.Name, AutomationProperties.GetName(label));
                Assert.Equal(build.Details, AutomationProperties.GetHelpText(label));
                Assert.NotNull(label.Flyout);
                // WCAG 2.5.8: a pointer target of at least 24 px.
                Assert.True(label.Bounds.Height >= 24, $"label height {label.Bounds.Height}");
                var texts = label.GetVisualDescendants().OfType<TextBlock>().ToList();
                Assert.Equal(new[] { build.Glyph, build.Name }, texts.Select(t => t.Text));
                Assert.Equal(TextWrapping.Wrap, texts[1].TextWrapping);
                Whole(texts[1], card, room, build.Name);
                var dimmed = label.GetVisualDescendants().OfType<DockPanel>().First();
                Assert.Equal(confirmed ? 1 : DeckBuildView.SavedOpacity, dimmed.Opacity);
                Assert.Equal(!confirmed, build.Name.Contains(Loc.T("Collection.Mark.Saved"), StringComparison.Ordinal));
            }

            // Turning the filter on from the view model leaves the three lists that can be built, each still labelled.
            if (confirmed)
            {
                filter.IsChecked = true;
                PageRowsPanelAvaloniaTests.Settle(window);
                Assert.True(model.CanBuildOnly);
                Assert.Equal(3, card.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("deckBuild") && b.IsEffectivelyVisible));
            }
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>
    /// The label and the missing cards as the deck page's list card and a label's flyout show them (the shared
    /// templates of CollectionResources.axaml), in a column as narrow as the compact deck page's and at the flyout's width.
    /// </summary>
    [Theory]
    [InlineData(260, "ru")]
    [InlineData(260, "en")]
    [InlineData(420, "ru")]
    [InlineData(420, "en")]
    public void The_label_and_the_missing_cards_are_whole_in_a_narrow_column(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-collection-layout") + ".db"), new AppSettings(), TestDataFolder.App) { CollectionReadable = true };
        model.SetCards(CollectionFixture.Cards);
        model.SetCollection(CollectionFixture.Collection(confirmed: false));
        var pages = new ResourceInclude(new Uri("avares://Hearthpulse.UI/")) { Source = new Uri("avares://Hearthpulse.UI/Main/CollectionResources.axaml") };
        var window = new Window { Width = width, Height = 900 };
        window.Resources.MergedDictionaries.Add(pages);
        var card = new Border { Padding = new Thickness(16, 14) };
        var column = new StackPanel();
        card.Child = column;
        window.Content = card;
        try
        {
            // The long legendary and two epics missing; then the list no dust completes; then an estimate.
            foreach (var code in new[] { CollectionFixture.NeedsDust, CollectionFixture.NotCraftable, CollectionFixture.Reward })
            {
                var block = model.DeckCollectionFor(code);
                Assert.NotNull(block.Build);
                column.Children.Add(new ContentControl { Content = block.Build, ContentTemplate = (IDataTemplate)window.FindResource("DeckBuildLabel")! });
                column.Children.Add(new ContentControl { Content = block.Build, ContentTemplate = (IDataTemplate)window.FindResource("DeckMissingList")! });
                column.Children.Add(new TextBlock { Text = block.Status, TextWrapping = TextWrapping.Wrap });
            }

            window.Show();
            PageRowsPanelAvaloniaTests.Settle(window);
            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right;
            var texts = column.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            foreach (var text in texts)
            {
                Whole(text, card, room, text.Text!);
            }

            // What the player needs is among them: the long name, each price, a note of the estimate, the sums, the status.
            var shown = texts.Select(t => t.Text!).ToList();
            var needs = model.DeckCollectionFor(CollectionFixture.NeedsDust).Build!;
            Assert.Contains(CollectionFixture.LongName, shown);
            Assert.All(needs.Missing, m => Assert.Contains(m.Price, shown));
            Assert.Contains(needs.Total, shown);
            Assert.Contains(Loc.T("Collection.Missing.NotCraftable"), shown);
            Assert.Contains(Loc.T("Collection.Missing.TotalNone"), shown);
            Assert.Contains(Loc.T("Collection.Missing.CraftUncertain"), shown);
            Assert.Contains(model.CollectionStatus, shown);

            // A price stays on one line beside its card's name, right of it.
            var name = texts.First(t => t.Text == CollectionFixture.LongName);
            var price = texts.First(t => t.Text == needs.Missing.Single(m => m.CardId == "C_LEGEND").Price);
            Assert.Single(price.TextLayout.TextLines);
            Assert.True(price.TranslatePoint(default, card)!.Value.X >= name.TranslatePoint(new Point(name.Bounds.Width, 0), card)!.Value.X, "the price stands right of the name");
            // At the flyout's least width, with the headless platform's wide characters, the name keeps a readable column.
            Assert.True(name.Bounds.Width >= 80, $"name column {name.Bounds.Width}");
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>The text is laid out whole (no ellipsis, no line wider than the block) and the block lies inside the card's content.</summary>
    private static void Whole(TextBlock block, Border card, double room, string what)
    {
        Assert.True(block.TextTrimming == TextTrimming.None, $"{what}: trimming is on");
        var layout = block.TextLayout;
        Assert.All(layout.TextLines, line => Assert.False(line.HasCollapsed, what));
        Assert.All(layout.TextLines, line => Assert.True(line.Width * GlyphScale <= block.Bounds.Width + 0.5, $"{what}: line {line.Width * GlyphScale} in {block.Bounds.Width}"));
        InCard(block, card, room, what);
    }

    private static void InCard(Control control, Border card, double room, string what)
    {
        var left = control.TranslatePoint(default, card)!.Value.X;
        var inner = card.Padding.Left + card.BorderThickness.Left;
        Assert.True(left >= inner - 0.5 && left + control.Bounds.Width <= inner + room + 0.5, $"{what}: {left}..{left + control.Bounds.Width} outside {inner}..{inner + room}");
    }
}

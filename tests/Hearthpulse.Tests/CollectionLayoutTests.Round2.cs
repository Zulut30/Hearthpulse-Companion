using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Decks;
using Hearthpulse.UI.Main.Meta;

namespace Hearthpulse.Tests;

/// <summary>
/// The collection in the Avalonia page, second round: the real deck page (<see cref="DeckDetailsView"/> in the main
/// view, opened as the render scene opens it) shows its collection block whole at both widths; the missing cards of a
/// label open in a flyout that takes focus and gives it back on Escape; the compact top-deck row keeps the deck's name
/// and its Copy button; and a pricing under way or failed has its own line.
/// </summary>
public sealed partial class CollectionLayoutTests
{
    private static MainViewModel Model() =>
        new(new HistoryStore(TestDataFolder.Scratch("hp-collection-layout") + ".db"), new AppSettings(), TestDataFolder.App) { CollectionReadable = true };

    private static DeckFamily SampleDeck()
    {
        var game = new GameRecord
        {
            Key = "collection-layout-sample",
            StartedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local),
            EndedAt = new DateTime(2026, 11, 14, 10, 8, 0, DateTimeKind.Local),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            Result = "Win",
            PlayerClass = "MAGE",
            OpponentClass = "WARRIOR",
            DeckName = "Sample Mage",
            DeckCode = CollectionFixture.NeedsDust,
        };
        return DeckFamilies.Build(DeckFamilies.Versions([game]), []).Single();
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/>: background work of the view model comes back through it.</summary>
    private static void SettleUntil(Window window, Func<bool> done, string what)
    {
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return done();
        }, TimeSpan.FromSeconds(30)), what);
        PageRowsPanelAvaloniaTests.Settle(window);
    }

    private static (Window Window, MainView View) Open(MainViewModel model, double width, double height = 1600)
    {
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = height };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        return (window, view);
    }

    // ------------------------------------------------------------------------------------------ the real deck page

    [Theory]
    [InlineData(420, "ru", "confirmed")]
    [InlineData(420, "en", "saved")]
    [InlineData(1280, "ru", "saved")]
    [InlineData(1280, "en", "confirmed")]
    [InlineData(420, "ru", "none")]
    [InlineData(1280, "en", "none")]
    public void The_deck_page_shows_its_collection_block_whole(double width, string language, string state) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = Model();
        model.SetCards(CollectionFixture.Cards);
        var (window, view) = Open(model, width, 3200);
        try
        {
            view.ShowPage("Standard");
            view.ShowConstructedTab("Top");
            model.SetCollection(state == "none" ? null : CollectionFixture.Collection(confirmed: state == "confirmed"));

            // As the render scene does: the made-up deck is opened, then the stats are set, which rebuilds the deck list
            // from a history without that deck. The page stays on the deck.
            model.OpenSampleDeck(SampleDeck());
            var built = model.DeckListBuilds;
            model.SetConstructedMeta(CollectionFixture.Meta());
            SettleUntil(window, () => model.DeckListBuilds > built && model.DeckPage.DeckList.Count > 0, "the deck list and the deck page were built");
            Assert.True(model.DeckOpen);

            // The real DeckDetailsView of the main view is what is on screen, not the tier list.
            var page = view.GetVisualDescendants().OfType<DeckDetailsView>().Single();
            Assert.True(page.IsEffectivelyVisible);
            Assert.Same(model.DeckPage, page.DataContext);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ConstructedMetaView>(), meta => meta.IsEffectivelyVisible);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

            var block = Named<StackPanel>("DeckCollection");
            Assert.True(block.IsEffectivelyVisible);
            var card = block.FindAncestorOfType<Border>()!;
            while (!card.Classes.Contains("card"))
            {
                card = card.FindAncestorOfType<Border>()!;
            }

            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right;
            Assert.True(room > 180, $"room {room}");

            // The status line, always; with a collection the label and the missing cards of this deck.
            var status = Named<TextBlock>("DeckCollectionStatus");
            Assert.Equal(model.CollectionStatus, status.Text);
            Assert.True(status.IsEffectivelyVisible);
            var label = Named<ContentControl>("DeckBuild");
            var missing = Named<ContentControl>("DeckMissing");
            Assert.Equal((state != "none", state != "none"), (label.IsEffectivelyVisible, missing.IsEffectivelyVisible));
            var texts = block.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            foreach (var text in texts)
            {
                Whole(text, card, room, text.Text!);
            }

            var shown = texts.Select(t => t.Text!).ToList();
            if (state == "none")
            {
                Assert.Equal(new[] { model.CollectionStatus }, shown);
            }
            else
            {
                var build = model.DeckPage.Collection.Build!;
                Assert.Equal("NeedsDust", build.Kind);
                Assert.Equal(build.Name, AutomationProperties.GetName(label));
                Assert.Contains(build.Name, shown);
                Assert.Contains(build.Tip, shown);
                Assert.Contains(CollectionFixture.LongName, shown);
                Assert.All(build.Missing, m => Assert.Contains(m.Price, shown));
                Assert.Contains(build.Total, shown);
                Assert.Equal(state == "saved", build.Name.Contains(Loc.T("Collection.Mark.Saved"), StringComparison.Ordinal));
                Assert.Equal(state == "saved" ? DeckBuildView.SavedOpacity : 1, label.GetVisualDescendants().OfType<DockPanel>().First().Opacity);
            }

            // A deck that cannot be checked says why in the same block (here: the card data went away).
            if (state == "confirmed")
            {
                model.SetCards(Hearthpulse.Core.Cards.CardDb.Empty);
                PageRowsPanelAvaloniaTests.Settle(window);
                var note = block.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible && t.Text == Loc.T("Collection.Deck.NoCardData"));
                Whole(note, card, room, note.Text!);
                Assert.False(label.IsEffectivelyVisible);
            }
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    // ------------------------------------------------------------------------------------------ the open list and focus

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void A_label_s_missing_cards_open_take_focus_and_give_it_back_on_escape(string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = Model();
        model.SetCards(CollectionFixture.Cards);
        var (window, view) = Open(model, 1280);
        try
        {
            view.ShowPage("Standard");
            view.ShowConstructedTab("Top");
            model.SetConstructedMeta(CollectionFixture.Meta());
            model.SetCollection(CollectionFixture.Collection());
            PageRowsPanelAvaloniaTests.Settle(window);
            var meta = view.GetVisualDescendants().OfType<ConstructedMetaView>().Single();

            // Opened as a press of the label opens it: the first label with missing cards (two epics and a legendary).
            var flyout = Assert.IsType<Flyout>(meta.OpenFirstDeckBuild());
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(flyout.IsOpen);
            var label = Assert.IsType<Button>(flyout.Target);
            var build = Assert.IsType<MetaDeckRow>(label.DataContext).Build!;
            Assert.Equal(CollectionFixture.NeedsDust, ((MetaDeckRow)label.DataContext!).Code);
            var details = Assert.IsAssignableFrom<ContentControl>(flyout.Content);
            var presenter = details.FindAncestorOfType<FlyoutPresenter>()!;
            Assert.True(presenter.IsEffectivelyVisible);
            Assert.Contains("deckBuild", presenter.Classes);

            // A floating plate must be opaque: the page's translucent card fill would show deck rows through it.
            Assert.Same(window.FindResource("Surface2"), presenter.Background);
            Assert.Equal((byte)255, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(presenter.Background).Color.A);
            Assert.Same(window.FindResource("CardEdge"), presenter.BorderBrush);
            Assert.Equal(new Thickness(16, 14), presenter.Padding);
            Assert.True(presenter.Bounds.Width is >= 260 and <= 420, $"flyout width {presenter.Bounds.Width}");

            // Its texts: the reason, each card with its price, the sum; whole inside the flyout.
            var texts = presenter.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            var shown = texts.Select(t => t.Text!).ToList();
            Assert.Contains(build.Tip, shown);
            Assert.Contains(CollectionFixture.LongName, shown);
            Assert.All(build.Missing, m => Assert.Contains(m.Price, shown));
            Assert.Contains(build.Total, shown);
            Assert.Contains(Loc.T("Collection.Missing.Title"), shown);
            foreach (var text in texts)
            {
                Assert.True(text.TextTrimming == TextTrimming.None, text.Text);
                Assert.All(text.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, text.Text));
                var left = text.TranslatePoint(default, presenter)!.Value.X;
                Assert.True(left >= 0 && left + text.Bounds.Width <= presenter.Bounds.Width + 0.5, $"{text.Text}: {left}..{left + text.Bounds.Width} in {presenter.Bounds.Width}");
            }

            // Focus is in the list (named for a screen reader), not left on the page behind it.
            var popup = TopLevel.GetTopLevel(presenter)!;
            Assert.Same(details, popup.FocusManager!.GetFocusedElement());
            Assert.True(details.Focusable);
            Assert.Equal(build.Details, AutomationProperties.GetName(details));

            // Escape closes the list and focus is back on the label it was opened from.
            popup.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(flyout.IsOpen);
            Assert.Same(label, window.FocusManager!.GetFocusedElement());

            // Open again; a new collection replaces the rows: the list of the old numbers does not stay on screen.
            Assert.NotNull(meta.OpenFirstDeckBuild());
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(flyout.IsOpen);
            model.SetCollection(CollectionFixture.Collection(dust: 5000));
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(flyout.IsOpen);
            Assert.False(label.IsEffectivelyVisible && ReferenceEquals(label.DataContext, model.MetaDecks.FirstOrDefault(d => d.Code == CollectionFixture.NeedsDust)) && flyout.IsOpen);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    // ------------------------------------------------------------------------------------------ the compact row

    [Theory]
    [InlineData(420, "ru")]
    [InlineData(420, "en")]
    [InlineData(1280, "ru")]
    public void A_top_deck_row_keeps_the_deck_s_name_and_its_copy_button_at_every_width(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = Model();
        model.SetCards(CollectionFixture.Cards);
        var (window, view) = Open(model, width);
        try
        {
            view.ShowPage("Standard");
            view.ShowConstructedTab("Top");
            model.SetConstructedMeta(CollectionFixture.Meta());
            model.SetCollection(CollectionFixture.Collection(confirmed: false));
            PageRowsPanelAvaloniaTests.Settle(window);
            var compact = width < 900;
            Assert.Equal(compact, view.Classes.Contains("compact"));
            var card = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MetaDecksPanel");
            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right;
            var rows = card.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("row") && b.DataContext is MetaDeckRow).ToList();
            Assert.Equal(model.MetaDecks.Count, rows.Count);
            foreach (var row in rows)
            {
                var deck = (MetaDeckRow)row.DataContext!;

                // The name has a column to stand in (it was squeezed to nothing at 420) and its full text in a tooltip.
                var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == deck.Name);
                Assert.True(name.IsEffectivelyVisible);
                Assert.True(name.Bounds.Width >= 72, $"name column {name.Bounds.Width} at {width}");
                Assert.Equal(deck.Name, ToolTip.GetTip(name));
                InCard(name, card, room, "name");

                // The win rate is always there; games and dust give their room away in compact.
                var winRate = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == deck.WinRate);
                Assert.True(winRate.IsEffectivelyVisible);
                InCard(winRate, card, room, "win rate");
                var games = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == deck.Games && t.Classes.Contains("metaWide"));
                Assert.Equal(!compact, games.IsEffectivelyVisible);

                // One Copy button on screen, whole and inside the card: beside the numbers, or on the second line in compact.
                var copies = row.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, Loc.T("Main.Meta.Copy"))).ToList();
                Assert.Equal(2, copies.Count);
                var copy = Assert.Single(copies, b => b.IsEffectivelyVisible);
                Assert.Equal(compact, copy.Classes.Contains("metaNarrow"));
                Assert.Same(model.CopyDeck, copy.Command);
                Assert.Same(deck, copy.CommandParameter);
                InCard(copy, card, room, "copy");
                Assert.True(copy.Bounds.Height >= 24, $"copy height {copy.Bounds.Height}");
                var text = copy.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.All(text.TextLayout.TextLines, line => Assert.True(line.Width * GlyphScale <= text.Bounds.Width + 0.5, "copy text"));
            }

            // The head of the table follows: "Deck" and the win rate stay, games and dust go with their columns.
            TextBlock Head(string key) => card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Loc.T(key) && t.Classes.Contains("tableHead"));
            Assert.True(Head("Main.Table.Deck").IsEffectivelyVisible);
            Assert.True(Head("Main.Table.WinRate").IsEffectivelyVisible);
            Assert.Equal(!compact, Head("Main.Table.Games").IsEffectivelyVisible);
            Assert.Equal(!compact, Head("Main.Table.Dust").IsEffectivelyVisible);
            Assert.Equal(Loc.T("Collection.Dust.ColumnTip"), ToolTip.GetTip(Head("Main.Table.Dust")));

            // The head's win rate stands over the rows' win rates.
            var firstRate = rows[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == model.MetaDecks[0].WinRate);
            double Right(Control c) => c.TranslatePoint(new Point(c.Bounds.Width, 0), card)!.Value.X;
            Assert.Equal(Right(Head("Main.Table.WinRate")), Right(firstRate), 1.0);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    // ------------------------------------------------------------------------------------------ pricing under way, failed

    [Theory]
    [InlineData(420, "ru")]
    [InlineData(1280, "en")]
    public void A_pricing_under_way_and_a_failed_one_say_so_on_the_page(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = Model();
        model.SetCards(CollectionFixture.Cards);
        var (window, view) = Open(model, width);
        using var gate = new ManualResetEventSlim();
        try
        {
            view.ShowPage("Standard");
            view.ShowConstructedTab("Top");
            model.SetConstructedMeta(CollectionFixture.Meta(more: MainViewModel.InlineDeckCosts + 20));
            PageRowsPanelAvaloniaTests.Settle(window);
            var real = model.PriceDecksWith;
            var fail = true;
            model.PriceDecksWith = (codes, collection, cards) =>
            {
                Assert.True(gate.Wait(TimeSpan.FromSeconds(30)), "the gate opened");
                return fail ? throw new InvalidOperationException("synthetic pricing failure") : real(codes, collection, cards);
            };
            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var card = Named<Border>("MetaDecksPanel");
            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right;

            model.CanBuildOnly = true;
            model.SetCollection(CollectionFixture.Collection());
            PageRowsPanelAvaloniaTests.Settle(window);

            // Under way: no rows yet, and the card says what it is doing instead of standing empty.
            Assert.True(model.MetaPricingShown, $"status {model.MetaStatusShown}, filter {model.CanBuildOnly}, pending {model.DeckCostsPending}");
            var pricing = Named<ContentControl>("MetaPricing");
            Assert.True(pricing.IsEffectivelyVisible);
            Assert.Equal(Loc.T("Collection.Pricing"), pricing.Content);
            Assert.False(Named<WrapPanel>("DeckCostsFailedLine").IsEffectivelyVisible);
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("deckBuild") && b.IsEffectivelyVisible);

            // Failed: the line with its retry button, whole and reachable from the keyboard.
            gate.Set();
            SettleUntil(window, () => model.DeckCostsFailed, "the pricing failed");
            Assert.False(pricing.IsEffectivelyVisible);
            var failed = Named<WrapPanel>("DeckCostsFailedLine");
            Assert.True(failed.IsEffectivelyVisible);
            var message = failed.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Loc.T("Collection.PricingFailed"));
            Whole(message, card, room, message.Text!);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(message));
            var retry = failed.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal(Loc.T("Collection.PricingRetry"), retry.Content);
            Assert.True(retry.Focusable && retry.IsEffectivelyVisible && retry.Bounds.Height >= 24);
            InCard(retry, card, room, "retry");

            // "Check again" prices the lists and the rows come.
            fail = false;
            retry.Command!.Execute(null);
            SettleUntil(window, () => !model.DeckCostsPending, "the pricing ended");
            Assert.False(failed.IsEffectivelyVisible);
            Assert.Equal(3, card.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("deckBuild") && b.IsEffectivelyVisible));
        }
        finally
        {
            gate.Set();
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });
}

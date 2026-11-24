using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using Hearthpulse.Tests;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfMeta = Hearthpulse.App.Main.Meta.ConstructedMetaView;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF deck page as the render scene opens it: the made-up deck opens the real deck page and stays through a
/// rebuild of the deck list. Also what the popup tests share: a window off screen on the top decks, a made-up deck.
/// </summary>
public sealed partial class CollectionContractTests
{
    /// <summary>
    /// The view model of a window on the top decks: an empty history read on this thread, and the setup wizard, which an
    /// empty history offers, already done. A read left on a pool thread came back at any point of the test and opened the
    /// wizard over the page then (review of TASK-2026-11-23-perf-arch-round2 P2, B1).
    /// </summary>
    internal static WpfMain TopDecksModel() => new(History(), new AppSettings { SetupDone = true }, TestApp.Data)
    {
        Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline, CollectionReadable = true,
    };

    internal static WpfWindow OffScreen(WpfMain model) => new(model)
    {
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
        ShowActivated = false, ShowInTaskbar = false,
    };

    internal static WpfMain TopDecks(WpfWindow window, WpfMain model)
    {
        window.Show();
        Pump();
        window.ShowPage("Standard");
        window.ShowConstructedTab("Top");
        model.SetConstructedMeta(CollectionFixture.Meta());
        model.SetCollection(CollectionFixture.Collection());
        Pump();
        return model;
    }

    internal static DeckFamily SampleDeck()
    {
        var game = new GameRecord
        {
            Key = "collection-contract-sample",
            StartedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local),
            EndedAt = new DateTime(2026, 11, 14, 10, 8, 0, DateTimeKind.Local),
            Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Win",
            PlayerClass = "MAGE", OpponentClass = "WARRIOR", DeckName = "Sample Mage", DeckCode = CollectionFixture.NeedsDust,
        };
        return DeckFamilies.Build(DeckFamilies.Versions([game]), []).Single();
    }

    [Fact]
    public void The_sample_deck_opens_the_real_WPF_deck_page_and_stays_through_new_stats() => WpfThread.RunFresh(() =>
    {
        var model = TopDecksModel();
        model.SetCards(CollectionFixture.Cards);
        var window = OffScreen(model);
        try
        {
            TopDecks(window, model);
            window.ShowConstructedTab("Tiers");
            Pump();

            // The scene: the made-up deck, then the stats set again (which reads the history and rebuilds the deck list).
            model.OpenSampleDeck(SampleDeck());
            var built = model.DeckListBuilds;
            model.SetConstructedMeta(CollectionFixture.Meta());
            PumpUntil(() => model.DeckListBuilds > built && model.DeckPage.DeckList.Count > 0);
            // With motion on the Standard page fades out under the deck page first (PageSwitch).
            PageSwitchTests.Settle();

            // The deck page is on screen, not the tier list with its side card.
            Assert.True(model.DeckOpen);
            Assert.True(window.PageDeck.IsVisible);
            var page = window.DeckDetailsPage;
            Assert.True(page.IsVisible);
            Assert.Same(model.DeckPage, page.DataContext);
            Assert.DoesNotContain(Descendants(window).OfType<WpfMeta>(), v => v.IsVisible);
            var block = (StackPanel)page.FindName("DeckCollection");
            Assert.True(block.IsVisible);
            var build = model.DeckPage.Collection.Build!;
            var texts = Descendants(block).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(build.Name, texts);
            Assert.Contains(build.Tip, texts);
            Assert.Contains(CollectionFixture.LongName, texts);
            Assert.All(build.Missing, m => Assert.Contains(m.Price, texts));
            Assert.Contains(build.Total, texts);
            Assert.Contains(model.CollectionStatus, texts);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });
}

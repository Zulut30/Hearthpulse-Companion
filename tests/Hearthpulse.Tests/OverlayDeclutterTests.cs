using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Core.Tracking.Counters;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 (release cleanup, AC2/AC3): what the overlay and the deck lists leave out because it changes nothing
/// in a game - the log's stand-in name for the opponent, counters at zero in Auto, a "No data" bar over every mulligan
/// card, the source caption over Battlegrounds Discover badges in "Stats only", the class twice in a deck's row.
/// The shared view models are one source for both UIs; Hearthpulse.Presentation.Tests runs the WPF flavour where it
/// differs.
/// </summary>
public sealed class OverlayDeclutterTests : IDisposable
{
    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static ConstructedView Constructed(string? opponentName, RankInfo? rank = null) =>
        new(3, "Deck", true, "HERO_10", "DEMONHUNTER", "HERO_01", "WARRIOR", opponentName, [], 20, 4, [], [], 20, 4, 0, null, rank);

    private static OverlayViewModel Updated(ConstructedView view, AppSettings? settings = null)
    {
        var model = new OverlayViewModel();
        model.Update(LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = view },
            CardDb.Empty, settings ?? new AppSettings(), null, null);
        return model;
    }

    [Fact]
    public void The_logs_stand_in_name_heads_the_panel_as_the_class_once()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var warrior = OverlayViewModel.ClassName("WARRIOR");

        // O-3: before the game names the opponent the log calls them "UNKNOWN HUMAN PLAYER": the class heads the panel,
        // and the line under it does not say the class again.
        var unknown = Updated(Constructed(GameState.UnknownPlayerName));
        Assert.Equal((warrior, string.Empty), (unknown.OpponentTitle, unknown.OpponentSubtitle));
        Assert.Equal((warrior, string.Empty), (Updated(Constructed(null)).OpponentTitle, Updated(Constructed(null)).OpponentSubtitle));

        // A rank still has its line; a real name keeps the class under it.
        var ranked = Updated(Constructed(null, new RankInfo("FT_STANDARD", 4, 5, 0, 1)));
        Assert.Equal(warrior, ranked.OpponentTitle);
        Assert.DoesNotContain(warrior, ranked.OpponentSubtitle, StringComparison.Ordinal);
        Assert.NotEmpty(ranked.OpponentSubtitle);
        var named = Updated(Constructed("Rival#1234"));
        Assert.Equal(("Rival#1234", warrior), (named.OpponentTitle, named.OpponentSubtitle));
    }

    [Fact]
    public void Auto_counters_wait_for_their_first_count_and_always_shows_the_zero()
    {
        // O-11: no "Deathrattle minions 0" in the mulligan; a counter under way and one with a count show.
        CounterView Counter(string id, string value, bool nothing) =>
            new(id, id, value, null, false, "CARD", string.Empty, [], Relevant: true) { NothingCounted = nothing };
        var view = Constructed("Rival#1234") with
        {
            Counters = [Counter("deathrattle-summoned", "0", true), Counter("spells-cast", "3", false), Counter("cthun", "0/4", true)],
        };

        Assert.Equal(["spells-cast"], Updated(view).MyCounters.Select(c => c.Id));

        var always = new AppSettings();
        always.CounterModes["deathrattle-summoned"] = nameof(CounterMode.Always);
        Assert.Equal(["deathrattle-summoned", "spells-cast"], Updated(view, always).MyCounters.Select(c => c.Id));

        // Nothing counted in any of them: no counters panel at all.
        var none = view with { Counters = [Counter("deathrattle-summoned", "0", true)] };
        Assert.False(Updated(none).ShowCounters);
    }

    [Fact]
    public void A_deck_without_mulligan_numbers_gets_one_summary_line_and_no_bars()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var deck = MulliganReplay.DeckSample(CardDb.Empty, near: false)!;
        var missing = Loc.T("Mulligan.NoStats.NotYet");
        var none = deck with { Mulligan = deck.Mulligan! with { NoStats = missing } };

        // O-13: no "No data" bar over every card; the summary says it once.
        var model = Updated(none);
        Assert.True(model.ShowMulligan);
        Assert.False(model.ShowMulliganBars);
        Assert.Empty(model.MulliganCards);
        Assert.Equal(missing, model.MulliganSummary);

        var numbers = Updated(deck);
        Assert.True(numbers.ShowMulliganBars);
        Assert.Equal(deck.Mulligan!.Cards.Count, numbers.MulliganCards.Count);
    }

    [Fact]
    public void Discover_badges_have_a_caption_only_when_one_is_marked()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var offer = new BattlegroundsView(6, 3, 6, "HERO_ME", 30, 0, 2, false, false, false, [], ["BEAST"], true, [], [], [], [], null, 0, [], [])
        {
            DiscoverOptions = [new BgDiscoverOption("A", "A", 3, null), new BgDiscoverOption("B", "B", 3, null)],
            DiscoverAdvice = [new ShopPick("A", "A", 3, 40, "Best stats available", Best: true, Slot: 0) { Placement = 3.9 }, new ShopPick("B", "B", 3, 10, string.Empty, Best: false, Slot: 1) { Placement = 4.4 }],
            DiscoverSource = "Shiny Ring",
        };
        OverlayViewModel Model(PickHints hints)
        {
            var model = new OverlayViewModel();
            model.Update(LiveSnapshot.Empty with { Battlegrounds = offer, InGame = true, HearthstoneRunning = true }, CardDb.Empty, new AppSettings { PickHints = hints }, null, null);
            return model;
        }

        // O-27: in "Stats only" no caption: it would name only the source, which the game shows itself.
        var stats = Model(PickHints.StatsOnly);
        Assert.True(stats.ShowDiscover);
        Assert.Equal(string.Empty, stats.DiscoverTitle);
        // "Mark the best" says whether one was marked.
        Assert.Equal(Loc.F("Overlay.Discover.BestMarked", "Shiny Ring"), Model(PickHints.Highlight).DiscoverTitle);
    }

    [Theory]
    [InlineData("Воин", "Воин", "10 нояб.")]
    [InlineData("Жрец 2", "Жрец", "10 нояб.")]
    [InlineData("Жрецы тьмы", "Жрец", "Жрец · 10 нояб.")]
    [InlineData("Cycle Rogue", "Разбойник", "Разбойник · 10 нояб.")]
    [InlineData("Warrior", "Warrior", "10 нояб.")]
    public void A_deck_named_after_its_class_does_not_say_the_class_twice(string name, string className, string caption)
    {
        // G-2: "Warrior / Warrior · 10 Nov" in Your decks.
        var row = new DeckRow("key", name, className, 3, "2–1", "67%", 0.67, "10 нояб.", null);
        Assert.Equal(caption, row.Caption);
    }

    /// <summary>
    /// O-20 with reading the client off (P5): the session panel's reason line takes the rating's place in both apps. A WPF
    /// binding with a wrong path fails silently; this pins the path and the swap.
    /// </summary>
    [Theory]
    [InlineData("src/Hearthpulse.App/Overlay/SessionPanel.xaml", "Visibility")]
    [InlineData("src/Hearthpulse.UI/Overlay/SessionPanel.axaml", "IsVisible")]
    public void The_session_panel_says_reading_is_off_in_place_of_the_rating(string file, string visibility)
    {
        var elements = System.Xml.Linq.XDocument.Load(Path.Combine(RepoFiles.Root, file)).Root!.Descendants().ToList();

        var reason = Assert.Single(elements, e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{Binding SessionOffReason}");
        Assert.StartsWith("{Binding SessionOffReason", (string?)reason.Attribute(visibility), StringComparison.Ordinal);
        var rating = Assert.Single(elements, e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == "{Binding SessionRating}");
        var group = rating.Parent!;
        Assert.Equal("StackPanel", group.Name.LocalName);
        Assert.Contains("SessionOffReason", (string?)group.Attribute(visibility), StringComparison.Ordinal);
        Assert.EndsWith("ConverterParameter=!}", (string?)group.Attribute(visibility), StringComparison.Ordinal);
    }
}

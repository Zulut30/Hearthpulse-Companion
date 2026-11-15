using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds.Site;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Pool;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The block "Hearthpulse statistics (own games)" of the Battlegrounds Heroes tab (roadmap
/// 2026-11-15-hsreplay-remediation, C1): the rows and lines of a published snapshot, values the pool keeps back shown
/// as "not enough data" and never filled from the external list, each empty state with its own text, and the block
/// hidden without a pool, for an unsupported mode, in Duos and on the other tabs. The view model of the Avalonia UI
/// and its page; the WPF twin is in <c>Hearthpulse.Presentation.Tests/BgOwnContractTests</c>.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed partial class BgOwnTests
{
    private static readonly DateTime Fetched = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    public BgOwnTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    private static string Fixture(string name = "bg-heroes.json") => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Analytics", name));

    private static OwnBgHeroesResult Published(string name = "bg-heroes.json", bool oldCopy = false) =>
        OwnBgHeroesResult.FromJson(Fixture(name), Fetched, oldCopy) ?? throw new InvalidOperationException("fixture");

    private static MainViewModel Model()
    {
        var vm = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-bg-own") + ".db"), new AppSettings(), TestDataFolder.App);
        vm.SetCards(CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(
            """
            [
              { "id": "TB_BaconShop_HERO_xx", "dbfId": 1, "name": "Test Hero", "type": "HERO" }
            ]
            """))));
        vm.BgTabShown("Heroes");
        return vm;
    }

    private static string Percent(double value) => value.ToString("P0", CultureInfo.CurrentCulture);

    private static string Number(double value) => value.ToString("0.0", CultureInfo.CurrentCulture);

    [Fact]
    public void A_published_snapshot_gives_the_rows_of_the_newest_build_with_samples_and_intervals() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetBgOwn(Published());

        Assert.True(vm.ShowBgOwn);
        Assert.True(vm.BgOwn.HasRows);
        Assert.Equal(string.Empty, vm.BgOwn.EmptyText);
        Assert.Equal(["TB_BaconShop_HERO_xx", "TB_BaconShop_HERO_yy", "TB_BaconShop_HERO_zz"], vm.BgOwnRows.Select(r => r.CardId));

        // The hero's name from the card database; an id the database lacks stays an id.
        var row = vm.BgOwnRows[0];
        Assert.Equal(("Test Hero", "TB_BaconShop_HERO_yy"), (row.Name, vm.BgOwnRows[1].Name));
        Assert.Equal(("40", "12", false), (row.Offered, row.Picked, row.IsSmall));
        Assert.Equal((Percent(0.3), Percent(0.181) + "–" + Percent(0.454)), (row.PickRate, row.PickRange));
        Assert.Equal((Number(3.92), Number(3.05) + "–" + Number(4.79) + " · 12 games"), (row.Place, row.PlaceDetail));
        Assert.Equal((Percent(0.583), Percent(0.32) + "–" + Percent(0.807)), (row.Top4, row.Top4Range));

        // Above the table: build, games and installations, the window, the publication date. Not stale, not a copy.
        var published = DateTimeOffset.FromUnixTimeMilliseconds(1762646400000).LocalDateTime;
        Assert.Equal(
            "game build 223456 · 120 games from 14 installations · " + Loc.P("Main.Bg.RangeDays", 30) + " · " + Loc.F("Main.BgOwn.Published", published),
            vm.BgOwn.Summary);
        Assert.Contains(Loc.T("Main.BgOwn.Caption"), vm.BgOwn.Note, StringComparison.Ordinal);
        Assert.Contains(Loc.P("Main.BgOwn.Suppressed", 3), vm.BgOwn.Note, StringComparison.Ordinal);
        Assert.Contains(Loc.T("Main.BgOwn.SmallNote"), vm.BgOwn.Note, StringComparison.Ordinal);
    });

    [Fact]
    public void Values_the_pool_keeps_back_read_not_enough_data_and_carry_no_number() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetBgOwn(Published());
        var small = Loc.T("Main.BgOwn.Small");

        // Counts published, values not (under 10 games): the counts stay, each value is "not enough data".
        var counted = vm.BgOwnRows[1];
        Assert.Equal(("9", "5", true), (counted.Offered, counted.Picked, counted.IsSmall));
        Assert.Equal((small, string.Empty), (counted.PickRate, counted.PickRange));
        Assert.Equal((small, "5 games"), (counted.Place, counted.PlaceDetail));
        Assert.Equal((small, string.Empty), (counted.Top4, counted.Top4Range));

        // Too few installations: no count either, only how often the hero was offered.
        var hidden = vm.BgOwnRows[2];
        Assert.Equal("6", hidden.Offered);
        Assert.Equal(
            (small, small, string.Empty, small, string.Empty, small, string.Empty),
            (hidden.Picked, hidden.PickRate, hidden.PickRange, hidden.Place, hidden.PlaceDetail, hidden.Top4, hidden.Top4Range));
        Assert.DoesNotContain(new[] { hidden.Picked, hidden.PickRate, hidden.Place, hidden.Top4 }, text => text.Any(char.IsDigit));
    });

    [Fact]
    public void A_missing_own_value_is_never_filled_from_the_external_list_and_the_external_list_stays() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetBgOwn(Published());
        var before = vm.BgOwnRows.ToList();

        // The external list knows the same heroes, with numbers of its own.
        vm.SetBgSiteStats(BgSiteTabsTests.Stats(heroes: BgSiteTabsTests.List(BgSiteList.Heroes,
            [BgSiteTabsTests.Hero("TB_BaconShop_HERO_zz", "S", 2.5, top4: 0.77, first: 0.3, pick: 0.66), BgSiteTabsTests.Hero("TB_BaconShop_HERO_yy", "A", 3.5)], tiers: ["S", "A"])));

        Assert.Equal(before, vm.BgOwnRows);
        Assert.Equal(Loc.T("Main.BgOwn.Small"), vm.BgOwnRows[2].Place);
        var external = vm.HeroTierRows.OfType<MetaHeroRow>().Single(r => r.CardId == "TB_BaconShop_HERO_zz");
        Assert.Contains("2", external.AveragePlace, StringComparison.Ordinal);
        Assert.True(vm.HeroTab.HasRows);

        // And the other way: own numbers going away leaves the external list as it was.
        var rows = vm.HeroTierRows.ToList();
        vm.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotPublished));
        Assert.Equal(rows, vm.HeroTierRows);
    });

    [Fact]
    public void Stale_numbers_and_an_earlier_copy_are_marked_in_the_line() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        var fresh = Published();
        vm.SetBgOwn(fresh);
        Assert.DoesNotContain(Loc.T("Main.BgOwn.Stale"), vm.BgOwn.Summary, StringComparison.Ordinal);

        vm.SetBgOwn(OwnBgHeroesResult.From(fresh.Snapshot! with { Stale = true }, Fetched));
        Assert.EndsWith(" · " + Loc.T("Main.BgOwn.Stale"), vm.BgOwn.Summary, StringComparison.Ordinal);

        vm.SetBgOwn(Published(oldCopy: true));
        Assert.EndsWith(" · " + Loc.F("Main.BgOwn.OldCopy", Fetched.ToLocalTime()), vm.BgOwn.Summary, StringComparison.Ordinal);
    });

    [Fact]
    public void Rows_without_values_are_shown_as_rows_and_a_build_without_rows_says_why() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        var small = Published("bg-heroes-render-small.json");
        Assert.Equal(OwnBgHeroesState.SmallSample, small.State);
        vm.SetBgOwn(small);

        Assert.True(vm.BgOwn.HasRows);
        Assert.Equal(4, vm.BgOwnRows.Count);
        Assert.All(vm.BgOwnRows, r => Assert.True(r.IsSmall));
        Assert.All(vm.BgOwnRows, r => Assert.Equal((Loc.T("Main.BgOwn.Small"), Loc.T("Main.BgOwn.Small")), (r.Place, r.Top4)));

        // Every hero of the newest build was offered to fewer than five installations: no row, one line.
        var group = small.Snapshot!.Groups[0] with { Heroes = [] };
        vm.SetBgOwn(OwnBgHeroesResult.From(small.Snapshot with { Groups = [group] }, Fetched));
        Assert.True(vm.ShowBgOwn);
        Assert.Empty(vm.BgOwnRows);
        Assert.Equal((false, Loc.T("Main.BgOwn.NoRows")), (vm.BgOwn.HasRows, vm.BgOwn.EmptyText));
    });

    [Theory]
    [InlineData(OwnBgHeroesState.NotPublished, "Main.BgOwn.NotPublished")]
    [InlineData(OwnBgHeroesState.Rebuilding, "Main.BgOwn.Rebuilding")]
    [InlineData(OwnBgHeroesState.Offline, "Main.BgOwn.Offline")]
    public void An_empty_state_has_its_own_text_no_rows_and_no_numbers(OwnBgHeroesState state, string key) => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetBgOwn(Published());

        vm.SetBgOwn(OwnBgHeroesResult.Of(state));

        Assert.True(vm.ShowBgOwn);
        Assert.Empty(vm.BgOwnRows);
        Assert.Equal((false, string.Empty, string.Empty, Loc.T(key)), (vm.BgOwn.HasRows, vm.BgOwn.Summary, vm.BgOwn.Note, vm.BgOwn.EmptyText));
        Assert.DoesNotContain(vm.BgOwn.EmptyText, c => char.IsDigit(c));
    });

    [Fact]
    public void The_not_published_text_is_the_agreed_one_in_both_languages() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        vm.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotPublished));
        Assert.Equal("No own data yet: the statistics appear once at least five installations have sent games", vm.BgOwn.EmptyText);

        // The language changes while the block is shown: the same state in the other language.
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            Assert.Equal("Своих данных пока нет: статистика появится, когда игры пришлют не меньше пяти установок", vm.BgOwn.EmptyText);
            vm.SetBgOwn(Published());
            Assert.Equal("мало данных", vm.BgOwnRows[2].Place);
            Assert.StartsWith("сборка игры 223456 · 120 игр от 14 установок · ", vm.BgOwn.Summary, StringComparison.Ordinal);
            Assert.Equal("Статистика Hearthpulse (свои игры)", Loc.T("Main.BgOwn.Title"));
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }

        Assert.Equal("not enough data", vm.BgOwnRows[2].Place);
        Assert.Equal("Hearthpulse statistics (own games)", Loc.T("Main.BgOwn.Title"));
    });

    [Fact]
    public void The_block_is_hidden_without_a_pool_for_an_unsupported_mode_in_duos_and_on_other_tabs() => LocBindingTests.Headless.Run(() =>
    {
        var vm = Model();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Before any load, and without a pool: nothing to show, not even an empty state.
        Assert.False(vm.ShowBgOwn);
        vm.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
        Assert.False(vm.ShowBgOwn);
        Assert.Empty(vm.BgOwnRows);
        vm.SetBgOwn(OwnBgHeroesResult.FromJson(Fixture().Replace("\"available\": true, \"reason\": null", "\"available\": false, \"reason\": \"unsupported_mode\"", StringComparison.Ordinal), Fetched)!);
        Assert.False(vm.ShowBgOwn);

        vm.SetBgOwn(Published());
        Assert.True(vm.ShowBgOwn);
        Assert.Contains(nameof(MainViewModel.ShowBgOwn), raised);

        // Only on the Heroes tab.
        foreach (var tab in new[] { "Mine", "Comps", "Trinkets", "Cards" })
        {
            vm.BgTabShown(tab);
            Assert.False(vm.ShowBgOwn);
        }

        vm.BgTabShown("Heroes");
        Assert.True(vm.ShowBgOwn);

        // Duos has no such metric: while the Duos list is shown the block is away, and back with Solo.
        vm.SetBgSiteStats(BgSiteTabsTests.Fixtures());
        raised.Clear();
        vm.SetBgMode.Execute("Duos");
        Assert.False(vm.ShowBgOwn);
        Assert.Contains(nameof(MainViewModel.ShowBgOwn), raised);
        vm.SetBgMode.Execute("Battlegrounds");
        Assert.True(vm.ShowBgOwn);
    });

    // ---- the Avalonia page ----------------------------------------------------------------------------

    [Fact]
    public void The_Avalonia_page_shows_the_block_on_the_Heroes_tab_with_the_models_texts() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var model = Model();
        model.BgTabShown("Mine");
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 800 };
        window.Resources["UiFont"] = Avalonia.Media.FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Battlegrounds");
            model.SetBgOwn(Published());
            PageRowsPanelAvaloniaTests.Settle(window);
            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var card = Named<Border>("BgOwnCard");

            // My stats is open: the block belongs to the Heroes tab.
            Assert.False(card.IsEffectivelyVisible);
            view.ShowBattlegroundsTab("Heroes");
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.True(card.IsEffectivelyVisible);
            Assert.Equal(model.BgOwn.Summary, Named<TextBlock>("BgOwnSummary").Text);
            Assert.Equal(model.BgOwn.Note, Named<TextBlock>("BgOwnNote").Text);
            var list = Named<ItemsControl>("BgOwnList");
            Assert.Equal(3, list.ItemCount);
            var texts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Test Hero", texts);
            Assert.Contains(model.BgOwnRows[0].PlaceDetail, texts);
            Assert.Equal(7, texts.Count(t => t == Loc.T("Main.BgOwn.Small")));
            Assert.False(Named<ContentControl>("BgOwnEmpty").IsEffectivelyVisible);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.T("Main.BgOwn.Offered") && t.IsEffectivelyVisible);

            // Nothing published: the text in place of the table.
            model.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotPublished));
            PageRowsPanelAvaloniaTests.Settle(window);
            var empty = Named<ContentControl>("BgOwnEmpty");
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Equal(Loc.T("Main.BgOwn.NotPublished"), empty.Content);
            Assert.False(list.IsEffectivelyVisible);

            // No pool: no block.
            model.SetBgOwn(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
            PageRowsPanelAvaloniaTests.Settle(window);
            Assert.False(card.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    });

    // ---- one block in two markups -----------------------------------------------------------------------

    [Fact]
    public void Both_pages_hold_the_same_block_with_the_same_names_bindings_and_texts()
    {
        var wpf = Block(Path.Combine(RepoFiles.Root, "src", "Hearthpulse.App", "Main", "Pages", "BattlegroundsPage.xaml"));
        var avalonia = Block(Path.Combine(RepoFiles.Root, "src", "Hearthpulse.UI", "Main", "Pages", "BattlegroundsPage.axaml"));

        static string[] All(string block, Regex pattern) => [.. pattern.Matches(block).Select(m => m.Groups[1].Value)];

        Assert.Equal(["BgOwnCard", "BgOwnSummary", "BgOwnList", "BgOwnNote", "BgOwnEmpty"], All(wpf, Names()));
        Assert.Equal(All(wpf, Names()), All(avalonia, Names()));
        Assert.Equal(All(wpf, Bindings()), All(avalonia, Bindings()));
        Assert.Equal(All(wpf, LocKeys()), All(avalonia, LocKeys()));
        Assert.Contains("ShowBgOwn", All(wpf, Bindings()));
        Assert.Contains("Main.BgOwn.Title", All(wpf, LocKeys()));

        // Tokens only: no colour is written out in the block.
        Assert.DoesNotMatch(HexColour(), wpf);
        Assert.DoesNotMatch(HexColour(), avalonia);
    }

    /// <summary>The block's element in a page's markup: from its card to the comment of the next section.</summary>
    private static string Block(string file)
    {
        var text = File.ReadAllText(file);
        var start = text.IndexOf("Name=\"BgOwnCard\"", StringComparison.Ordinal);
        var end = text.IndexOf("<!-- My stats (Main/Stats)", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, file);
        return text[start..end];
    }

    [GeneratedRegex("Name=\"(BgOwn\\w+)\"")]
    private static partial Regex Names();

    [GeneratedRegex("\\{Binding !?([\\w.]+)")]
    private static partial Regex Bindings();

    [GeneratedRegex("\\{i:Loc ([\\w.]+)\\}")]
    private static partial Regex LocKeys();

    [GeneratedRegex("\"#[0-9A-Fa-f]{3,8}\"")]
    private static partial Regex HexColour();
}

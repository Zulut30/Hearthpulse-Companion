using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The meta tabs while new stats load (shared view model, Avalonia build): a filter change keeps the old rows where they
/// are, dimmed, with no loading box under them, and the new ones replace them in one step; stats of an older request or
/// another format never end the wait; an early tier list keeps the matrix and the mulligan until the full stats; the
/// mulligan of a rank keeps its rows while its slice loads; the top decks keep the rows that stay.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class MetaReloadTests : IDisposable
{
    private readonly string _db = TestDataFolder.Scratch("hp-meta-reload") + ".db";
    private readonly HistoryStore _history;

    public MetaReloadTests()
    {
        _history = new HistoryStore(_db);
        Loc.Instance.SetLanguage(Loc.English);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_db);
    }

    [Fact]
    public void A_filter_change_keeps_the_old_rows_in_place_dimmed_with_no_loading_box_under_them()
    {
        var vm = Shown(Meta("all"));
        var rows = vm.MetaArchetypes.ToList();
        var tiers = vm.MetaTierRows.ToList();
        var decks = vm.MetaDecks.ToList();
        var mulligan = vm.Mulligan;
        var status = vm.ConstructedMetaStatus;
        string? asked = "none";
        vm.ConstructedMetaRequested += format => asked = format;

        vm.SetMetaRank.Execute("legend");

        // Every format loads again, and meanwhile nothing moves: the same rows in the same places, the status line kept.
        Assert.Null(asked);
        Assert.False(vm.MetaLoading);
        Assert.True(vm.MetaRefreshing);
        Assert.True(vm.MetaDecksRefreshing);
        Assert.True(vm.MetaMatchupsRefreshing);
        Assert.True(vm.MetaStatusShown);
        Assert.False(vm.MetaEmpty || vm.MetaDecksEmpty || vm.MetaMatchupsEmpty || vm.MetaUnavailable);
        Assert.Equal(status, vm.ConstructedMetaStatus);
        AssertSame(rows, vm.MetaArchetypes);
        AssertSame(tiers, vm.MetaTierRows);
        AssertSame(decks, vm.MetaDecks);
        Assert.Same(mulligan, vm.Mulligan);

        // The new stats replace them in one step; a row that shows what it showed keeps its object (and its container).
        vm.SetConstructedMeta(Meta("legend", bigRogue: 0.47));
        Assert.False(vm.MetaRefreshing || vm.MetaDecksRefreshing || vm.MetaMatchupsRefreshing || vm.MetaLoading);
        Assert.Same(rows.Single(r => r.Name == "Zoolock"), vm.MetaArchetypes.Single(r => r.Name == "Zoolock"));
        Assert.NotSame(rows.Single(r => r.Name == "Big Rogue"), vm.MetaArchetypes.Single(r => r.Name == "Big Rogue"));
        AssertSame(decks, vm.MetaDecks);
        Assert.Contains("Legend", vm.ConstructedMetaStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_of_an_older_request_or_of_another_format_never_end_the_wait()
    {
        var vm = Shown(Meta("all"));
        vm.SetMetaRank.Execute("legend");
        vm.SetMetaRank.Execute("diamond");

        // The Legend stats asked for first, late: still not the filter shown, so the rows stay dimmed, never "fresh".
        vm.SetConstructedMeta(Meta("legend"));
        Assert.True(vm.MetaRefreshing);

        // Wild's new stats do not end the Standard page's wait either.
        vm.SetConstructedMeta(Meta("diamond", format: "wild"));
        Assert.True(vm.MetaRefreshing);

        vm.SetConstructedMeta(Meta("diamond"));
        Assert.False(vm.MetaRefreshing);
        Assert.Contains("Diamond", vm.ConstructedMetaStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_nothing_of_its_format_waits_with_its_loading_state()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(null);

        vm.SetMetaRank.Execute("legend");
        Assert.True(vm.MetaLoading);
        Assert.False(vm.MetaRefreshing);
        Assert.True(vm.MetaMatchupsLoading);
        Assert.False(vm.MetaStatusShown);
    }

    [Fact]
    public void Unavailable_new_stats_end_the_wait_and_the_old_rows_stay()
    {
        var vm = Shown(Meta("all"));
        var rows = vm.MetaArchetypes.ToList();
        vm.SetMetaRank.Execute("legend");

        vm.SetConstructedMeta(null);
        Assert.False(vm.MetaRefreshing || vm.MetaLoading || vm.MetaUnavailable);
        AssertSame(rows, vm.MetaArchetypes);
        Assert.Equal(Loc.T("Main.Meta.Unavailable"), vm.ConstructedMetaStatus);
    }

    [Fact]
    public void An_early_tier_list_keeps_the_matrix_and_the_mulligan_until_the_full_stats()
    {
        var vm = Shown(Meta("all"));
        var matrix = vm.MatchupMatrix;
        var mulligan = vm.Mulligan;
        Assert.NotEmpty(mulligan);
        vm.SetMetaRank.Execute("legend");

        // The tier list is in; the mulligan numbers and the matchups are not: what shows stays, dimmed, never "none".
        vm.SetConstructedMeta(Meta("legend", early: true));
        Assert.False(vm.MetaRefreshing);
        Assert.Same(matrix, vm.MatchupMatrix);
        Assert.True(vm.MetaMatchupsRefreshing);
        Assert.False(vm.MetaMatchupsEmpty || vm.MetaMatchupsLoading);
        Assert.Same(mulligan, vm.Mulligan);
        Assert.True(vm.MulliganLoading);
        Assert.False(vm.MulliganEmptyShown);
        Assert.Equal(Loc.T("Main.Meta.LoadingShort"), vm.MulliganSource);

        vm.SetConstructedMeta(Meta("legend"));
        Assert.False(vm.MetaMatchupsRefreshing || vm.MulliganLoading);
        Assert.True(vm.HasMatchupMatrix);
        Assert.NotEmpty(vm.Mulligan);
        Assert.Equal(string.Empty, vm.MulliganSource);
    }

    [Fact]
    public void A_first_early_tier_list_waits_for_its_matrix_and_says_what_is_missing_once_the_load_ends_without_it()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.ShowConstructedFormat("FT_STANDARD");

        vm.SetConstructedMeta(Meta("all", early: true));
        Assert.NotEmpty(vm.MetaArchetypes);
        Assert.False(vm.MetaLoading);
        Assert.True(vm.MetaMatchupsLoading);
        Assert.False(vm.MetaMatchupsEmpty);
        Assert.True(vm.MulliganLoading);
        Assert.False(vm.MulliganEmptyShown);

        // The full stats never come: the parts that were waiting say what they lack.
        vm.SetConstructedMeta(null);
        Assert.False(vm.MetaMatchupsLoading || vm.MulliganLoading);
        Assert.True(vm.MetaMatchupsEmpty);
        Assert.True(vm.MulliganEmptyShown);
    }

    [Fact]
    public void Another_format_s_matrix_never_stands_in_for_an_early_tier_list()
    {
        var vm = Shown(Meta("all"));
        Assert.True(vm.HasMatchupMatrix);

        vm.ShowConstructedFormat("FT_WILD");
        vm.SetConstructedMeta(Meta("all", format: "wild", early: true));
        Assert.False(vm.HasMatchupMatrix);
        Assert.True(vm.MetaMatchupsLoading);
    }

    [Fact]
    public void The_mulligan_of_a_rank_keeps_its_rows_dimmed_while_the_slice_loads()
    {
        var vm = Shown(Meta("all"));
        var pending = new TaskCompletionSource<RankSlice?>();
        vm.LoadRankMulligan = (_, _, _, _) => pending.Task;
        var rows = vm.Mulligan;

        vm.SetMulliganRank.Execute("diamond");
        Assert.Same(rows, vm.Mulligan);
        Assert.True(vm.MulliganLoading);
        Assert.False(vm.MulliganEmptyShown);
        Assert.Equal(Loc.T("Main.Meta.LoadingShort"), vm.MulliganSource);

        // The slice came and has nothing: now the guide says so.
        vm.ShowRankSlice(null, "diamond");
        Assert.False(vm.MulliganLoading);
        Assert.Empty(vm.Mulligan);
        Assert.True(vm.MulliganEmptyShown);
    }

    [Fact]
    public void The_top_decks_change_in_place_and_keep_the_rows_that_stay()
    {
        var vm = Shown(Meta("all"));
        var decks = vm.MetaDecks;
        var first = decks[0];
        var notices = 0;
        decks.CollectionChanged += (_, _) => notices++;

        // The same lists again (another sort of the archetypes): nothing changes at all.
        vm.SetMetaSort.Execute("Name");
        Assert.Equal(0, notices);

        // A list more, lower: the rows above it stay the same objects.
        vm.SetConstructedMeta(Meta("all", extraDeck: true));
        Assert.Same(decks, vm.MetaDecks);
        Assert.Same(first, vm.MetaDecks[0]);
        Assert.Equal(2, vm.MetaDecks.Count);
        Assert.Equal(1, notices);
    }

    private static void AssertSame<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
        where T : class
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }
    }

    private MainViewModel Shown(ConstructedMeta meta)
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App);
        vm.ShowConstructedFormat("FT_STANDARD");
        vm.SetConstructedMeta(meta);
        return vm;
    }

    /// <param name="rank">The rank the stats are of (the filter's default is "all").</param>
    /// <param name="early">The tier list sent ahead: no mulligan numbers, no matrix yet.</param>
    private static ConstructedMeta Meta(string rank, string format = "standard", bool early = false, double bigRogue = 0.46, bool extraDeck = false)
    {
        MetaArchetype Archetype(int id, string name, string heroClass, int games, double winRate, double share, string? keep = null) =>
            new(id, name, name, heroClass, games, (int)(games * winRate), winRate, share, 0, 0, 0, 0, [],
                keep is null || early ? [] : [new MetaMulliganCard(keep, 900, 0.8, 700, winRate + 0.03)]);

        var archetypes = new[]
        {
            Archetype(1, "Zoolock", "WARLOCK", 5000, 0.56, 0.20, "CS2_065"),
            Archetype(2, "Control Warrior", "WARRIOR", 800, 0.50, 0.10),
            Archetype(3, "Big Rogue", "ROGUE", 6000, bigRogue, 0.30, "EX1_134"),
        };
        var seen = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var decks = new List<MetaDeck> { new("AAECAf0GAA==", 1, "Zoolock", "WARLOCK", 900, 520, 0.578, seen) };
        if (extraDeck)
        {
            decks.Add(new MetaDeck("AAECAaIHAA==", 3, "Big Rogue", "ROGUE", 900, 400, 0.44, seen));
        }

        var matchups = early ? MetaMatchups.Empty : new MetaMatchups(
            [new MatchupArchetype("Zoolock", "Zoolock", "WARLOCK", 5000, 0.56, 0.2), new MatchupArchetype("Big Rogue", "Big Rogue", "ROGUE", 6000, bigRogue, 0.3)],
            [new MatchupCell("Zoolock", "Big Rogue", 0.64, 210), new MatchupCell("Big Rogue", "Zoolock", 0.36, 210)],
            11000);
        return new ConstructedMeta(format, rank, "past_week", new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), 11800, archetypes, [.. decks])
        {
            Matchups = matchups,
            IsEarly = early,
        };
    }
}

using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Replay;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The library's tabs while a folder is read (shared view model, Avalonia build): another tab's cards never stand in, the
/// tab shows its cards from the last visit at once or a loading state, and a new set of cards counts a turn (the view
/// fades it in) while a same-tab refresh or a search does not.
/// </summary>
public sealed class LibraryTabLoadingTests : IDisposable
{
    private readonly string _dir = TestDataFolder.Scratch("hp-library-tabs");
    private readonly HistoryStore _history;
    private readonly ReplayLibrary _files;

    public LibraryTabLoadingTests()
    {
        Directory.CreateDirectory(_dir);
        _history = new HistoryStore(Path.Combine(_dir, "history.db"));
        _files = new ReplayLibrary(new ReplayStore(Path.Combine(_dir, "replays")), Path.Combine(_dir, "saved"), Path.Combine(_dir, "imported"));
        var start = new DateTime(2026, 10, 28, 12, 0, 0);
        for (var i = 0; i < 2; i++)
        {
            var key = "2026102810000" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|me#1|GT_RANKED";
            _history.Add(new GameRecord
            {
                Key = key,
                StartedAt = start.AddHours(i),
                EndedAt = start.AddHours(i).AddMinutes(7),
                Mode = "Ranked",
                PlayerName = "Me#1234",
                HeroCardId = "HERO_08",
                PlayerClass = "MAGE",
                OpponentClass = "WARRIOR",
                Result = "Win",
                Turns = 9,
            });
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestDataFolder.TryDelete(_dir);
    }

    [Fact]
    public void Another_tab_s_cards_go_at_once_and_the_tab_waits_with_its_loading_state()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { LibraryFiles = _files };
        var library = vm.Library;
        var recent = new[] { Card(ReplayShelf.Recent), Card(ReplayShelf.Recent) };
        library.SetCards(recent);
        var turn = library.CardsTurn;

        // Saved opens: its folder is still read, and Recent's cards are not shown meanwhile.
        library.ShowTab("Saved");
        library.BeginLoading();
        Assert.Empty(library.Cards);
        Assert.True(library.IsLoadingEmpty);
        Assert.False(library.IsEmpty);
        Assert.Equal(turn + 1, library.CardsTurn);

        // The folder is read: one saved card, faded in as a new set.
        var saved = Card(ReplayShelf.Saved);
        library.SetCards([saved]);
        Assert.Same(saved, Assert.Single(library.Cards));
        Assert.False(library.IsLoadingEmpty);
        Assert.Equal(turn + 2, library.CardsTurn);

        // Back on Saved later: its cards from the last visit at once, while its folder is read again.
        library.ShowTab("Recent");
        library.SetCards(recent);
        library.ShowTab("Saved");
        library.BeginLoading();
        Assert.Same(saved, Assert.Single(library.Cards));
        Assert.False(library.IsLoadingEmpty || library.IsEmpty);

        // The same tab read again, or a search: the cards change, but no new set comes in.
        var turnBefore = library.CardsTurn;
        library.BeginLoading();
        library.SetCards([saved, Card(ReplayShelf.Saved)]);
        library.Search = "no such game";
        library.Search = string.Empty;
        Assert.Equal(turnBefore, library.CardsTurn);
    }

    [Fact]
    public async Task The_saved_tab_never_shows_the_recent_tab_s_cards()
    {
        var vm = new MainViewModel(_history, new AppSettings(), TestDataFolder.App) { LibraryFiles = _files };
        await vm.RefreshAsync();
        await vm.LibraryIdleAsync();
        Assert.Equal(2, vm.Library.Cards.Count);

        vm.Library.SetTab.Execute("Saved");
        Assert.DoesNotContain(vm.Library.Cards, c => c.Shelf != ReplayShelf.Saved);
        await vm.LibraryIdleAsync();
        Assert.Empty(vm.Library.Cards);
        Assert.True(vm.Library.IsEmpty);
        Assert.False(vm.Library.IsLoadingEmpty);

        vm.Library.SetTab.Execute("Recent");
        Assert.Equal(2, vm.Library.Cards.Count);
        Assert.All(vm.Library.Cards, c => Assert.Equal(ReplayShelf.Recent, c.Shelf));
    }

    private static Hearthpulse.UI.Main.Match.LibraryCard Card(ReplayShelf shelf) =>
        new(shelf, "file-" + Guid.NewGuid().ToString("N") + ".hpr", null, battlegrounds: false) { Title = "Game", SortKey = "1" };
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Hosting;

namespace Hearthpulse.App;

/// <summary>
/// <c>HEARTHPULSE_RENDER_ONLY=library</c>: the replay library (History) with Arena and Underground Arena cards
/// (<see cref="ArenaRunsScene"/>) beside Constructed and Battlegrounds ones (<see cref="VisualBaseline.Games"/>), a card
/// shown as uploaded (a made-up link, nothing is sent), the list scrolled, the minimum width, and the match page header
/// of a game with a replay and of an Arena game.
/// </summary>
public partial class App
{
    /// <summary>The made-up link of the "uploaded" card: the site's form, never a real upload.</summary>
    private const string RenderSiteUrl = "https://hearthpulse.net/replay/render0001";

    private async Task RenderLibraryAsync(string folder)
    {
        // The made-up games go into the history only when it is a copy (HEARTHPULSE_DATA), never the player's own.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEARTHPULSE_DATA")))
        {
            Console.WriteLine("main-library: skipped, HEARTHPULSE_DATA is not set (the scene writes to the history)");
            return;
        }

        foreach (var game in ArenaRunsScene.Games().Concat(ArenaRunsScene.UndergroundGames()).Concat(VisualBaseline.Games))
        {
            _history.Add(game);
        }

        await _mainViewModel.RefreshAsync();
        _main.ShowPage("History");
        await Task.Delay(2500);
        _main.UpdateLayout();
        Save(_main, Path.Combine(folder, "main-library.png"));

        var library = _mainViewModel.Library;
        if (library.AllCards.FirstOrDefault(c => c.CanUpload) is { } uploaded)
        {
            uploaded.SiteUrl = RenderSiteUrl;
            await Task.Delay(300);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-library-onsite.png"));
            uploaded.SiteUrl = string.Empty;
        }

        if (LibraryScroll() is { } scroll)
        {
            // A card cut by the list's top edge: the rows under the tabs, the search and the status line.
            scroll.ScrollToVerticalOffset(40);
            await Task.Delay(300);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-library-scrolled.png"));
            // The oldest games: the made-up Battlegrounds and Ranked cards without files.
            scroll.ScrollToEnd();
            await Task.Delay(1500);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-library-end.png"));
            scroll.ScrollToVerticalOffset(0);
        }

        var width = _main.Width;
        _main.Width = 1040;
        await Task.Delay(500);
        _main.UpdateLayout();
        Save(_main, Path.Combine(folder, "main-library-1040.png"));
        _main.Width = width;

        if (_history.Recent(400).FirstOrDefault(g => g.Replay is not null && !g.IsBattlegrounds) is { } withReplay
            && await _mainViewModel.OpenMatchPageAsync(withReplay.Key))
        {
            await SaveMatchAsync(folder, "main-match-browser", full: false);
            // "View in browser" on a game not on the site asks first; the question only, never an upload or a browser here.
            if (_mainViewModel.MatchPage.HeaderCard is { FileName: { } file } card)
            {
                _mainViewModel.AskSiteUpload(card, file);
                await SaveMatchAsync(folder, "main-match-browser-confirm", full: false);
                _mainViewModel.MatchPage.CancelHeader.Execute(null);
            }

            _mainViewModel.MatchPage.Back.Execute(null);
        }

        if (ArenaRunsScene.UndergroundGames()[^1] is { } arena && await _mainViewModel.OpenMatchPageAsync(arena.Key))
        {
            await SaveMatchAsync(folder, "main-match-arena", full: false);
            _mainViewModel.MatchPage.Back.Execute(null);
        }
    }

    /// <summary>The library list's scroll viewer (the History page's cards).</summary>
    private ScrollViewer? LibraryScroll()
    {
        var list = Descendant<Hearthpulse.App.Main.Match.ReplayLibraryView>(_main) is { } view ? Named<ListBox>(view, "List") : null;
        return list is null ? null : Descendant<ScrollViewer>(list);
    }

    /// <summary>The first visual descendant of a type.</summary>
    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (Descendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}

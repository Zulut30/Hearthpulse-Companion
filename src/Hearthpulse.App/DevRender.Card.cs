using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;

namespace Hearthpulse.App;

/// <summary>
/// Render-check scenes of the card page (<c>HEARTHPULSE_RENDER_ONLY=card</c>, docs/card-page.md): the card of
/// <c>HEARTHPULSE_RENDER_CARD</c>, else the first card of the loaded Standard meta's mulligan numbers.
/// <list type="bullet">
/// <item><c>main-card.png</c>, <c>main-card-full.png</c>: the window and the whole page on the live list and meta, with
/// "Your games" on made-up games (<see cref="CardPageScene.Games"/>).</item>
/// <item><c>main-card-loading.png</c>: the card shown while its list is on the way.</item>
/// <item><c>main-card-error.png</c>: the list did not load and there is no copy: the error with "Try again".</item>
/// <item><c>main-card-stale.png</c>: an older copy (<see cref="CardPageScene.StaleSlice"/>) under a late source: its date and the delay line.</item>
/// <item><c>main-card-theme.txt</c>: the theme the frames were drawn in and its accent and background colours.</item>
/// </list>
/// </summary>
public partial class App
{
    private async Task RenderCardPageAsync(string folder)
    {
        _main.ShowPage("Standard");
        // The meta names the card to show and fills two of the page's sections: wait for it and for its mulligan numbers
        // (the tier list comes first), but not for ever.
        for (var i = 0; i < 200 && _mainViewModel.ShownConstructedMeta?.Archetypes.Any(a => a.Mulligan.Count > 0) != true; i++)
        {
            await Task.Delay(200);
        }

        var cardId = Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_CARD") is { Length: > 0 } asked
            ? asked
            : _mainViewModel.ShownConstructedMeta?.Archetypes.SelectMany(a => a.Mulligan).FirstOrDefault()?.CardId;
        if (cardId is null)
        {
            Diag.Info("Card page render skipped: no Standard meta with mulligan numbers, and HEARTHPULSE_RENDER_CARD is not set");
            return;
        }

        if (_cards.Get(cardId) is not { } card)
        {
            Diag.Info($"Card page render skipped: the card data has no card {cardId}");
            return;
        }

        var theme = CurrentTheme();
        await File.WriteAllTextAsync(Path.Combine(folder, "main-card-theme.txt"),
            $"ui=wpf theme={theme.Name} preset={ThemePalette.Presets.Any(p => p.Name == theme.Name)} accent={TryFindResource("Accent")} bg={TryFindResource("Bg")}"
            + $" language={Core.Localization.Loc.Instance.Language}{Environment.NewLine}");

        var live = _mainViewModel.LoadCardStats;
        _mainViewModel.ShowCardGamesSample(CardPageScene.Games(cardId));
        try
        {
            // A meta, card data or data summary that arrives meanwhile builds the page again (the call then answers
            // false): what counts is the page being open and built.
            await _mainViewModel.OpenCardPageAsync(cardId);
            await CardBuiltAsync();
            Save(_main, Path.Combine(folder, "main-card.png"));
            SaveCardPage(folder, "main-card-full.png");

            // The list on its way: the card itself is already there.
            _mainViewModel.LoadCardStats = (_, _) => new TaskCompletionSource<CardStatsSlice?>().Task;
            _mainViewModel.CardPage.Retry.Execute(null);
            await Task.Delay(1200);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-card-loading.png"));

            // The list did not load and there is no copy.
            _mainViewModel.LoadCardStats = (_, _) => Task.FromResult<CardStatsSlice?>(null);
            _mainViewModel.CardPage.Retry.Execute(null);
            await CardBuiltAsync();
            Save(_main, Path.Combine(folder, "main-card-error.png"));

            // An older copy under a late source: its own date, and the delay line.
            _mainViewModel.LoadCardStats = (format, _) => Task.FromResult<CardStatsSlice?>(CardPageScene.StaleSlice(format, card));
            _mainViewModel.CardPage.Retry.Execute(null);
            await CardBuiltAsync();
            _mainViewModel.SetDataHealth(CardPageScene.LateReport("standard"));
            await CardBuiltAsync();
            Save(_main, Path.Combine(folder, "main-card-stale.png"));
        }
        finally
        {
            _mainViewModel.LoadCardStats = live;
            _mainViewModel.ShowCardGamesSample(null);
            _mainViewModel.CardPage.Back.Execute(null);
        }
    }

    /// <summary>Waits for the open page to be built, then for its art and portraits.</summary>
    private async Task CardBuiltAsync()
    {
        await Task.Delay(300);
        for (var i = 0; i < 150 && _mainViewModel.CardOpen && _mainViewModel.CardPage.Loading; i++)
        {
            await Task.Delay(200);
        }

        await Task.Delay(2500);
        _main.UpdateLayout();
    }

    private void SaveCardPage(string folder, string name)
    {
        if (_main.FindName("PageCard") is ScrollViewer { Content: FrameworkElement content })
        {
            SaveFull(content, Path.Combine(folder, name));
        }
    }
}

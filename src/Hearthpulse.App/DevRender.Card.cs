using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hearthpulse.App;

/// <summary>
/// Render-check scenes of the card page (<c>HEARTHPULSE_RENDER_ONLY=card</c>, docs/card-page.md): the card of
/// <c>HEARTHPULSE_RENDER_CARD</c>, else the first card of the loaded Standard meta's mulligan numbers.
/// <c>main-card.png</c> is the window, <c>main-card-full.png</c> the whole page.
/// </summary>
public partial class App
{
    private async Task RenderCardPageAsync(string folder)
    {
        _main.ShowPage("Standard");
        // The meta names the card to show and fills two of the page's sections: wait for it, but not for ever.
        for (var i = 0; i < 100 && _mainViewModel.ShownConstructedMeta is null; i++)
        {
            await Task.Delay(200);
        }

        var cardId = Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_CARD") is { Length: > 0 } asked
            ? asked
            : _mainViewModel.ShownConstructedMeta?.Archetypes.SelectMany(a => a.Mulligan).FirstOrDefault()?.CardId;
        if (cardId is null || !await _mainViewModel.OpenCardPageAsync(cardId))
        {
            return;
        }

        // The card's art and the class portraits arrive after the numbers.
        await Task.Delay(2500);
        _main.UpdateLayout();
        Save(_main, Path.Combine(folder, "main-card.png"));
        if (_main.FindName("PageCard") is ScrollViewer { Content: FrameworkElement content })
        {
            SaveFull(content, Path.Combine(folder, "main-card-full.png"));
        }

        _mainViewModel.CardPage.Back.Execute(null);
    }
}

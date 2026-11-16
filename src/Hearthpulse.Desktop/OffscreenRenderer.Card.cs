using Avalonia.Controls;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>
/// Render-check scenes of the card page (<c>HEARTHPULSE_RENDER_ONLY=card</c>, docs/card-page.md): the card of
/// <c>HEARTHPULSE_RENDER_CARD</c>, else the first card of the loaded Standard meta's mulligan numbers.
/// <c>main-card.png</c> is the window, <c>main-card-full.png</c> the whole page, <c>main-card-compact.png</c> the page
/// at phone width.
/// </summary>
internal static partial class OffscreenRenderer
{
    private static async Task RenderCardPageAsync(Window main, MainView view, AppHost host, string folder)
    {
        view.ShowPage("Standard");
        // The meta names the card to show and fills two of the page's sections: wait for it, but not for ever.
        for (var i = 0; i < 100 && host.Main.ShownConstructedMeta is null; i++)
        {
            await Task.Delay(200);
        }

        var cardId = Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_CARD") is { Length: > 0 } asked
            ? asked
            : host.Main.ShownConstructedMeta?.Archetypes.SelectMany(a => a.Mulligan).FirstOrDefault()?.CardId;
        if (cardId is null || !await host.Main.OpenCardPageAsync(cardId))
        {
            return;
        }

        var (width, height) = (main.Width, main.Height);
        // The card's art and the class portraits arrive after the numbers.
        await Task.Delay(2500);
        await Save(main, Path.Combine(folder, "main-card.png"));
        main.Height = 2600;
        await Save(main, Path.Combine(folder, "main-card-full.png"));

        // Phone width: two tiles in a row, the copies, games and hand columns hide.
        main.Width = 420;
        main.Height = 3600;
        await Task.Delay(300);
        await Save(main, Path.Combine(folder, "main-card-compact.png"));
        main.Width = width;
        main.Height = height;
        host.Main.CardPage.Back.Execute(null);
    }
}

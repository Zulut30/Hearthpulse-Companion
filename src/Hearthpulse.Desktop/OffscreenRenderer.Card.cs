using Avalonia;
using Avalonia.Controls;
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

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
/// Here also <c>main-card-compact.png</c> (the whole page at phone width) and <c>main-card-error-compact.png</c>.
/// </summary>
internal static partial class OffscreenRenderer
{
    private static async Task RenderCardPageAsync(Window main, MainView view, AppHost host, string folder)
    {
        view.ShowPage("Standard");
        // The meta names the card to show and fills two of the page's sections: wait for it and for its mulligan numbers
        // (the tier list comes first), but not for ever.
        for (var i = 0; i < 200 && host.Main.ShownConstructedMeta?.Archetypes.Any(a => a.Mulligan.Count > 0) != true; i++)
        {
            await Task.Delay(200);
        }

        var cardId = Environment.GetEnvironmentVariable("HEARTHPULSE_RENDER_CARD") is { Length: > 0 } asked
            ? asked
            : host.Main.ShownConstructedMeta?.Archetypes.SelectMany(a => a.Mulligan).FirstOrDefault()?.CardId;
        if (cardId is null)
        {
            Diag.Info("Card page render skipped: no Standard meta with mulligan numbers, and HEARTHPULSE_RENDER_CARD is not set");
            return;
        }

        if (host.Cards.Get(cardId) is not { } card)
        {
            Diag.Info($"Card page render skipped: the card data has no card {cardId}");
            return;
        }

        object? Token(string key) => Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) ? value : null;
        var theme = Environment.GetEnvironmentVariable("HEARTHPULSE_THEME") is { Length: > 0 } forced ? forced : "(default colours)";
        await File.WriteAllTextAsync(Path.Combine(folder, "main-card-theme.txt"),
            $"ui=avalonia theme={theme} preset={ThemePalette.Presets.Any(p => p.Name == theme)} accent={Token("Accent")} bg={Token("Bg")}"
            + $" language={Core.Localization.Loc.Instance.Language}{Environment.NewLine}");

        var (width, height) = (main.Width, main.Height);
        var live = host.Main.LoadCardStats;
        host.Main.ShowCardGamesSample(CardPageScene.Games(cardId));
        try
        {
            async Task BuiltAsync()
            {
                await Task.Delay(300);
                for (var i = 0; i < 150 && host.Main.CardOpen && host.Main.CardPage.Loading; i++)
                {
                    await Task.Delay(200);
                }

                // The card's art and the class portraits arrive after the numbers.
                await Task.Delay(2500);
            }

            // A meta, card data or data summary that arrives meanwhile builds the page again (the call then answers
            // false): what counts is the page being open and built.
            await host.Main.OpenCardPageAsync(cardId);
            await BuiltAsync();
            await Save(main, Path.Combine(folder, "main-card.png"));
            // The whole page: the archetype list is long, and the mulligan and "Your games" blocks sit under it.
            main.Height = 4800;
            await Save(main, Path.Combine(folder, "main-card-full.png"));

            // Phone width: two tiles in a row, the name over the numbers, the samples under their numbers.
            main.Width = 420;
            main.Height = 7200;
            await Task.Delay(300);
            await Save(main, Path.Combine(folder, "main-card-compact.png"));
            main.Width = width;
            main.Height = height;

            // The list on its way: the card itself is already there.
            host.Main.LoadCardStats = (_, _) => new TaskCompletionSource<CardStatsSlice?>().Task;
            host.Main.CardPage.Retry.Execute(null);
            await Task.Delay(1200);
            await Save(main, Path.Combine(folder, "main-card-loading.png"));

            // The list did not load and there is no copy.
            host.Main.LoadCardStats = (_, _) => Task.FromResult<CardStatsSlice?>(null);
            host.Main.CardPage.Retry.Execute(null);
            await BuiltAsync();
            await Save(main, Path.Combine(folder, "main-card-error.png"));
            main.Width = 420;
            main.Height = 1500;
            await Task.Delay(300);
            await Save(main, Path.Combine(folder, "main-card-error-compact.png"));
            main.Width = width;
            main.Height = height;

            // An older copy under a late source: its own date, and the delay line.
            host.Main.LoadCardStats = (format, _) => Task.FromResult<CardStatsSlice?>(CardPageScene.StaleSlice(format, card));
            host.Main.CardPage.Retry.Execute(null);
            await BuiltAsync();
            host.Main.SetDataHealth(CardPageScene.LateReport("standard"));
            await BuiltAsync();
            await Save(main, Path.Combine(folder, "main-card-stale.png"));
        }
        finally
        {
            main.Width = width;
            main.Height = height;
            host.Main.LoadCardStats = live;
            host.Main.ShowCardGamesSample(null);
            host.Main.CardPage.Back.Execute(null);
        }
    }
}

using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Main;

/// <summary>The hero behind the pages, cross-faded when a page changes it (<see cref="HeroSwap"/>).</summary>
public partial class MainWindow
{
    private HeroSwap? _pageHero;

    /// <summary>Shows the view model's page hero when <paramref name="property"/> may have changed it (null or empty: every property).</summary>
    private void ShowPageHero(string? property)
    {
        if (string.IsNullOrEmpty(property) || property == nameof(MainViewModel.PageHero))
        {
            (_pageHero ??= new HeroSwap(PageHeroGlowA, PageHeroArtA, PageHeroGlowB, PageHeroArtB)).Show(_viewModel.PageHero);
        }
    }
}

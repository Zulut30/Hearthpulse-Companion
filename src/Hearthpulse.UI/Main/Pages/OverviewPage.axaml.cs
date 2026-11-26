using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Hearthpulse.UI.Main.Pages;

/// <summary>
/// The Home page of the main view (its data: <see cref="MainViewModel"/>). Its buttons ask the view for what only the
/// view does: the log import, arranging the overlay, opening History.
/// </summary>
public partial class OverviewPage : UserControl
{
    public OverviewPage() => InitializeComponent();

    /// <summary>"Import" on the getting-started card.</summary>
    public event Action? ImportRequested;

    /// <summary>"Arrange the overlay" on the getting-started card.</summary>
    public event Action? ArrangeRequested;

    /// <summary>"View all" on the recent games card.</summary>
    public event Action? HistoryRequested;

    private void OnImport(object? sender, RoutedEventArgs e) => ImportRequested?.Invoke();

    private void OnArrange(object? sender, RoutedEventArgs e) => ArrangeRequested?.Invoke();

    private void OnViewHistory(object? sender, RoutedEventArgs e) => HistoryRequested?.Invoke();

    /// <summary>
    /// The narrowest a deck banner may be: the key card's art (102) and beside it the name, the line and the mana curve
    /// (bug-hunt #29: three in a row at the narrowest wide window, or two in compact, left the name three letters long
    /// or nothing at all).
    /// </summary>
    internal const double DeckBannerMinWidth = 260;

    /// <summary>The gap right of each banner (Button.homeDeck).</summary>
    private const double DeckBannerGap = 12;

    /// <summary>
    /// Your decks: as many banners in a row as keep <see cref="DeckBannerMinWidth"/> each, three at most. The list (not
    /// its panel: the banners' styling parent is the list) is "single" when one banner takes the whole row.
    /// </summary>
    private void OnHomeDecksSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsControl { ItemsPanelRoot: UniformGrid grid } list)
        {
            var columns = Math.Clamp((int)(e.NewSize.Width / (DeckBannerMinWidth + DeckBannerGap)), 1, 3);
            grid.Columns = columns;
            list.Classes.Set("single", columns == 1);
        }
    }
}

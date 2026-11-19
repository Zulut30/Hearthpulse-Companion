using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main.Decks;

public partial class DeckDetailsView : UserControl
{
    /// <summary>The card diffs (of a version, of a merge suggestion) open when their rows were last built.</summary>
    private readonly HashSet<string> _openDiffs = new(StringComparer.Ordinal);

    public DeckDetailsView() => InitializeComponent();

    /// <summary>
    /// A row's card diff joined the page: a diff toggled builds every row of its list anew. The one just opened comes in
    /// with a reveal (before its first frame); the ones open before stay as they were.
    /// </summary>
    private void OnDiffAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control diff || DiffKey(diff.DataContext) is not { } key)
        {
            return;
        }

        if (!diff.IsVisible)
        {
            _openDiffs.Remove(key);
        }
        else if (_openDiffs.Add(key))
        {
            Motion.Reveal(diff);
        }
    }

    private static string? DiffKey(object? row) => row switch
    {
        DeckVersionRow version => "version:" + version.Key,
        DeckSuggestionRow suggestion => "suggestion:" + suggestion.SecondKey,
        _ => null,
    };

    /// <summary>Shows the hover card of one chart point (the UI render check uses it; -1 hides it).</summary>
    public void HoverGame(int index)
    {
        // The chart sits under «Подробнее»: a hover opens it, the end of a hover (-1) closes it again.
        DeckMore.IsChecked = index >= 0;
        WinRateChart.HoverIndex = index;
    }

    /// <summary>The Versions card, so the UI render check can crop it.</summary>
    public Control Versions => VersionsCard;

    /// <summary>Opens the ⋯ menu of the last version row (UI render check: not the main one when there are several); null without one.</summary>
    public MenuFlyout? OpenLastVersionMenu()
    {
        var button = VersionsCard.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.Flyout is MenuFlyout && b.IsEffectivelyVisible);
        if (button?.Flyout is not MenuFlyout menu)
        {
            return null;
        }

        menu.ShowAt(button);
        return menu;
    }

    private DeckDetailsViewModel? Page => DataContext as DeckDetailsViewModel;

    // The ⋯ menu's items: the flyout is not in the page's tree, so they reach the page's commands from here.
    private void OnMakeMain(object? sender, RoutedEventArgs e) => Page?.SetMain.Execute(KeyOf(sender));

    private void OnSplit(object? sender, RoutedEventArgs e) => Page?.Split.Execute(KeyOf(sender));

    private void OnToggleDiff(object? sender, RoutedEventArgs e) => Page?.ToggleDiff.Execute(KeyOf(sender));

    private static string? KeyOf(object? sender) => (sender as Control)?.DataContext is DeckVersionRow row ? row.Key : null;

    /// <summary>The header's pencil: opens the name box in the Versions card and focuses it.</summary>
    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Page is not { } page)
        {
            return;
        }

        page.StartRename.Execute(null);
        VersionsCard.BringIntoView();
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }
}

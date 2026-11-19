using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hearthpulse.UI.Main.Pages;

/// <summary>
/// The Achievements page (docs/achievements-page.md). Its data: <see cref="MainViewModel"/>; the code here only moves the
/// view: to the journal when a category of the summary is pressed, and the focus after a star leaves "Tracked".
/// </summary>
public partial class AchievementsPage : UserControl
{
    public AchievementsPage() => InitializeComponent();

    /// <summary>Brings the journal's card into view (a category of the summary was chosen; the render scenes).</summary>
    public void ShowBrowse() => BrowseCard.BringIntoView();

    /// <summary>A category of the summary: the view model shows it in the journal; the journal's card comes into view.</summary>
    private void OnCategory(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(ShowBrowse, DispatcherPriority.Loaded);

    /// <summary>
    /// A star pressed in "Tracked" takes its row away: the focus goes to the star of the row that takes its place, else
    /// of the row before it, else to the block's heading, so the keyboard does not fall back to the window's start.
    /// </summary>
    private void OnStar(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AchievementRow { Tracked: true } row } star || !TrackedList.IsVisualAncestorOf(star))
        {
            return;
        }

        var index = TrackedList.Items.IndexOf(row);
        // The click comes before the command: the row is gone once the dispatcher has laid the list out again.
        Dispatcher.UIThread.Post(() => FocusTrackedStar(index), DispatcherPriority.Loaded);
    }

    private void FocusTrackedStar(int index)
    {
        var count = TrackedList.ItemCount;
        if (count > 0 && TrackedList.ContainerFromIndex(Math.Clamp(index, 0, count - 1)) is { } container
            && container.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } next)
        {
            next.Focus(NavigationMethod.Tab);
            return;
        }

        // An empty block: its heading takes the focus (out of the Tab order, so it is not one more stop).
        TrackedHead.IsTabStop = false;
        TrackedHead.Focusable = true;
        TrackedHead.Focus(NavigationMethod.Tab);
    }
}

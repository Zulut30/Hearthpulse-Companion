using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hearthpulse.UI.Controls;

namespace Hearthpulse.UI.Main;

/// <summary>
/// Where the keyboard focus goes around the card page (docs/card-page.md, WCAG 2.4.3): to the page's Back button when
/// the page opens, and back to the card row it was opened from when it closes - by Back, Esc, Alt+Left or the mouse's
/// back button alike, since each ends in <see cref="MainViewModel.CardOpen"/> turning false.
/// </summary>
public partial class MainView
{
    /// <summary>The row the open card page was opened from, its card and its list (the row may be rebuilt meanwhile).</summary>
    private (Button Row, string CardId, ItemsControl? List)? _cardOrigin;

    /// <summary>A card row was activated (<see cref="CardLink"/>): its card page opens, and the row is remembered for the way back.</summary>
    private void OnCardLink(object? sender, CardLinkEventArgs e)
    {
        _cardOrigin = e.Source is Button row ? (row, e.CardId, row.FindAncestorOfType<ItemsControl>()) : null;
        _viewModel?.OpenCard.Execute(e.CardId);
        e.Handled = true;
    }

    /// <summary>True while the focus sits on the card page (or nowhere): then closing the page must put it somewhere.</summary>
    private bool FocusIsOnCardPage() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual { IsEffectivelyVisible: true } focused
        || ReferenceEquals(focused, PageCard) || PageCard.IsVisualAncestorOf(focused);

    /// <summary>The page has opened: the focus leaves the row (now hidden) for the page's first control, Back.</summary>
    private void FocusCardPage() => FocusWhenShown(() => _viewModel?.CardOpen == true ? CardDetails.BackButton : null);

    /// <summary>
    /// Focuses a control now and once more after the layout pass: the page it sits on was shown this moment, and the
    /// control that had the focus is being hidden, which clears the focus after this call.
    /// </summary>
    private void FocusWhenShown(Func<InputElement?> target)
    {
        target()?.Focus(NavigationMethod.Tab);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (target() is { IsEffectivelyVisible: true, IsFocused: false } control && FocusIsOnCardPageOrNowhere(control))
            {
                control.Focus(NavigationMethod.Tab);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The focus is where this file left it or lost: a focus the player has moved since is not taken away.</summary>
    private bool FocusIsOnCardPageOrNowhere(InputElement wanted) =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual { IsEffectivelyVisible: true } focused
        || ReferenceEquals(focused, wanted) || ReferenceEquals(focused, PageCard) || PageCard.IsVisualAncestorOf(focused);

    /// <summary>
    /// The page has closed: the focus returns to the row it was opened from; when that row is gone (the list was built
    /// again), to the row of the same card in that list, else the list's first row, else the page's sidebar button.
    /// </summary>
    private void RestoreCardFocus()
    {
        var origin = _cardOrigin;
        _cardOrigin = null;

        static IEnumerable<Button> Rows(ItemsControl list) =>
            list.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && CardLink.GetCardId(b) is { Length: > 0 });

        InputElement? Row() => origin is not { } from ? null
            : from.Row.IsEffectivelyVisible && from.Row.IsAttachedToVisualTree() ? from.Row
            : from.List is { IsEffectivelyVisible: true } list ? Rows(list).FirstOrDefault(r => CardLink.GetCardId(r) == from.CardId) ?? Rows(list).FirstOrDefault()
            : null;

        var first = Row();
        first?.Focus(NavigationMethod.Tab);
        // Once more after the layout pass: a list built again while the card was open has its rows only then, and the
        // Back button being hidden clears the focus after this call.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.CardOpen != false)
            {
                return;
            }

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            var moved = focused is Visual { IsEffectivelyVisible: true } visible && !ReferenceEquals(focused, first)
                        && !ReferenceEquals(visible, PageCard) && !PageCard.IsVisualAncestorOf(visible);
            if (moved)
            {
                // The player's own focus (a sidebar button pressed meanwhile) stays where they put it.
                return;
            }

            var landing = Row() ?? PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, CurrentPage)];
            if (!landing.IsFocused)
            {
                landing.Focus(NavigationMethod.Tab);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}

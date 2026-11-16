using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Controls;

namespace Hearthpulse.App.Main;

/// <summary>
/// Where the keyboard focus goes around the card page (docs/card-page.md, WCAG 2.4.3): to the page's Back button when
/// the page opens, and back to the card row it was opened from when it closes - by Back, Esc, Alt+Left or the mouse's
/// back button alike, since each ends in <see cref="MainViewModel.CardOpen"/> turning false.
/// </summary>
public partial class MainWindow
{
    /// <summary>The row the open card page was opened from, its card and its list (the row may be rebuilt meanwhile).</summary>
    private (ButtonBase Row, string CardId, ItemsControl? List)? _cardOrigin;

    /// <summary>A card row was activated (<see cref="CardLink"/>): its card page opens, and the row is remembered for the way back.</summary>
    private void OnCardLink(object? sender, CardLinkEventArgs e)
    {
        _cardOrigin = e.OriginalSource is ButtonBase row ? (row, e.CardId, CardLink.ListOf(row)) : null;
        _viewModel.OpenCard.Execute(e.CardId);
        e.Handled = true;
    }

    /// <summary>True while the focus sits on the card page (or nowhere): then closing the page must put it somewhere.</summary>
    private bool FocusIsOnCardPage()
    {
        var focused = FocusManager.GetFocusedElement(this) as DependencyObject;
        if (focused is null || focused is UIElement { IsVisible: false })
        {
            return true;
        }

        for (var parent = focused; parent is not null; parent = parent is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(parent) : LogicalTreeHelper.GetParent(parent))
        {
            if (ReferenceEquals(parent, PageCard))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The page has opened: the focus leaves the row (now hidden) for the page's first control, Back.</summary>
    private void FocusCardPage()
    {
        PageCard.UpdateLayout();
        var back = CardDetailsPage.BackButton;
        if (!Take(back))
        {
            // The page was built this moment: its button takes the focus once it is loaded.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.CardOpen)
                {
                    Take(back);
                }
            });
        }
    }

    /// <summary>
    /// The page has closed: the focus returns to the row it was opened from; when that row is gone (the list was built
    /// again), to the row of the same card in that list, else the list's first row, else the page's sidebar button.
    /// </summary>
    private void RestoreCardFocus()
    {
        var origin = _cardOrigin;
        _cardOrigin = null;
        UpdateLayout();
        ButtonBase? target = null;
        if (origin is { } from)
        {
            target = Usable(from.Row) ? from.Row
                : from.List is { IsVisible: true } list
                    ? CardLink.Rows(list).FirstOrDefault(r => CardLink.GetCardId(r) == from.CardId) ?? CardLink.Rows(list).FirstOrDefault()
                    : null;
        }

        Take(target ?? PageButtons[MainViewModel.NavIndex(MainViewModel.NavPages, CurrentPage)]);
    }

    private static bool Usable(ButtonBase row) => row is { IsVisible: true, IsEnabled: true } && PresentationSource.FromVisual(row) is not null;

    /// <summary>Gives an element the window's focus: the logical one always, the keyboard's when the window has it.</summary>
    private bool Take(UIElement element)
    {
        if (!element.IsVisible || !element.Focusable)
        {
            return false;
        }

        FocusManager.SetFocusedElement(this, element);
        Keyboard.Focus(element);
        return ReferenceEquals(FocusManager.GetFocusedElement(this), element);
    }
}

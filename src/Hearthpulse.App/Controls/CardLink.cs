using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.App.Controls;

/// <summary>A card row asked for its card page: <see cref="CardId"/> is the row's card.</summary>
public sealed class CardLinkEventArgs(RoutedEvent routedEvent, object source, string cardId) : RoutedEventArgs(routedEvent, source)
{
    public string CardId { get; } = cardId;
}

/// <summary>
/// Makes a card row open the card's page (docs/card-page.md). The row is a <c>Button</c> (style <c>CardRowButton</c>)
/// with <c>CardLink.CardId</c> and <c>CardLink.CardName</c>: a click, Enter or Space raises <see cref="OpenEvent"/>,
/// which bubbles to the main window. The button is named "Open the card page of ..." for a screen reader; its list is
/// one Tab stop, and Up / Down move between its rows. The row keeps its hover preview (<see cref="CardPreview"/>).
/// </summary>
public static class CardLink
{
    public static readonly DependencyProperty CardIdProperty = DependencyProperty.RegisterAttached(
        "CardId", typeof(string), typeof(CardLink), new PropertyMetadata(null, OnCardIdChanged));

    /// <summary>The card's name as the row shows it, for the button's accessible name.</summary>
    public static readonly DependencyProperty CardNameProperty = DependencyProperty.RegisterAttached(
        "CardName", typeof(string), typeof(CardLink), new PropertyMetadata(null, OnCardNameChanged));

    public static readonly RoutedEvent OpenEvent = EventManager.RegisterRoutedEvent(
        "Open", RoutingStrategy.Bubble, typeof(EventHandler<CardLinkEventArgs>), typeof(CardLink));

    public static string? GetCardId(DependencyObject d) => (string?)d.GetValue(CardIdProperty);

    public static void SetCardId(DependencyObject d, string? value) => d.SetValue(CardIdProperty, value);

    public static string? GetCardName(DependencyObject d) => (string?)d.GetValue(CardNameProperty);

    public static void SetCardName(DependencyObject d, string? value) => d.SetValue(CardNameProperty, value);

    private static void OnCardIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
        {
            return;
        }

        var (was, now) = (e.OldValue is string { Length: > 0 }, e.NewValue is string { Length: > 0 });
        if (now && !was)
        {
            button.Click += OnClick;
            button.PreviewKeyDown += OnKeyDown;
            button.Loaded += OnLoaded;
        }
        else if (was && !now)
        {
            button.Click -= OnClick;
            button.PreviewKeyDown -= OnKeyDown;
            button.Loaded -= OnLoaded;
        }
    }

    private static void OnCardNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is string { Length: > 0 } name)
        {
            AutomationProperties.SetName(d, Loc.F("Card.OpenName", name));
        }
        else
        {
            d.ClearValue(AutomationProperties.NameProperty);
        }
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button)
        {
            Open(button);
        }
    }

    /// <summary>The rows' list is one Tab stop (the arrows move inside it), so a 30-card list is not 30 stops.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject row && ListOf(row) is { } list)
        {
            KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Once);
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.None || sender is not ButtonBase button || ListOf(button) is not { } list)
        {
            return;
        }

        var rows = Rows(list).ToList();
        var next = rows.IndexOf(button) + (e.Key == Key.Down ? 1 : -1);
        if (next >= 0 && next < rows.Count)
        {
            rows[next].Focus();
            rows[next].BringIntoView();
        }

        // At either end the focus stays: the page does not scroll away under the list.
        e.Handled = true;
    }

    private static ItemsControl? ListOf(DependencyObject row)
    {
        for (var parent = VisualTreeHelper.GetParent(row); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ItemsControl list)
            {
                return list;
            }
        }

        return null;
    }

    /// <summary>The card rows built in a list, top to bottom.</summary>
    private static IEnumerable<ButtonBase> Rows(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase { IsVisible: true } button && GetCardId(button) is { Length: > 0 })
            {
                yield return button;
                continue;
            }

            foreach (var deeper in Rows(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Raises <see cref="OpenEvent"/> for the row's card (a click does; tests call it). False without a card.</summary>
    public static bool Open(UIElement row)
    {
        if (GetCardId(row) is not { Length: > 0 } cardId)
        {
            return false;
        }

        row.RaiseEvent(new CardLinkEventArgs(OpenEvent, row, cardId));
        return true;
    }
}

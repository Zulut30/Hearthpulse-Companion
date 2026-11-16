using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.App.Controls;

/// <summary>A card row asked for its card page: <see cref="CardId"/> is the row's card.</summary>
public sealed class CardLinkEventArgs(RoutedEvent routedEvent, object source, string cardId) : RoutedEventArgs(routedEvent, source)
{
    public string CardId { get; } = cardId;
}

/// <summary>
/// Makes a card row open the card's page (docs/card-page.md): any element sets <c>CardLink.CardId</c>, a click on it
/// raises <see cref="OpenEvent"/>, which bubbles to the main window. The row keeps its hover preview
/// (<see cref="CardPreview"/>); the hand cursor says it can be clicked, and
/// the row's help text names what the click does.
/// </summary>
public static class CardLink
{
    public static readonly DependencyProperty CardIdProperty = DependencyProperty.RegisterAttached(
        "CardId", typeof(string), typeof(CardLink), new PropertyMetadata(null, OnCardIdChanged));

    public static readonly RoutedEvent OpenEvent = EventManager.RegisterRoutedEvent(
        "Open", RoutingStrategy.Bubble, typeof(EventHandler<CardLinkEventArgs>), typeof(CardLink));

    public static string? GetCardId(DependencyObject d) => (string?)d.GetValue(CardIdProperty);

    public static void SetCardId(DependencyObject d, string? value) => d.SetValue(CardIdProperty, value);

    private static void OnCardIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var (was, now) = (e.OldValue is string { Length: > 0 }, e.NewValue is string { Length: > 0 });
        if (now && !was)
        {
            element.Cursor = Cursors.Hand;
            AutomationProperties.SetHelpText(element, Loc.T("Card.OpenTip"));
            element.MouseLeftButtonUp += OnClick;
        }
        else if (was && !now)
        {
            element.ClearValue(FrameworkElement.CursorProperty);
            element.ClearValue(AutomationProperties.HelpTextProperty);
            element.MouseLeftButtonUp -= OnClick;
        }
    }

    private static void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled && sender is FrameworkElement element)
        {
            e.Handled = Open(element);
        }
    }

    /// <summary>Raises <see cref="OpenEvent"/> for the element's card (a click does; tests and the render check call it). False without a card.</summary>
    public static bool Open(FrameworkElement element)
    {
        if (GetCardId(element) is not { Length: > 0 } cardId)
        {
            return false;
        }

        element.RaiseEvent(new CardLinkEventArgs(OpenEvent, element, cardId));
        return true;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.UI.Controls;

/// <summary>A card row asked for its card page: <see cref="CardId"/> is the row's card.</summary>
public sealed class CardLinkEventArgs(RoutedEvent routedEvent, object source, string cardId) : RoutedEventArgs(routedEvent, source)
{
    public string CardId { get; } = cardId;
}

/// <summary>
/// Makes a card row open the card's page (docs/card-page.md): any control sets <c>CardLink.CardId</c>, a tap on it
/// raises <see cref="OpenEvent"/>, which bubbles to the main view. The row keeps its hover preview
/// (<see cref="CardPreview"/>); the hand cursor says it can be clicked, and
/// the row's help text names what the click does.
/// </summary>
public static class CardLink
{
    public static readonly AttachedProperty<string?> CardIdProperty = AvaloniaProperty.RegisterAttached<Control, string?>("CardId", typeof(CardLink));

    public static readonly RoutedEvent<CardLinkEventArgs> OpenEvent =
        RoutedEvent.Register<CardLinkEventArgs>("Open", RoutingStrategies.Bubble, typeof(CardLink));

    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    static CardLink()
    {
        CardIdProperty.Changed.AddClassHandler<Control>(OnCardIdChanged);
    }

    public static string? GetCardId(Control control) => control.GetValue(CardIdProperty);

    public static void SetCardId(Control control, string? value) => control.SetValue(CardIdProperty, value);

    private static void OnCardIdChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        var (was, now) = (e.OldValue is string { Length: > 0 }, e.NewValue is string { Length: > 0 });
        if (now && !was)
        {
            control.Cursor = Hand;
            Avalonia.Automation.AutomationProperties.SetHelpText(control, Loc.T("Card.OpenTip"));
            control.Tapped += OnTapped;
        }
        else if (was && !now)
        {
            control.ClearValue(InputElement.CursorProperty);
            control.ClearValue(Avalonia.Automation.AutomationProperties.HelpTextProperty);
            control.Tapped -= OnTapped;
        }
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!e.Handled && sender is Control control)
        {
            e.Handled = Open(control);
        }
    }

    /// <summary>Raises <see cref="OpenEvent"/> for the control's card (a tap does; tests and the render check call it). False without a card.</summary>
    public static bool Open(Control control)
    {
        if (GetCardId(control) is not { Length: > 0 } cardId)
        {
            return false;
        }

        control.RaiseEvent(new CardLinkEventArgs(OpenEvent, control, cardId));
        return true;
    }
}

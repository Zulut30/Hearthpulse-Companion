using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.UI.Controls;

/// <summary>A card row asked for its card page: <see cref="CardId"/> is the row's card.</summary>
public sealed class CardLinkEventArgs(RoutedEvent routedEvent, object source, string cardId) : RoutedEventArgs(routedEvent, source)
{
    public string CardId { get; } = cardId;
}

/// <summary>
/// Makes a card row open the card's page (docs/card-page.md). The row is a <c>Button</c> (classes <c>bare</c>) with
/// <c>CardLink.CardId</c> and <c>CardLink.CardName</c>: a click, Enter or Space raises <see cref="OpenEvent"/>, which
/// bubbles to the main view. The button is named "Open the card page of ..." for a screen reader; its list is one Tab
/// stop, and Up / Down move between its rows. The row keeps its hover preview (<see cref="CardPreview"/>).
/// </summary>
public static class CardLink
{
    public static readonly AttachedProperty<string?> CardIdProperty = AvaloniaProperty.RegisterAttached<Control, string?>("CardId", typeof(CardLink));

    /// <summary>The card's name as the row shows it, for the button's accessible name.</summary>
    public static readonly AttachedProperty<string?> CardNameProperty = AvaloniaProperty.RegisterAttached<Control, string?>("CardName", typeof(CardLink));

    public static readonly RoutedEvent<CardLinkEventArgs> OpenEvent =
        RoutedEvent.Register<CardLinkEventArgs>("Open", RoutingStrategies.Bubble, typeof(CardLink));

    static CardLink()
    {
        CardIdProperty.Changed.AddClassHandler<Button>(OnCardIdChanged);
        CardNameProperty.Changed.AddClassHandler<Control>(OnCardNameChanged);
    }

    public static string? GetCardId(Control control) => control.GetValue(CardIdProperty);

    public static void SetCardId(Control control, string? value) => control.SetValue(CardIdProperty, value);

    public static string? GetCardName(Control control) => control.GetValue(CardNameProperty);

    public static void SetCardName(Control control, string? value) => control.SetValue(CardNameProperty, value);

    private static void OnCardIdChanged(Button button, AvaloniaPropertyChangedEventArgs e)
    {
        var (was, now) = (e.OldValue is string { Length: > 0 }, e.NewValue is string { Length: > 0 });
        if (now && !was)
        {
            button.Click += OnClick;
            button.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            button.AttachedToVisualTree += OnAttached;
            if (button.IsAttachedToVisualTree())
            {
                OneTabStop(button);
            }
        }
        else if (was && !now)
        {
            button.Click -= OnClick;
            button.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            button.AttachedToVisualTree -= OnAttached;
        }
    }

    private static void OnCardNameChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is string { Length: > 0 } name)
        {
            AutomationProperties.SetName(control, Loc.F("Card.OpenName", name));
        }
        else
        {
            control.ClearValue(AutomationProperties.NameProperty);
        }
    }

    private static void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            Open(button);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button button)
        {
            OneTabStop(button);
        }
    }

    /// <summary>The rows' list is one Tab stop (the arrows move inside it), so a 30-card list is not 30 stops.</summary>
    private static void OneTabStop(Button row)
    {
        if (row.FindAncestorOfType<ItemsControl>() is { } list)
        {
            KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Once);
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != KeyModifiers.None || sender is not Button button
            || button.FindAncestorOfType<ItemsControl>() is not { } list)
        {
            return;
        }

        var rows = list.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && GetCardId(b) is { Length: > 0 }).ToList();
        var next = rows.IndexOf(button) + (e.Key == Key.Down ? 1 : -1);
        if (next >= 0 && next < rows.Count)
        {
            rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }

        // At either end the focus stays: the page does not scroll away under the list.
        e.Handled = true;
    }

    /// <summary>Raises <see cref="OpenEvent"/> for the row's card (a click does; tests call it). False without a card.</summary>
    public static bool Open(Control row)
    {
        if (GetCardId(row) is not { Length: > 0 } cardId)
        {
            return false;
        }

        row.RaiseEvent(new CardLinkEventArgs(OpenEvent, row, cardId));
        return true;
    }
}

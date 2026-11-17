using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Behaviors;

namespace Hearthpulse.UI.Controls;

/// <summary>A card row asked for its card page: <see cref="CardId"/> is the row's card.</summary>
public sealed class CardLinkEventArgs(RoutedEvent routedEvent, object source, string cardId) : RoutedEventArgs(routedEvent, source)
{
    public string CardId { get; } = cardId;
}

/// <summary>
/// Makes a card row open the card's page (docs/card-page.md). The row is a <c>Button</c> (classes <c>bare</c>) with
/// <c>CardLink.CardId</c> and <c>CardLink.CardName</c>: a click, Enter or Space raises <see cref="OpenEvent"/>, which
/// bubbles to the main view. The button is named "Open the card page of ..." for a screen reader, with its place in the
/// list ("3 of 30"); its list is one Tab stop, Up / Down move between its rows and typing a card's first letters jumps
/// to it (the shared list behaviors, docs/ui-behaviors.md). The row keeps its hover preview (<see cref="CardPreview"/>).
/// </summary>
public static class CardLink
{
    public static readonly AttachedProperty<string?> CardIdProperty = AvaloniaProperty.RegisterAttached<Control, string?>("CardId", typeof(CardLink));

    /// <summary>The card's name as the row shows it, for the button's accessible name.</summary>
    public static readonly AttachedProperty<string?> CardNameProperty = AvaloniaProperty.RegisterAttached<Control, string?>("CardName", typeof(CardLink));

    public static readonly RoutedEvent<CardLinkEventArgs> OpenEvent =
        RoutedEvent.Register<CardLinkEventArgs>("Open", RoutingStrategies.Bubble, typeof(CardLink));

    /// <summary>Up / Down between the rows; at either end the focus stays, and Home / End stay the page's.</summary>
    private static readonly ListNavigationOptions Navigation = ListNavigationOptions.Vertical with { HomeEnd = false };

    /// <summary>A list's type-to-find query, kept on the list.</summary>
    private static readonly AttachedProperty<Typeahead?> TypeaheadProperty = AvaloniaProperty.RegisterAttached<ItemsControl, Typeahead?>("Typeahead", typeof(CardLink));

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
            button.AddHandler(InputElement.TextInputEvent, OnTextInput);
            button.GotFocus += OnFocused;
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
            button.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            button.GotFocus -= OnFocused;
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
        if (e.KeyModifiers != KeyModifiers.None || sender is not Button button || button.FindAncestorOfType<ItemsControl>() is not { } list)
        {
            return;
        }

        // At either end the focus stays and the key is still the list's: the page does not scroll away under it.
        if (RovingFocus.Move(button, Rows(list), e.Key, Navigation))
        {
            e.Handled = true;
        }
    }

    /// <summary>Type-to-find: the next row whose card name starts with the typed letters takes the focus.</summary>
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not Button button || button.FindAncestorOfType<ItemsControl>() is not { } list)
        {
            return;
        }

        if (list.GetValue(TypeaheadProperty) is not { } typeahead)
        {
            typeahead = RovingFocus.NewTypeahead();
            list.SetValue(TypeaheadProperty, typeahead);
        }

        if (RovingFocus.Search(typeahead, e.Text, button, Rows(list), GetCardName))
        {
            e.Handled = true;
        }
    }

    /// <summary>A screen reader hears the row's place in its list.</summary>
    private static void OnFocused(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Button button && button.FindAncestorOfType<ItemsControl>() is { } list)
        {
            RovingFocus.Announce(button, Rows(list));
        }
    }

    /// <summary>The card rows shown in a list, top to bottom.</summary>
    internal static List<Button> Rows(ItemsControl list) =>
        list.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && GetCardId(b) is { Length: > 0 }).ToList();

    /// <summary>
    /// The card rows as <see cref="FocusRecovery{TElement}"/> reads them: a row is usable while shown and in the visual
    /// tree; a list's items are its shown rows; a row's key is its card id (the row is rebuilt, the card stays).
    /// </summary>
    internal sealed class FocusTree : IFocusTree<Button>
    {
        public static FocusTree Instance { get; } = new();

        public bool IsUsable(Button element) => element.IsEffectivelyVisible && element.IsAttachedToVisualTree();

        public IReadOnlyList<Button> ItemsOf(object collection) => collection is ItemsControl { IsEffectivelyVisible: true } list ? Rows(list) : [];

        public string? KeyOf(Button element) => GetCardId(element);
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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hearthpulse.App.Behaviors;
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
/// which bubbles to the main window. The button is named "Open the card page of ..." for a screen reader, with its
/// place in the list ("3 of 30"); its list is one Tab stop, Up / Down move between its rows and typing a card's first
/// letters jumps to it (the shared list behaviors, docs/ui-behaviors.md). The row keeps its hover preview
/// (<see cref="CardPreview"/>).
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

    /// <summary>Up / Down between the rows; at either end the focus stays, and Home / End stay the page's.</summary>
    private static readonly ListNavigationOptions Navigation = ListNavigationOptions.Vertical with { HomeEnd = false };

    /// <summary>A list's type-to-find query, kept on the list.</summary>
    private static readonly DependencyProperty TypeaheadProperty = DependencyProperty.RegisterAttached(
        "Typeahead", typeof(Typeahead), typeof(CardLink), new PropertyMetadata(null));

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
            button.TextInput += OnTextInput;
            button.GotFocus += OnFocused;
            button.Loaded += OnLoaded;
        }
        else if (was && !now)
        {
            button.Click -= OnClick;
            button.PreviewKeyDown -= OnKeyDown;
            button.TextInput -= OnTextInput;
            button.GotFocus -= OnFocused;
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
        if (Keyboard.Modifiers != ModifierKeys.None || sender is not ButtonBase button || ListOf(button) is not { } list)
        {
            return;
        }

        // At either end the focus stays and the key is still the list's: the page does not scroll away under it.
        e.Handled = RovingFocus.Move(button, Rows(list).ToList(), e.Key, Navigation);
    }

    /// <summary>Type-to-find: the next row whose card name starts with the typed letters takes the focus.</summary>
    private static void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not ButtonBase button || ListOf(button) is not { } list || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }

        if (list.GetValue(TypeaheadProperty) is not Typeahead typeahead)
        {
            typeahead = RovingFocus.NewTypeahead();
            list.SetValue(TypeaheadProperty, typeahead);
        }

        e.Handled = RovingFocus.Search(typeahead, e.Text, button, Rows(list).ToList(), row => GetCardName(row));
    }

    /// <summary>A screen reader hears the row's place in its list.</summary>
    private static void OnFocused(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button && ListOf(button) is { } list)
        {
            RovingFocus.Announce(button, Rows(list).ToList());
        }
    }

    internal static ItemsControl? ListOf(DependencyObject row)
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
    internal static IEnumerable<ButtonBase> Rows(DependencyObject root)
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

    /// <summary>
    /// The card rows as <see cref="FocusRecovery{TElement}"/> reads them: a row is usable while shown, enabled and in a
    /// window; a list's items are its shown rows; a row's key is its card id (the row is rebuilt, the card stays).
    /// </summary>
    internal sealed class FocusTree : IFocusTree<ButtonBase>
    {
        public static FocusTree Instance { get; } = new();

        public bool IsUsable(ButtonBase element) => element is { IsVisible: true, IsEnabled: true } && PresentationSource.FromVisual(element) is not null;

        public IReadOnlyList<ButtonBase> ItemsOf(object collection) => collection is ItemsControl { IsVisible: true } list ? Rows(list).ToList() : [];

        public string? KeyOf(ButtonBase element) => GetCardId(element);
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

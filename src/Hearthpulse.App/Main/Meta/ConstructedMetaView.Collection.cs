using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Hearthpulse.App.Behaviors;

namespace Hearthpulse.App.Main.Meta;

/// <summary>
/// The missing cards behind a deck's build label, in a popup opened by the label's button. The popup exists only
/// while it is open and has neither a parent nor a placement target: UI Automation keeps the elements a client asked
/// for (a screen reader, any tool that listens to the desktop) alive for as long as that client holds them, and a
/// popup tied to this view's tree keeps the view, and the closed main window with it, alive through them (found in
/// process dumps: a ref-counted handle, the popup root's automation peer, the popup root, then this view or the
/// popup's inherited values, the window).
/// </summary>
public partial class ConstructedMetaView
{
    private Popup? _deckBuild;
    private Button? _deckBuildOpener;
    private Window? _deckBuildWindow;

    /// <summary>Where the focus goes as the popup opens and closes (the shared popup behavior, docs/ui-behaviors.md).</summary>
    private readonly PopupFocus _deckBuildFocus = new();

    /// <summary>Whether the missing cards of a label are open.</summary>
    internal bool DeckBuildOpen => _deckBuild is { IsOpen: true };

    /// <summary>The open popup of the missing cards, null while none is open (the tests look into it).</summary>
    internal Popup? DeckBuildPopup => _deckBuild;

    /// <summary>
    /// Opens the missing cards of the first build label on the page that has any (the UI render check and the tests),
    /// as a press of that label does. Returns the popup's content, null when no label has missing cards.
    /// </summary>
    public FrameworkElement? OpenFirstDeckBuild()
    {
        var label = Descendants(this).OfType<Button>()
            .FirstOrDefault(b => b.IsVisible && b.DataContext is MetaDeckRow { Build.HasMissing: true } && b.Content is DeckBuildView);
        if (label is null)
        {
            return null;
        }

        OpenDeckBuild(label);
        return _deckBuild?.Child as FrameworkElement;
    }

    /// <summary>Closes the missing cards from code (the row or the view went away, another label opened).</summary>
    public void CloseDeckBuild() => CloseDeckBuild(DismissReason.Imperative);

    private void CloseDeckBuild(DismissReason reason)
    {
        if (_deckBuild is { } popup)
        {
            if (!_deckBuildFocus.RequestClose(reason, focusInside: popup.Child?.IsKeyboardFocusWithin == true))
            {
                return;
            }

            popup.IsOpen = false;
            // Closed can be dispatched later: release handlers before a programmatic reopen replaces the popup.
            if (ReferenceEquals(_deckBuild, popup))
            {
                OnDeckBuildClosed(popup, EventArgs.Empty);
            }
        }
    }

    private void OnDeckBuild(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            OpenDeckBuild(button);
        }
    }

    private void OpenDeckBuild(Button button)
    {
        if (button.DataContext is not MetaDeckRow { Build: { } build })
        {
            return;
        }

        // One popup at a time: opened from another label, the first one closes first.
        CloseDeckBuild();
        var details = new ContentControl
        {
            Content = build,
            ContentTemplate = (DataTemplate)FindResource("DeckMissingList"),
            Focusable = true,
            FocusVisualStyle = (Style)FindResource("FocusRing"),
        };
        AutomationProperties.SetName(details, build.Details);
        details.KeyDown += OnDeckBuildKey;
        var plate = new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(16, 14, 16, 14),
            MinWidth = 260,
            MaxWidth = 420,
            Child = details,
        };

        // The popup has no parent and no placement target (either would give it this window's inherited values to keep,
        // the window itself among them): it stands at the label's place on the screen and is given the text look here.
        TextElement.SetFontFamily(plate, TextElement.GetFontFamily(this));
        TextElement.SetFontSize(plate, TextElement.GetFontSize(this));
        TextElement.SetForeground(plate, TextElement.GetForeground(this));
        plate.Language = Language;
        plate.FlowDirection = FlowDirection;
        var below = button.PointToScreen(new Point(0, button.ActualHeight + 4));
        if (PresentationSource.FromVisual(button)?.CompositionTarget is { } target)
        {
            below = target.TransformFromDevice.Transform(below);
        }

        var popup = new Popup
        {
            StaysOpen = false,
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = below.X,
            VerticalOffset = below.Y,
            AllowsTransparency = true,
            Child = plate,
        };
        popup.Closed += OnDeckBuildClosed;
        _deckBuild = popup;
        _deckBuildOpener = button;

        // The row's numbers are the popup's: a new collection or a new list replaces the row (or gives its container
        // another one), and the popup closes rather than show the old numbers. A view that leaves its window closes it too.
        button.Unloaded += OnDeckBuildGone;
        button.DataContextChanged += OnDeckBuildRowChanged;
        Unloaded += OnDeckBuildGone;

        // A window that closes does not unload its views: the popup, a window of its own, would outlive it.
        _deckBuildWindow = Window.GetWindow(this);
        if (_deckBuildWindow is not null)
        {
            _deckBuildWindow.Closed += OnDeckBuildWindowClosed;
        }

        popup.IsOpen = true;

        // Focus goes into the popup, so a screen reader reads it and Escape closes it.
        _deckBuildFocus.Opened(button, details);
    }

    /// <summary>Escape, Enter and Space close the list (it has nothing to activate) and give the focus back to the label.</summary>
    private void OnDeckBuildKey(object sender, KeyEventArgs e)
    {
        if (PopupFocus.ReasonFor(e.Key, activateCloses: true) is { } reason)
        {
            CloseDeckBuild(reason);
            e.Handled = true;
        }
    }

    private void OnDeckBuildGone(object sender, RoutedEventArgs e) => CloseDeckBuild();

    private void OnDeckBuildWindowClosed(object? sender, EventArgs e) => CloseDeckBuild();

    private void OnDeckBuildRowChanged(object sender, DependencyPropertyChangedEventArgs e) => CloseDeckBuild();

    private void OnDeckBuildClosed(object? sender, EventArgs e)
    {
        if (sender is not Popup popup)
        {
            return;
        }

        // Nothing of the row or of this view stays with the closed popup: not the label it was placed at, not the
        // row's cards, not a handler of this view.
        popup.Closed -= OnDeckBuildClosed;
        if (popup.Child is Border { Child: ContentControl details } plate)
        {
            details.KeyDown -= OnDeckBuildKey;
            details.Content = null;
            details.ContentTemplate = null;
            plate.Child = null;
        }

        popup.Child = null;
        popup.ClearValue(FocusManager.FocusedElementProperty);
        if (!ReferenceEquals(popup, _deckBuild))
        {
            return;
        }

        var opener = _deckBuildOpener;
        _deckBuild = null;
        _deckBuildOpener = null;
        Unloaded -= OnDeckBuildGone;
        if (_deckBuildWindow is { } window)
        {
            window.Closed -= OnDeckBuildWindowClosed;
            _deckBuildWindow = null;
        }

        if (opener is not null)
        {
            opener.Unloaded -= OnDeckBuildGone;
            opener.DataContextChanged -= OnDeckBuildRowChanged;
        }

        // Closed from the keyboard (or by code with the focus inside): focus returns to the label it was opened from. A
        // click on another control keeps the click's focus.
        _deckBuildFocus.Closed();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}

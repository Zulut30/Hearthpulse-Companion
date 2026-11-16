using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hearthpulse.App.Main.Meta;

/// <summary>The missing cards behind a deck's build label: one popup for every row, opened by the label's button.</summary>
public partial class ConstructedMetaView
{
    private Button? _deckBuildOpener;
    private bool _deckBuildClosedByKey;

    /// <summary>The label a render scene or a test opens the popup from: the first build label of the top decks, if any.</summary>
    internal bool DeckBuildOpen => DeckBuildPopup.IsOpen;

    private void OnDeckBuild(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MetaDeckRow { Build: { } build } } button)
        {
            return;
        }

        _deckBuildOpener = button;
        _deckBuildClosedByKey = false;
        DeckBuildPopup.DataContext = build;
        DeckBuildPopup.PlacementTarget = button;
        DeckBuildPopup.IsOpen = true;

        // Focus goes into the popup, so a screen reader reads it and Escape closes it.
        DeckBuildDetails.Focus();
    }

    private void OnDeckBuildKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            _deckBuildClosedByKey = true;
            DeckBuildPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnDeckBuildClosed(object? sender, EventArgs e)
    {
        // Closed from the keyboard: focus returns to the label it was opened from. A click away keeps the click's focus.
        var opener = _deckBuildOpener;
        _deckBuildOpener = null;
        if (_deckBuildClosedByKey && opener is { IsVisible: true })
        {
            opener.Focus();
        }
    }
}

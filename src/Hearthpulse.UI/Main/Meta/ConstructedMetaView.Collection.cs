using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Hearthpulse.UI.Main.Meta;

/// <summary>
/// The missing cards behind a deck's build label: the label's flyout. Where the focus goes with it (into the list as it
/// opens, back to the label as it closes) is the shared popup behavior, <c>b:PopupFocus.ReturnFocus</c> in the markup.
/// </summary>
public partial class ConstructedMetaView
{
    /// <summary>
    /// Opens the missing cards of the first build label on the page that has any (the UI render check and the tests),
    /// as a press of that label does. Returns the flyout, null when no label has missing cards.
    /// </summary>
    public Flyout? OpenFirstDeckBuild()
    {
        var label = this.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("deckBuild") && b.IsEffectivelyVisible && b.DataContext is MetaDeckRow { Build.HasMissing: true });
        if (label?.Flyout is not Flyout flyout)
        {
            return null;
        }

        flyout.ShowAt(label);
        return flyout;
    }
}

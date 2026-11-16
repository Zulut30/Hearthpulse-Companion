using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Hearthpulse.UI.Main.Meta;

/// <summary>The missing cards behind a deck's build label: the label's flyout, and where focus goes with it.</summary>
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

    /// <summary>The missing cards opened: focus goes into them, so a screen reader reads them and Escape closes them.</summary>
    private void OnDeckBuildOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control details })
        {
            details.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>
    /// The missing cards closed: focus returns to the label they were opened from (the flyout does not put it back by
    /// itself). A click away closes them first and then gives focus to what was clicked.
    /// </summary>
    private void OnDeckBuildClosed(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target: Button { IsEffectivelyVisible: true } label })
        {
            label.Focus(NavigationMethod.Tab);
        }
    }
}

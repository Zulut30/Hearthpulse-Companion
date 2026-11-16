using Avalonia.Controls;

namespace Hearthpulse.UI.Main.Cards;

/// <summary>The card page of the main view (docs/card-page.md); its data is <see cref="CardPageViewModel"/>.</summary>
public partial class CardPageView : UserControl
{
    public CardPageView() => InitializeComponent();

    /// <summary>The sections, top to bottom, so the UI render check and the tests can find them.</summary>
    public IReadOnlyList<Control> Sections => [StatsCard, ArchetypesCard, MulliganCard, MineCard];
}

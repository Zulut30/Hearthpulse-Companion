using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main;

/// <summary>
/// The tabs inside the Battlegrounds and Standard/Wild pages: which is open, and what each one shows. A view that shares
/// one cell with the others cross-fades with them (the Battlegrounds tier tabs); a view stacked with the others reveals
/// itself as it comes, the others hidden at once (My stats and the tier lists through <c>Motion.RevealOnShow</c> in the
/// Battlegrounds page; My decks and the meta views here).
/// </summary>
public partial class MainView
{
    /// <summary>The Constructed tab shown last, to reveal only what a change of tab brings.</summary>
    private string? _constructedTabShown;

    /// <summary>The Battlegrounds tabs in <see cref="MainViewModel.BattlegroundsTabs"/> order.</summary>
    private ToggleButton[] BattlegroundsTabButtons => [PageBattlegrounds.TabMine, PageBattlegrounds.TabHeroes, PageBattlegrounds.TabComps, PageBattlegrounds.TabTrinkets, PageBattlegrounds.TabCards];

    /// <summary>The Constructed tabs in <see cref="MainViewModel.ConstructedTabs"/> order.</summary>
    private ToggleButton[] ConstructedTabButtons => [PageConstructed.TabMyDecks, PageConstructed.TabMetaTiers, PageConstructed.TabMetaDecks, PageConstructed.TabMetaMatchups];

    /// <summary>The open Battlegrounds tab ("Mine", "Heroes", "Comps", "Trinkets", "Cards"), kept when the window is rebuilt.</summary>
    public string BattlegroundsTab
    {
        get => MainViewModel.NavChecked(MainViewModel.BattlegroundsTabs, States(BattlegroundsTabButtons)) ?? MainViewModel.BattlegroundsTabs[0];
        set
        {
            BattlegroundsTabButtons[MainViewModel.NavIndex(MainViewModel.BattlegroundsTabs, value)].IsChecked = true;
            UpdateTabs();
        }
    }

    /// <summary>The open Constructed tab ("Decks", "Tiers", "Top", "Matchups"), kept when the window is rebuilt.</summary>
    public string ConstructedTab
    {
        get => MainViewModel.NavChecked(MainViewModel.ConstructedTabs, States(ConstructedTabButtons)) ?? MainViewModel.ConstructedTabs[0];
        set => ShowConstructedTab(value);
    }

    /// <summary>
    /// Opens a Battlegrounds tier list tab ("Heroes", "Comps", "Trinkets", "Cards") and scrolls the tabs to the top of the
    /// page (used by the UI render check).
    /// </summary>
    public void ShowBattlegroundsTab(string tab)
    {
        (tab switch
        {
            "Comps" => PageBattlegrounds.TabComps,
            "Trinkets" => PageBattlegrounds.TabTrinkets,
            "Cards" => PageBattlegrounds.TabCards,
            _ => PageBattlegrounds.TabHeroes,
        }).IsChecked = true;
        UpdateTabs();
        PageBattlegrounds.TabHeroes.BringIntoView(new Rect(0, 0, PageBattlegrounds.TabHeroes.Bounds.Width, 700));
    }

    private void UpdateTabs()
    {
        // My stats and the tier lists share the tab row: the period shows with My stats, the bracket with the tier lists.
        var mine = PageBattlegrounds.TabMine.IsChecked == true;
        PageBattlegrounds.BgStats.IsVisible = mine;
        PageBattlegrounds.BgPeriodBar.IsVisible = mine;
        PageBattlegrounds.BgTiers.IsVisible = !mine;
        PageBattlegrounds.BracketPicker.IsVisible = !mine;
        // The view model follows the tab: the MMR slices and "Solo / Duos" belong to some tabs only. A tier tab's card
        // is built here, on its first opening; My stats builds none.
        _viewModel?.BgTabShown(BattlegroundsTab);
        PageBattlegrounds.BgTiers.ShowTab(BattlegroundsTab);
    }

    /// <summary>Opens a Constructed tab: "Decks", "Tiers", "Top" or "Matchups" (used by the UI render check).</summary>
    public void ShowConstructedTab(string tab)
    {
        ConstructedTabButtons[MainViewModel.NavIndex(MainViewModel.ConstructedTabs, tab)].IsChecked = true;
        UpdateConstructedTabs();
    }

    private void UpdateConstructedTabs()
    {
        var mine = PageConstructed.TabMyDecks.IsChecked == true;
        PageConstructed.CtStats.IsVisible = mine;
        PageConstructed.DeckColumns.IsVisible = mine;
        PageConstructed.CtPeriodBar.IsVisible = mine;
        var tab = ConstructedTab;
        PageConstructed.CtMeta.ShowTab(tab == MainViewModel.ConstructedTabs[0] ? null : tab);
        var changed = _constructedTabShown is not null && tab != _constructedTabShown;
        _constructedTabShown = tab;
        if (!changed)
        {
            return;
        }

        // My decks and the meta views are stacked: the view that comes reveals itself, the others hidden at once (as the
        // WPF window does). A meta tab's panel is revealed by the meta view itself (ConstructedMetaView.ShowTab), so only
        // My decks' columns and stats are revealed here: one reveal per change, never two.
        if (tab != MainViewModel.ConstructedTabs[0])
        {
            return;
        }

        foreach (var view in new Control[] { PageConstructed.DeckColumns, PageConstructed.CtStats })
        {
            Motion.Reveal(view);
        }
    }
}

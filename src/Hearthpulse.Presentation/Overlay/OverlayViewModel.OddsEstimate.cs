using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The "odds" panel's estimate mark: the odds rest on values the player cannot see or the client has not given yet
/// (<see cref="CombatOdds.Estimated"/>), shown as a "≈" at the end of the share bar with a tooltip naming what was
/// estimated. Separate from the bottom line's unsimulated effects (<see cref="CombatOdds.Unsimulated"/>): those effects
/// did not run, these ran on an estimate. Both can show at once.
/// </summary>
public sealed partial class OverlayViewModel
{
    private bool _oddsEstimated;
    private string _oddsEstimatedTip = string.Empty;

    /// <summary>The odds rest on estimates (<see cref="CombatOdds.Estimated"/> &gt; 0): the "≈" mark at the share bar's end.</summary>
    public bool OddsEstimated { get => _oddsEstimated; private set => Set(ref _oddsEstimated, value); }

    /// <summary>
    /// The mark's tooltip: a line saying the odds include estimates, then what was estimated, by name only. Never the value
    /// used: during the game the player may know only that something of the opponent's was estimated.
    /// </summary>
    public string OddsEstimatedTip { get => _oddsEstimatedTip; private set => Set(ref _oddsEstimatedTip, value); }

    private void UpdateOddsEstimate(CombatOdds odds, IReadOnlyList<EstimatedItem> estimates)
    {
        OddsEstimated = odds.Estimated > 0;
        OddsEstimatedTip = OddsEstimated ? EstimatedTip(estimates) : string.Empty;
    }

    /// <summary>The header and one line per estimated input (two trinkets or both sides' lobby tribes make one line).</summary>
    private static string EstimatedTip(IReadOnlyList<EstimatedItem> estimates)
    {
        var names = estimates.Select(EstimateName).Distinct(StringComparer.Ordinal).Select(name => "• " + name);
        return string.Join('\n', names.Prepend(Loc.T("Overlay.Odds.EstimatedTip")));
    }

    private static string EstimateName(EstimatedItem item) => item.Input switch
    {
        SimInputs.DeityStats => Loc.T("Overlay.Odds.Estimated.Deity"),
        SimInputs.BeetleStats => Loc.T("Overlay.Odds.Estimated.Beetles"),
        SimInputs.BloodGemBonus => Loc.T("Overlay.Odds.Estimated.BloodGems"),
        SimInputs.TavernSpellBonus => Loc.T("Overlay.Odds.Estimated.TavernSpells"),
        SimInputs.TrinketValue => Loc.T("Overlay.Odds.Estimated.Trinket"),
        SimInputs.LobbyTribes => Loc.T("Overlay.Odds.Estimated.LobbyTribes"),
        SimInputs.DuosOpponentTeammate => Loc.T("Overlay.Odds.Estimated.DuosOpponentTeammate"),
        SimInputs.DuosPartnerBoard => Loc.T("Overlay.Odds.Estimated.DuosPartnerBoard"),
        _ => Loc.T("Overlay.Odds.Estimated.Other"),
    };
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The "odds" panel's mark for odds that are not exact, a "≈" at the end of the share bar: effects that did not run
/// (<see cref="CombatOdds.Unsimulated"/>), values the player cannot see or the client has not given yet that the fight
/// ran on an estimate of (<see cref="CombatOdds.Estimated"/>), or both. Its tooltip says which. The panel's bottom line
/// is left to the hero damage cap, and only while the damage shown reaches it (<see cref="CapReached"/>).
/// </summary>
public sealed partial class OverlayViewModel
{
    private bool _oddsEstimated;
    private string _oddsEstimatedTip = string.Empty;

    /// <summary>
    /// The odds are not exact: some effects were not simulated or some inputs were estimated. The "≈" mark at the share
    /// bar's end.
    /// </summary>
    public bool OddsEstimated { get => _oddsEstimated; private set => Set(ref _oddsEstimated, value); }

    /// <summary>
    /// The mark's tooltip: how many effects were not simulated, then a line saying the odds include estimates and what was
    /// estimated, by name only. Never the value used: during the game the player may know only that something of the
    /// opponent's was estimated.
    /// </summary>
    public string OddsEstimatedTip { get => _oddsEstimatedTip; private set => Set(ref _oddsEstimatedTip, value); }

    /// <summary>The hero damage cap cuts the damage range shown: its upper end (8 fights in 10) reaches the cap.</summary>
    internal static bool CapReached(CombatOdds odds) =>
        odds.DamageCap > 0 && (odds.DamageTaken?.High >= odds.DamageCap || odds.DamageDealt?.High >= odds.DamageCap);

    private void UpdateOddsEstimate(CombatOdds odds, IReadOnlyList<EstimatedItem> estimates)
    {
        OddsEstimated = odds.Estimated > 0 || odds.Approximate;
        OddsEstimatedTip = OddsEstimated ? EstimatedTip(odds, estimates) : string.Empty;
    }

    /// <summary>
    /// The effects not simulated, then the header and one line per estimated input (two trinkets or both sides' lobby
    /// tribes make one line).
    /// </summary>
    private static string EstimatedTip(CombatOdds odds, IReadOnlyList<EstimatedItem> estimates)
    {
        var lines = new List<string>();
        if (odds.Approximate)
        {
            lines.Add(Loc.P("Overlay.Odds.Unsimulated", odds.Unsimulated));
        }

        if (odds.Estimated > 0)
        {
            lines.Add(Loc.T("Overlay.Odds.EstimatedTip"));
            lines.AddRange(estimates.Select(EstimateName).Distinct(StringComparer.Ordinal).Select(name => "• " + name));
        }

        return string.Join('\n', lines);
    }

    private static string EstimateName(EstimatedItem item) => item.Input switch
    {
        SimInputs.DeityStats => Loc.T("Overlay.Odds.Estimated.Deity"),
        SimInputs.BeetleStats => Loc.T("Overlay.Odds.Estimated.Beetles"),
        SimInputs.BloodGemBonus => Loc.T("Overlay.Odds.Estimated.BloodGems"),
        SimInputs.TavernSpellBonus => Loc.T("Overlay.Odds.Estimated.TavernSpells"),
        SimInputs.TrinketValue => Loc.T("Overlay.Odds.Estimated.Trinket"),
        SimInputs.LobbyTribes => Loc.T("Overlay.Odds.Estimated.LobbyTribes"),
        // A second board never seen is not in the odds at all: the line says so, not "last seen".
        SimInputs.DuosOpponentTeammate => Loc.T(SimInputs.IsUnseenBoard(item)
            ? "Overlay.Odds.Estimated.DuosOpponentTeammateUnseen" : "Overlay.Odds.Estimated.DuosOpponentTeammate"),
        SimInputs.DuosPartnerBoard => Loc.T(SimInputs.IsUnseenBoard(item)
            ? "Overlay.Odds.Estimated.DuosPartnerBoardUnseen" : "Overlay.Odds.Estimated.DuosPartnerBoard"),
        SimInputs.OpponentSecrets => Loc.T("Overlay.Odds.Estimated.OpponentSecrets"),
        SimInputs.ProgressLeft => Loc.T("Overlay.Odds.Estimated.ProgressLeft"),
        SimInputs.HeroPowerUsed => Loc.T("Overlay.Odds.Estimated.HeroPowerUsed"),
        _ => Loc.T("Overlay.Odds.Estimated.Other"),
    };
}

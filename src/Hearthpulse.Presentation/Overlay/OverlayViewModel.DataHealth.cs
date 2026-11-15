using Hearthpulse.Core.Api;

#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Overlay;
#else
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The date under the overlay's numbers from outside the machine: the Arena draft panel and the Battlegrounds hero and
/// trinket picks. The hosts hand in the dates of the stats they loaded and the data summary; a late source adds
/// "update delayed" to its line. The numbers themselves are never hidden.
/// </summary>
public sealed partial class OverlayViewModel
{
    private DataHealthReport _dataHealth = DataHealthReport.None;
    private DateTime? _bgDataDate;
    private DateTime? _arenaDataDate;

    /// <summary>"Hearthpulse data · 15 Nov" under the hero pick badges; empty while no badge has numbers.</summary>
    public string BgHeroDataNote => HeroPicks.Count > 0 ? DataHealthText.Overlay(_bgDataDate, _dataHealth, DataSection.BgHeroes) : string.Empty;

    /// <summary>
    /// The same line under the trinket pick badges. Their numbers are the stats pool's, which the data summary does not
    /// list: the line carries the date of the loaded stats and never a delay mark.
    /// </summary>
    public string BgTrinketDataNote => TrinketPicks.Count > 0 ? DataHealthText.Overlay(_bgDataDate, _dataHealth) : string.Empty;

    /// <summary>The same line at the bottom of the Arena draft panel; empty while the panel has no scores.</summary>
    public string ArenaDataNote => ArenaNoData.Length == 0 ? DataHealthText.Overlay(_arenaDataDate, _dataHealth, DataSection.Arena) : string.Empty;

    /// <summary>When the Battlegrounds stats the session advises from were collected (<c>BattlegroundsMeta.Updated</c>).</summary>
    public void SetBgDataDate(DateTime? date)
    {
        _bgDataDate = date;
        RaiseDataNotes();
    }

    /// <summary>When the Arena scores were collected (<c>ArenaMeta.FetchedAt</c>).</summary>
    public void SetArenaDataDate(DateTime? date)
    {
        _arenaDataDate = date;
        RaiseDataNotes();
    }

    /// <summary>The data summary the host read.</summary>
    public void SetDataHealth(DataHealthReport report)
    {
        _dataHealth = report;
        RaiseDataNotes();
    }

    private void RaiseDataNotes()
    {
        Raise(nameof(BgHeroDataNote));
        Raise(nameof(BgTrinketDataNote));
        Raise(nameof(ArenaDataNote));
    }
}

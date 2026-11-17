using Hearthpulse.Core.Api;

#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Overlay;
#else
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The date under the overlay's numbers from outside the machine, one per element and in one wording
/// (<see cref="DataHealthText.Overlay"/>, "data from 15 Nov"): a line under the Battlegrounds hero pick badges and at
/// the bottom of the Arena draft panel, from the dates the hosts hand in; each trinket badge carries its own date
/// (<c>TrinketMeta.Updated</c>, built with the badges). A late source adds "update delayed" to its line; the narrow
/// trinket badge puts it on a warning line of its own (<c>TrinketBadge.Delay</c>). The numbers themselves are never hidden.
/// </summary>
public sealed partial class OverlayViewModel
{
    private DataHealthReport _dataHealth = DataHealthReport.None;
    private DateTime? _bgDataDate;
    private bool _bgDataExternal;
    private DateTime? _arenaDataDate;

    /// <summary>
    /// "data from 15 Nov" under the hero pick badges (the hero numbers and the comps named on the badges); empty while
    /// no badge has numbers.
    /// </summary>
    public string BgHeroDataNote => HeroPicks.Count > 0
        ? DataHealthText.Overlay(_bgDataDate, _dataHealth, _bgDataExternal ? [DataSection.BgHeroes, DataSection.BgHeroComps] : [])
        : string.Empty;

    /// <summary>The same line at the bottom of the Arena draft panel; empty while the panel has no scores.</summary>
    public string ArenaDataNote => ArenaNoData.Length == 0 ? DataHealthText.Overlay(_arenaDataDate, _dataHealth, DataSection.Arena) : string.Empty;

    /// <summary>
    /// The Battlegrounds stats the session advises from: when they were collected (<c>BattlegroundsMeta.Updated</c>)
    /// and whether the data API is among their sources. Stats that are the stats pool's alone get the date and never
    /// the delay mark: the summary's collectors are not theirs (the same rule as a trinket badge's <c>External</c>).
    /// </summary>
    public void SetBgData(DateTime? date, bool external)
    {
        _bgDataDate = date;
        _bgDataExternal = external;
        RaiseDataNotes();
    }

    /// <summary><see cref="SetBgData(DateTime?, bool)"/> from the loaded stats: external when their source names the data API.</summary>
    public void SetBgData(Hearthpulse.Core.Battlegrounds.BattlegroundsMeta meta) =>
        SetBgData(meta.Updated, meta.Source.Contains(Hearthpulse.Core.Battlegrounds.BattlegroundsMeta.Attribution, StringComparison.Ordinal));

    /// <summary>When the Arena scores were collected (<c>ArenaMeta.FetchedAt</c>).</summary>
    public void SetArenaDataDate(DateTime? date)
    {
        _arenaDataDate = date;
        RaiseDataNotes();
    }

    /// <summary>
    /// The data summary the host read. The lines here follow at once; the trinket badges' dates on the next
    /// <c>Update</c> (the hosts push the last snapshot again after this call).
    /// </summary>
    public void SetDataHealth(DataHealthReport report)
    {
        if (!report.Supersedes(_dataHealth))
        {
            return; // an older load's answer after a newer one
        }

        _dataHealth = report;
        RaiseDataNotes();
    }

    private void RaiseDataNotes()
    {
        Raise(nameof(BgHeroDataNote));
        Raise(nameof(ArenaDataNote));
    }
}

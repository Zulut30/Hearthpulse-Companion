using Hearthpulse.Core.Api;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Main;
#else
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main;
#endif

/// <summary>One line of the Settings → Data "Data status" card: a part of the app, its data's date and state.</summary>
/// <param name="Attention">The state is late or failed: the state text takes the warning colour.</param>
public sealed record DataHealthRow(string Name, string Date, string State, bool Attention);

/// <summary>
/// How current the numbers from the data API are (<see cref="DataHealthReport"/>, read by the host): the "Data status"
/// card of Settings → Data, and the "update is delayed" line of a page whose source is late. Nothing is hidden: the
/// numbers stay, the line says how old they are.
/// </summary>
public sealed partial class MainViewModel
{
    private DataHealthReport _dataHealth = DataHealthReport.None;
    private bool _dataHealthLoaded;
    private bool _dataHealthChecking;
    private IReadOnlyList<DataHealthRow>? _dataHealthRows;
    private RelayCommand? _checkDataHealth;

    /// <summary>The app's sections that have a source in the summary, in the card's order.</summary>
    public IReadOnlyList<DataHealthRow> DataHealthRows
    {
        get => _dataHealthRows ??= BuildDataHealthRows();
        private set => Set(ref _dataHealthRows, value);
    }

    /// <summary>Under the card's rows: when the summary was read, or that the server did not answer.</summary>
    public string DataHealthNote =>
        _dataHealthChecking || !_dataHealthLoaded ? Loc.T("Source.Health.Checking")
        : _dataHealth.Offline ? _dataHealth.CheckedAt is { } last ? Loc.F("Source.Health.Offline", last.ToLocalTime()) : Loc.T("Source.Health.OfflineNever")
        : _dataHealth.CheckedAt is { } at ? Loc.F("Source.Health.Checked", at.ToLocalTime())
        : string.Empty;

    /// <summary>A check the player asked for is on its way.</summary>
    public bool DataHealthChecking
    {
        get => _dataHealthChecking;
        private set
        {
            if (Set(ref _dataHealthChecking, value))
            {
                Raise(nameof(DataHealthNote));
            }
        }
    }

    /// <summary>"Check now": asks the host for a summary past the 30-minute cache; a second click while one runs does nothing.</summary>
    public RelayCommand CheckDataHealth => _checkDataHealth ??= new RelayCommand(() =>
    {
        if (DataHealthChecking)
        {
            return;
        }

        DataHealthChecking = true;
        DataHealthCheckRequested?.Invoke();
    });

    /// <summary>The player pressed "Check now": the host reads the summary again and calls <see cref="SetDataHealth"/>.</summary>
    public event Action? DataHealthCheckRequested;

    /// <summary>The Constructed meta page (tier list, top decks, matchups).</summary>
    public string MetaDataDelay => DataHealthText.Delay(_dataHealth, DataSection.ConstructedMeta, DataSection.ConstructedMatchups);

    /// <summary>The Arena page.</summary>
    public string ArenaDataDelay => DataDelay(DataSection.Arena);

    /// <summary>The Brawl page.</summary>
    public string BrawlDataDelay => DataDelay(DataSection.Brawl);

    /// <summary>"Hearthpulse data · updated 15 Nov 20:00" under the Arena page's community numbers; empty without numbers.</summary>
    public string ArenaDataDate => _arenaMeta?.FetchedAt is { } at && at != default ? Loc.F("Source.Health.Updated", at.ToLocalTime()) : string.Empty;

    /// <summary>The summary the host read (on the UI thread): the card and every page's delay line follow it.</summary>
    public void SetDataHealth(DataHealthReport report)
    {
        if (!report.Supersedes(_dataHealth))
        {
            // An older load's answer that arrived after a newer one: what is shown already knows more.
            return;
        }

        var bgBefore = BgDelays();
        _dataHealth = report;
        _dataHealthLoaded = true;
        DataHealthChecking = false;
        RaiseDataHealth();
        if (!bgBefore.SequenceEqual(BgDelays(), StringComparer.Ordinal))
        {
            // The Battlegrounds tabs carry the line inside their source text, built with the rows.
            RebuildBgTabs();
        }
    }

    /// <summary>The delay line of one section; empty while it is fresh, waiting for its origin, or unknown.</summary>
    internal string DataDelay(DataSection section) => DataHealthText.Delay(_dataHealth, section);

    /// <summary>A source line with the delay line under it.</summary>
    internal static string WithDelay(string line, string delay) =>
        delay.Length == 0 ? line : line.Length == 0 ? delay : line + "\n" + delay;

    /// <summary>The card's texts and the pages' lines again: after a new summary or a language switch.</summary>
    private void RaiseDataHealth()
    {
        DataHealthRows = BuildDataHealthRows();
        Raise(nameof(DataHealthNote));
        Raise(nameof(MetaDataDelay));
        Raise(nameof(ConstructedMetaStatus));
        Raise(nameof(ArenaDataDelay));
        Raise(nameof(ArenaDataDate));
        Raise(nameof(BrawlDataDelay));
        Raise(nameof(BrawlSource));
    }

    private List<DataHealthRow> BuildDataHealthRows() => DataSections.All
        .Where(s => DataSections.Sources(s).Count > 0)
        .Select(_dataHealth.Section)
        .Select(s => new DataHealthRow(
            Loc.T("Source.Health.Section." + s.Section),
            s.DataDate is { } date ? Loc.F("Source.Health.Date", date.ToLocalTime()) : "—",
            DataHealthText.State(s.State),
            s.Delayed))
        .ToList();

    private string[] BgDelays() =>
        [DataDelay(DataSection.BgHeroes), DataDelay(DataSection.BgMinions), DataDelay(DataSection.BgTrinkets), DataDelay(DataSection.BgComps)];
}

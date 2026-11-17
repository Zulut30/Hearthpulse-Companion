using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.SourceCapture;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

public sealed partial class MainViewModel
{
    private SourceCaptureState _sourceCaptureState = new("disabled", 0, 0, 0);
    private GameStatsState _gameStatsState = new(0, 0, 0);

    /// <summary>Deletion requests kept for a pool address other than the current one (game records and journals).</summary>
    private int HeldDeletions => _sourceCaptureState.HeldDeletions + _gameStatsState.HeldDeletions;

    /// <summary>
    /// The sending state under the game records switch. "Kept for another pool address" is a state of its own, apart
    /// from off, offline and unavailable: that data is not sent to the current pool and waits for its own
    /// (docs/game-stats.md, "Queue destination").
    /// </summary>
    public string SourceCaptureStatusText => _sourceCaptureState.PendingDeletions > 0
        ? Loc.T("Settings.Data.SourceDeleting")
        : HeldDeletions > 0 ? Loc.F("Settings.Data.HeldDeletions", HeldDeletions)
        : Settings.GameStatsOff ? Loc.T("Settings.Data.SourceOff")
        : _gameStatsState.HeldRecords > 0 || _sourceCaptureState.HeldCaptures > 0 || _sourceCaptureState.Status == "other_pool"
            ? Loc.F("Settings.Data.HeldData", _gameStatsState.HeldRecords, _sourceCaptureState.HeldCaptures + (_sourceCaptureState.Status == "other_pool" ? _sourceCaptureState.PendingCaptures : 0))
        : _sourceCaptureState.Dropped > 0 ? Loc.F("Settings.Data.SourceDropped", _sourceCaptureState.Dropped)
        : _sourceCaptureState.Status is "unavailable" or "disk_error" or "rejected" or "corrupt_local" or "invalid_receipt" ? Loc.T("Settings.Data.SourceUnavailable")
        : _sourceCaptureState.PendingCaptures > 0 ? Loc.F("Settings.Data.SourcePending", _sourceCaptureState.PendingCaptures)
        : Loc.T("Settings.Data.SourceReady");

    public void UpdateSourceCaptureState(SourceCaptureState state)
    {
        // Reports are numbered by the host: one that was overtaken on its way here does not replace a newer one.
        if (state.Revision < _sourceCaptureState.Revision)
        {
            return;
        }

        _sourceCaptureState = state;
        Raise(nameof(SourceCaptureStatusText));
    }

    /// <summary>The game record queues changed: what waits for the current pool and what is kept for others.</summary>
    public void UpdateGameStatsState(GameStatsState state)
    {
        if (state.Revision < _gameStatsState.Revision)
        {
            return;
        }

        _gameStatsState = state;
        Raise(nameof(SourceCaptureStatusText));
    }

    /// <summary>
    /// Settings → Data: send every finished game (all modes) as a game record for Hearthpulse's statistics
    /// (docs/game-stats.md). On by default; turning it off (<see cref="Core.AppSettings.GameStatsOff"/>) asks the pool
    /// to delete what this PC sent.
    /// </summary>
    public bool GameStatsSharing
    {
        get => !Settings.GameStatsOff;
        set
        {
            if (GameStatsSharing == value)
            {
                return;
            }

            // The install's secret is made by the first record sent while this is on, never here: off never makes one.
            Settings.GameStatsOff = !value;
            Raise();
            Raise(nameof(SourceCaptureStatusText));
            GameStatsChanged?.Invoke(value);
        }
    }

    /// <summary>The switch shows only once the stats pool is set up.</summary>
    public bool GameStatsAvailable => PoolClient.Available(Settings);

    /// <summary>The game records switch changed (true = on): the host saves the settings and tells the sender.</summary>
    public event Action<bool>? GameStatsChanged;
}

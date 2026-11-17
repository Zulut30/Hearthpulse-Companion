#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
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
        : !SourceCaptureClient.NoticeSeen(Settings) ? Loc.T("Settings.Data.SourceNotice")
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

            Settings.GameStatsOff = !value;
            // Turned on in Settings: the player has seen what it sends there.
            Settings.GameStatsNoticeShown = true;
            if (value)
            {
                // Game records and journals only: the shared games keep waiting for the notice itself.
                Settings.GameStatsNoticeVersion = PoolClient.NoticeVersion;
                Settings.SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion;
                GameStatsClient.InstallId(Settings);
            }

            Raise();
            Raise(nameof(ShowGameStatsNotice));
            Raise(nameof(SourceCaptureStatusText));
            GameStatsChanged?.Invoke(value);
        }
    }

    /// <summary>The switch shows only once the stats pool is set up.</summary>
    public bool GameStatsAvailable => PoolClient.Available(Settings);

    /// <summary>
    /// The one-time notice in the main window: game records and the stats pool's shared games are on by default, what
    /// they hold and how to turn them off (not a consent question). Nothing is sent before it was shown. It shows
    /// again for whichever of its texts the player has not seen and has not turned off: game records, source
    /// journals (<see cref="SourceCaptureClient.NoticeVersion"/>), shared games (<see cref="PoolClient.NoticeVersion"/>).
    /// A flag or version left by an earlier notice does not stand in for the current one: the notice shows while any
    /// flow the player has not turned off still lacks it (docs/game-stats.md, "Notice contract").
    /// </summary>
    public bool ShowGameStatsNotice => GameStatsAvailable && (!Settings.GameStatsNoticeShown
        || !Settings.GameStatsOff && !SourceCaptureClient.NoticeSeen(Settings)
        || Settings.PoolConsent != false && Settings.PoolNoticeVersion < PoolClient.NoticeVersion);

    private RelayCommand? _answerGameStats;
    private bool _gameStatsNoticeDetails;

    /// <summary>
    /// The notice's full text (what is sent, what never, how long it is kept, what turning off deletes) is shown under
    /// its short summary. Collapsed by default so the notice stays a few lines above the page; opening it is not an answer.
    /// </summary>
    public bool GameStatsNoticeDetails
    {
        get => _gameStatsNoticeDetails;
        private set => Set(ref _gameStatsNoticeDetails, value);
    }

    /// <summary>
    /// The notice's buttons: "ok" keeps sending, "off" turns off both game records and the stats pool's shared games,
    /// "more" opens the privacy policy, "details" shows or hides the full text. Only "ok" and "off" answer the notice;
    /// neither "ok" nor a newer notice turns back on what the player turned off.
    /// </summary>
    public RelayCommand AnswerGameStats => _answerGameStats ??= new RelayCommand(p =>
    {
        if (p as string == "more")
        {
            OpenPrivacy.Execute(null);
            return;
        }

        if (p as string == "details")
        {
            GameStatsNoticeDetails = !GameStatsNoticeDetails;
            return;
        }

        Settings.GameStatsNoticeShown = true;
        Settings.GameStatsNoticeVersion = PoolClient.NoticeVersion;
        Settings.SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion;
        Settings.PoolNoticeVersion = PoolClient.NoticeVersion;
        if (p as string == "off")
        {
            Settings.GameStatsOff = true;
            Settings.PoolConsent = false;
        }
        else
        {
            // The install id comes from the pool secret: made now, saved with this answer.
            GameStatsClient.InstallId(Settings);
        }

        Raise(nameof(ShowGameStatsNotice));
        Raise(nameof(GameStatsSharing));
        Raise(nameof(SourceCaptureStatusText));
        Raise(nameof(PoolSharing));
        GameStatsChanged?.Invoke(GameStatsSharing);
        PoolSharingChanged?.Invoke();
    });

    /// <summary>The game records switch changed (true = on): the host saves the settings and tells the sender.</summary>
    public event Action<bool>? GameStatsChanged;
}

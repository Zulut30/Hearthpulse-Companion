#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core.Pool;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>The stats pool's consent question: the first start banner and the Settings → Data switch.</summary>
public sealed partial class MainViewModel
{
    private bool _poolAvailable;

    /// <summary>The stats pool is set up; the first start banner and the Settings switch only show then.</summary>
    public bool PoolAvailable
    {
        get => _poolAvailable;
        set
        {
            if (Set(ref _poolAvailable, value))
            {
                Raise(nameof(AskPoolConsent));
                Raise(nameof(ShowGameStatsNotice));
            }
        }
    }

    /// <summary>The player has not said yet whether to share their Battlegrounds results.</summary>
    public bool AskPoolConsent => _poolAvailable
        && (Settings.PoolConsent is null || (Settings.PoolConsent == true && Settings.PoolConsentVersion < PoolClient.ConsentVersion));

    public bool PoolSharing
    {
        get => Settings.PoolConsent == true;
        set
        {
            Settings.PoolConsent = value;
            Settings.PoolConsentVersion = PoolClient.ConsentVersion;
            Raise();
            Raise(nameof(AskPoolConsent));
            PoolConsentChanged?.Invoke();
        }
    }

    /// <summary>The banner's buttons: "yes" shares results, anything else declines.</summary>
    public RelayCommand AnswerPool { get; }

    /// <summary>The player answered the question or moved the switch: the host saves the settings and shares the earlier games.</summary>
    public event Action? PoolConsentChanged;
}

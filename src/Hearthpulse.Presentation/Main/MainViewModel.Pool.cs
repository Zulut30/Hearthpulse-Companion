using Hearthpulse.Core.Pool;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// Sharing games with the stats pool: on by default, told in the one-time notice (<see cref="ShowGameStatsNotice"/>),
/// turned off by the Settings → Data switch or the notice's "Turn off".
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The stats pool is set up; the Settings switch only shows then.</summary>
    public bool PoolAvailable => PoolClient.Available(Settings);

    /// <summary>
    /// The Settings → Data switch. Off is the player's refusal (<see cref="Core.AppSettings.PoolConsent"/> false): no
    /// later game and no further batch of earlier games is sent. On shares again, once the notice was shown.
    /// </summary>
    public bool PoolSharing
    {
        get => Settings.PoolConsent != false;
        set
        {
            if (PoolSharing == value)
            {
                return;
            }

            Settings.PoolConsent = value;
            Raise();
            Raise(nameof(ShowGameStatsNotice));
            PoolSharingChanged?.Invoke();
        }
    }

    /// <summary>
    /// The switch moved or the notice was answered: the host saves the settings and, when sharing is on, shares the
    /// earlier games.
    /// </summary>
    public event Action? PoolSharingChanged;
}

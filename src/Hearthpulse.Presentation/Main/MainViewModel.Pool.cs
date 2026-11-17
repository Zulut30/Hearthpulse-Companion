using Hearthpulse.Core.Pool;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// Sharing games with the stats pool: on by default, turned off by the Settings → Data switch (an explicit refusal,
/// always kept).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The stats pool is set up; the Settings switch only shows then.</summary>
    public bool PoolAvailable => PoolClient.Available(Settings);

    /// <summary>
    /// The Settings → Data switch. Off is the player's refusal (<see cref="Core.AppSettings.PoolConsent"/> false): no
    /// later game and no further batch of earlier games is sent. On shares again.
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
            PoolSharingChanged?.Invoke();
        }
    }

    /// <summary>
    /// The switch moved: the host saves the settings and, when sharing is on, shares the earlier games.
    /// </summary>
    public event Action? PoolSharingChanged;
}

#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The "Read data from the game client" switch (Settings → Data, <see cref="AppSettings.ReadGameMemory"/>,
/// docs/adr/0006-client-read-only.md) and what follows from it on the pages that show values read from the client: with
/// reading off, the collection, the achievements and the Battlegrounds rating say so and where to turn it back on,
/// instead of waiting for a read that will not come.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The switch, on by default; the host applies a change (<see cref="GameMemoryChanged"/>).</summary>
    public bool ReadGameMemory
    {
        get => Settings.ReadGameMemory;
        set
        {
            if (Settings.ReadGameMemory == value)
            {
                return;
            }

            Settings.ReadGameMemory = value;
            Raise();
            Raise(nameof(GameMemoryOff));
            Raise(nameof(GameMemorySwitchShown));
            Raise(nameof(BgRatingEmpty));
            Raise(nameof(BgRatingOffShown));
            RaiseCollectionStatus();
            RaiseAchievementsStatus();
            RebuildBattlegroundsStats();
            // The home page's rating tile.
            RefreshAsync().Forget("History refresh");
            GameMemoryChanged?.Invoke();
        }
    }

    /// <summary>Reading the client is turned off: the pages that need it say so.</summary>
    public bool GameMemoryOff => !Settings.ReadGameMemory;

    /// <summary>
    /// The switch is offered where the client can be read at all (Windows), and anywhere it is off, so that it can be
    /// turned back on where the pages send the player.
    /// </summary>
    public bool GameMemorySwitchShown => HearthstoneMemory.IsSupported || GameMemoryOff;

    /// <summary>The switch changed: the host tells the session at once and saves the settings.</summary>
    public event Action? GameMemoryChanged;

    /// <summary>The rating chart's empty state: play rated games, or (reading the client off) why no rating comes.</summary>
    public string BgRatingEmpty => Loc.T(GameMemoryOff ? "Stats.Bg.RatingOff" : "Stats.Bg.RatingEmpty");

    /// <summary>The chart has the rating of earlier games while reading the client is off: a line says new games add none.</summary>
    public bool BgRatingOffShown => GameMemoryOff && _bgRatingChart.Count > 0;
}

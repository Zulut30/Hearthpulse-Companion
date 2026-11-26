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
/// reading off, the collection, the achievements, the Battlegrounds rating and the Constructed rank say so and where to
/// turn it back on, instead of waiting for a read that will not come.
/// <para>
/// A rating or rank block keeps one reason line while reading is off: <see cref="RatingOffReason"/> and
/// <see cref="RankOffReason"/> (the coordinator's rule for blocks hidden for having no rating: with reading off they stay,
/// with this line alone).
/// </para>
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
            Raise(nameof(RatingOffReason));
            Raise(nameof(RankOffReason));
            Raise(nameof(BgRatingEmpty));
            Raise(nameof(BgRatingOffShown));
            Raise(nameof(HomeRatingOffShown));
            Raise(nameof(CtRankEmpty));
            Raise(nameof(CtRankOffShown));
            RaiseCollectionStatus();
            RaiseAchievementsStatus();
            RebuildBattlegroundsStats();
            RebuildConstructedStats();
            ShowSoloRatingTile();
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

    /// <summary>Why a Battlegrounds rating block has no new rating, with the way back; empty while reading is on.</summary>
    public string RatingOffReason => GameMemoryOff ? Loc.T("Stats.Bg.RatingOff") : string.Empty;

    /// <summary>Why a Constructed rank block has no new rank, with the way back; empty while reading is on.</summary>
    public string RankOffReason => GameMemoryOff ? Loc.T("Stats.Ct.RankOff") : string.Empty;

    /// <summary>The empty rating charts (home, My stats): play rated games, or with reading off <see cref="RatingOffReason"/>.</summary>
    public string BgRatingEmpty => GameMemoryOff ? RatingOffReason : Loc.T("Stats.Bg.RatingEmpty");

    /// <summary>My stats' MMR chart has earlier games' points while reading is off: <see cref="RatingOffReason"/> stands over them.</summary>
    public bool BgRatingOffShown => GameMemoryOff && _bgRatingChart.Count > 0;

    /// <summary>Home's rating chart draws earlier games while reading is off (two points or more): the reason stands over it.</summary>
    public bool HomeRatingOffShown => GameMemoryOff && _ratingPoints.Count >= 2;

    /// <summary>The empty rank chart: play ranked games, or with reading off <see cref="RankOffReason"/>.</summary>
    public string CtRankEmpty => GameMemoryOff ? RankOffReason : Loc.T("Stats.Ct.RankEmpty");

    /// <summary>The rank chart has earlier games' points while reading is off: <see cref="RankOffReason"/> stands over them.</summary>
    public bool CtRankOffShown => GameMemoryOff && _ctRankChart.Count > 0;

    /// <summary>Home's solo rating tile: the session's change, or with reading off that the rating is not read.</summary>
    private StatTile SoloRatingTile() => GameMemoryOff
        ? new StatTile(Loc.T("Main.Tile.SoloRating"), SoloRating, Loc.T("Main.Tile.ReadingOff"))
        : new StatTile(Loc.T("Main.Tile.SoloRating"), SoloRating, SessionDelta, SessionDeltaValue);

    /// <summary>The switch moved: home's first tile follows in place, without reading the history again.</summary>
    private void ShowSoloRatingTile()
    {
        if (OverviewTiles.Count > 0)
        {
            OverviewTiles = [SoloRatingTile(), .. OverviewTiles.Skip(1)];
        }
    }
}

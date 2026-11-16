using System.Globalization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// Labels beside the Battlegrounds leaderboard tiles, in words: every opponent's "Tavern 5" and, once a fight showed
/// their board and a tribe has two minions on it, "Quilboar: 4 of 7". They come from <see cref="LobbyRows"/>, so they
/// say exactly what the lobby panels say; the view places them with <see cref="ArrangeLeaderboard"/> on the calibrated
/// tiles. The player's own tile and the players who are out have none. While the mouse is on any tile the labels hide:
/// the game draws its own popup right of the leaderboard, where they sit.
/// </summary>
public sealed partial class OverlayViewModel
{
    private bool _showBgBadges;
    private SyncList<BgBadge> _bgBadges = new();
    private bool _bgBadgesOn;
    private double _surfaceWidth;
    private double _surfaceHeight;
    private AppSettings? _leaderboardSettings;
    private BadgeInputs _badgeInputs;
    private bool _leaderboardHovered;
    private bool _bgBadgesPlaced;
    private int _trinketBadges;

    public bool ShowBgBadges { get => _showBgBadges; private set => Set(ref _showBgBadges, value); }

    /// <summary>
    /// Changed in place (<see cref="Sync{T}"/>): a fight or a tavern turn changes one or two badges, and a new list made
    /// the overlay build all seven again (audit, stage 10).
    /// </summary>
    public IReadOnlyList<BgBadge> BgBadges => _bgBadges;

    /// <summary>
    /// The view laid the surface out at <paramref name="surfaceWidth"/> x <paramref name="surfaceHeight"/> overlay units
    /// with the leaderboard calibration in <paramref name="settings"/>. Called every frame; the badges move only when
    /// the size, the calibration or the mode changed.
    /// </summary>
    public void ArrangeLeaderboard(double surfaceWidth, double surfaceHeight, AppSettings settings)
    {
        _surfaceWidth = surfaceWidth;
        _surfaceHeight = surfaceHeight;
        _leaderboardSettings = settings;
        RebuildBgBadges();
    }

    private void UpdateBgBadges(BattlegroundsView? view, AppSettings settings)
    {
        _bgBadgesOn = view is { IsHeroSelection: false } && settings.ShowBgLeaderboardBadges;
        _trinketBadges = ShowTrinkets ? TrinketPicks.Count : 0;
        RebuildBgBadges();
    }

    private void RebuildBgBadges()
    {
        // Off (outside Battlegrounds, the hero pick, the setting) with nothing shown: every frame ends here.
        if (!_bgBadgesOn && _bgBadges.Count == 0)
        {
            return;
        }

        // Unchanged size, calibration, mode and lobby rows (their fingerprint): the badges stay as they are.
        var inputs = new BadgeInputs(_surfaceWidth, _surfaceHeight, _leaderboardSettings?.LeaderboardTop, _leaderboardSettings?.LeaderboardSlotHeight,
            _leaderboardSettings?.LeaderboardWidth, IsDuos, _bgBadgesOn, _fingerprints.GetValueOrDefault(nameof(LobbyRows)), Loc.Instance.Language,
            _trinketBadges, _trinketBadges > 0 && _leaderboardSettings is { } placed ? TrinketSpot(placed) : default);
        if (inputs == _badgeInputs)
        {
            return;
        }

        _badgeInputs = inputs;
        PlaceBgBadges();
    }

    /// <summary>Builds the badges; kept out of <see cref="RebuildBgBadges"/> so its closure is not allocated every frame.</summary>
    private void PlaceBgBadges()
    {
        var badges = new List<BgBadge>();
        if (_bgBadgesOn && _leaderboardSettings is { } settings && _surfaceHeight > 0)
        {
            var left = LeaderboardGeometry.Beside(_surfaceWidth, _surfaceHeight, settings);
            // A trinket choice puts its badges over the offered trinkets, the leftmost of them where the labels are.
            var trinkets = _trinketBadges > 0
                ? PickRowLayout.Trinkets.OnSurface(_trinketBadges, _surfaceWidth, _surfaceHeight, SurfaceScale(settings), settings.Panels.GetValueOrDefault("trinket-row"))
                : [];
            foreach (var row in LobbyRows)
            {
                // No tier yet (not seen in a live game) would be an empty label: none until there is one.
                if (row.You || row.Dead || row.Place is < 1 or > 8 || row.Tier < 1)
                {
                    continue;
                }

                var (top, height) = LeaderboardGeometry.Tile(row.Place - 1, _surfaceHeight, settings, IsDuos);
                if (LabelMeetsBadges(left, top, height, trinkets))
                {
                    continue;
                }

                var tribe = row is { Tribe: { } t, TribeCount: >= 2 }
                    ? Loc.F("Overlay.LeaderboardBadge.TribeCount", t.Plural, row.TribeCount, row.BoardCount)
                    : string.Empty;
                badges.Add(new BgBadge(row.Place, left, top, height, row.Tier, tribe));
            }
        }

        _bgBadgesPlaced = badges.Count > 0;
        ShowOrHideBgBadges();
        Replace(nameof(BgBadges), badges, Print, () => Sync(ref _bgBadges, nameof(BgBadges), badges));

        static string Print(BgBadge b) => string.Create(CultureInfo.InvariantCulture,
            $"{b.Place}:{b.Left:0.#}:{b.Top:0.#}:{b.Height:0.#}:{b.Tier}:{b.Tribe}");
    }

    /// <summary>The mouse is on a leaderboard tile (any, the player's own too) or left it: the labels hide or come back.</summary>
    private void HideBgBadgesWhileHovered(bool hovered)
    {
        _leaderboardHovered = hovered;
        ShowOrHideBgBadges();
    }

    private void ShowOrHideBgBadges() => ShowBgBadges = _bgBadgesPlaced && !_leaderboardHovered;

    /// <summary>The most a label is wide, in overlay units: the view holds it to this, and a longer tribe line wraps.</summary>
    public const double BgBadgeLabelWidth = 132;

    /// <summary>The most a label is high, in overlay units (the tier line and a tribe line that wrapped once); never more than its tile's strip.</summary>
    public const double BgBadgeLabelHeight = 60;

    /// <summary>The room kept between a label and a trinket badge, in overlay units.</summary>
    public const double BgBadgeClearance = 6;

    /// <summary>
    /// The room a label can take beside its tile, in overlay units: from the strip's left edge
    /// <see cref="BgBadgeLabelWidth"/> wide, centred on the tile and at most <see cref="BgBadgeLabelHeight"/> high.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) LabelRoom(double left, double top, double height)
    {
        var high = Math.Min(height, BgBadgeLabelHeight);
        var y = top + ((height - high) / 2);
        return (left, y, left + BgBadgeLabelWidth, y + high);
    }

    /// <summary>
    /// True when a label beside the tile strip would run into one of the trinket badges (their rectangles on the
    /// surface, <see cref="PickRowLayout.OnSurface"/>) or come closer than <see cref="BgBadgeClearance"/>: that label
    /// gives way for the time of the choice, the others stay.
    /// </summary>
    public static bool LabelMeetsBadges(double left, double top, double height, IReadOnlyList<(double Left, double Top, double Right, double Bottom)> badges)
    {
        var room = LabelRoom(left, top, height);
        foreach (var badge in badges)
        {
            if (room.Left < badge.Right + BgBadgeClearance && badge.Left - BgBadgeClearance < room.Right
                && room.Top < badge.Bottom + BgBadgeClearance && badge.Top - BgBadgeClearance < room.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Everything the badges are built from; <c>Rows</c> is the fingerprint of <see cref="LobbyRows"/>, <c>Language</c> the words'.</summary>
    private readonly record struct BadgeInputs(double Width, double Height, double? Top, double? Slot, double? TileWidth, bool Duos, bool On, string? Rows, string Language,
        int Trinkets, (double Scale, double X, double Y, double Size) TrinketSpot);

    /// <summary>What decides where the trinket badges are: the overlay scale and the panel's saved spot (NaN: its default one).</summary>
    private static (double Scale, double X, double Y, double Size) TrinketSpot(AppSettings settings) =>
        settings.Panels.GetValueOrDefault("trinket-row") is { } spot
            ? (SurfaceScale(settings), spot.X, spot.Y, spot.Scale)
            : (SurfaceScale(settings), double.NaN, double.NaN, 1);
}

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The rows of pick badges that stand over the game's own cards: one badge above each offered hero or trinket. The
/// game draws those cards a fixed step apart whatever the player's overlay scale is, so the step of the badges is kept
/// in screen units (it undoes the overlay scale), and a badge never grows wider than its step: past that scale it
/// stops growing instead of running into its neighbour or off the screen. The same numbers in both overlays.
/// </summary>
/// <param name="Pitch">The step between two cards of the game, in units of a 1080-high screen.</param>
/// <param name="Card">The badge's width at overlay scale 1, in the same units.</param>
/// <param name="Slots">The most cards the game offers in the row: the width of the row's placeholder.</param>
/// <param name="Shift">How far right of the screen's centre the row's centre is, in units of a 1080-high screen.</param>
/// <param name="Top">The row's top at its default spot, in units of a 1080-high screen.</param>
/// <param name="Height">
/// The most a badge takes from the row's top down, in the badge's own units (its top margin and every line it can
/// have); the layout tests hold the real badges to it.
/// </param>
public sealed record PickRowLayout(double Pitch, double Card, int Slots, double Shift, double Top, double Height)
{
    /// <summary>The least room left between two neighbouring badges, in the badge's own units.</summary>
    public const double Gap = 12;

    /// <summary>Hero pick: HDT's hero step, a 232-wide badge, four heroes at most, the row's centre 7 right of the screen's.</summary>
    public static PickRowLayout Heroes { get; } = new(340, 232, 4, 7, 212, 104);

    /// <summary>Trinket pick: HDT's trinket step, a 196-wide badge, four trinkets at most, the row's centre 10 right of the screen's.</summary>
    public static PickRowLayout Trinkets { get; } = new(277, 196, 4, 10, 226, 88);

    /// <summary>
    /// What the row is scaled by on top of the overlay scale: 1 while a badge with its <see cref="Gap"/> fits the step,
    /// less past that, so the badge on screen is never wider than its step.
    /// </summary>
    public double Zoom(double overlayScale) => overlayScale > 0 ? Math.Min(1, Pitch / ((Card + Gap) * overlayScale)) : 1;

    /// <summary>The width of one badge's slot in the row's own units (inside <see cref="Zoom"/>): one step of the game on screen.</summary>
    public double Slot(double overlayScale) => overlayScale > 0 ? Pitch / (overlayScale * Zoom(overlayScale)) : Pitch;

    /// <summary>The width of the whole row with every slot taken, in surface units: the panel's least width.</summary>
    public double RowWidth(double overlayScale) => overlayScale > 0 ? Slots * Pitch / overlayScale : Slots * Pitch;

    /// <summary>
    /// Where the badges of <paramref name="count"/> offered cards stand on a screen <paramref name="screenWidth"/> x
    /// <paramref name="screenHeight"/> pixels at the default spot: each badge's edges and the centre of its slot, in
    /// pixels. The row is centred (the game centres two cards as it does four).
    /// </summary>
    public IReadOnlyList<(double Left, double Right, double Centre)> OnScreen(int count, double screenWidth, double screenHeight, double overlayScale)
    {
        var unit = screenHeight / 1080;
        var badge = Card * Zoom(overlayScale) * overlayScale * unit;
        var first = (screenWidth / 2) + (Shift * unit) - ((count - 1) * Pitch * unit / 2);
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            var centre = first + (i * Pitch * unit);
            return (centre - (badge / 2), centre + (badge / 2), centre);
        })];
    }

    /// <summary>
    /// The badges of <paramref name="count"/> offered cards as rectangles on the surface, in surface units: at the
    /// row's default spot, or where the player's saved <paramref name="placement"/> puts the panel (its top-left corner
    /// as fractions of the surface, with the panel's own scale).
    /// </summary>
    public IReadOnlyList<(double Left, double Top, double Right, double Bottom)> OnSurface(
        int count, double surfaceWidth, double surfaceHeight, double overlayScale, Hearthpulse.Core.PanelPlacement? placement = null)
    {
        var scale = overlayScale > 0 ? overlayScale : 1;
        var panel = placement is null ? 1 : PanelLayout.PlacedScale(placement);
        var pitch = Pitch / scale * panel;
        var card = Card * Zoom(scale) * panel;
        var centre = placement is null ? (surfaceWidth / 2) + (Shift / scale) : (placement.X * surfaceWidth) + (RowWidth(scale) * panel / 2);
        var top = placement is null ? Top / scale : placement.Y * surfaceHeight;
        var bottom = top + (Height * Zoom(scale) * panel);
        var first = centre - ((count - 1) * pitch / 2);
        return [.. Enumerable.Range(0, count).Select(i => (first + (i * pitch) - (card / 2), top, first + (i * pitch) + (card / 2), bottom))];
    }
}

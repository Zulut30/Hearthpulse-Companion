#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Infrastructure;
#else
namespace Hearthpulse.UI.Infrastructure;
#endif

/// <summary>
/// How card art comes in, the same in both UIs. Art the memory cache holds is drawn at once, so a list seen before
/// never flickers and a recycled row never shows its old card. Art that arrives after the element asked for it fades
/// in over <see cref="Motion.Fast"/> (opacity only: the image element's, or the art brush's on a border), on screen
/// and with motion on. Windows drawn over the game (the overlay: a transparent window, where every frame of motion
/// costs a full-surface copy) take their art without motion. The hover preview waits <see cref="PreviewWait"/> for
/// its card, so it never opens as an empty frame; the page's hero waits <see cref="HeroWait"/> before the old hero
/// gives way to the stone, and cross-fades to the new one over <see cref="Motion.Enter"/>.
/// </summary>
public static class ArtMotion
{
    /// <summary>
    /// How long the hover preview waits for its card's art before it shows with whatever is there: a render on disk
    /// decodes well within it, so the frame opens with the card, and a hover still reads as immediate.
    /// </summary>
    public static readonly TimeSpan PreviewWait = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How long a page's hero waits for its art before the old hero fades to the stone: about as long as a page takes
    /// to lay out for its first opening, so the cross-fade usually goes straight from the old hero to the new one.
    /// </summary>
    public static readonly TimeSpan HeroWait = TimeSpan.FromMilliseconds(250);
}

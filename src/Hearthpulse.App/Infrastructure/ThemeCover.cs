using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// A picture of a window laid over it while its theme changes: an adorner, so it never takes the mouse and changes no
/// layout. It fades out over <see cref="Motion.Enter"/> once the new look is on screen and then lets the picture go. One
/// per element: a new picture takes the place of the one before, wherever its fade was.
/// </summary>
internal sealed class ThemeCover : Adorner
{
    private readonly BitmapSource _picture;
    private readonly Point _origin;

    private ThemeCover(UIElement element, BitmapSource picture, Point origin)
        : base(element)
    {
        _picture = picture;
        _origin = origin;
        IsHitTestVisible = false;
        // The picture is drawn at its own pixels; a rounding of the window's place never blurs it.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>The cover over <paramref name="element"/>, or null.</summary>
    public static ThemeCover? On(UIElement element) =>
        AdornerLayer.GetAdornerLayer(element)?.GetAdorners(element)?.OfType<ThemeCover>().FirstOrDefault();

    /// <summary>
    /// Lays <paramref name="picture"/> over <paramref name="element"/>, its top left corner at <paramref name="origin"/>
    /// (in the element's coordinates); false when the element has no adorner layer to hold it.
    /// </summary>
    public static bool Lay(UIElement element, BitmapSource picture, Point origin)
    {
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer)
        {
            return false;
        }

        Remove(element, layer);
        layer.Add(new ThemeCover(element, picture, origin));
        return true;
    }

    /// <summary>Takes the cover off <paramref name="element"/>: faded out over <see cref="Motion.Enter"/>, or at once.</summary>
    public static void Lift(UIElement element, bool fade)
    {
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer || On(element) is not { } cover)
        {
            return;
        }

        if (!fade)
        {
            Remove(element, layer);
            return;
        }

        var animation = new DoubleAnimation(1, 0, Motion.Enter) { EasingFunction = Motion.Ease };
        animation.Completed += (_, _) =>
        {
            // A newer picture may have taken its place meanwhile; that one stays.
            if (ReferenceEquals(VisualTreeHelper.GetParent(cover), layer))
            {
                layer.Remove(cover);
            }
        };
        cover.BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawImage(_picture, new Rect(_origin, new Size(_picture.Width, _picture.Height)));

    private static void Remove(UIElement element, AdornerLayer layer)
    {
        foreach (var cover in layer.GetAdorners(element)?.OfType<ThemeCover>().ToList() ?? [])
        {
            cover.BeginAnimation(OpacityProperty, null);
            layer.Remove(cover);
        }
    }
}

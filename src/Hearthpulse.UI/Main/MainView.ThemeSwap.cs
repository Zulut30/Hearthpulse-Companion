using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Hearthpulse.Core;
using Hearthpulse.UI.Infrastructure;

namespace Hearthpulse.UI.Main;

/// <summary>
/// The theme cross-fade: the theme's brushes change in place, so without it every colour would snap in one frame. A
/// picture of the view is laid over it (an adorner: it never takes the pointer) before the brushes change and fades out
/// over <see cref="Motion.Enter"/> once the new look is drawn. With motion off there is no picture and the change is
/// instant. A change during the fade pictures what is on the screen, the fading picture included, and takes its place.
/// </summary>
public partial class MainView
{
    private Image? _themeCover;
    private RenderTargetBitmap? _themeCoverPicture;
    private CancellationTokenSource? _themeFade;

    /// <summary>True while a picture of the old theme lies over the view.</summary>
    internal bool ThemeCovered => _themeCover is not null;

    /// <summary>Lays a picture of the view as the player sees it over it; nothing (and no old picture) with motion off.</summary>
    private void CoverForTheme()
    {
        if (!Motion.On || TopLevel.GetTopLevel(this) is not { } top || Bounds.Width < 1 || Bounds.Height < 1)
        {
            DropThemeCover();
            return;
        }

        // A theme tile was rebuilt for the new selection: it is laid out before it is pictured.
        UpdateLayout();
        var scaling = top.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
        var picture = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        try
        {
            picture.Render(this);
            if (_themeCover is { } fading && _themeCoverPicture is { } before)
            {
                using var context = picture.CreateDrawingContext(false);
                using (context.PushOpacity(fading.Opacity))
                {
                    context.DrawImage(before, new Rect(Bounds.Size));
                }
            }
        }
        catch
        {
            picture.Dispose();
            throw;
        }

        DropThemeCover();
        _themeCoverPicture = picture;
        _themeCover = new Image { Source = picture, Stretch = Stretch.Fill, IsHitTestVisible = false };
        AdornerLayer.SetAdorner(this, _themeCover);
    }

    /// <summary>Fades the picture out from the frame after the first one drawn in the new theme.</summary>
    private void FadeThemeCover()
    {
        if (_themeCover is not { } cover || TopLevel.GetTopLevel(this) is not { } top)
        {
            DropThemeCover();
            return;
        }

        top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => StartThemeFade(cover)));
    }

    private void StartThemeFade(Image cover)
    {
        if (!ReferenceEquals(cover, _themeCover))
        {
            return;
        }

        var fade = new CancellationTokenSource();
        _themeFade = fade;
        var animation = new Animation
        {
            Duration = Motion.Enter,
            Easing = new CubicEaseOut(),
            // Kept at the end until the picture is taken away, so it never shows again for a frame.
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        FadeAsync(animation, cover, fade.Token).Forget("Theme cross-fade");
    }

    private async Task FadeAsync(Animation animation, Image cover, CancellationToken token)
    {
        await animation.RunAsync(cover, token);
        if (!token.IsCancellationRequested && ReferenceEquals(cover, _themeCover))
        {
            DropThemeCover();
        }
    }

    /// <summary>Takes the picture away at once and lets it go.</summary>
    private void DropThemeCover()
    {
        _themeFade?.Cancel();
        _themeFade?.Dispose();
        _themeFade = null;
        if (_themeCover is { } cover)
        {
            AdornerLayer.SetAdorner(this, null);
            cover.Source = null;
            _themeCover = null;
        }

        _themeCoverPicture?.Dispose();
        _themeCoverPicture = null;
    }
}

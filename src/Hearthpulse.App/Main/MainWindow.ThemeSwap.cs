using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hearthpulse.App.Infrastructure;

namespace Hearthpulse.App.Main;

/// <summary>
/// The window rebuilt for a new theme without a seam (the host's <c>App.OnThemeChanged</c>): the old window's picture,
/// scrolled lists and focused control are taken; the new window opens on them maximized or not from the start, off the
/// screen (DWM cloak) until its first frames are drawn; then it takes the old one's place with Windows' open and close
/// animations off, and the picture fades out over it.
/// </summary>
public partial class MainWindow
{
    /// <summary>The control that last had the keyboard here: a theme tile the player clicks is rebuilt (and loses focus) before the host hears of it.</summary>
    private VisualAddress? _focusAddress;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _focusAddress = VisualAddress.Of(Frame, e.NewFocus as DependencyObject);
    }

    /// <summary>
    /// What the player sees of this window: its picture (when <paramref name="picture"/>, for the fade), its scrolled
    /// lists and the control <paramref name="focused"/> (else the last one that had the keyboard here).
    /// </summary>
    internal MainWindowView TakeView(bool picture, IInputElement? focused)
    {
        var scroll = VisualAddress.ScrollMarks(Frame);
        var focus = VisualAddress.Of(Frame, focused as DependencyObject) ?? _focusAddress;
        return new MainWindowView(picture ? Picture() : null, scroll, focus, IsActive);
    }

    /// <summary>Lays <paramref name="picture"/> (of this window or the one it replaces) over the window, at its corner.</summary>
    internal void Cover(BitmapSource picture) => ThemeCover.Lay(Frame, picture, TranslatePoint(default, Frame));

    /// <summary>Takes the picture off: faded out over Motion.Enter, or at once.</summary>
    internal void Uncover(bool fade) => ThemeCover.Lift(Frame, fade);

    /// <summary>True while a picture lies over the window.</summary>
    internal bool Covered => ThemeCover.On(Frame) is not null;

    /// <summary>Scrolls the lists of this window as far as they were in <paramref name="view"/> (applied by the next layout).</summary>
    internal void ScrollAs(MainWindowView view) => VisualAddress.Scroll(Frame, view.Scroll);

    /// <summary>The twin of the control focused in <paramref name="view"/>, when it can take the keyboard.</summary>
    internal UIElement? FocusTarget(MainWindowView view) =>
        view.Focus?.Find(Frame) is UIElement { Focusable: true, IsEnabled: true } target ? target : null;

    /// <summary>This window is leaving: Windows closes (or hides) it without its fade.</summary>
    internal void LeaveWithoutAnimation()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            Native.NoTransitions(handle);
        }
    }

    /// <summary>
    /// Shows this new window in place of the one <paramref name="view"/> was taken from, off the screen until
    /// <see cref="RevealSwapped"/>: maximized from the start (no normal-size frame first), scrolled where the old one was,
    /// under its picture. The caller has placed it (<see cref="MoveTo"/>).
    /// </summary>
    internal void ShowSwapped(bool maximized, MainWindowView view)
    {
        if (maximized)
        {
            WindowState = WindowState.Maximized;
        }

        var handle = new WindowInteropHelper(this).EnsureHandle();
        Native.Cloak(handle, true);
        Native.NoTransitions(handle);
        // Not activated while off the screen; WPF always activates a window shown maximized (and refuses otherwise).
        ShowActivated = maximized;
        Show();
        ShowActivated = true;
        if (maximized)
        {
            // The caption glyph and the maximized inset follow StateChanged; shown maximized, Windows may not report a change.
            OnStateChanged(EventArgs.Empty);
        }

        UpdateLayout();
        ScrollAs(view);
        UpdateLayout();
        if (view.Picture is { } picture)
        {
            Cover(picture);
        }
    }

    /// <summary>
    /// Puts the window on the screen (its first frames are drawn), active and focused as the old one was. When this
    /// returns the screen shows it, so the old window can go without a gap.
    /// </summary>
    internal void RevealSwapped(MainWindowView view)
    {
        // One more composed frame first: the window's own frames are surely on its surface when it comes on the screen.
        Native.DwmFlush();
        Native.Cloak(new WindowInteropHelper(this).Handle, false);
        if (view.WasActive)
        {
            Activate();
            if (FocusTarget(view) is { } target)
            {
                // The control was on screen: focusing it must not scroll the page under the picture.
                var stay = new RequestBringIntoViewEventHandler(StayInPlace);
                target.AddHandler(RequestBringIntoViewEvent, stay);
                target.Focus();
                target.RemoveHandler(RequestBringIntoViewEvent, stay);
            }
        }

        Native.DwmFlush();
    }

    private static void StayInPlace(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

    /// <summary>
    /// The window as the screen shows it, at its pixels: the frame Windows composed last (exact, and five or six times
    /// quicker than drawing the window again: 45 against 290 ms for 1260×840 at 175 %), else drawn again.
    /// </summary>
    private BitmapSource? Picture()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        BitmapSource picture;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && Native.WindowPixels(handle) is { } shot)
        {
            picture = BitmapSource.Create(shot.Width, shot.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Bgr32, null, shot.Pixels, shot.Width * 4);
        }
        else
        {
            UpdateLayout();
            if (ActualWidth < 1 || ActualHeight < 1)
            {
                return null;
            }

            var drawn = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            drawn.Render(this);
            picture = drawn;
        }

        picture.Freeze();
        return picture;
    }
}

/// <summary>
/// What the player saw of a main window when the theme changed: its picture (null with motion off), its scrolled lists,
/// the control that had the keyboard, and whether the window was the active one.
/// </summary>
internal sealed record MainWindowView(BitmapSource? Picture, IReadOnlyList<ScrollMark> Scroll, VisualAddress? Focus, bool WasActive);

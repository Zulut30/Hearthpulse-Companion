using Hearthpulse.Core.Hearthstone;
using System.Runtime.InteropServices;

namespace Hearthpulse.App.Infrastructure;

internal static partial class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint SWP_NOACTIVATE = 0x10;
    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>Parent of a message-only window: never shown, still gets posted messages such as WM_HOTKEY.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    public record struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Waits for the next frame Windows composes: what was changed before it is on screen after it.</summary>
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmFlush();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    /// <summary>
    /// The window's own last frame as Windows composed it (PrintWindow with PW_RENDERFULLCONTENT reads its surface, not
    /// the screen, so nothing over it is taken): top-down 32-bit BGR rows of its whole rectangle; null when it could not
    /// be read (an all-black result counts as unread).
    /// </summary>
    public static (byte[] Pixels, int Width, int Height)? WindowPixels(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        try
        {
            var previous = SelectObject(dc, bitmap);
            var printed = PrintWindow(hwnd, dc, 2);
            SelectObject(dc, previous);
            var header = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = rect.Width, Height = -rect.Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[rect.Width * rect.Height * 4];
            if (!printed || GetDIBits(dc, bitmap, 0, (uint)rect.Height, pixels, ref header, 0) != rect.Height)
            {
                return null;
            }

            for (var i = 0; i < pixels.Length; i += 4 * 997)
            {
                if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0)
                {
                    return (pixels, rect.Width, rect.Height);
                }
            }

            return null;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    /// <summary>No zoom or fade from Windows when the window is shown, hidden or closed (DWMWA_TRANSITIONS_FORCEDISABLED).</summary>
    public static void NoTransitions(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, 3, ref on, sizeof(int));
    }

    /// <summary>
    /// Takes the window off the screen, or puts it back (DWMWA_CLOAK): a cloaked window is still drawn and composed, so it
    /// can be shown, laid out and painted before anyone sees it.
    /// </summary>
    public static void Cloak(IntPtr hwnd, bool cloaked)
    {
        var on = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 13, ref on, sizeof(int));
    }

    public static void SetExStyle(IntPtr hwnd, int add, int remove)
    {
        var style = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var updated = (style | add) & ~remove;
        if (updated != style)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, updated);
        }
    }

    /// <summary>Hearthstone's client area in physical screen pixels, or null when not visible.</summary>
    public static RECT? ClientRectOnScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || IsIconic(hwnd) || !GetClientRect(hwnd, out var client))
        {
            return null;
        }

        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin) || client.Width < 200 || client.Height < 200)
        {
            return null;
        }

        return new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
    }

    public static IntPtr FindHearthstoneWindow() => GameProcess.MainWindow();

    /// <summary>Dark title bar and rounded corners on Windows 11.</summary>
    public static void UseDarkChrome(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }
}

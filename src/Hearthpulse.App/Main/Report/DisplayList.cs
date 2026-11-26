using System.Runtime.InteropServices;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core.Diagnostics;
using Forms = System.Windows.Forms;

namespace Hearthpulse.App.Main.Report;

/// <summary>
/// The monitors for the diagnostics archive (roadmap E1): Windows' screens in pixels, each with its own scale (the
/// per-monitor DPI an overlay misplacement usually comes down to). The Avalonia window reads <c>Screens</c> instead.
/// </summary>
internal static partial class DisplayList
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<DisplayInfo> All() =>
        [.. Forms.Screen.AllScreens.Select(s => new DisplayInfo(s.Bounds.Width, s.Bounds.Height, Scale(s.Bounds), s.Primary))];

    /// <summary>The scale of the monitor under the screen's centre (1.5 = 150 %); 0 when Windows does not say.</summary>
    private static double Scale(System.Drawing.Rectangle bounds)
    {
        try
        {
            var centre = new Native.POINT { X = bounds.Left + (bounds.Width / 2), Y = bounds.Top + (bounds.Height / 2) };
            var monitor = MonitorFromPoint(centre, MonitorDefaultToNearest);
            return monitor != 0 && GetDpiForMonitor(monitor, EffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(Native.POINT point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}

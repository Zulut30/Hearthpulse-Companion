using Hearthpulse.Core.Api;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Infrastructure;
#else
namespace Hearthpulse.UI.Infrastructure;
#endif

/// <summary>The words for the data summary (<see cref="DataHealthReport"/>), shared by the main window and the overlay.</summary>
public static class DataHealthText
{
    /// <summary>
    /// "Data from 12 Nov 03:35: the update is delayed." when one of <paramref name="sections"/> is late (the oldest
    /// one's date), else empty. Offline and "waiting for the origin" give no line.
    /// </summary>
    public static string Delay(DataHealthReport report, params DataSection[] sections)
    {
        var late = sections.Select(report.Section).Where(s => s.Delayed).ToList();
        if (late.Count == 0)
        {
            return string.Empty;
        }

        return late.Where(s => s.DataDate is not null).Select(s => s.DataDate).Min() is { } date
            ? Loc.F("Source.Health.Delay", date.ToLocalTime())
            : Loc.T("Source.Health.DelayNoDate");
    }

    /// <summary>The state in words: "Up to date", "Update delayed", ...</summary>
    public static string State(DataFreshness state) => Loc.T("Source.Health.State." + state);

    /// <summary>
    /// The overlay's one line under numbers from the data API: "Hearthpulse data · 15 Nov", with "update delayed" when
    /// one of <paramref name="sections"/> is late. Empty without a date.
    /// </summary>
    public static string Overlay(DateTime? dataDate, DataHealthReport report, params DataSection[] sections)
    {
        if (dataDate is not { } date || date == default)
        {
            return string.Empty;
        }

        var late = sections.Any(s => report.Section(s).Delayed);
        return Loc.F(late ? "Source.Health.OverlayDelayed" : "Source.Health.Overlay", date.ToLocalTime());
    }
}

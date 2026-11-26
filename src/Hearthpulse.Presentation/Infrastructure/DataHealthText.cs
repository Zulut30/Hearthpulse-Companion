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
    /// The overlay's one line under numbers from the data API while one of <paramref name="sections"/> is late: "data
    /// from 15 Nov · update delayed". Empty while they are fresh (a date on screen all game changed nothing) and without
    /// a date.
    /// </summary>
    public static string Overlay(DateTime? dataDate, DataHealthReport report, params DataSection[] sections) =>
        dataDate is { } date && date != default && sections.Any(s => report.Section(s).Delayed)
            ? Loc.F("Source.Health.OverlayDelayed", date.ToLocalTime())
            : string.Empty;

    /// <summary>
    /// The overlay's date alone, "data from 15 Nov", for a narrow badge that puts the delay on a line of its own
    /// (<see cref="OverlayDelay"/>): the two in one line are cut at the badge's width. Empty without a date.
    /// </summary>
    public static string OverlayDate(DateTime? dataDate) =>
        dataDate is { } date && date != default ? Loc.F("Source.Health.Overlay", date.ToLocalTime()) : string.Empty;

    /// <summary>
    /// "update delayed" for that line when the numbers have a date and one of <paramref name="sections"/> is late, else
    /// empty: the same condition as the delayed form of <see cref="Overlay"/>.
    /// </summary>
    public static string OverlayDelay(DateTime? dataDate, DataHealthReport report, params DataSection[] sections) =>
        dataDate is { } date && date != default && sections.Any(s => report.Section(s).Delayed)
            ? Loc.T("Source.Health.OverlayDelay")
            : string.Empty;
}

namespace Hearthpulse.Core;

/// <summary>A read settings file made safe to use (explicit JSON nulls, numbers out of range) and old files brought up to date.</summary>
public sealed partial class AppSettings
{
    // Nullable annotations do not reject explicit JSON nulls. Repair them before migrations and UI bindings use
    // these values; keep valid choices and dictionary keys, including panels/counters from a newer version.
    private void Normalize()
    {
        Locale = DefaultIfBlank(Locale, "auto");
        Language = DefaultIfBlank(Language, Localization.Loc.Auto);
        SettingsTab = DefaultIfBlank(SettingsTab, "General");
        MetaFormat = DefaultIfBlank(MetaFormat, "standard");
        MetaRank = DefaultIfBlank(MetaRank, "all");
        MulliganRank = DefaultIfBlank(MulliganRank, "legend");
        MetaPeriod = DefaultIfBlank(MetaPeriod, "past_week");
        Theme = DefaultIfBlank(Theme, "Tavern");
        if (!StreamBackgrounds.All.Contains(StreamWindowBackground, StringComparer.Ordinal)) StreamWindowBackground = StreamBackgrounds.Theme;
        CounterModes ??= new(StringComparer.Ordinal);
        Panels ??= new(StringComparer.Ordinal);
        PanelLooks ??= new(StringComparer.Ordinal);
        StreamWindows ??= new(StringComparer.Ordinal);
        if (CounterModes.Count != 0 || Panels.Count != 0 || PanelLooks.Count != 0 || StreamWindows.Count != 0)
        {
            NormalizeOverrides();
        }

        if (!Enum.IsDefined(PickHints)) PickHints = PickHints.StatsOnly;
        if (!Enum.IsDefined(CardPoolHover)) CardPoolHover = CardPoolHover.Ctrl;
        TwitchDelaySeconds = Math.Clamp(TwitchDelaySeconds, 0, MaxTwitchDelaySeconds);
        OverlayScale = Bounded(OverlayScale, MinScale, MaxScale, 1);
        OverlayOpacity = Bounded(OverlayOpacity, 0.3, 1, 1);
        CardPoolBrowserWidth = Bounded(CardPoolBrowserWidth, MinCardPoolBrowserWidth, MaxCardPoolBrowserWidth, DefaultCardPoolBrowserWidth);
        LadderSessionBreakHours = Bounded(LadderSessionBreakHours, 1, 12, 2);
        LeaderboardTop = Bounded(LeaderboardTop, 0, 1, DefaultLeaderboardTop);
        LeaderboardSlotHeight = Bounded(LeaderboardSlotHeight, 0.01, 1, DefaultLeaderboardSlotHeight);
        LeaderboardWidth = Bounded(LeaderboardWidth, 0.01, 1, DefaultLeaderboardWidth);
        ThemeAccentHue = double.IsFinite(ThemeAccentHue) ? ThemeAccentHue : 42;
        ThemeBaseHue = double.IsFinite(ThemeBaseHue) ? ThemeBaseHue : 28;
        ThemeBaseTint = Bounded(ThemeBaseTint, 0, 1.5, 1);
        NormalizeAppearance();
        NormalizeGrowth();
    }

    // Dictionary.Remove preserves active enumerators on .NET. Repair entries in place.
    private void NormalizeOverrides()
    {
        foreach (var (key, mode) in CounterModes)
        {
            if (mode is null) CounterModes.Remove(key);
        }

        foreach (var (key, panel) in Panels)
        {
            if (panel is null || !double.IsFinite(panel.X) || !double.IsFinite(panel.Y) || !double.IsFinite(panel.Scale))
            {
                Panels.Remove(key);
                continue;
            }

            panel.X = Math.Clamp(panel.X, 0, 1);
            panel.Y = Math.Clamp(panel.Y, 0, 1);
            panel.Scale = ClampScale(panel.Scale);
        }

        foreach (var (key, look) in PanelLooks)
        {
            if (look is null || look.IsDefault)
            {
                PanelLooks.Remove(key);
                continue;
            }

            look.Opacity = Bounded(look.Opacity, PanelLook.MinOpacity, 1, 1);
        }

        foreach (var (key, window) in StreamWindows)
        {
            if (window is null || !double.IsFinite(window.X) || !double.IsFinite(window.Y) || !double.IsFinite(window.Width) || !double.IsFinite(window.Height)
                || window.Width <= 0 || window.Height <= 0)
            {
                StreamWindows.Remove(key);
                continue;
            }

            // Far outside any desktop is no place either: the window then opens at its default spot (StreamWindowLayout).
            window.X = Math.Clamp(window.X, -StreamWindowPlacement.MaxCoordinate, StreamWindowPlacement.MaxCoordinate);
            window.Y = Math.Clamp(window.Y, -StreamWindowPlacement.MaxCoordinate, StreamWindowPlacement.MaxCoordinate);
            window.Width = Math.Min(window.Width, StreamWindowPlacement.MaxSize);
            window.Height = Math.Min(window.Height, StreamWindowPlacement.MaxSize);
        }
    }

    private static string DefaultIfBlank(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static double Bounded(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private void Migrate()
    {
        // One switch used to turn every pick hint on or off: off stays off, on becomes the "stats only" default.
        if (ShowHeroPick is { } showPicks)
        {
            if (!showPicks)
            {
                PickHints = PickHints.Off;
            }

            ShowHeroPick = null;
        }

        // Version 1 measured X from the window edge, which misses the leaderboard on wide screens.
        if (LeaderboardGeometryVersion < CurrentLeaderboardGeometry)
        {
            LeaderboardTop = DefaultLeaderboardTop;
            LeaderboardSlotHeight = DefaultLeaderboardSlotHeight;
            LeaderboardWidth = DefaultLeaderboardWidth;
            LeaderboardGeometryVersion = CurrentLeaderboardGeometry;
        }

        // Version 1: panels that repeat what the game already shows went off by default; older files still hold "on".
        if (OverlayDefaultsVersion < CurrentOverlayDefaults)
        {
            ShowTurnTimer = false;
            ShowGameToasts = false;
            ShowLobbyPanel = false;
            OverlayDefaultsVersion = CurrentOverlayDefaults;
        }

        // The mulligan strip under the cards became bars over them ("mulligan-bars"): its old spot does not fit them, and
        // a placement always pins a position, so its scale cannot move over alone. The stale entry goes.
        Panels.Remove("mulligan-strip");
    }
}

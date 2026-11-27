using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Main.SettingsTabs;

/// <summary>
/// Settings, Tracker (labelled Counters): the in-game counters and the mulligan guide (its data:
/// <see cref="MainViewModel"/>); the deck tracker's switches are on the Overlay tab.
/// </summary>
public partial class TrackerSettings : UserControl
{
    /// <summary>Whether the overlay switch (the Overlay tab's) is on: the "overlay is off" banner shows while it is not.</summary>
    public static readonly StyledProperty<bool> OverlayOnProperty = AvaloniaProperty.Register<TrackerSettings, bool>(nameof(OverlayOn), true);

    public TrackerSettings() => InitializeComponent();

    /// <summary>The view binds it to the Overlay tab's switch.</summary>
    public bool OverlayOn
    {
        get => GetValue(OverlayOnProperty);
        set => SetValue(OverlayOnProperty, value);
    }
}

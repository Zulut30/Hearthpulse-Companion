using Hearthpulse.Core;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>The hero and trinket badge rows kept on the game's own cards at any overlay scale (<see cref="PickRowLayout"/>).</summary>
public sealed partial class OverlayViewModel
{
    private double _heroRowWidth = PickRowLayout.Heroes.RowWidth(1);
    private double _heroPickSlot = PickRowLayout.Heroes.Slot(1);
    private double _heroPickZoom = 1;
    private double _trinketRowWidth = PickRowLayout.Trinkets.RowWidth(1);
    private double _trinketPickSlot = PickRowLayout.Trinkets.Slot(1);
    private double _trinketPickZoom = 1;

    /// <summary>The least width of the hero row's panel, in surface units: four heroes of the game.</summary>
    public double HeroRowWidth { get => _heroRowWidth; private set => Set(ref _heroRowWidth, value); }

    /// <summary>The width of one hero badge's slot, in the row's own units.</summary>
    public double HeroPickSlot { get => _heroPickSlot; private set => Set(ref _heroPickSlot, value); }

    /// <summary>What the hero badges are scaled by on top of the overlay scale (1, or less once a badge would pass its hero's step).</summary>
    public double HeroPickZoom { get => _heroPickZoom; private set => Set(ref _heroPickZoom, value); }

    /// <summary>The least width of the trinket row's panel, in surface units: four trinkets of the game.</summary>
    public double TrinketRowWidth { get => _trinketRowWidth; private set => Set(ref _trinketRowWidth, value); }

    /// <summary>The width of one trinket badge's slot, in the row's own units.</summary>
    public double TrinketPickSlot { get => _trinketPickSlot; private set => Set(ref _trinketPickSlot, value); }

    /// <summary>What the trinket badges are scaled by on top of the overlay scale.</summary>
    public double TrinketPickZoom { get => _trinketPickZoom; private set => Set(ref _trinketPickZoom, value); }

    private void UpdatePickRows(AppSettings settings)
    {
        var scale = SurfaceScale(settings);
        HeroRowWidth = PickRowLayout.Heroes.RowWidth(scale);
        HeroPickSlot = PickRowLayout.Heroes.Slot(scale);
        HeroPickZoom = PickRowLayout.Heroes.Zoom(scale);
        TrinketRowWidth = PickRowLayout.Trinkets.RowWidth(scale);
        TrinketPickSlot = PickRowLayout.Trinkets.Slot(scale);
        TrinketPickZoom = PickRowLayout.Trinkets.Zoom(scale);
    }
}

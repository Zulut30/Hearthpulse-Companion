using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.UI.Main.SettingsTabs;

/// <summary>
/// Settings, Overlay: what is drawn over the game, its size, panel layout (its data: <see cref="MainViewModel"/>). What
/// only the main window does (saving and applying settings, opening folders, the overlay) the window keeps: this tab's
/// buttons raise events it handles.
/// </summary>
public partial class OverlaySettings : UserControl
{
    public OverlaySettings()
    {
        InitializeComponent();
        SizeSlider.ValueChanged += (_, _) => OnLookChanged();
        OpacitySlider.ValueChanged += (_, _) => OnLookChanged();
        // Shown again (the tab, the page): a panel's rows may have changed on the overlay since, and the theme with it.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                OnLookChanged();
            }
        };
        DataContextChanged += (_, _) => OnLookChanged();
        // The look block: the preview beside the controls while both fit, under them when not (the minimum window, compact)
        LookGrid.SizeChanged += (_, e) => PlaceLook(IsNarrowLook(e.NewSize.Width));
    }

    /// <summary>
    /// Narrow: the preview's columns close (fixed widths would keep the grid 296 wide however narrow the page is) and the
    /// styles of <c>lookNarrow</c> span the controls over the width and put the preview under them.
    /// </summary>
    private void PlaceLook(bool narrow)
    {
        LookGrid.Classes.Set("lookNarrow", narrow);
        LookGrid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 24);
        LookGrid.ColumnDefinitions[2].Width = new GridLength(narrow ? 0 : 272);
    }

    /// <summary>
    /// Below this width the look block puts its preview under the controls: beside a 272 preview the controls would get
    /// less than the sliders' one-line label column (about 216 in Russian), a usable slider and its value need.
    /// </summary>
    internal const double NarrowLook = 680;

    /// <summary>
    /// Too narrow for the preview beside the controls: under <see cref="NarrowLook"/>, or when the sliders' label column (a
    /// longer translation, a larger font) would leave a slider less than 120 beside its 56 value column.
    /// </summary>
    private bool IsNarrowLook(double width)
    {
        var label = Math.Max(SizeLabel.DesiredSize.Width, OpacityLabel.DesiredSize.Width);
        return width < NarrowLook || width - 24 - 272 < label + 56 + 120;
    }

    /// <summary>The window saves the settings after a density preset (D5).</summary>
    public event Action? DensityChanged;

    /// <summary>A density preset: the size and every panel's rows at once; the size slider and the preview follow.</summary>
    private void OnDensity(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || sender is not RadioButton { Tag: string preset })
        {
            return;
        }

        model.ApplyOverlayDensity(preset);
        // The settings object does not notify: the slider takes the new size (and the window saves on its change)
        SizeSlider.Value = model.Settings.OverlayScale;
        OnLookChanged();
        DensityChanged?.Invoke();
    }

    private void OnLookChanged()
    {
        (DataContext as MainViewModel)?.RefreshOverlayLook();
        RefreshPreview();
    }

    /// <summary>
    /// The preview's deck panel at the overlay's size, its backgrounds at the panels' opacity: faded copies of the
    /// backdrop brushes in the preview box, as <see cref="OverlayView"/> does for the overlay.
    /// </summary>
    private void RefreshPreview()
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        OverlayPreviewScale.LayoutTransform = new ScaleTransform(model.PreviewScale, model.PreviewScale);
        var opacity = model.Settings.OverlayOpacity;
        foreach (var key in OverlayViewModel.BackdropBrushes)
        {
            if (opacity < 1 && Application.Current is { } app && app.TryGetResource(key, ActualThemeVariant, out var value)
                && OverlayView.Faded(value as IBrush, opacity) is { } faded)
            {
                OverlayPreviewBox.Resources[key] = faded;
            }
            else
            {
                OverlayPreviewBox.Resources.Remove(key);
            }
        }
    }

    /// <summary>The window's <c>OnOpenTrackerTab</c> for this tab's Click.</summary>
    public event Action<RoutedEventArgs>? OpenTrackerTabClicked;

    private void RaiseOpenTrackerTabClicked(object? sender, RoutedEventArgs e) => OpenTrackerTabClicked?.Invoke(e);

    /// <summary>The window's <c>OnArrangeOverlay</c> for this tab's Click.</summary>
    public event Action<RoutedEventArgs>? ArrangeOverlayClicked;

    private void RaiseArrangeOverlayClicked(object? sender, RoutedEventArgs e) => ArrangeOverlayClicked?.Invoke(e);
}

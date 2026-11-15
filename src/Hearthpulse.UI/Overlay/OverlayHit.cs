using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Overlay;

/// <summary>Marks overlay elements that should receive the mouse; everything else clicks through to the game.</summary>
public static class OverlayHit
{
    public static readonly AttachedProperty<bool> InteractiveProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Interactive", typeof(OverlayHit));

    public static bool GetInteractive(AvaloniaObject element) => element.GetValue(InteractiveProperty);

    public static void SetInteractive(AvaloniaObject element, bool value) => element.SetValue(InteractiveProperty, value);
}

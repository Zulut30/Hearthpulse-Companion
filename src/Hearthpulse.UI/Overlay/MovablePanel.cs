using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hearthpulse.Core;

namespace Hearthpulse.UI.Overlay;

/// <summary>
/// Overlay panel host the player can drag, resize (corner marker or mouse wheel), fade (Shift+wheel) and reset
/// (right-click) while the overlay is in layout mode, and the same during a game while holding Ctrl over it or, with
/// the panels unlocked, while the pointer is on it (<see cref="IsDragArmed"/>). Its name tag carries buttons for size,
/// opacity, compact lists, reset and the panels' lock (<see cref="OffersLock"/>). Without a saved placement it keeps
/// the alignment and margin from XAML.
/// </summary>
public sealed partial class MovablePanel : ContentControl, IArmablePanel
{
    public static readonly StyledProperty<string> IdProperty = AvaloniaProperty.Register<MovablePanel, string>(nameof(Id), string.Empty);

    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<MovablePanel, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<MovablePanel, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<bool> ShownProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(Shown));

    public static readonly StyledProperty<bool> IsEditingProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(IsEditing));

    public static readonly StyledProperty<bool> IsDragArmedProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(IsDragArmed));

    public static readonly StyledProperty<bool> IsUnlockedProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(IsUnlocked));

    public static readonly StyledProperty<bool> OffersLockProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(OffersLock));

    public static readonly StyledProperty<string> ScaleTextProperty = AvaloniaProperty.Register<MovablePanel, string>(nameof(ScaleText), string.Empty);

    public static readonly StyledProperty<HorizontalAlignment> PlaceholderAlignmentProperty =
        AvaloniaProperty.Register<MovablePanel, HorizontalAlignment>(nameof(PlaceholderAlignment), HorizontalAlignment.Stretch);

    public static readonly StyledProperty<double> PanelOpacityProperty = AvaloniaProperty.Register<MovablePanel, double>(nameof(PanelOpacity), 1);

    public static readonly StyledProperty<string> OpacityTextProperty = AvaloniaProperty.Register<MovablePanel, string>(nameof(OpacityText), string.Empty);

    public static readonly StyledProperty<bool> CanCompactProperty = AvaloniaProperty.Register<MovablePanel, bool>(nameof(CanCompact));

    /// <summary>How far below its spot a panel starts when it fades in, in overlay units.</summary>
    private const double RevealRise = 6;

    private static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HideTime = TimeSpan.FromMilliseconds(110);

    private readonly TranslateTransform _rise = new();
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _fadeClock = new();
    private double _fadeFrom;
    private bool _fadingIn;
    private bool _hiding;
    private bool _lastEditing;
    private HorizontalAlignment _defaultHorizontal;
    private VerticalAlignment _defaultVertical;
    private Thickness _defaultMargin;
    private bool _defaultsCaptured;
    private double _scale = 1;
    private LayoutTransformControl? _scaler;

    // Saved top-left in surface units (null = XAML default). Kept apart from Margin so a panel that grows past the
    // screen edge is pulled back inside, then returns to its spot when it shrinks again.
    private Point? _saved;

    // Height of the content measured without the parent's limit (see MeasureOverride), for KeepOnScreen.
    private double _contentHeight;

    static MovablePanel()
    {
        ShownProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
        IsEditingProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
        IsDragArmedProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
        IsUnlockedProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
        OffersLockProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
        HintProperty.Changed.AddClassHandler<MovablePanel>((p, _) => p.UpdateState());
    }

    public MovablePanel()
    {
        IsVisible = false;
        RenderTransform = _rise;
        _fade.Tick += (_, _) => StepFade();
        // The layout-mode frame and name tag draw just outside the panel.
        ClipToBounds = false;
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnCaptureLost);
        SizeChanged += (_, _) => KeepOnScreen();
    }

    public string Id { get => GetValue(IdProperty); set => SetValue(IdProperty, value); }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>When the panel shows, for its layout-mode placeholder (menu and draft panels); empty for match panels.</summary>
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    /// <summary>
    /// Where the layout-mode placeholder sits inside a wide panel. The pick rows (heroes, trinkets, Discover, tavern) share
    /// the game's card row and are as wide as it: stretched, their placeholders would lie on top of each other.
    /// </summary>
    public HorizontalAlignment PlaceholderAlignment { get => GetValue(PlaceholderAlignmentProperty); set => SetValue(PlaceholderAlignmentProperty, value); }

    public bool Shown { get => GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }

    /// <summary>
    /// Set by the overlay while Ctrl is held over this panel outside layout mode, or the pointer is on it with the panels
    /// unlocked: the panel takes the mouse, shows its frame and name tag, and moves or resizes like in layout mode.
    /// Otherwise it lets every click through as before.
    /// </summary>
    public bool IsDragArmed { get => GetValue(IsDragArmedProperty); set => SetValue(IsDragArmedProperty, value); }

    /// <summary>The player unlocked the panels (from the overlay's view model): free panels show their frame.</summary>
    public bool IsUnlocked { get => GetValue(IsUnlockedProperty); set => SetValue(IsUnlockedProperty, value); }

    /// <summary>
    /// Set by the overlay while the pointer rests on the panel or arms it: the name tag shows the lock button,
    /// "Unlock panels" or, once unlocked, "Lock panels". Only that button takes the mouse on a locked panel.
    /// </summary>
    public bool OffersLock { get => GetValue(OffersLockProperty); set => SetValue(OffersLockProperty, value); }

    /// <summary>
    /// The panel lies on the game's own cards (the mulligan, picks, Discover and tavern rows): unlocked panels leave it
    /// alone, so it moves only with Ctrl or in layout mode and never takes a click meant for a card.
    /// </summary>
    public bool SitsOnGame => GameAnchored || PlaceholderAlignment != HorizontalAlignment.Stretch;

    /// <summary>Raised when the player presses the lock button on the panel's tag.</summary>
    public event Action<MovablePanel>? LockToggled;

    /// <summary>The player is moving or resizing the panel right now (the overlay keeps it armed until the button is released).</summary>
    public bool IsDragging => _dragging;

    private bool CanMove => IsEditing || IsDragArmed;

    /// <summary>The player's size for the layout-mode tag ("115%"); empty at the default size.</summary>
    public string ScaleText { get => GetValue(ScaleTextProperty); private set => SetValue(ScaleTextProperty, value); }

    /// <summary>The player's opacity for this panel; only the content fades, the layout-mode frame and tag stay solid.</summary>
    public double PanelOpacity { get => GetValue(PanelOpacityProperty); private set => SetValue(PanelOpacityProperty, value); }

    /// <summary>The player's opacity for the tag ("70%"); empty when opaque.</summary>
    public string OpacityText { get => GetValue(OpacityTextProperty); private set => SetValue(OpacityTextProperty, value); }

    /// <summary>The panel holds card lists that have a compact form: its tag shows the compact button.</summary>
    public bool CanCompact { get => GetValue(CanCompactProperty); set => SetValue(CanCompactProperty, value); }

    /// <summary>Card lists in the panel use short rows without art (the <c>compact</c> class, styled in OverlayView.axaml).</summary>
    public bool IsCompact => Classes.Contains("compact");

    /// <summary>Raised when the player moves, resizes or resets the panel (null = reset).</summary>
    public event Action<MovablePanel, PanelPlacement?>? PlacementChanged;

    /// <summary>Raised when the player changes the panel's opacity or compact lists (null = back to the default look).</summary>
    public event Action<MovablePanel, PanelLook?>? LookChanged;

    /// <summary>Shows the panel with the player's look (null = opaque, full rows).</summary>
    public void ApplyLook(PanelLook? look)
    {
        var opacity = PanelLayout.LookOpacity(look);
        PanelOpacity = opacity;
        OpacityText = PanelSnap.OpacityText(opacity);
        Classes.Set("compact", look?.Compact == true);
    }

    private void ChangeLook(double opacity, bool compact)
    {
        var look = new PanelLook { Opacity = opacity, Compact = compact };
        ApplyLook(look);
        LookChanged?.Invoke(this, look.IsDefault ? null : look);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _scaler = e.NameScope.Find<LayoutTransformControl>("PART_Scaler");
        _chrome = e.NameScope.Find<Control>("PART_Chrome");
        _tagRow = e.NameScope.Find<Control>("PART_TagRow");
        _strip = _chrome?.Parent as PanelTagStrip;
        if (_chrome is not null)
        {
            _chrome.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty)
                {
                    FollowTag();
                }
            };
        }

        _grip = e.NameScope.Find<Control>("PART_Grip");
        Hook("PART_Smaller", () => StepScale(up: false));
        Hook("PART_Larger", () => StepScale(up: true));
        Hook("PART_Fade", () => ChangeLook(PanelSnap.NextOpacity(PanelOpacity), IsCompact));
        Hook("PART_Compact", () => ChangeLook(PanelOpacity, !IsCompact));
        Hook("PART_Reset", Reset);
        Hook("PART_Lock", () => LockToggled?.Invoke(this));
        ApplyScale();
        UpdateChrome();

        void Hook(string name, Action action)
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Click += (_, args) =>
                {
                    action();
                    args.Handled = true;
                };
            }
        }
    }

    // The corner marker that resizes the panel.
    private Control? _grip;

    /// <summary>Back to the XAML spot and size, opaque with full rows.</summary>
    private void Reset()
    {
        ApplyLook(null);
        LookChanged?.Invoke(this, null);
        PlacementChanged?.Invoke(this, null);
    }

    private void UpdateState()
    {
        // Entering or leaving layout mode switches every panel at once: no fades there, only for a panel the game shows.
        // Off with the Windows animation setting (and elsewhere), like the card pool's reveal.
        var animate = IsEditing == _lastEditing && !IsEditing && TopLevel.GetTopLevel(this) is not null && Infrastructure.Motion.On;
        _lastEditing = IsEditing;
        SetVisible(Shown || IsEditing, animate);
        Classes.Set("editing", IsEditing);
        Classes.Set("placeholder", IsEditing && !Shown);
        Classes.Set("hinted", !string.IsNullOrEmpty(Hint));
        Classes.Set("armed", IsDragArmed && !IsEditing);
        // Unlocked and off the game's cards: the frame shows and the pointer alone moves the panel.
        Classes.Set("free", IsUnlocked && !IsEditing && !SitsOnGame);
        Classes.Set("unlocked", IsUnlocked);
        Classes.Set("offers", OffersLock && !IsEditing);
        OverlayHit.SetInteractive(this, CanMove);
        Cursor = CanMove ? new Cursor(StandardCursorType.SizeAll) : null;
        UpdateChrome();
    }

    /// <summary>
    /// Where the pointer keeps an armed panel armed, in its own coordinates: the panel, the strip with its name tag and
    /// buttons, which can reach past a narrow panel's right edge, and the corner marker just outside its corner.
    /// </summary>
    public Rect ArmedArea()
    {
        var area = new Rect(Bounds.Size);
        Add(_chrome);
        Add(_grip);
        return area;

        void Add(Control? part)
        {
            if (part is { IsVisible: true } && part.TranslatePoint(default, this) is { } origin)
            {
                area = area.Union(new Rect(origin, part.Bounds.Size));
            }
        }
    }

    /// <summary>
    /// Shows or hides the panel; with <paramref name="animate"/> a short fade and rise in (ease-out), or a quicker fade
    /// out. The timer runs only during those few frames, never while the panel just sits there.
    /// </summary>
    private void SetVisible(bool visible, bool animate)
    {
        if (visible)
        {
            var appearing = !IsVisible || _hiding;
            // A panel shown again mid fade-out comes back from where it was.
            var from = _hiding ? Opacity : 0;
            _hiding = false;
            IsHitTestVisible = true;
            IsVisible = true;
            if (animate && appearing)
            {
                StartFade(from, fadingIn: true);
            }
            else if (!animate)
            {
                StopFade();
            }

            return;
        }

        if (!IsVisible || _hiding)
        {
            return;
        }

        if (!animate)
        {
            StopFade();
            IsVisible = false;
            return;
        }

        // The game moved on: the panel stops taking clicks at once and is gone a moment later.
        _hiding = true;
        IsHitTestVisible = false;
        StartFade(Opacity, fadingIn: false);
    }

    private void StartFade(double from, bool fadingIn)
    {
        _fadeFrom = from;
        _fadingIn = fadingIn;
        Opacity = from;
        _rise.Y = fadingIn ? RevealRise : 0;
        _fadeClock.Restart();
        _fade.Start();
    }

    private void StepFade()
    {
        var t = Math.Min(1, _fadeClock.Elapsed / (_fadingIn ? RevealTime : HideTime));
        var eased = 1 - Math.Pow(1 - t, 3);
        Opacity = _fadingIn ? _fadeFrom + ((1 - _fadeFrom) * eased) : _fadeFrom * (1 - eased);
        _rise.Y = _fadingIn ? RevealRise * (1 - eased) : 0;
        if (t < 1)
        {
            return;
        }

        if (_hiding)
        {
            _hiding = false;
            IsHitTestVisible = true;
            IsVisible = false;
        }

        StopFade();
    }

    private void StopFade()
    {
        _fade.Stop();
        Opacity = 1;
        _rise.Y = 0;
    }

    private void ApplyScale()
    {
        ScaleText = PanelSnap.ScaleText(_scale);
        if (_scaler is not null)
        {
            _scaler.LayoutTransform = Math.Abs(_scale - 1) < 0.001 ? null : new ScaleTransform(_scale, _scale);
        }
    }

    private void CaptureDefaults()
    {
        if (_defaultsCaptured)
        {
            return;
        }

        _defaultsCaptured = true;
        _defaultHorizontal = HorizontalAlignment;
        _defaultVertical = VerticalAlignment;
        _defaultMargin = Margin;
    }

    /// <summary>
    /// The XAML margin is a spot on the game's own screen (the mulligan strip under the offered cards), in units of a
    /// 1080-high screen: it must not grow or shrink with the player's overlay scale, only the panel's content does.
    /// </summary>
    public bool GameAnchored { get; set; }

    /// <summary>Places the panel from settings for the current surface size.</summary>
    /// <param name="overlayScale">The player's overlay scale, which a <see cref="GameAnchored"/> default margin undoes.</param>
    public void Apply(PanelPlacement? placement, Size surface, double overlayScale = 1)
    {
        CaptureDefaults();
        if (placement is null)
        {
            _saved = null;
            HorizontalAlignment = _defaultHorizontal;
            VerticalAlignment = _defaultVertical;
            var divisor = PanelLayout.DefaultMarginDivisor(GameAnchored, overlayScale);
            Margin = new Thickness(_defaultMargin.Left / divisor, _defaultMargin.Top / divisor, _defaultMargin.Right / divisor, _defaultMargin.Bottom / divisor);
            _scale = 1;
            ApplyScale();
            return;
        }

        _scale = PanelLayout.PlacedScale(placement);
        ApplyScale();
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        var (x, y) = PanelLayout.PlacedSpot(placement, surface.Width, surface.Height);
        _saved = new Point(x, y);
        Margin = new Thickness(_saved.Value.X, _saved.Value.Y, 0, 0);
        KeepOnScreen();
    }

    /// <summary>
    /// A saved panel is measured without a height limit: the parent only offers the room down to the screen edge, so a
    /// panel that grows past it was cut there and never looked too tall for <see cref="KeepOnScreen"/> to move it up.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_saved is null)
        {
            return base.MeasureOverride(availableSize);
        }

        var size = base.MeasureOverride(availableSize.WithHeight(double.PositiveInfinity));
        if (Math.Abs(size.Height - _contentHeight) > 0.5)
        {
            _contentHeight = size.Height;
            // The margin is not changed in the middle of a layout pass.
            Dispatcher.UIThread.Post(KeepOnScreen, DispatcherPriority.Render);
        }

        return size;
    }

    /// <summary>Pulls a saved panel back inside the screen when its content grows past an edge.</summary>
    private void KeepOnScreen()
    {
        if (_saved is not { } saved || _dragging || Surface is not { Bounds: { Width: > 0, Height: > 0 } } surface || Bounds.Width <= 0)
        {
            return;
        }

        // Past an edge the surface grid hands the panel only the space left, so Bounds shrink with it and the panel
        // would never come back; content with a fixed size says how much room it really needs.
        var content = Content as Control;
        var width = Math.Max(Bounds.Width, content is { Width: > 0 } ? (content.Width + content.Margin.Left + content.Margin.Right) * _scale : 0);
        var height = Math.Max(Math.Max(Bounds.Height, _contentHeight), content is { Height: > 0 } ? (content.Height + content.Margin.Top + content.Margin.Bottom) * _scale : 0);
        // Fit sets the surface size before the next layout pass: its Bounds are still the old size (a panel applied
        // right after an overlay-scale change was clamped to the smaller surface and stayed there).
        var surfaceWidth = double.IsNaN(surface.Width) ? surface.Bounds.Width : surface.Width;
        var surfaceHeight = double.IsNaN(surface.Height) ? surface.Bounds.Height : surface.Height;
        var (left, top) = PanelLayout.KeepInside(saved.X, saved.Y, width, height, surfaceWidth, surfaceHeight);
        var margin = new Thickness(left, top, 0, 0);
        if (Margin != margin)
        {
            Margin = margin;
        }
    }

    private Control? Surface => Parent as Control;

    private Point TopLeft() => Surface is { } surface && this.TranslatePoint(default, surface) is { } p ? p : new Point(Margin.Left, Margin.Top);

    private PanelPlacement CurrentPlacement()
    {
        var surface = Surface;
        var origin = TopLeft();
        return PanelLayout.Placement(origin.X, origin.Y, surface?.Bounds.Width ?? 0, surface?.Bounds.Height ?? 0, _scale);
    }
}

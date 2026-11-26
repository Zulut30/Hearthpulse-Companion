using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hearthpulse.App.Controls;
using Hearthpulse.Core.CardPool;

namespace Hearthpulse.App.Overlay;

/// <summary>
/// The card pool panel. It lets clicks through to the game except on its "All cards" button: the pointer is read while
/// it is shown and handed to <see cref="OverlayViewModel.UpdateCardPoolPointer(bool)"/> (resting on the panel holds a
/// hand card's pool) and to the rows' card previews. With <see cref="Anchored"/> it sits in a Canvas over the whole
/// surface and stands beside the options of the player's own Discover. It fades in when it first appears
/// (<see cref="Reveal"/>); another card's pool replaces it without a fade, so the layered overlay window repaints less.
/// </summary>
public partial class CardPoolPanel : UserControl
{
    private static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(120);

    // Input priority: the poll must not starve behind rendering, or the hold and the row previews lag the pointer.
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
    private OverlayViewModel? _subscribed;
    private FrameworkElement? _previewRow;
    private readonly List<(FrameworkElement Row, Rect Bounds)> _rows = [];
    private bool _rowsDirty = true;

    public CardPoolPanel()
    {
        InitializeComponent();
        ApplyAnchored();
        _poll.Tick += (_, _) => Poll();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _poll.Start();
                // Measured and placed before the first frame, so the panel never shows at its previous spot.
                UpdateLayout();
                Place();
                Reveal();
            }
            else
            {
                _poll.Stop();
                ViewModel?.UpdateCardPoolPointer(false);
            }
        };
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) =>
        {
            // Any layout pass may move, add or hide a row: the cached rows are collected again on the next poll over the
            // panel. Only while loaded: the layout manager holds the handler strongly, and a theme change rebuilds the
            // overlay, so an old panel must let go of it (Unloaded).
            Card.LayoutUpdated -= OnCardLayoutUpdated;
            Card.LayoutUpdated += OnCardLayoutUpdated;
            _rowsDirty = true;
            // The game window was resized: the surface (and the card's spot on it) changes, the panel does not.
            if (Anchored && _layer is null && Parent is FrameworkElement layer)
            {
                _layer = layer;
                layer.SizeChanged += OnLayerSizeChanged;
            }
        };
        // A theme change rebuilds the overlay: the old layer must not keep this panel (and place it) after that.
        Unloaded += (_, _) =>
        {
            Card.LayoutUpdated -= OnCardLayoutUpdated;
            if (_layer is not null)
            {
                _layer.SizeChanged -= OnLayerSizeChanged;
                _layer = null;
            }
        };
        DataContextChanged += (_, _) => Subscribe();
    }

    // The layer an anchored panel follows while it is loaded.
    private FrameworkElement? _layer;

    private void OnLayerSizeChanged(object sender, SizeChangedEventArgs e) => Place();

    private void OnCardLayoutUpdated(object? sender, EventArgs e) => _rowsDirty = true;

    /// <summary>The panel is the popup beside the Discover options (in a Canvas over the surface), not the movable panel's content.</summary>
    public bool Anchored
    {
        get => _anchored;
        set
        {
            _anchored = value;
            ApplyAnchored();
        }
    }

    private bool _anchored;
    private ScaleTransform? _floor;

    /// <summary>The popup's fill does not fade with the overlay opacity; the panel at its own spot fades like the others.</summary>
    private void ApplyAnchored()
    {
        if (_anchored)
        {
            Card.SetResourceReference(Border.BackgroundProperty, "OverlayPopupSolid");
        }
        else
        {
            Card.ClearValue(Border.BackgroundProperty);
        }
    }

    private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

    /// <summary>
    /// A weak subscription: the view model lives with the app, and a closed overlay window (a theme change builds a new
    /// one) keeps its DataContext, so a plain one would keep the old window alive.
    /// </summary>
    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            PropertyChangedEventManager.RemoveHandler(_subscribed, OnViewModelChanged, string.Empty);
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            PropertyChangedEventManager.AddHandler(_subscribed, OnViewModelChanged, string.Empty);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.CardPoolAnchor))
        {
            Place();
        }
    }

    /// <summary>
    /// A short fade (ease-out, opacity only) when the panel appears; nothing when Windows' animation setting is off. The
    /// position is set before, so the panel never slides.
    /// </summary>
    private void Reveal()
    {
        if (!Infrastructure.Motion.On)
        {
            Card.BeginAnimation(OpacityProperty, null);
            return;
        }

        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, RevealTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Stands beside the Discover options: <see cref="PoolPlacement"/> in surface units, from the panel's measured size.</summary>
    private void Place()
    {
        if (!Anchored || !IsVisible || ViewModel is not { CardPoolAnchor: { } anchor } vm || Parent is not FrameworkElement { ActualWidth: > 0 } layer
            || ActualWidth <= 0)
        {
            return;
        }

        // Drawn no smaller than PoolPlacement.MinPopupScale of a game card, whatever the player's overlay scale.
        var floor = PoolPlacement.PopupFloor(vm.CardPoolOverlayScale);
        if (floor != (_floor?.ScaleX ?? 1))
        {
            _floor = floor == 1 ? null : new ScaleTransform(floor, floor);
            RenderTransform = _floor ?? Transform.Identity;
        }

        var (x, y) = PoolPlacement.PlaceOnSurface(anchor, layer.ActualWidth, vm.CardPoolOverlayScale, ActualWidth * floor, ActualHeight * floor);
        Canvas.SetLeft(this, x);
        Canvas.SetTop(this, y);
    }

    private void Poll()
    {
        if (PoolInput.Cursor() is { } cursor)
        {
            PollAt(new Point(cursor.X, cursor.Y));
        }
    }

    /// <summary>One poll with the cursor at <paramref name="screen"/> (screen pixels).</summary>
    private void PollAt(Point screen)
    {
        if (ViewModel is not { } vm || PresentationSource.FromVisual(Card) is null)
        {
            return;
        }

        var point = Card.PointFromScreen(screen);
        var over = point.X >= 0 && point.Y >= 0 && point.X <= Card.ActualWidth && point.Y <= Card.ActualHeight;
        vm.UpdateCardPoolPointer(over);
        PreviewRowAt(over ? point : null);
    }

    /// <summary>
    /// The full card of the row under the pointer (rows of this game, Excavate, the pool's cards): the panel takes no mouse,
    /// so the poll does what a hover would. The preview leaves with the pointer.
    /// </summary>
    private void PreviewRowAt(Point? inCard)
    {
        var row = inCard is { } at ? RowAt(at) : null;
        if (row == _previewRow)
        {
            return;
        }

        if (_previewRow is not null)
        {
            CardPreviewHost.For(_previewRow)?.HideSoon(_previewRow);
        }

        _previewRow = row;
        if (row is not null)
        {
            CardPreviewHost.For(row)?.Show(row);
        }
    }

    /// <summary>
    /// The row with a preview at a point of <see cref="Card"/>. The rows and their bounds are collected once per layout
    /// pass (<see cref="_rowsDirty"/>), not by walking the panel's tree on every poll.
    /// </summary>
    private FrameworkElement? RowAt(Point inCard)
    {
        if (_rowsDirty)
        {
            _rowsDirty = false;
            _rows.Clear();
            CollectRows(Card);
        }

        foreach (var (row, bounds) in _rows)
        {
            if (bounds.Contains(inCard))
            {
                return row;
            }
        }

        return null;
    }

    private void CollectRows(DependencyObject node)
    {
        if (node is FrameworkElement { IsVisible: true } element && CardPreview.HasPreview(element) && element.ActualWidth > 0)
        {
            _rows.Add((element, element.TransformToAncestor(Card).TransformBounds(new Rect(element.RenderSize))));
            return;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            CollectRows(VisualTreeHelper.GetChild(node, i));
        }
    }
}

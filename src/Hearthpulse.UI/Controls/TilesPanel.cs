using Avalonia;
using Avalonia.Controls;

namespace Hearthpulse.UI.Controls;

/// <summary>
/// The row of KPI tiles of My stats and the pages like it (StatsKpiPanel): every tile on one line, or <see cref="Columns"/>
/// to a line in compact. A last line with fewer tiles shares its width among them, so an odd count leaves no hole (three
/// tiles in two columns: two, then one across; release cleanup m3). Each line takes its tallest tile's height.
/// </summary>
public sealed class TilesPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<TilesPanel, int>(nameof(Columns));

    static TilesPanel() => AffectsMeasure<TilesPanel>(ColumnsProperty);

    /// <summary>Tiles to a line; 0 puts them all on one.</summary>
    public int Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tiles = Shown();
        var width = 0.0;
        var height = 0.0;
        foreach (var line in Lines(tiles.Count))
        {
            var cell = availableSize.Width / line.Count;
            var tallest = 0.0;
            var widest = 0.0;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                tiles[i].Measure(new Size(cell, double.PositiveInfinity));
                tallest = Math.Max(tallest, tiles[i].DesiredSize.Height);
                widest = Math.Max(widest, tiles[i].DesiredSize.Width);
            }

            width = Math.Max(width, widest * line.Count);
            height += tallest;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tiles = Shown();
        var top = 0.0;
        foreach (var line in Lines(tiles.Count))
        {
            var cell = finalSize.Width / line.Count;
            var tallest = 0.0;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                tallest = Math.Max(tallest, tiles[i].DesiredSize.Height);
            }

            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                tiles[i].Arrange(new Rect((i - line.Start) * cell, top, cell, tallest));
            }

            top += tallest;
        }

        return finalSize;
    }

    private List<Control> Shown() => Children.Where(c => c.IsVisible).ToList();

    /// <summary>The lines of <paramref name="count"/> tiles: where each starts and how many it holds.</summary>
    private IEnumerable<(int Start, int Count)> Lines(int count)
    {
        var perLine = Columns > 0 ? Columns : Math.Max(count, 1);
        for (var start = 0; start < count; start += perLine)
        {
            yield return (start, Math.Min(perLine, count - start));
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking.Counters;
using WpfChip = Hearthpulse.App.Overlay.CounterChip;
using WpfTrinket = Hearthpulse.App.Overlay.TrinketBadge;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Text of the WPF overlay that was cut mid-word: every counter name (Constructed and Battlegrounds, Russian and
/// English) fits its row in two lines, and a trinket badge's "update delayed" line wraps whole words at the badge's
/// width instead of losing its end. Laid out with the real templates of <c>OverlayPanels.xaml</c>.
/// </summary>
public sealed class OverlayNamesFitTests
{
    /// <summary>Inside the counters card: its width (232) less its margin (9 + 9).</summary>
    private const double CountersInner = 214;

    [Fact]
    public void Every_counter_name_fits_its_row_in_two_lines_in_both_languages() => WpfThread.RunThemed(() =>
    {
        var panels = Panels();
        var row = Laid(panels, "CounterTemplate", Chip("Способности К'Туна"), CountersInner);
        var title = Single(row, "Способности К'Туна");
        Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
        Assert.Equal("Способности К'Туна", AutomationName(row));

        // As laid out, a name too long for one line takes two, not one with an ellipsis.
        var twoLines = Laid(panels, "CounterTemplate", Chip("Погибло ваших существ"), CountersInner);
        var wrapped = Single(twoLines, "Погибло ваших существ");
        Assert.True(wrapped.ActualHeight >= 2 * wrapped.LineHeight, $"\"{wrapped.Text}\" is {wrapped.ActualHeight:0.##} high at {wrapped.ActualWidth:0.##}, desired {wrapped.DesiredSize}");

        var chip = Laid(panels, "BgCounterTemplate", Chip("Погибло ваших Вечных рыцарей"), 400);
        var bgTitle = Single(chip, "Погибло ваших Вечных рыцарей");
        Assert.Equal(TextWrapping.Wrap, bgTitle.TextWrapping);
        Assert.True(bgTitle.ActualHeight >= 2 * bgTitle.LineHeight, $"\"{bgTitle.Text}\" is {bgTitle.ActualHeight:0.##} high at {bgTitle.ActualWidth:0.##}");

        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                foreach (var name in Counters.All.Select(c => c.Title))
                {
                    AssertFits(title, name, lines: 2, $"{language} counter");
                }

                foreach (var name in BattlegroundsCounters.All.Select(c => c.Title))
                {
                    AssertFits(bgTitle, name, lines: 2, $"{language} Battlegrounds counter");
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    [Fact]
    public void A_trinket_badge_shows_a_late_update_on_its_own_line_whole() => WpfThread.RunThemed(() =>
    {
        var panels = Panels();
        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                var date = Loc.F("Source.Health.Overlay", new DateTime(2026, 11, 28));
                var delay = Loc.T("Source.Health.OverlayDelay");
                var badge = Laid(panels, "TrinketBadgeTemplate", new WpfTrinket("TRK", "Trinket", "S", "4.55", Loc.F("Overlay.Badge.PickRate", "14 %"), false, true) { Updated = date, Delay = delay }, 400);
                var texts = Texts(badge).Where(t => Shown(t, badge)).ToList();

                // The date stays one line; the delay is a warning mark and words that wrap, never an ellipsis.
                var dateLine = Assert.Single(texts, t => t.Text == date);
                Assert.True(Width(dateLine, date) <= dateLine.ActualWidth + 0.5, $"{language}: \"{date}\" is cut at {dateLine.ActualWidth:0.#}");
                var delayLine = Assert.Single(texts, t => t.Text == delay);
                Assert.Equal(TextWrapping.Wrap, delayLine.TextWrapping);
                AssertFits(delayLine, delay, lines: 2, language);
                Assert.Contains(texts, t => t.Text == "\U000F0026");

                // Fresh numbers: no warning line.
                var fresh = Laid(panels, "TrinketBadgeTemplate", new WpfTrinket("TRK", "Trinket", "S", "4.55", "14 %", false, true) { Updated = date }, 400);
                Assert.DoesNotContain(Texts(fresh).Where(t => Shown(t, fresh)), t => t.Text == delay || t.Text == "\U000F0026");
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    private static WpfChip Chip(string title) => new("id", title, "3", 0, false, null, string.Empty, false, []);

    private static ResourceDictionary Panels() => new() { Source = new Uri("/Hearthpulse;component/Overlay/OverlayPanels.xaml", UriKind.Relative) };

    /// <summary>
    /// <paramref name="text"/> set in <paramref name="block"/>'s font at its laid-out width: no word wider than the
    /// line, and no more than <paramref name="lines"/> lines of its line height.
    /// </summary>
    private static void AssertFits(TextBlock block, string text, int lines, string what)
    {
        // A capped name (the Battlegrounds chip) may take up to its cap; a row's name, the width its column gives it.
        var width = block.MaxWidth is > 0 and < double.PositiveInfinity ? block.MaxWidth : block.ActualWidth;
        var lineHeight = double.IsNaN(block.LineHeight) ? block.FontFamily.LineSpacing * block.FontSize : block.LineHeight;
        var set = Formatted(block, text);
        set.MaxTextWidth = width;
        set.LineHeight = lineHeight;
        Assert.True(set.MinWidth <= width + 0.5, $"{what} \"{text}\": a word is {set.MinWidth:0.#} wide, the line {width:0.#}");
        Assert.True(set.Height <= (lines * lineHeight) + 0.5, $"{what} \"{text}\": {set.Height / lineHeight:0.#} lines at {width:0.#}");
    }

    private static double Width(TextBlock block, string text) => Formatted(block, text).WidthIncludingTrailingWhitespace;

    private static FormattedText Formatted(TextBlock block, string text) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.Black, 1.0);

    private static TextBlock Single(FrameworkElement root, string text) => Assert.Single(Texts(root), t => t.Text == text);

    private static string AutomationName(FrameworkElement row) =>
        System.Windows.Automation.AutomationProperties.GetName(Assert.IsType<Border>(VisualTreeHelper.GetChild(row, 0)));

    /// <summary>A template of the overlay's panels laid out with <paramref name="item"/> at <paramref name="width"/>.</summary>
    private static ContentPresenter Laid(ResourceDictionary panels, string template, object item, double width)
    {
        // Text set as the overlay window sets it (OverlayWindow.xaml): whole pixels, rounded layout.
        var host = new ContentControl { Resources = panels, Width = width, UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
        var presenter = new ContentPresenter { Content = item, ContentTemplate = (DataTemplate)panels[template], HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Content = presenter;
        host.Measure(new Size(width, 600));
        host.Arrange(new Rect(0, 0, width, 600));
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        host.UpdateLayout();
        return presenter;
    }

    /// <summary>Not collapsed or hidden up to <paramref name="root"/> (outside a window <c>IsVisible</c> is always false).</summary>
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e is not null && !ReferenceEquals(e, root); e = VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text)
            {
                yield return text;
            }

            foreach (var nested in Texts(child))
            {
                yield return nested;
            }
        }
    }
}

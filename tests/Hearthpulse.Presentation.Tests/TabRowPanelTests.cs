using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hearthpulse.App.Controls;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF tab row (bug hunt d1): the right-hand bar stays on the tabs' line while both fit, takes a line of its own
/// when they do not, and follows the window when it is made narrower or wider again, as the main window's minimum
/// width (1040) does in a render.
/// </summary>
public sealed class TabRowPanelTests
{
    [Fact]
    public void The_right_bar_follows_the_row_as_it_narrows_and_widens() => WpfThread.Run(() =>
    {
        var tabs = new Border { Width = 350, Height = 32 };
        var picker = new Border { Width = 190, Height = 32 };
        var row = new TabRowPanel();
        row.Children.Add(tabs);
        row.Children.Add(new Grid { HorizontalAlignment = HorizontalAlignment.Right, Children = { picker } });
        var page = new StackPanel { Margin = new Thickness(40, 0, 40, 0), Children = { row, new Border { Height = 100 } } };
        var window = new Window { Width = 1000, Height = 400, Content = new ScrollViewer { Content = page }, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            foreach (var (width, wrapped) in new[] { (1000.0, false), (700.0, false), (560.0, true), (1000.0, false) })
            {
                window.Width = width;
                window.UpdateLayout();
                var box = picker.TransformToAncestor(row).TransformBounds(new Rect(picker.RenderSize));
                Assert.Equal(wrapped, row.Wrapped);
                Assert.Equal(row.ActualWidth, box.Right, 1);
                Assert.Equal(wrapped ? tabs.ActualHeight + row.LineGap : 0, box.Top, 1);
                Assert.Equal(wrapped ? 32 + row.LineGap + 32 : 32, row.ActualHeight, 1);
                Assert.True(VisualTreeHelper.GetParent(picker) is not null);
            }

            // The window narrowed while the page was hidden (another page shown), then the page comes back.
            page.Visibility = Visibility.Collapsed;
            window.UpdateLayout();
            window.Width = 700;
            window.UpdateLayout();
            page.Visibility = Visibility.Visible;
            window.UpdateLayout();
            var back = picker.TransformToAncestor(row).TransformBounds(new Rect(picker.RenderSize));
            Assert.False(row.Wrapped);
            Assert.Equal((Math.Round(row.ActualWidth, 1), 0.0), (Math.Round(back.Right, 1), Math.Round(back.Top, 1)));

            // The bar itself hidden and shown again meanwhile (the MMR slices come and go with the tab and the list).
            var holder = (FrameworkElement)row.Children[1];
            holder.Visibility = Visibility.Collapsed;
            window.UpdateLayout();
            page.Visibility = Visibility.Collapsed;
            window.Width = 900;
            window.UpdateLayout();
            holder.Visibility = Visibility.Visible;
            page.Visibility = Visibility.Visible;
            window.UpdateLayout();
            back = picker.TransformToAncestor(row).TransformBounds(new Rect(picker.RenderSize));
            Assert.Equal((Math.Round(row.ActualWidth, 1), 0.0, 190.0), (Math.Round(back.Right, 1), Math.Round(back.Top, 1), picker.ActualWidth));
        }
        finally
        {
            window.Close();
        }
    });
}

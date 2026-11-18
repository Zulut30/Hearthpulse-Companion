using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF main window rebuilt for a new theme (App.OnThemeChanged, MainWindow.ThemeSwap.cs): the new window opens where
/// the old one was scrolled and focused, and the old look's picture over it never stays or piles up. The window itself is
/// shown off the screen only in the app; here both windows are laid out without being shown, like MainWindowXamlLoadTests.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class ThemeSwapTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void Rebuilt_window_opens_on_the_settings_scroll_and_the_tile_that_had_focus() => WpfThread.RunFresh(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var old = Settings(model);
        var page = (ScrollViewer)old.FindName("PageSettings");
        page.ScrollToVerticalOffset(Math.Min(240, page.ScrollableHeight));
        old.Frame.UpdateLayout();
        Assert.True(page.VerticalOffset > 0, "the Appearance tab scrolls in an 840 px window");
        var tiles = Tiles(old);
        var clicked = tiles[3];

        var view = old.TakeView(picture: false, clicked);
        Assert.Null(view.Picture);

        var rebuilt = Settings(model);
        rebuilt.ScrollAs(view);
        rebuilt.Frame.UpdateLayout();
        Assert.Equal(page.VerticalOffset, ((ScrollViewer)rebuilt.FindName("PageSettings")).VerticalOffset);
        var twin = Assert.IsType<Button>(rebuilt.FocusTarget(view));
        Assert.Equal(clicked.CommandParameter, twin.CommandParameter);
        Assert.Contains(twin, Tiles(rebuilt));
        old.Close();
        rebuilt.Close();
    });

    [Fact]
    public void A_twin_found_by_type_and_name_when_lazily_built_pages_moved_its_index() => WpfThread.RunFresh(() =>
    {
        var target = new Button { Name = "Target" };
        var first = Host(new Border(), target);
        var address = VisualAddress.Of(first, target);
        Assert.NotNull(address);

        // A page built first in the other window sits before it now.
        var twin = new Button { Name = "Target" };
        var second = Host(new TextBlock(), new Border(), twin);
        Assert.Same(twin, address.Find(second));

        // Two candidates of the same type and name: no guess.
        var third = Host(new TextBlock(), new Border(), new Button { Name = "Target" }, new Button { Name = "Target" });
        Assert.Null(address.Find(third));
        Assert.Null(VisualAddress.Of(first, new Button()));
    });

    [Fact]
    public void A_new_picture_takes_the_old_ones_place_and_lifting_without_motion_leaves_none() => WpfThread.RunFresh(() =>
    {
        var element = new Border { Width = 40, Height = 30 };
        var host = new AdornerDecorator { Child = element };
        host.Measure(new Size(40, 30));
        host.Arrange(new Rect(0, 0, 40, 30));

        Assert.True(ThemeCover.Lay(element, Picture(), default));
        Assert.True(ThemeCover.Lay(element, Picture(), new Point(-7, -7)));
        Assert.Single(AdornerLayer.GetAdornerLayer(element)!.GetAdorners(element)!);
        Assert.NotNull(ThemeCover.On(element));

        ThemeCover.Lift(element, fade: false);
        Assert.Null(ThemeCover.On(element));
        Assert.Null(AdornerLayer.GetAdornerLayer(element)!.GetAdorners(element));
        // Nothing to lift: nothing happens.
        ThemeCover.Lift(element, fade: true);
        Assert.Null(ThemeCover.On(element));
    });

    [Fact]
    public void With_motion_off_the_old_window_is_not_pictured() => WpfThread.RunFresh(() =>
    {
        var saved = Motion.Reduced;
        try
        {
            Motion.Reduced = true;
            var window = Settings(new WpfMain(History(), new AppSettings(), TestApp.Data));
            var view = window.TakeView(Motion.On, focused: null);
            Assert.Null(view.Picture);
            Assert.False(window.Covered);
            window.Close();
        }
        finally
        {
            Motion.Reduced = saved;
        }
    });

    /// <summary>A main window on Settings, Appearance, laid out at 1260×840 without being shown.</summary>
    private static WpfWindow Settings(WpfMain model)
    {
        var window = new WpfWindow(model);
        window.ShowPage("Settings");
        window.ShowSettingsTab("Appearance");
        var root = window.Frame;
        root.Measure(new Size(1260, 840));
        root.Arrange(new Rect(0, 0, 1260, 840));
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        root.UpdateLayout();
        return window;
    }

    private static List<Button> Tiles(WpfWindow window)
    {
        var swatch = window.FindResource("ThemeSwatch");
        var tiles = Descendants<Button>((DependencyObject)window.FindName("SettingsAppearance")).Where(b => ReferenceEquals(b.Style, swatch)).ToList();
        Assert.True(tiles.Count > 3);
        return tiles;
    }

    private static Grid Host(params UIElement[] children)
    {
        var grid = new Grid();
        foreach (var child in children)
        {
            grid.Children.Add(child);
        }

        return grid;
    }

    private static BitmapSource Picture()
    {
        var picture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, new byte[16], 8);
        picture.Freeze();
        return picture;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                yield return found;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}

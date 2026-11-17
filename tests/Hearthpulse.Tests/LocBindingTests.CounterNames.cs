using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// A long counter name in the Avalonia overlay ("Способности К'Т…" was cut on one line) wraps to a second line of
    /// its row, Constructed and Battlegrounds, and the row's button carries the whole name. Whether every name fits two
    /// lines is measured with real fonts in the WPF suite (<c>OverlayNamesFitTests</c>) and in the renders: the headless
    /// platform's glyphs are not the overlay's.
    /// </summary>
    [Fact]
    public void Avalonia_counter_names_wrap_to_a_second_line_and_name_their_button() => Headless.Run(() =>
    {
        const string name = "Доп. срабатывания Лей-линий";
        var theme = Theme();
        var window = new Window { Width = 600, Height = 400 };
        // The overlay's UI font is Inter, which the app registers at startup and the headless platform does not have.
        window.Resources["UiFont"] = FontFamily.Default;
        try
        {
            var view = new OverlayView { DataContext = new OverlayViewModel() };
            var chip = new CounterChip("id", name, "3", 0, false, null, string.Empty, false, []);
            // Inside the counters card: its width (232) less its margin (9 + 9).
            var row = new ContentControl { Width = 214, Content = chip, ContentTemplate = Assert.IsAssignableFrom<IDataTemplate>(view.FindResource("CounterTemplate")) };
            var bg = new ContentControl { Content = chip, ContentTemplate = Assert.IsAssignableFrom<IDataTemplate>(view.FindResource("BgCounterTemplate")) };
            window.Content = new StackPanel { Children = { row, bg } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            foreach (var host in new[] { row, bg })
            {
                var title = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == name);
                Assert.Equal((TextWrapping.Wrap, 2), (title.TextWrapping, title.MaxLines));
                Assert.Equal(2, title.TextLayout.TextLines.Count);
                Assert.Contains(host.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == name);
            }
        }
        finally
        {
            window.Close();
            Avalonia.Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });
}

using System.Diagnostics;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hosting;

namespace Hearthpulse.Bench;

internal static partial class WpfBench
{
    public static void Theme(Options options)
    {
        var (app, settings) = NewApplication();
        var look = ThemeLook.FromSettings(settings);
        settings.Theme = "Shadow";
        ThemeEngine.Apply(app, ThemePalette.FromSettings(settings), look);
        var model = new MainViewModel(new HistoryStore(AppPaths.Database), settings, AppData.FromAppPaths());
        var window = OffScreen(new MainWindow(model));
        window.Show();
        Pump();
        for (var round = 1; round <= Math.Max(1, options.Rounds); round++)
        {
            foreach (var name in new[] { "Frost", "Tavern", "Shadow" })
            {
                var place = window.Place;
                settings.Theme = name;
                var theme = ThemePalette.FromSettings(settings);
                var watch = Stopwatch.StartNew();
                ThemeEngine.Apply(app, theme, look);
                var apply = watch.Elapsed.TotalMilliseconds;
                var previous = window;
                window = OffScreen(new MainWindow(model));
                window.MoveTo(place);
                window.Show();
                Pump();
                Close(previous, settings);
                Console.WriteLine($"[theme] round {round} {name} apply {apply:N1} ms, main window ready {watch.Elapsed.TotalMilliseconds:N1} ms");
            }
        }

        Close(window, settings);
    }
}

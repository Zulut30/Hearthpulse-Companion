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
        ThemeEngine.Apply(app, ThemePalette.FromSettings(settings, "Shadow"));
        var window = OffScreen(new MainWindow(new MainViewModel(new HistoryStore(AppPaths.Database), settings, AppData.FromAppPaths())));
        window.Show();
        Pump();
        for (var round = 1; round <= Math.Max(1, options.Rounds); round++)
        {
            foreach (var name in new[] { "Frost", "Tavern", "Shadow" })
            {
                var theme = ThemePalette.FromSettings(settings, name);
                var watch = Stopwatch.StartNew();
                ThemeEngine.Apply(app, theme);
                var apply = watch.Elapsed.TotalMilliseconds;
                Pump();
                Console.WriteLine($"[theme] round {round} {name} apply {apply:N1} ms, ready {watch.Elapsed.TotalMilliseconds:N1} ms");
            }
        }

        Close(window, settings);
    }
}

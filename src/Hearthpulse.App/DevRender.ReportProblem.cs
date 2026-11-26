using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.App.Main.Report;
using Hearthpulse.Core;

namespace Hearthpulse.App;

/// <summary>
/// <c>--render-ui</c>, group <c>report</c> (roadmap E1, TASK-2026-11-26-release-cleanup P4): Settings → About scrolled to
/// the Feedback card (<c>main-settings-feedback.png</c>) and the "Report a problem" window on the data copy: as it opens
/// (<c>report-window.png</c>), with the game logs on when the copy's Hearthstone has a log session
/// (<c>report-window-logs.png</c>), after a save (<c>report-window-saved.png</c>; the archive goes to a temporary file,
/// deleted after) and after a refused one (<c>report-window-error.png</c>). Nothing is sent or opened.
/// </summary>
public partial class App
{
    private async Task RenderReportProblemAsync(string folder)
    {
        try
        {
            _main.ShowPage("Settings");
            _main.ShowSettingsTab("About");
            await Task.Delay(800);
            _main.UpdateLayout();
            Named<ScrollViewer>(_main, "PageSettings")?.ScrollToEnd();
            await Task.Delay(300);
            _main.UpdateLayout();
            Save(_main, Path.Combine(folder, "main-settings-feedback.png"));

            var model = _mainViewModel.ReportProblem();
            var window = new ReportProblemWindow(model)
            {
                Owner = _main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
            };
            window.Show();
            try
            {
                await window.LoadAsync();
                await ShotAsync(window, folder, "report-window.png");
                if (model.GameLogsAvailable)
                {
                    model.IncludeGameLogs = true;
                    await UntilAsync(() => model.CanSave);
                    await ShotAsync(window, folder, "report-window-logs.png");
                    model.IncludeGameLogs = false;
                    await UntilAsync(() => model.CanSave);
                }

                var archive = Path.Combine(Path.GetTempPath(), $"hp-report-scene-{Guid.NewGuid():N}.zip");
                try
                {
                    await model.SaveAsync(archive);
                    await ShotAsync(window, folder, "report-window-saved.png");
                }
                finally
                {
                    File.Delete(archive);
                }

                await model.SaveAsync(() => throw new UnauthorizedAccessException("render scene"), model.SuggestedFileName);
                await ShotAsync(window, folder, "report-window-error.png");
            }
            finally
            {
                window.Close();
            }
        }
        catch (IOException ex)
        {
            Diag.Error("Render of the report window failed", ex);
        }
    }

    private static async Task ShotAsync(Window window, string folder, string name)
    {
        await Task.Delay(400);
        window.UpdateLayout();
        Save(window, Path.Combine(folder, name));
    }

    /// <summary>Waits for the window's list to come back from the pool (ten seconds at most).</summary>
    private static async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(50);
        }
    }
}

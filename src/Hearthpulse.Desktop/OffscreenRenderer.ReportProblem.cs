using Avalonia.Controls;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Report;

namespace Hearthpulse.Desktop;

/// <summary>
/// <c>--render-ui</c>, group <c>report</c> (roadmap E1, TASK-2026-11-26-release-cleanup P4), as WPF's
/// <c>DevRender.ReportProblem.cs</c>: Settings → About scrolled to the Feedback card (<c>main-settings-feedback.png</c>) and
/// the "Report a problem" window on the data copy: as it opens (<c>report-window.png</c>), with the game logs on when the
/// copy's Hearthstone has a log session (<c>report-window-logs.png</c>), after a save (<c>report-window-saved.png</c>; the
/// archive goes to a temporary file, deleted after) and after a refused one (<c>report-window-error.png</c>).
/// </summary>
internal static partial class OffscreenRenderer
{
    private static async Task RenderReportProblemAsync(Window main, MainView view, AppHost host, string folder)
    {
        try
        {
            view.ShowPage("Settings");
            view.ShowSettingsTab("About");
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            view.FindControl<ScrollViewer>("PageSettings")?.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            await Save(main, Path.Combine(folder, "main-settings-feedback.png"));

            var model = host.Main.ReportProblem();
            var window = new ReportProblemWindow(model, main);
            window.Show();
            try
            {
                await window.LoadAsync();
                await Save(window, Path.Combine(folder, "report-window.png"));
                if (model.GameLogsAvailable)
                {
                    model.IncludeGameLogs = true;
                    await UntilAsync(() => model.CanSave);
                    await Save(window, Path.Combine(folder, "report-window-logs.png"));
                    model.IncludeGameLogs = false;
                    await UntilAsync(() => model.CanSave);
                }

                var archive = Path.Combine(Path.GetTempPath(), $"hp-report-scene-{Guid.NewGuid():N}.zip");
                try
                {
                    await model.SaveAsync(archive);
                    await Save(window, Path.Combine(folder, "report-window-saved.png"));
                }
                finally
                {
                    File.Delete(archive);
                }

                await model.SaveAsync(() => throw new UnauthorizedAccessException("render scene"), model.SuggestedFileName);
                await Save(window, Path.Combine(folder, "report-window-error.png"));
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

    /// <summary>Waits for the window's list to come back from the pool (ten seconds at most).</summary>
    private static async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(50);
        }
    }
}

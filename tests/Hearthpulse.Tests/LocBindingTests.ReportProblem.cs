using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Report;
using Hearthpulse.UI.Main.SettingsTabs;

namespace Hearthpulse.Tests;

/// <summary>
/// "Report a problem" (roadmap E1, AC6 of TASK-2026-11-26-release-cleanup) in the Avalonia views: the Feedback card of
/// Settings → About with its button, and the window - the file list, the game logs switch and its note, the save
/// button, and after a save the status line with the form's button (no folder button without a local path).
/// </summary>
public sealed partial class LocBindingTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Report_a_problem_card_and_window_bind(string language) => Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(language);
        var app = Application.Current!;
        var theme = Theme();
        var data = Directory.CreateTempSubdirectory("hp-report-view-").FullName;
        File.WriteAllText(Path.Combine(data, "hearthpulse.log"), "INFO Started\n");
        File.WriteAllText(Path.Combine(data, "settings.json"), "{ \"Theme\": \"Frost\" }");
        var main = new MainViewModel(new HistoryStore(Path.Combine(AppPaths.Root, $"hp-report-{Guid.NewGuid():N}.db")), new AppSettings(), TestDataFolder.App);
        var about = new AboutSettings { DataContext = main };
        var aboutWindow = new Window { Width = 780, Height = 1600, Content = about };
        var model = new ReportProblemViewModel(data, null, "0.16.0") { FindGameLogs = _ => GameLogsLookup.NotFound };
        var window = new ReportProblemWindow(model);
        try
        {
            aboutWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<Border>(about, "FeedbackCard").IsEffectivelyVisible);
            var open = Named<Button>(about, "ReportProblemButton");
            Assert.Equal(Loc.T("Report.Open"), open.Content);
            Assert.Equal(Loc.T("Report.OpenTip"), ToolTip.GetTip(open));

            Show(window);
            var load = window.LoadAsync();
            Pump(() => load.IsCompleted);
            Assert.True(load.IsCompletedSuccessfully);
            window.UpdateLayout();

            Assert.Equal(Loc.T("Report.Title"), window.Title);
            var list = Named<ItemsControl>(window, "FileList");
            Assert.Equal(model.Files, list.ItemsSource!.Cast<ReportFileRow>());
            Assert.Equal(["system.txt", "hearthpulse.log", "settings.json"], model.Files.Select(f => f.Name));
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.T("Report.File.Settings"));
            Assert.Equal(model.Summary, Named<TextBlock>(window, "SummaryLine").Text);
            var logs = Named<CheckBox>(window, "GameLogsSwitch");
            Assert.False(logs.IsEnabled);
            Assert.Equal(Loc.T("Report.GameLogs"), logs.Content);
            Assert.True(Named<TextBlock>(window, "GameLogsNoteLine").IsEffectivelyVisible);
            Assert.Equal(Loc.T("Report.GameLogsMissing"), Named<TextBlock>(window, "GameLogsNoteLine").Text);
            Assert.True(Named<Button>(window, "SaveButton").IsEnabled);
            Assert.False(Named<TextBlock>(window, "StatusLine").IsEffectivelyVisible);
            Assert.False(Named<Button>(window, "OpenFormButton").IsEffectivelyVisible);

            var save = model.SaveAsync(() => Task.FromResult<Stream>(new MemoryStream()), "report.zip");
            Pump(() => save.IsCompleted);
            Assert.True(save.IsCompletedSuccessfully);
            window.UpdateLayout();

            var status = Named<TextBlock>(window, "StatusLine");
            Assert.True(status.IsEffectivelyVisible);
            Assert.StartsWith(Loc.F("Report.Saved", "report.zip", "§").Split('§')[0], status.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("error", status.Classes);
            Assert.True(Named<Button>(window, "OpenFormButton").IsEffectivelyVisible);
            Assert.Equal(Loc.T("Report.OpenForm"), Named<Button>(window, "OpenFormButton").Content);
            Assert.False(Named<Button>(window, "OpenFolderButton").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            aboutWindow.Close();
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(data, recursive: true);
        }
    });

    /// <summary>Runs the UI thread's queue until <paramref name="done"/> (the model's work goes to the pool and back).</summary>
    private static void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Dispatcher.UIThread.RunJobs();
    }
}

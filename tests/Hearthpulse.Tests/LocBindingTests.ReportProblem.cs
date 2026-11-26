using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Report;
using Hearthpulse.UI.Main.SettingsTabs;

namespace Hearthpulse.Tests;

/// <summary>
/// "Report a problem" (AC6 of TASK-2026-11-26-release-cleanup) in the Avalonia views: the Feedback card with its button,
/// and the window's file list, game logs switch and note, save button, status line and, after a save, the form's button.
/// The model's work runs on this thread (BackgroundWork.Inline).
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
        var model = new ReportProblemViewModel(data, null, "0.16.0") { FindGameLogs = _ => GameLogsLookup.NotFound, Background = BackgroundWork.Inline };
        var window = new ReportProblemWindow(model);
        try
        {
            aboutWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<Border>(about, "FeedbackCard").IsEffectivelyVisible);
            var open = Named<Button>(about, "ReportProblemButton");
            Assert.Equal(Loc.T("Report.Open"), open.Content);
            Assert.Equal(Loc.T("Report.OpenTip"), ToolTip.GetTip(open));

            // As the app does: the list is read before the window shows.
            Assert.True(window.LoadAsync().IsCompletedSuccessfully);
            Show(window);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(Loc.T("Report.Title"), window.Title);
            var list = Named<ItemsControl>(window, "FileList");
            Assert.Equal(model.Files, list.ItemsSource!.Cast<ReportFileRow>());
            Assert.Equal(["system.txt", "hearthpulse.log", "settings.json"], model.Files.Select(f => f.Name));
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.T("Report.File.Settings"));
            // Review M1: the list scrolls within a limit instead of pushing the buttons off a short screen.
            var scroll = Named<ScrollViewer>(window, "FileScroll");
            Assert.Contains(list, scroll.GetLogicalDescendants());
            Assert.InRange(scroll.MaxHeight, 1, 400);
            Assert.Equal(model.Summary, Named<TextBlock>(window, "SummaryLine").Text);
            var logs = Named<CheckBox>(window, "GameLogsSwitch");
            Assert.False(logs.IsEnabled);
            Assert.Equal(Loc.T("Report.GameLogs"), logs.Content);
            Assert.True(Named<TextBlock>(window, "GameLogsNoteLine").IsEffectivelyVisible);
            Assert.Equal(Loc.T("Report.GameLogsMissing"), Named<TextBlock>(window, "GameLogsNoteLine").Text);
            Assert.True(Named<Button>(window, "SaveButton").IsEnabled);
            // The status line is in the tree before it has text, so a screen reader hears the text arrive.
            var status = Named<TextBlock>(window, "StatusLine");
            Assert.True(status.IsEffectivelyVisible);
            Assert.True(string.IsNullOrEmpty(status.Text));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.False(Named<Button>(window, "OpenFormButton").IsEffectivelyVisible);

            Assert.True(model.SaveAsync(() => Task.FromResult<Stream>(new MemoryStream()), "report.zip").IsCompletedSuccessfully);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

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
}

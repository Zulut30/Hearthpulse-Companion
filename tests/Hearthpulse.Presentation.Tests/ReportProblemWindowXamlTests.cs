using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfReport = Hearthpulse.App.Main.Report.ReportProblemViewModel;
using WpfReportWindow = Hearthpulse.App.Main.Report.ReportProblemWindow;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// "Report a problem" (roadmap E1, AC6 of TASK-2026-11-26-release-cleanup) in the WPF views, built off screen with a
/// non-default theme: the BETA flag is a button with the action in its name and tip, Settings → About has the Feedback
/// card, and the window binds its list, switch, note, save button and, after a save, the status and the form's button.
/// </summary>
public sealed class ReportProblemWindowXamlTests
{
    [Fact]
    public void Beta_flag_about_card_and_window_bind() => OnSta(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Frost", 0, 0, 1)));
        var model = new WpfMain(new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db")), new AppSettings(), TestApp.Data);

        var main = new WpfWindow(model);
        var beta = (Button)main.FindName("BetaLink");
        Assert.Equal(Loc.T("Main.BetaReport"), AutomationProperties.GetName(beta));
        Assert.Equal(Loc.T("Main.BetaTip"), beta.ToolTip);
        Assert.Equal("BetaBadge", ((FrameworkElement)beta.Content).Name);
        main.ShowPage("Settings");
        main.ShowSettingsTab("About");
        Layout((FrameworkElement)main.Content, 1260, 840);
        var about = main.SettingsAbout;
        Assert.Equal(Visibility.Visible, ((Border)about.FindName("FeedbackCard")).Visibility);
        var open = (Button)about.FindName("ReportProblemButton");
        Assert.Equal(Loc.T("Report.Open"), open.Content);
        Assert.Equal(Loc.T("Report.OpenTip"), open.ToolTip);
        main.Close();

        var data = Directory.CreateTempSubdirectory("hp-report-wpf-").FullName;
        File.WriteAllText(Path.Combine(data, "hearthpulse.log"), "INFO Started\n");
        var report = new WpfReport(data, null, "0.16.0") { FindGameLogs = _ => GameLogsLookup.NotFound };
        var window = new WpfReportWindow(report);
        var load = window.LoadAsync();
        Pump(() => load.IsCompleted);
        Assert.True(load.IsCompletedSuccessfully);
        Layout((FrameworkElement)window.Content, 640, 900);

        Assert.Equal(Loc.T("Report.Title"), window.Title);
        var list = (ItemsControl)window.FindName("FileList");
        Assert.Equal(["system.txt", "hearthpulse.log"], list.Items.Cast<Hearthpulse.App.Main.Report.ReportFileRow>().Select(r => r.Name));
        Assert.Equal(report.Summary, ((TextBlock)window.FindName("SummaryLine")).Text);
        var logs = (CheckBox)window.FindName("GameLogsSwitch");
        Assert.False(logs.IsEnabled);
        Assert.Equal(Loc.T("Report.GameLogsMissing"), ((TextBlock)window.FindName("GameLogsNoteLine")).Text);
        Assert.True(((Button)window.FindName("SaveButton")).IsEnabled);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("StatusLine")).Visibility);

        var save = report.SaveAsync(() => Task.FromResult<Stream>(new MemoryStream()), "report.zip");
        Pump(() => save.IsCompleted);
        Assert.True(save.IsCompletedSuccessfully);
        Layout((FrameworkElement)window.Content, 640, 900);

        var status = (TextBlock)window.FindName("StatusLine");
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.Equal(report.Status, status.Text);
        Assert.Same(window.FindResource("Text"), status.Foreground);
        Assert.Equal(Visibility.Visible, ((WrapPanel)window.FindName("SavedActions")).Visibility);
        Assert.Equal(Loc.T("Report.OpenForm"), ((Button)window.FindName("OpenFormButton")).Content);
        Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("OpenFolderButton")).Visibility);
        window.Close();
        Directory.Delete(data, recursive: true);
    });

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        Pump(() => true);
        root.UpdateLayout();
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/>: the model's work goes to the pool and comes back here.</summary>
    private static void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        do
        {
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            if (done())
            {
                return;
            }

            Thread.Sleep(5);
        }
        while (DateTime.UtcNow < until);
    }

    private static void OnSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                check();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Report a problem check failed", failure);
        }
    }
}

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
        var report = new WpfReport(data, null, "0.16.0") { FindGameLogs = _ => GameLogsLookup.NotFound, Background = BackgroundWork.Inline };
        var window = new WpfReportWindow(report);
        // The model's work runs on this thread (BackgroundWork.Inline): the list is read when the call returns.
        var load = window.LoadAsync();
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
        // The status line is in the tree before it has text, so a screen reader hears the text arrive.
        var status = (TextBlock)window.FindName("StatusLine");
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.Equal(string.Empty, status.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));

        var save = report.SaveAsync(() => Task.FromResult<Stream>(new MemoryStream()), "report.zip");
        Assert.True(save.IsCompletedSuccessfully);
        Layout((FrameworkElement)window.Content, 640, 900);

        Assert.Equal(report.Status, status.Text);
        Assert.StartsWith("Archive saved: report.zip (", status.Text, StringComparison.Ordinal);
        Assert.Same(window.FindResource("Text"), status.Foreground);
        Assert.Equal(Visibility.Visible, ((WrapPanel)window.FindName("SavedActions")).Visibility);
        Assert.Equal(Loc.T("Report.OpenForm"), ((Button)window.FindName("OpenFormButton")).Content);
        Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("OpenFolderButton")).Visibility);
        window.Close();
        Directory.Delete(data, recursive: true);
    });

    /// <summary>
    /// Review M1: the window never grows past the screen. The list sits in a ScrollViewer with a height limit, and with
    /// every row of the archive (six) and a save's status, a window held to 560 high (150 % on 1080p leaves 688) keeps
    /// the save button inside it: only the list's row gives way and scrolls.
    /// </summary>
    [Fact]
    public void A_short_screen_shrinks_the_list_not_the_buttons() => OnSta(() =>
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
        var root = Directory.CreateTempSubdirectory("hp-report-wpf-short-").FullName;
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(data, "crash-reports"));
        File.WriteAllText(Path.Combine(data, "hearthpulse.log"), "INFO\n");
        File.WriteAllText(Path.Combine(data, "hearthpulse.old.log"), "INFO\n");
        File.WriteAllText(Path.Combine(data, "settings.json"), "{ \"Theme\": \"Tavern\" }");
        File.WriteAllText(Path.Combine(data, "crash-reports", "a.json"), "{}");
        var session = Path.Combine(root, "Hearthstone_2026_11_25_18_02_11");
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "Power.log"), "D CREATE_GAME\n");
        var report = new WpfReport(data, null, "0.16.0") { FindGameLogs = _ => new GameLogsLookup(true, session), Background = BackgroundWork.Inline };
        var window = new WpfReportWindow(report);
        Assert.True(window.LoadAsync().IsCompletedSuccessfully);
        report.IncludeGameLogs = true;
        Assert.True(report.SaveAsync(() => Task.FromResult<Stream>(new MemoryStream()), "report.zip").IsCompletedSuccessfully);

        var content = (FrameworkElement)window.Content;
        Layout(content, 640, 560);

        var scroll = (ScrollViewer)window.FindName("FileScroll");
        Assert.Equal(6, ((ItemsControl)window.FindName("FileList")).Items.Count);
        Assert.True(scroll.MaxHeight < 560, scroll.MaxHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(scroll.ScrollableHeight > 0, "the list scrolls in a short window");
        var save = (Button)window.FindName("SaveButton");
        var bottom = save.TranslatePoint(new Point(0, save.ActualHeight), content).Y;
        Assert.InRange(bottom, 1, 560);
        window.Close();
        Directory.Delete(root, recursive: true);
    });

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        // One pass of the dispatcher for the templates the layout queued; no waiting on the model.
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        root.UpdateLayout();
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

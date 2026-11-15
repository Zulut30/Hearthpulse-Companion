using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;

namespace Hearthpulse.App;

/// <summary>
/// What the host does with an error nobody caught (docs/crash-reports.md): the log, the report the player agreed to and
/// the error window, for the UI thread, a dying background thread and a forgotten task.
/// </summary>
public partial class App
{
    // Crash reports the player agreed to (plan 0.5); null until the services are built, then errors are only logged.
    private Hearthpulse.Core.Diagnostics.CrashReporter? _crashReporter;

    /// <summary>Hooked first thing at start, before the settings load: an error while the app starts is caught too.</summary>
    private void WatchForErrors()
    {
        // Every unexpected error is logged; the player sees a window (Open log / Continue) the first time a kind of error
        // happens, instead of a silent stop or nothing at all. Not while rendering pages off-screen (--render-ui).
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (CrashLog.Record("UI error", args.Exception) && _renderDir is null)
            {
                // This handler runs inside the dispatcher's exception filter, where a modal window is never drawn:
                // show it once the failed operation has unwound.
                var error = args.Exception;
                var reporter = _crashReporter;
                var autoSent = reporter?.OnError("UI error", error, fatal: false) ?? false;
                _ = Dispatcher.BeginInvoke(() => CrashDialog.Show(error, fatal: false, reporter, "UI error", autoSent));
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString());
            var where = args.IsTerminating ? "Fatal error" : "Unhandled error";
            var first = CrashLog.Record(where, ex);
            var reporter = _renderDir is null ? _crashReporter : null;
            // A crash that ends the process is only queued: it goes at the next start.
            var autoSent = (first || args.IsTerminating) && (reporter?.OnError(where, ex, fatal: args.IsTerminating) ?? false);
            if (args.IsTerminating && _renderDir is null)
            {
                try
                {
                    // A background thread is dying and takes the process with it: say so before it goes.
                    // Bounded: the UI thread may itself be waiting on the dying thread, and the process must still end.
                    Dispatcher.Invoke(() => CrashDialog.Show(ex, fatal: true, reporter, where, autoSent), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(30));
                }
                catch (Exception dialogError)
                {
                    Diag.Error("Error window could not be shown", dialogError);
                }
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (CrashLog.Record("Background error", args.Exception) && _renderDir is null)
            {
                _crashReporter?.OnError("Background error", args.Exception, fatal: false);
            }

            args.SetObserved();
        };
    }
}

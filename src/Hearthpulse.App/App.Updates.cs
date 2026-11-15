using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Updates;

namespace Hearthpulse.App;

/// <summary>The host's side of updates: the update loop, its notifications and installing a downloaded update.</summary>
public partial class App
{
    // Written by the update loop on the pool, read on the UI thread.
    private volatile UpdateInfo? _stagedUpdate;
    private readonly UpdateTrigger _updateTrigger = new();
    private readonly UpdateLoop _updateLoop = new();

    /// <summary>
    /// Looks for a newer release shortly after startup and every six hours after that (with automatic updates off, only
    /// on "Check now"). A downloaded update installs by itself when the app closes. Called once at start: it also reads
    /// how the install the previous run started went, and a failure is told once, in a notification and in About.
    /// </summary>
    private void StartUpdateChecks()
    {
        if (UpdateService.TakeInstallReport() is { Succeeded: false } report)
        {
            _mainViewModel.SetInstallFailed(report.Version, report.Message);
            Notify(Loc.T("Host.Update.InstallFailedTitle"), report.Message());
        }

        if (!_settings.AutoUpdate)
        {
            _mainViewModel.SetUpdateStatus(() => Loc.T("Host.Update.Off"));
        }

        // The loop reports a version once (again only for a newer one), so each notification below is once a version;
        // results of a check, "Check now" included, go only to the status line.
        var callbacks = new UpdateCallbacks(
            // The setup would install a second copy; the portable one only says a new version is out.
            Available: info =>
            {
                _ = Dispatcher.BeginInvoke(() => _mainViewModel.SetReleaseNotes(info.Version, info.Notes, info.PageUrl));
                Notify(Loc.T("Host.Update.AvailableTitle"), Loc.F("Host.Update.AvailableZip", info.Version));
            },
            Staged: info =>
            {
                _stagedUpdate = info;
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _mainViewModel.SetUpdateReady(info.Version);
                    _mainViewModel.SetReleaseNotes(info.Version, info.Notes, info.PageUrl);
                });
                Notify(Loc.T("Host.Update.ReadyTitle"), Loc.F("Host.Update.Ready", info.Version));
            },
            // Failed check, unverifiable file, failed download or a version left to a manual install: About only.
            Failed: status => _ = Dispatcher.BeginInvoke(() => _mainViewModel.SetUpdateStatus(status)),
            Checked: check => _ = Dispatcher.BeginInvoke(() => _mainViewModel.UpdateCheckFinished(() => check.Message)));
        _mainViewModel.UpdateCheckRequested += () =>
        {
            if (!_updateTrigger.Request())
            {
                _mainViewModel.UpdateCheckRefused(_updateTrigger.RefusalMessage);
            }
        };
        _updateLoop.Start(stop => UpdateService.WatchAsync(_http, AppPaths.IsPortable, _settings.AutoUpdate, callbacks, _updateTrigger, stop));
    }

    /// <summary>Stops the update loop and waits for it (bounded) before a staged update is installed: a download still
    /// running would otherwise replace the staged file under the installer. The loop stays off for this run.</summary>
    private void StopUpdateLoop()
    {
        if (!_updateLoop.Stop(UpdateLoop.StopWait))
        {
            Diag.Info($"Update: the update loop did not end within {UpdateLoop.StopWait.TotalSeconds:0} s; installing anyway.");
        }
    }

    /// <summary>The "Update ready" chip: stop the update loop, install the downloaded update now and start the new version.</summary>
    private void OnInstallUpdateRequested()
    {
        if (_stagedUpdate is null)
        {
            return;
        }

        StopUpdateLoop();
        // Read after the loop has stopped: the newest staged version, whose file nothing deletes any more.
        if (_stagedUpdate is not { } staged || !UpdateService.ApplyStaged(staged, relaunch: true))
        {
            return;
        }

        _stagedUpdate = null;
        Quit();
    }
}

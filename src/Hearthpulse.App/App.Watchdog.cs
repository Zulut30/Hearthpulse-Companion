using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Logs;

namespace Hearthpulse.App;

/// <summary>
/// Watches the app's own health while it runs: the log pipeline (a stuck watcher is restarted, a stalled game is read
/// again; also the manual hard refresh) and how fast the UI thread answers.
/// </summary>
public partial class App
{
    private DispatcherTimer? _watchdog;
    private long _lastPending = -1;
    private DateTime _stallSince;
    private DateTime _lastRecovery;
    private DateTime _lastRestart;

    // Diagnostics: how fast the UI thread shows snapshots and answers at all.
    private System.Threading.Timer? _uiProbe;
    private long _probeSentAt;
    private long _lastFreezeNote;
    private long _slowestUi;
    private int _watchTicks;
    private bool _capNotified;

    /// <summary>
    /// Watches the UI thread from a background thread. When the window stops answering, this writes it to the log while
    /// it happens; a frozen window could never write that line itself.
    /// </summary>
    private void ProbeUiThread(object? state)
    {
        var now = Environment.TickCount64;
        var sent = Volatile.Read(ref _probeSentAt);
        var snapshotWait = _feed.Waiting;
        if (((sent != 0 && now - sent > 5000) || snapshotWait > 5000) && now - _lastFreezeNote > 10_000)
        {
            _lastFreezeNote = now;
            Diag.Info($"UI thread slow: {(sent != 0 ? $"not answering for {now - sent:N0} ms" : "answering")}, newest snapshot waiting {snapshotWait:N0} ms to be shown");
        }

        if (sent != 0)
        {
            return;
        }

        Volatile.Write(ref _probeSentAt, now);
        _ = Dispatcher.BeginInvoke(() =>
        {
            var lag = Environment.TickCount64 - Volatile.Read(ref _probeSentAt);
            Volatile.Write(ref _probeSentAt, 0);
            _slowestUi = Math.Max(_slowestUi, lag);
            if (lag > 1000)
            {
                Diag.Info($"UI thread answered after {lag:N0} ms");
            }
        }, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Re-reads the log and rebuilds the tracked game: the manual recovery for an overlay frozen on an old turn.
    /// It also un-hides a suppressed overlay and re-asserts topmost, so a vanished overlay comes back with it.
    /// </summary>
    private void HardRefresh(string reason, bool quiet = false)
    {
        if (_watcher is null)
        {
            return;
        }

        Diag.Info($"Hard refresh ({reason})");
        _watcher.RequestResync(replayCurrentGame: true);
        _overlay.Suppressed = false;
        _overlay.ReassertTopmost();
        _lastPending = -1;
        _stallSince = default;
        if (!quiet)
        {
            Notify(Loc.T("Host.Notify.RefreshedTitle"), Loc.T("Host.Notify.Refreshed"));
        }
    }

    /// <summary>
    /// Watches the log pipeline itself. A watcher that stops polling was killed by an error on a background
    /// thread; a log that keeps growing while nothing is delivered means the parser is wedged. Either way the
    /// overlay would stay frozen, so the pipeline is restarted or the current game is replayed.
    /// </summary>
    private void WatchPipeline()
    {
        if (_watcher is null)
        {
            return;
        }

        if (++_watchTicks % 8 == 0)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var (applied, slowestApply, longestWait) = _feed.TakeStats();
            Diag.Debug($"Window: showed {applied} snapshots (BG turn {_feed.Last.Battlegrounds?.Turn}), slowest {slowestApply:N0} ms, "
                       + $"longest wait {longestWait:N0} ms, slowest UI answer {_slowestUi:N0} ms, overlay {(_overlay.Suppressed ? "hidden" : "on")}, "
                       + $"RAM {process.WorkingSet64 / (1024 * 1024):N0} MB, managed {GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
            _slowestUi = 0;
        }

        if (_watcher.PowerLogCapped)
        {
            if (!_capNotified)
            {
                _capNotified = true;
                var install = _installDir;
                Task.Run(() => LogConfig.EnsureAll(install)).Forget("log.config update");
                Notify(Loc.T("Host.Notify.RestartTitle"), Loc.T("Host.Notify.LogCapped"));
            }
        }
        else
        {
            _capNotified = false;
        }

        if (_watcher.LastProgressAgeMs > 15_000)
        {
            // A long replay delivers batches and keeps the clock moving, so only a pass stuck in one place gets here.
            // At most one restart a minute: restarting a watcher that is still busy only adds work.
            if (DateTime.UtcNow - _lastRestart >= TimeSpan.FromSeconds(60))
            {
                _lastRestart = DateTime.UtcNow;
                Diag.Info($"Log watcher stuck for {_watcher.LastProgressAgeMs / 1000}s while {_watcher.Stage}; restarting it.");
                RestartWatcher();
            }

            return;
        }

        if (_watcher.LastPollAgeMs > 2_000)
        {
            // A pass is still running (a replay or a big batch): that is busy, not stalled. Replaying the game now
            // would only pile more work on a slow PC.
            _stallSince = default;
            return;
        }

        var pending = _watcher.PendingBytes;
        if (pending <= 0 || pending < _lastPending)
        {
            // Caught up, or still draining a backlog (progress) — not a stall.
            _stallSince = default;
            _lastPending = pending;
            return;
        }

        _lastPending = pending;
        if (_stallSince == default)
        {
            _stallSince = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _stallSince < TimeSpan.FromSeconds(12) || DateTime.UtcNow - _lastRecovery < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastRecovery = DateTime.UtcNow;
        Diag.Info($"Log pipeline stalled with {pending} unread bytes; re-reading the current game.");
        HardRefresh("automatic recovery", quiet: true);
    }

    private void RestartWatcher()
    {
        try
        {
            _watcher?.Dispose();
        }
        catch (Exception ex)
        {
            Diag.Error("Log watcher restart failed", ex);
        }

        _watcher = new LogWatcher(_session, () => _installDir);
        _watcher.Start();
        _lastPending = -1;
        _stallSince = default;
    }
}

using Avalonia.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;

namespace Hearthpulse.UI;

/// <summary>
/// Reads the data API's freshness summary (Core/Api/DataHealth.cs) for the "Data status" card, the pages' delay lines
/// and the overlay's date lines: at start, every 30 minutes, and on "Check now".
/// </summary>
public sealed partial class AppHost
{
    private DataHealth? _dataHealth;
    private DispatcherTimer? _dataHealthRefresh;
    private bool _dataHealthStopped;

    /// <summary>Hooks "Check now" and the 30-minute refresh; the first summary is read with the community stats.</summary>
    private void StartDataHealth()
    {
        _dataHealth = new DataHealth(Api);
        Main.DataHealthCheckRequested += () => LoadDataHealthAsync(force: true).Forget("Data health check");
        _dataHealthRefresh = new DispatcherTimer { Interval = DataHealth.MaxAge };
        _dataHealthRefresh.Tick += (_, _) => LoadDataHealthAsync().Forget("Data health refresh");
        _dataHealthRefresh.Start();
    }

    /// <summary>The app is closing: no further refresh, the load that is running is cancelled and its answer dropped.</summary>
    private void StopDataHealth()
    {
        _dataHealthStopped = true;
        _dataHealthRefresh?.Stop();
        _dataHealth?.Dispose();
    }

    /// <summary>
    /// The summary goes to both view models on the UI thread; the read itself runs on the thread pool, one at a time,
    /// and never throws. The view models keep the newest answer, whatever order two loads come back in.
    /// </summary>
    public async Task LoadDataHealthAsync(bool force = false)
    {
        if (_dataHealthStopped || _dataHealth is not { } health)
        {
            return;
        }

        var report = await health.LoadAsync(force);
        if (_dataHealthStopped)
        {
            return;
        }

        Main.SetDataHealth(report);
        Overlay.SetDataHealth(report);
        RefreshOverlay();
    }
}

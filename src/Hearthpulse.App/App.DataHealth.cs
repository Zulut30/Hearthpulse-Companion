using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;

namespace Hearthpulse.App;

/// <summary>
/// Reads the data API's freshness summary (Core/Api/DataHealth.cs) for the "Data status" card, the pages' delay lines
/// and the overlay's date lines: at start, every 30 minutes, and on "Check now".
/// </summary>
public partial class App
{
    private DataHealth? _dataHealth;
    private DispatcherTimer? _dataHealthRefresh;

    /// <summary>Hooks "Check now" and the 30-minute refresh; the first summary is read with the community stats.</summary>
    private void StartDataHealth()
    {
        _mainViewModel.DataHealthCheckRequested += () => LoadDataHealthAsync(force: true).Forget("Data health check");
        _dataHealthRefresh = new DispatcherTimer { Interval = DataHealth.MaxAge };
        _dataHealthRefresh.Tick += (_, _) => LoadDataHealthAsync().Forget("Data health refresh");
        _dataHealthRefresh.Start();
    }

    /// <summary>The summary goes to both view models on the UI thread; the read itself runs on the thread pool and never throws.</summary>
    private async Task LoadDataHealthAsync(bool force = false)
    {
        var report = await (_dataHealth ??= new DataHealth(_api)).LoadAsync(force);
        _mainViewModel.SetDataHealth(report);
        _overlayViewModel.SetDataHealth(report);
        RefreshOverlay();
    }
}

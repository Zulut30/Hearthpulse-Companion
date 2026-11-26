using Avalonia.Interactivity;
using Hearthpulse.UI.Main.Report;

namespace Hearthpulse.UI.Main;

/// <summary>The BETA flag opens "Report a problem" (roadmap E1, K2), as Settings → About's button does.</summary>
public partial class MainView
{
    private void OnReportProblem(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { } model)
        {
            ReportProblemWindow.Open(this, model.ReportProblem());
        }
    }
}

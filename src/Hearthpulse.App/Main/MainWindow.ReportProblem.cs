using System.Windows;
using Hearthpulse.App.Main.Report;

namespace Hearthpulse.App.Main;

/// <summary>The BETA flag opens "Report a problem" (roadmap E1, K2), as Settings → About's button does.</summary>
public partial class MainWindow
{
    private void OnReportProblem(object sender, RoutedEventArgs e) => ReportProblemWindow.Open(this, _viewModel.ReportProblem());
}

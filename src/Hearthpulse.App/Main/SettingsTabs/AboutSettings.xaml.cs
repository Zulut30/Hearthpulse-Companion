using System.Windows;
using System.Windows.Controls;
using Hearthpulse.App.Main.Report;

namespace Hearthpulse.App.Main.SettingsTabs;

/// <summary>Settings, About: version, updates, licences, feedback (its data: <see cref="MainViewModel"/>).</summary>
public partial class AboutSettings : UserControl
{
    public AboutSettings() => InitializeComponent();

    /// <summary>"Report a problem" opens its window over this one, on a fresh model (roadmap E1).</summary>
    private void OnReportProblem(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            ReportProblemWindow.Open(Window.GetWindow(this), model.ReportProblem());
        }
    }
}

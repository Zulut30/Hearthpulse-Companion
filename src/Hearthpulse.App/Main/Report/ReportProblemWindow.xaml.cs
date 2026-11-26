using System.Windows;
using System.Windows.Media;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Microsoft.Win32;

namespace Hearthpulse.App.Main.Report;

/// <summary>
/// "Report a problem" (roadmap E1, docs/report-a-problem.md): what the diagnostics archive holds, the save dialog, then
/// the folder and GitHub's issue form. Opened from Settings → About and the BETA flag; it sends nothing.
/// </summary>
public partial class ReportProblemWindow : Window
{
    private readonly ReportProblemViewModel _model;

    public ReportProblemWindow(ReportProblemViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        _model = model;
        DataContext = model;
        Closed += (_, _) => model.Detach();
    }

    /// <summary>Opens the window over <paramref name="owner"/> (not modal) and lists the archive's files.</summary>
    public static ReportProblemWindow Open(Window? owner, ReportProblemViewModel model)
    {
        var window = new ReportProblemWindow(model) { Owner = owner };
        window.Show();
        window.LoadAsync().Forget("Report a problem: listing");
        return window;
    }

    /// <summary>Lists the files, with the monitors as Windows reports them and the main window's scale.</summary>
    public Task LoadAsync() => _model.LoadAsync(DisplayList.All(), VisualTreeHelper.GetDpi(Owner ?? this).DpiScaleX);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = _model.SuggestedFileName,
            Filter = Loc.T("Report.FileFilter") + " (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _model.SaveAsync(dialog.FileName).Forget("Report a problem: saving");
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

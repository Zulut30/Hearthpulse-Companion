using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Hearthpulse.App.Main.Report;

/// <summary>
/// "Report a problem" (roadmap E1, docs/report-a-problem.md): what the diagnostics archive holds, the save dialog, then
/// the folder and GitHub's issue form. Opened from Settings → About and the BETA flag; it sends nothing. It is shown once
/// its list is read, so it is centred on its real height, and it stays inside the work area of its screen as it grows.
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
        model.PropertyChanged += OnModelChanged;
        SizeChanged += (_, _) => KeepOnScreen();
        Closed += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            model.Detach();
        };
    }

    /// <summary>
    /// Reads the list, then shows the window over <paramref name="owner"/> (not modal): centred on the height it will
    /// keep, and no taller than <paramref name="owner"/>'s screen allows.
    /// </summary>
    public static async Task<ReportProblemWindow> OpenAsync(Window? owner, ReportProblemViewModel model)
    {
        var window = new ReportProblemWindow(model) { Owner = owner };
        if (owner is not null && WorkArea(owner) is { } area)
        {
            window.MaxHeight = area.Height;
        }

        await window.LoadAsync();
        window.Show();
        return window;
    }

    /// <summary>Lists the files, with the monitors as Windows reports them and the main window's scale.</summary>
    public Task LoadAsync() => _model.LoadAsync(DisplayList.All(), VisualTreeHelper.GetDpi(Owner ?? this).DpiScaleX);

    /// <summary>A save or a list that made the window taller moves it up rather than past the screen's bottom edge.</summary>
    private void KeepOnScreen()
    {
        if (!IsVisible || WorkArea(this) is not { } area)
        {
            return;
        }

        MaxHeight = area.Height;
        if (Top + ActualHeight > area.Bottom)
        {
            Top = Math.Max(area.Top, area.Bottom - ActualHeight);
        }
    }

    /// <summary>The work area of the screen <paramref name="window"/> is on, in its device-independent units.</summary>
    private static Rect? WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }

    /// <summary>The status line is a live region: WPF announces its new text only when told (better-accessibility).</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReportProblemViewModel.Status) || _model.Status.Length == 0)
        {
            return;
        }

        StatusLine.UpdateLayout();
        var peer = UIElementAutomationPeer.FromElement(StatusLine) ?? UIElementAutomationPeer.CreatePeerForElement(StatusLine);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hearthpulse.Core;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.UI.Main.Report;

/// <summary>
/// "Report a problem" (roadmap E1, docs/report-a-problem.md): what the diagnostics archive holds, the save dialog, then
/// the folder and GitHub's issue form. Opened from Settings → About and the BETA flag; it sends nothing.
/// </summary>
public partial class ReportProblemWindow : Window
{
    private readonly ReportProblemViewModel _model = null!;
    private readonly TopLevel? _owner;

    /// <summary>For the XAML loader only.</summary>
    public ReportProblemWindow() => InitializeComponent();

    public ReportProblemWindow(ReportProblemViewModel model, TopLevel? owner = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        _model = model;
        _owner = owner;
        DataContext = model;
        Closed += (_, _) => model.Detach();
    }

    /// <summary>Opens the window over <paramref name="owner"/>'s window (not modal) and lists the archive's files.</summary>
    public static ReportProblemWindow Open(Control? owner, ReportProblemViewModel model)
    {
        var parent = TopLevel.GetTopLevel(owner);
        var window = new ReportProblemWindow(model, parent);
        if (parent is Window main)
        {
            window.Show(main);
        }
        else
        {
            window.Show();
        }

        window.LoadAsync().Forget("Report a problem: listing");
        return window;
    }

    /// <summary>Lists the files, with the monitors as the system reports them and the main window's scale.</summary>
    public Task LoadAsync()
    {
        var displays = Screens.All.Select(s => new DisplayInfo(s.Bounds.Width, s.Bounds.Height, s.Scaling, s.IsPrimary)).ToList();
        return _model.LoadAsync(displays, (_owner ?? this).RenderScaling);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => SaveAsync().Forget("Report a problem: saving");

    private async Task SaveAsync()
    {
        if (StorageProvider is not { CanSave: true } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Report.Title"),
            SuggestedFileName = _model.SuggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType(Loc.T("Report.FileFilter")) { Patterns = ["*.zip"], MimeTypes = ["application/zip"] }],
            SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
        });
        if (file is null)
        {
            return;
        }

        try
        {
            // A file on this computer is written by path (and can be shown in its folder); another storage gives a stream.
            await (file.TryGetLocalPath() is { } path ? _model.SaveAsync(path) : _model.SaveAsync(file.OpenWriteAsync, file.Name));
        }
        finally
        {
            file.Dispose();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

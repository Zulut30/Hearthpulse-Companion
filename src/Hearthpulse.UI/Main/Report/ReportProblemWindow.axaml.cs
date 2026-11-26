using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Hearthpulse.Core;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.UI.Main.Report;

/// <summary>
/// "Report a problem" (roadmap E1, docs/report-a-problem.md): what the diagnostics archive holds, the save dialog, then
/// the folder and GitHub's issue form. Opened from Settings → About and the BETA flag; it sends nothing. It is shown once
/// its list is read, so it is centred on its real height, and it stays inside the work area of its screen as it grows.
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
        SizeChanged += (_, _) => KeepOnScreen();
        Closed += (_, _) => model.Detach();
    }

    /// <summary>
    /// Reads the list, then shows the window over <paramref name="owner"/>'s window (not modal): centred on the height it
    /// will keep, and no taller than that window's screen allows.
    /// </summary>
    public static async Task<ReportProblemWindow> OpenAsync(Control? owner, ReportProblemViewModel model)
    {
        var parent = TopLevel.GetTopLevel(owner);
        var window = new ReportProblemWindow(model, parent);
        if (parent is Window main && ScreenOf(main) is { } screen)
        {
            window.MaxHeight = screen.WorkingArea.Height / screen.Scaling;
        }

        await window.LoadAsync();
        if (parent is Window ownerWindow)
        {
            window.Show(ownerWindow);
        }
        else
        {
            window.Show();
        }

        return window;
    }

    /// <summary>Lists the files, with the monitors as the system reports them and the main window's scale.</summary>
    public Task LoadAsync()
    {
        var screens = ((_owner as WindowBase) ?? this).Screens.All;
        var displays = screens.Select(s => new DisplayInfo(s.Bounds.Width, s.Bounds.Height, s.Scaling, s.IsPrimary)).ToList();
        return _model.LoadAsync(displays, (_owner ?? this).RenderScaling);
    }

    /// <summary>A save or a list that made the window taller moves it up rather than past the screen's bottom edge.</summary>
    private void KeepOnScreen()
    {
        if (!IsVisible || ScreenOf(this) is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        MaxHeight = area.Height / screen.Scaling;
        var height = (int)Math.Ceiling((FrameSize?.Height ?? Bounds.Height) * DesktopScaling);
        if (Position.Y + height > area.Bottom)
        {
            Position = new PixelPoint(Position.X, Math.Max(area.Y, area.Bottom - height));
        }
    }

    /// <summary>The screen under the window's top-left corner, else the primary one; null where there are none (headless).</summary>
    private static Screen? ScreenOf(Window window)
    {
        var all = window.Screens.All;
        var corner = new PixelPoint(window.Position.X + 1, window.Position.Y + 1);
        return all.FirstOrDefault(s => s.Bounds.Contains(corner)) ?? all.FirstOrDefault(s => s.IsPrimary) ?? all.FirstOrDefault();
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

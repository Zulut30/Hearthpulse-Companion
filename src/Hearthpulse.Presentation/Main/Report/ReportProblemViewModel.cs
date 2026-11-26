using System.ComponentModel;
using System.Globalization;
using System.IO;
using Hearthpulse.Core;
using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main.Report;
#else
namespace Hearthpulse.UI.Main.Report;
#endif

/// <summary>A line of the archive's preview: the file (or folder), what it holds and its size before compression.</summary>
public sealed record ReportFileRow(string Name, string Description, string Size);

/// <summary>
/// "Report a problem" (roadmap E1, docs/report-a-problem.md), opened from Settings → About and the BETA flag: what the
/// diagnostics archive holds (<see cref="DiagnosticsBundle"/>), the switch for the game's logs, saving the archive where
/// the player picks, then the folder and GitHub's issue form. Nothing is sent: the player attaches the file themselves.
/// The window shows the save dialog and hands over the picked file (<see cref="SaveAsync(string)"/>).
/// </summary>
public sealed class ReportProblemViewModel : Observable
{
    private readonly string _dataFolder;
    private readonly string? _configuredInstall;
    private readonly string _version;
    private readonly string _ui;
    private DiagnosticsBundle? _bundle;
    private GameLogsLookup? _gameLogs;
    private IReadOnlyList<DiagnosticsEntry> _entries = [];
    private bool _includeGameLogs;
    private bool _gameLogsAvailable;
    private bool _loaded;
    private bool _busy;
    private bool _saved;
    private string? _savedPath;
    private Func<string>? _status;
    private bool _statusIsError;
    private int _generation;
    private RelayCommand? _openFolder;
    private RelayCommand? _openIssueForm;

    /// <param name="dataFolder">Hearthpulse's data folder (<c>MainViewModel.DataFolder</c>).</param>
    /// <param name="configuredInstall">The Hearthstone folder from the settings, if the player set one.</param>
    /// <param name="version">The app's version.</param>
    public ReportProblemViewModel(string dataFolder, string? configuredInstall, string version)
    {
        _dataFolder = dataFolder;
        _configuredInstall = configuredInstall;
        _version = version;
#if HEARTHPULSE_WPF
        _ui = "wpf";
#else
        _ui = "avalonia";
#endif
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The files of the archive as it would be saved now; empty until <see cref="LoadAsync"/> has listed them.</summary>
    public IReadOnlyList<ReportFileRow> Files => Rows();

    /// <summary>"6 files, 4.8 MB before compression"; the listing line while the files are read.</summary>
    public string Summary => !_loaded ? Loc.T("Report.Loading")
        : Loc.P("Report.Total", _entries.Count, SizeText(_entries.Sum(e => e.Bytes)));

    /// <summary>The newest Hearthstone session's logs go in too (off each time the window opens: they name players).</summary>
    public bool IncludeGameLogs
    {
        get => _includeGameLogs;
        set
        {
            if (Set(ref _includeGameLogs, value && _gameLogsAvailable))
            {
                ClearResult();
                ListAsync().Forget("Report a problem: listing");
            }
        }
    }

    /// <summary>The switch can be turned on: the newest session has the logs Hearthpulse reads.</summary>
    public bool GameLogsAvailable { get => _gameLogsAvailable; private set => Set(ref _gameLogsAvailable, value); }

    /// <summary>Under the switch: what the game's logs hold, or why there are none; empty while they are looked for.</summary>
    public string GameLogsNote => _gameLogs switch
    {
        null => string.Empty,
        { InstallFound: false } => Loc.T("Report.GameLogsMissing"),
        _ when !GameLogsAvailable => Loc.T("Report.GameLogsNone"),
        _ => Loc.T("Report.GameLogsNote"),
    };

    /// <summary>"Save archive…" works: the files are listed and nothing is being written.</summary>
    public bool CanSave => _loaded && !_busy;

    /// <summary>What the last save did, in the interface language; empty before the first save.</summary>
    public string Status => _status?.Invoke() ?? string.Empty;

    /// <summary>The last save failed: the line takes the error colour.</summary>
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    /// <summary>The archive is saved: "Open folder" and "Open the GitHub form" show.</summary>
    public bool Saved
    {
        get => _saved;
        private set
        {
            if (Set(ref _saved, value))
            {
                Raise(nameof(CanOpenFolder));
            }
        }
    }

    /// <summary>The saved file has a path on this computer to show it in its folder.</summary>
    public bool CanOpenFolder => _saved && _savedPath is not null;

    /// <summary>The name the save dialog suggests.</summary>
    public string SuggestedFileName => DiagnosticsBundle.FileName(Now());

    /// <summary>Shows the saved archive in its folder (selected where the system can).</summary>
    public RelayCommand OpenFolder => _openFolder ??= new RelayCommand(() =>
    {
        if (_savedPath is { } path && !RevealFile(path))
        {
            ShowStatus(() => Loc.T("Report.OpenFolderFailed"), isError: true);
        }
    });

    /// <summary>Opens GitHub's "new issue" page with the templates (<see cref="DiagnosticsBundle.IssueFormUrl"/>).</summary>
    public RelayCommand OpenIssueForm => _openIssueForm ??= new RelayCommand(() =>
    {
        if (!OpenBrowser(new Uri(DiagnosticsBundle.IssueFormUrl)))
        {
            ShowStatus(() => Loc.F("Report.OpenFormFailed", DiagnosticsBundle.IssueFormUrl), isError: true);
        }
    });

    /// <summary>Finds the game's logs (the tracker's lookup), swapped in tests.</summary>
    internal Func<string?, GameLogsLookup> FindGameLogs { get; set; } = GameLogsLookup.Find;

    /// <summary>The browser call, swapped in tests.</summary>
    internal Func<Uri, bool> OpenBrowser { get; set; } = SystemBrowser.Open;

    /// <summary>Shows a file in its folder, swapped in tests.</summary>
    internal Func<string, bool> RevealFile { get; set; } = RevealInShell;

    /// <summary>The local clock for the suggested name, swapped in tests.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>
    /// The window opened: looks for the game's logs and lists the files. <paramref name="displays"/> and
    /// <paramref name="windowScale"/> come from the UI framework and go into <c>system.txt</c>.
    /// </summary>
    public async Task LoadAsync(IReadOnlyList<DisplayInfo> displays, double windowScale)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var find = FindGameLogs;
        var install = _configuredInstall;
        var gameLogs = await Task.Run(() => find(install));
        _bundle = new DiagnosticsBundle(_dataFolder, gameLogs, new DiagnosticsContext(_version, _ui, Loc.Instance.Language, displays, windowScale));
        var bundle = _bundle;
        GameLogsAvailable = await Task.Run(() => bundle.HasGameLogs);
        _gameLogs = gameLogs;
        Raise(nameof(GameLogsNote));
        await ListAsync();
    }

    /// <summary>
    /// Writes the archive to <paramref name="path"/>, the file the player picked on this computer: "Open folder" can show
    /// it. A failed write is said in the status line and the half-written file removed.
    /// </summary>
    public Task SaveAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return SaveCoreAsync(null, Path.GetFileName(path), path);
    }

    /// <summary>
    /// Writes the archive to a storage that gives only a stream (<paramref name="open"/>, Avalonia's picker off this
    /// computer's disk); <paramref name="fileName"/> names it in the status line. There is no folder to show.
    /// </summary>
    public Task SaveAsync(Func<Task<Stream>> open, string fileName)
    {
        ArgumentNullException.ThrowIfNull(open);
        return SaveCoreAsync(open, fileName, null);
    }

    /// <summary>Creates the archive's file on this computer, swapped in tests.</summary>
    internal Func<string, Stream> CreateFile { get; set; } = File.Create;

    private async Task SaveCoreAsync(Func<Task<Stream>>? open, string fileName, string? localPath)
    {
        if (_bundle is not { } bundle || _busy)
        {
            return;
        }

        SetBusy(true);
        ClearResult();
        ShowStatus(() => Loc.T("Report.Saving"), isError: false);
        var include = IncludeGameLogs;
        // Only a file this save created or emptied is removed on a failure, never one it could not open.
        var opened = false;
        try
        {
            IReadOnlyList<DiagnosticsEntry> written;
            long size;
            var output = open is null ? CreateFile(localPath!) : await open();
            opened = true;
            await using (output)
            {
                if (output.CanSeek)
                {
                    // A longer file of the same name is replaced, not left with its old tail (a zip is read from its end).
                    output.SetLength(0);
                }

                written = await Task.Run(() => bundle.Write(output, include));
                size = output.CanSeek ? output.Length : -1;
            }

            if (size < 0 && localPath is not null && File.Exists(localPath))
            {
                size = new FileInfo(localPath).Length;
            }

            _savedPath = localPath;
            Saved = true;
            var sizeText = size >= 0 ? SizeText(size) : "?";
            ShowStatus(size > DiagnosticsBundle.AttachmentLimit
                ? () => Loc.F("Report.SavedTooLarge", fileName, sizeText)
                : () => Loc.F("Report.Saved", fileName, sizeText), isError: false);
            Diag.Info(string.Create(CultureInfo.InvariantCulture,
                $"Report a problem: archive saved, {written.Count} file(s), {size} bytes, game logs {(include ? "included" : "not included")}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Diag.Error("Report a problem: the archive was not saved", ex);
            if (opened)
            {
                RemovePartial(localPath);
            }

            ShowStatus(ex is UnauthorizedAccessException ? () => Loc.T("Report.SaveDenied") : () => Loc.T("Report.SaveFailed"), isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>The window closed: stop following the language.</summary>
    public void Detach() => Loc.Instance.LanguageChanged -= OnLanguageChanged;

    private async Task ListAsync()
    {
        if (_bundle is not { } bundle)
        {
            return;
        }

        var generation = ++_generation;
        var include = IncludeGameLogs;
        SetLoaded(false);
        var entries = await Task.Run(() => bundle.Plan(include));
        if (generation != _generation)
        {
            return; // the switch changed again while this list was read
        }

        _entries = entries;
        Raise(nameof(Files));
        SetLoaded(true);
    }

    private List<ReportFileRow> Rows()
    {
        var rows = new List<ReportFileRow>();
        foreach (var entry in _entries.Where(e => e.Part is not (DiagnosticsPart.CrashReport or DiagnosticsPart.GameLog)))
        {
            rows.Add(new ReportFileRow(entry.Name, Describe(entry.Part), SizeText(entry.Bytes)));
        }

        var reports = _entries.Where(e => e.Part == DiagnosticsPart.CrashReport).ToList();
        if (reports.Count > 0)
        {
            rows.Add(new ReportFileRow(DiagnosticsBundle.CrashReportsFolder, Loc.F("Report.File.CrashReports", reports.Count), SizeText(reports.Sum(e => e.Bytes))));
        }

        var logs = _entries.Where(e => e.Part == DiagnosticsPart.GameLog).ToList();
        if (logs.Count > 0)
        {
            var names = string.Join(", ", logs.Select(e => e.Name[DiagnosticsBundle.GameLogsFolder.Length..]));
            rows.Add(new ReportFileRow(DiagnosticsBundle.GameLogsFolder, Loc.F("Report.File.GameLogs", names), SizeText(logs.Sum(e => e.Bytes))));
        }

        return rows;
    }

    private static string Describe(DiagnosticsPart part) => part switch
    {
        DiagnosticsPart.System => Loc.T("Report.File.System"),
        DiagnosticsPart.AppLog => Loc.T("Report.File.AppLog"),
        DiagnosticsPart.OldAppLog => Loc.T("Report.File.OldAppLog"),
        DiagnosticsPart.Settings => Loc.T("Report.File.Settings"),
        _ => string.Empty,
    };

    /// <summary>"812 B", "4 KB", "1.3 MB" in the interface language and its number format.</summary>
    internal static string SizeText(long bytes) => bytes switch
    {
        < 1024 => Loc.F("Report.Size.Bytes", bytes),
        < 1024 * 1024 => Loc.F("Report.Size.Kb", Math.Max(1, (int)Math.Round(bytes / 1024.0))),
        _ => Loc.F("Report.Size.Mb", Math.Round(bytes / 1048576.0, 1)),
    };

    private void SetLoaded(bool loaded)
    {
        _loaded = loaded;
        Raise(nameof(Summary));
        Raise(nameof(CanSave));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Raise(nameof(CanSave));
    }

    private void ClearResult()
    {
        _savedPath = null;
        Saved = false;
        ShowStatus(null, isError: false);
    }

    private void ShowStatus(Func<string>? status, bool isError)
    {
        _status = status;
        StatusIsError = isError;
        Raise(nameof(Status));
    }

    private void OnLanguageChanged()
    {
        Raise(nameof(Files));
        Raise(nameof(Summary));
        Raise(nameof(GameLogsNote));
        Raise(nameof(Status));
    }

    private static void RemovePartial(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Info($"Report a problem: the partial archive was not removed ({ex.GetType().Name})");
        }
    }

    /// <summary>Opens the folder of <paramref name="file"/> with the file selected where the system can (Windows, macOS).</summary>
    private static bool RevealInShell(string file)
    {
        try
        {
            var start = OperatingSystem.IsWindows() ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"")
                : OperatingSystem.IsMacOS() ? new System.Diagnostics.ProcessStartInfo("open", ["-R", file])
                : new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(file) ?? file) { UseShellExecute = true };
            System.Diagnostics.Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Diag.Error("Report a problem: the folder did not open", ex);
            return false;
        }
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Updates;

namespace Hearthpulse.Core.Diagnostics;

/// <summary>
/// One monitor as the app's UI framework reports it: its size in pixels, its scale (1.5 = 150 %; 0 when the framework
/// does not say) and whether it is the primary one.
/// </summary>
public sealed record DisplayInfo(int Width, int Height, double Scale, bool Primary);

/// <summary>What only the running app knows about itself, for the archive's <c>system.txt</c>.</summary>
/// <param name="Version">The app's version (<c>UpdateService.CurrentVersion</c>).</param>
/// <param name="Ui">The interface: <c>wpf</c> or <c>avalonia</c>.</param>
/// <param name="Language">The interface language (<c>Loc.Language</c>).</param>
/// <param name="Displays">The monitors, as the UI framework reports them.</param>
/// <param name="WindowScale">The main window's scale (1.5 = 150 %); 0 when unknown.</param>
public sealed record DiagnosticsContext(string Version, string Ui, string Language, IReadOnlyList<DisplayInfo> Displays, double WindowScale);

/// <summary>What a file of the archive is, for its line in the preview.</summary>
public enum DiagnosticsPart
{
    /// <summary><c>system.txt</c>: the version, the OS, the monitors and what was cut or left out.</summary>
    System,

    /// <summary><c>hearthpulse.log</c>, Hearthpulse's own log.</summary>
    AppLog,

    /// <summary><c>hearthpulse.old.log</c>, the log before the last rotation.</summary>
    OldAppLog,

    /// <summary><c>settings.json</c> without keys, tokens and passwords.</summary>
    Settings,

    /// <summary>A crash report waiting to be sent (<c>crash-reports/</c>).</summary>
    CrashReport,

    /// <summary>A Hearthstone log of the newest session (<c>game-logs/</c>), only when the player asks for them.</summary>
    GameLog,
}

/// <summary>One file of the archive: its path inside the zip, what it is and its size before compression.</summary>
public sealed record DiagnosticsEntry(string Name, DiagnosticsPart Part, long Bytes);

/// <summary>The newest Hearthstone log session, or why there is none.</summary>
/// <param name="InstallFound">Hearthstone's folder was found.</param>
/// <param name="Session">The newest <c>Logs/Hearthstone_*</c> folder; null when there is none.</param>
public sealed record GameLogsLookup(bool InstallFound, string? Session)
{
    /// <summary>No Hearthstone folder: nothing to look in.</summary>
    public static GameLogsLookup NotFound { get; } = new(false, null);

    /// <summary>The newest session of the install the tracker uses (the configured folder first, as at start).</summary>
    public static GameLogsLookup Find(string? configuredInstall)
    {
        if (HearthstoneInstall.FindInstallDirectory(configuredInstall) is not { } install)
        {
            return NotFound;
        }

        try
        {
            return new GameLogsLookup(true, HearthstoneInstall.LatestLogSession(install));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Info($"Report a problem: the game's log folder was not read ({ex.GetType().Name})");
            return new GameLogsLookup(true, null);
        }
    }
}

/// <summary>
/// The diagnostics archive of "Report a problem" (roadmap E1, docs/report-a-problem.md): a zip the player saves and
/// attaches to a GitHub issue by hand. It holds <c>system.txt</c> (version, OS, monitors), Hearthpulse's two logs,
/// <c>settings.json</c> without keys, tokens and passwords, the crash reports waiting to be sent and, only when asked,
/// the logs of the newest Hearthstone session. Never <c>account.dat</c>, the history, replays or the collection. The
/// user's folder and name, e-mails, BattleTags and tokens are cut from Hearthpulse's own files; the game's logs go as
/// the game wrote them (the preview says they name players). Nothing here sends anything.
/// </summary>
/// <param name="dataFolder">Hearthpulse's data folder.</param>
/// <param name="gameLogs">Where the newest Hearthstone log session is (<see cref="GameLogsLookup.Find"/>).</param>
/// <param name="context">What the running app knows about itself.</param>
public sealed partial class DiagnosticsBundle(string dataFolder, GameLogsLookup gameLogs, DiagnosticsContext context)
{
    /// <summary>GitHub's "new issue" page with the repository's templates to choose from.</summary>
    public const string IssueFormUrl = "https://github.com/" + UpdateService.RepoOwner + "/" + UpdateService.RepoName + "/issues/new/choose";

    /// <summary>The largest file GitHub takes in an issue (other than pictures and videos): 25 MB.</summary>
    public const long AttachmentLimit = 25L * 1024 * 1024;

    public const string SystemFile = "system.txt";
    public const string AppLogFile = "hearthpulse.log";
    public const string OldAppLogFile = "hearthpulse.old.log";
    public const string SettingsFile = "settings.json";

    /// <summary>The archive's folder of the crash reports waiting to be sent.</summary>
    public const string CrashReportsFolder = CrashReporter.FolderName + "/";

    /// <summary>The archive's folder of the Hearthstone logs.</summary>
    public const string GameLogsFolder = "game-logs/";

    /// <summary>A log is cut to its last 8 MB: the app's log rotates at 4 MB, so only a broken rotation reaches this.</summary>
    internal const long DefaultAppLogCap = 8L * 1024 * 1024;

    /// <summary>A game log is cut to its last 32 MB (about 3 MB packed), so the archive stays under GitHub's limit.</summary>
    internal const long DefaultGameLogCap = 32L * 1024 * 1024;

    /// <summary>A settings file is a few kilobytes; past this it is not read.</summary>
    private const long MaxSettingsBytes = 1_000_000;

    /// <summary>A crash report is under 32 KB (<c>CrashReport.MaxJsonBytes</c>); past this it is not read.</summary>
    private const long MaxCrashReportBytes = 64 * 1024;

    /// <summary>The queue keeps five (<see cref="CrashReporter.MaxPending"/>); more is not a queue any more.</summary>
    private const int MaxCrashReports = 20;

    /// <summary>How far a cut log looks for the next line start.</summary>
    private const int LineSearch = 64 * 1024;

    internal long AppLogCap { get; init; } = DefaultAppLogCap;

    internal long GameLogCap { get; init; } = DefaultGameLogCap;

    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>The file name the save dialog suggests: <c>hearthpulse-diagnostics-2026-11-26-1530.zip</c>.</summary>
    public static string FileName(DateTime localNow) => string.Create(CultureInfo.InvariantCulture, $"hearthpulse-diagnostics-{localNow:yyyy-MM-dd-HHmm}.zip");

    /// <summary>The newest session has at least one of the logs Hearthpulse asks the game for.</summary>
    public bool HasGameLogs => GameLogPaths().Any();

    /// <summary>What the archive would hold now, for the preview: the same files and sizes <see cref="Write"/> writes.</summary>
    public IReadOnlyList<DiagnosticsEntry> Plan(bool includeGameLogs) => [.. Prepare(includeGameLogs).Select(i => i.Entry)];

    /// <summary>
    /// Writes the archive to <paramref name="output"/> (left open) and returns what went in, sizes before compression.
    /// The files are read again, so a log that grew since the preview goes in as it is now.
    /// </summary>
    /// <exception cref="IOException">The output could not be written, or a game log there could not be read.</exception>
    public IReadOnlyList<DiagnosticsEntry> Write(Stream output, bool includeGameLogs)
    {
        ArgumentNullException.ThrowIfNull(output);
        var items = Prepare(includeGameLogs);
        // Zip keeps local time without a zone: the archive shows the player's clock.
        var stamp = new DateTimeOffset(DateTime.SpecifyKind(UtcNow(), DateTimeKind.Utc)).ToLocalTime();
        var written = new List<DiagnosticsEntry>(items.Count);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var item in items)
            {
                if (item.Content is { } content)
                {
                    using var target = Open(zip, item.Entry.Name, stamp);
                    target.Write(content);
                    written.Add(item.Entry);
                    continue;
                }

                if (!File.Exists(item.Source))
                {
                    // Gone since it was measured (the game started a new session): the archive goes without it.
                    Diag.Info($"Report a problem: {item.Entry.Name} is gone, left out");
                    continue;
                }

                using var source = OpenShared(item.Source!);
                using var copy = Open(zip, item.Entry.Name, stamp);
                written.Add(item.Entry with { Bytes = CopyRange(source, item.Start, item.Entry.Bytes, copy) });
            }
        }

        return written;
    }

    private static Stream Open(ZipArchive zip, string name, DateTimeOffset stamp)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = stamp;
        return entry.Open();
    }

    /// <summary>One file of the archive: its bytes (text files, read and cleaned now) or a range of a game log.</summary>
    private sealed record Item(DiagnosticsEntry Entry, byte[]? Content, string? Source = null, long Start = 0);

    private List<Item> Prepare(bool includeGameLogs)
    {
        var notes = new List<string>();
        var items = new List<Item>();
        AddAppLog(items, notes, AppLogFile, DiagnosticsPart.AppLog);
        AddAppLog(items, notes, OldAppLogFile, DiagnosticsPart.OldAppLog);
        AddSettings(items, notes);
        AddCrashReports(items, notes);
        if (includeGameLogs)
        {
            AddGameLogs(items, notes);
        }

        var system = Encoding.UTF8.GetBytes(SystemText(includeGameLogs, notes));
        items.Insert(0, new Item(new DiagnosticsEntry(SystemFile, DiagnosticsPart.System, system.Length), system));
        return items;
    }

    private void AddAppLog(List<Item> items, List<string> notes, string name, DiagnosticsPart part)
    {
        var path = Path.Combine(dataFolder, name);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = OpenShared(path);
            var total = stream.Length;
            var start = TailStart(stream, total, AppLogCap);
            stream.Position = start;
            var bytes = new byte[total - start];
            var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            var content = Encoding.UTF8.GetBytes(Scrub(Encoding.UTF8.GetString(bytes, 0, read)));
            items.Add(new Item(new DiagnosticsEntry(name, part, content.Length), content));
            if (start > 0)
            {
                notes.Add(Cut(name, total - start, total));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"{name}: not read ({ex.GetType().Name})");
        }
    }

    private void AddSettings(List<Item> items, List<string> notes)
    {
        var path = Path.Combine(dataFolder, SettingsFile);
        string json;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                notes.Add($"{SettingsFile}: none yet (default settings)");
                return;
            }

            if (info.Length > MaxSettingsBytes)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{SettingsFile}: not included, {info.Length} bytes is not a settings file"));
                return;
            }

            using var stream = OpenShared(path);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"{SettingsFile}: not read ({ex.GetType().Name})");
            return;
        }

        if (SettingsCopy(json, Scrub) is not { } copy)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{SettingsFile}: not included, not valid JSON ({json.Length} characters)"));
            return;
        }

        var content = Encoding.UTF8.GetBytes(copy.Json);
        items.Add(new Item(new DiagnosticsEntry(SettingsFile, DiagnosticsPart.Settings, content.Length), content));
        if (copy.Unknown > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{SettingsFile}: {copy.Unknown} field(s) this version does not read left out"));
        }
    }

    private void AddCrashReports(List<Item> items, List<string> notes)
    {
        var folder = Path.Combine(dataFolder, CrashReporter.FolderName);
        FileInfo[] files;
        try
        {
            files = Directory.Exists(folder) ? new DirectoryInfo(folder).GetFiles("*.json") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"{CrashReportsFolder}: not read ({ex.GetType().Name})");
            return;
        }

        foreach (var file in files.OrderBy(f => f.Name, StringComparer.Ordinal).Take(MaxCrashReports))
        {
            try
            {
                if (file.Length > MaxCrashReportBytes)
                {
                    notes.Add($"{CrashReportsFolder}{file.Name}: not included, too large for a report");
                    continue;
                }

                using var stream = OpenShared(file.FullName);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = Encoding.UTF8.GetBytes(Scrub(reader.ReadToEnd()));
                items.Add(new Item(new DiagnosticsEntry(CrashReportsFolder + file.Name, DiagnosticsPart.CrashReport, content.Length), content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"{CrashReportsFolder}{file.Name}: not read ({ex.GetType().Name})");
            }
        }
    }

    private void AddGameLogs(List<Item> items, List<string> notes)
    {
        foreach (var path in GameLogPaths())
        {
            var name = Path.GetFileName(path);
            try
            {
                using var stream = OpenShared(path);
                var total = stream.Length;
                var start = TailStart(stream, total, GameLogCap);
                items.Add(new Item(new DiagnosticsEntry(GameLogsFolder + name, DiagnosticsPart.GameLog, total - start), null, path, start));
                if (start > 0)
                {
                    notes.Add(Cut(GameLogsFolder + name, total - start, total));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"{GameLogsFolder}{name}: not read ({ex.GetType().Name})");
            }
        }
    }

    /// <summary>The logs Hearthpulse asks the game for (<see cref="LogConfig.Sections"/>) that the newest session has.</summary>
    private IEnumerable<string> GameLogPaths() => gameLogs.Session is { } session
        ? LogConfig.Sections.Select(section => Path.Combine(session, section + ".log")).Where(File.Exists)
        : [];

    private string SystemText(bool includeGameLogs, List<string> notes)
    {
        var text = new StringBuilder();
        text.AppendLine("Hearthpulse diagnostics");
        Line(text, "Created", UtcNow().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        Line(text, "Version", context.Version);
        Line(text, "Interface", context.Ui);
        Line(text, "OS", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Line(text, "Runtime", $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        Line(text, "Language", $"{context.Language}; culture {CultureInfo.CurrentCulture.Name}, interface culture {CultureInfo.CurrentUICulture.Name}");
        Line(text, "Portable", AppPaths.IsPortable ? "yes" : "no");
        Line(text, "Displays", context.Displays.Count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < context.Displays.Count; i++)
        {
            var display = context.Displays[i];
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {i + 1}. {display.Width} x {display.Height}, scale {Percent(display.Scale)}{(display.Primary ? ", primary" : string.Empty)}"));
        }

        Line(text, "Main window scale", Percent(context.WindowScale));
        Line(text, "Hearthstone logs", GameLogsLine(includeGameLogs));
        if (notes.Count > 0)
        {
            text.AppendLine("Notes:");
            foreach (var note in notes)
            {
                text.Append("  ").AppendLine(note);
            }
        }

        return text.ToString();
    }

    private string GameLogsLine(bool includeGameLogs)
    {
        if (!gameLogs.InstallFound)
        {
            return "Hearthstone folder not found";
        }

        if (gameLogs.Session is not { } session)
        {
            return "no log session in the Hearthstone folder";
        }

        return $"newest session {Path.GetFileName(session)}, {(includeGameLogs ? "included" : "not included")}";
    }

    private static void Line(StringBuilder text, string name, string value) => text.Append(name).Append(": ").AppendLine(value);

    private static string Percent(double scale) =>
        scale > 0 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(scale * 100)} %") : "unknown";

    private static string Cut(string name, long kept, long total) => $"{name}: the last {Size(kept)} of {Size(total)}";

    private static string Size(long bytes) => bytes < 1048576
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0.#} MB");

    /// <summary>Read while Hearthpulse or the game keeps writing (and may rename) the file.</summary>
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Where the last <paramref name="cap"/> bytes start, moved on to the next line, so a cut log starts with a whole
    /// line; 0 when the file fits.
    /// </summary>
    private static long TailStart(FileStream stream, long length, long cap)
    {
        if (length <= cap)
        {
            return 0;
        }

        var start = length - cap;
        stream.Position = start;
        var buffer = new byte[(int)Math.Min(LineSearch, cap)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
        return newline >= 0 ? start + newline + 1 : start;
    }

    /// <summary>Copies <paramref name="count"/> bytes from <paramref name="start"/>, fewer when the file is shorter now.</summary>
    private static long CopyRange(FileStream source, long start, long count, Stream target)
    {
        source.Position = start;
        var buffer = new byte[81920];
        var left = count;
        while (left > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0)
            {
                break;
            }

            target.Write(buffer, 0, read);
            left -= read;
        }

        return count - left;
    }
}

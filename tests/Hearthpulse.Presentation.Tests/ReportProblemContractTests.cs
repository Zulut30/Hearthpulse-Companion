using System.Globalization;
using System.IO.Compression;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Localization;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using AvaReport = Hearthpulse.UI.Main.Report.ReportProblemViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfReport = Hearthpulse.App.Main.Report.ReportProblemViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// "Report a problem" (roadmap E1, AC6 of TASK-2026-11-26-release-cleanup) in both apps: the list of what the archive
/// holds, the game logs switch and why it may be off, saving (the archive written, a longer old file replaced), a
/// failed save, then GitHub's form and the folder. Nothing is sent: the browser and the folder are the only ways out.
/// </summary>
public sealed class ReportProblemContractTests : IDisposable
{
    private const string Form = "https://github.com/Zulut30/Hearthpulse-Companion/issues/new/choose";
    private readonly string _root = Directory.CreateTempSubdirectory("hp-report-").FullName;
    private readonly List<Uri> _browsed = [];
    private readonly List<string> _revealed = [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file the test still had open: the temp folder is cleaned by the system.
        }
    }

    private string Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private dynamic Model(bool wpf, GameLogsLookup? logs = null, bool browserOpens = true)
    {
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        dynamic model = wpf ? new WpfReport(data, null, "0.16.0") : new AvaReport(data, null, "0.16.0");
        model.FindGameLogs = new Func<string?, GameLogsLookup>(_ => logs ?? GameLogsLookup.NotFound);
        model.OpenBrowser = new Func<Uri, bool>(uri =>
        {
            _browsed.Add(uri);
            return browserOpens;
        });
        model.RevealFile = new Func<string, bool>(path =>
        {
            _revealed.Add(path);
            return true;
        });
        model.Now = new Func<DateTime>(() => new DateTime(2026, 11, 26, 15, 30, 0, DateTimeKind.Local));
        // The reading and writing run on the test's thread: each call has finished when it returns, no clock or pool.
        if (wpf)
        {
            model.Background = Hearthpulse.App.Infrastructure.BackgroundWork.Inline;
        }
        else
        {
            model.Background = Hearthpulse.UI.Infrastructure.BackgroundWork.Inline;
        }

        return model;
    }

    private static Task Load(dynamic model) => (Task)model.LoadAsync(new List<DisplayInfo> { new(1920, 1080, 1.25, true) }, 1.25);

    private static List<(string Name, string Description, string Size)> Rows(dynamic model) =>
        ((IEnumerable<object>)model.Files).Select(r => ((string)((dynamic)r).Name, (string)((dynamic)r).Description, (string)((dynamic)r).Size)).ToList();

    /// <summary>Static on purpose: a lambda cannot go into a dynamically bound call, so callers pass the model as object.</summary>
    private static Task Save(object model, Func<Task<Stream>> open, string name) => (Task)((dynamic)model).SaveAsync(open, name);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_list_names_each_file_with_what_it_holds_and_the_total(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        Put("data/hearthpulse.log", "INFO Started\n");
        Put("data/settings.json", "{ \"Theme\": \"Frost\" }");
        Put("data/crash-reports/a.json", "{}");
        Put("data/crash-reports/b.json", "{}");
        Put("data/account.dat", "session-token");
        var model = Model(wpf);
        Assert.False((bool)model.CanSave);
        Assert.Equal("Listing the files…", (string)model.Summary);
        Assert.Equal(string.Empty, (string)model.GameLogsNote);

        await Load(model);

        List<(string Name, string Description, string Size)> rows = Rows(model);
        Assert.Equal(["system.txt", "hearthpulse.log", "settings.json", "crash-reports/"], rows.Select(r => r.Name));
        Assert.Equal(
            ["Version, system, monitors and scale", "Hearthpulse’s log", "Settings without keys, tokens and passwords", "Crash reports waiting to be sent: 2"],
            rows.Select(r => r.Description));
        Assert.All(rows, r => Assert.NotEmpty(r.Size));
        Assert.StartsWith("5 files, ", (string)model.Summary, StringComparison.Ordinal);
        Assert.EndsWith(" before compression", (string)model.Summary, StringComparison.Ordinal);
        Assert.True((bool)model.CanSave);
        Assert.False((bool)model.GameLogsAvailable);
        Assert.Equal("Hearthstone wasn’t found on this computer, so there are no game logs to attach.", (string)model.GameLogsNote);
        Assert.Equal(string.Empty, (string)model.Status);
        Assert.False((bool)model.Saved);

        Loc.Instance.SetLanguage(Loc.Russian);
        List<(string Name, string Description, string Size)> russian = Rows(model);
        Assert.Equal("Отчёты о сбоях, ждущие отправки: 2", russian[3].Description);
        Assert.StartsWith("5 файлов, ", (string)model.Summary, StringComparison.Ordinal);
        Assert.Equal("Hearthstone не найден на этом компьютере: логи игры приложить нельзя.", (string)model.GameLogsNote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Game_logs_go_in_only_when_switched_on_and_the_note_says_names_are_replaced(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        Put("data/hearthpulse.log", "INFO\n");
        var session = Path.GetDirectoryName(Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", "D CREATE_GAME\n"))!;
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Decks.log", "I deck\n");
        var model = Model(wpf, new GameLogsLookup(true, session));

        await Load(model);

        Assert.True((bool)model.GameLogsAvailable);
        Assert.False((bool)model.IncludeGameLogs);
        Assert.Contains("BattleTags and Battle.net account numbers in them, yours and your opponents’, are replaced with stand-ins",
            (string)model.GameLogsNote, StringComparison.Ordinal);
        Assert.Contains("an attached file stays available by its link", (string)model.GameLogsNote, StringComparison.Ordinal);
        List<(string Name, string Description, string Size)> before = Rows(model);
        Assert.DoesNotContain(before, r => r.Name == "game-logs/");

        model.IncludeGameLogs = true;
        List<(string Name, string Description, string Size)> withLogs = Rows(model);
        Assert.Equal("Hearthstone logs: Power.log, Decks.log", withLogs.Single(r => r.Name == "game-logs/").Description);

        model.IncludeGameLogs = false;
        List<(string Name, string Description, string Size)> withoutLogs = Rows(model);
        Assert.DoesNotContain(withoutLogs, r => r.Name == "game-logs/");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_a_log_session_the_switch_stays_off_and_says_why(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf, new GameLogsLookup(true, null));

        await Load(model);
        model.IncludeGameLogs = true;

        Assert.False((bool)model.GameLogsAvailable);
        Assert.False((bool)model.IncludeGameLogs);
        Assert.Equal("There are no game logs in the Hearthstone folder yet. They appear after the game runs.", (string)model.GameLogsNote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Saving_writes_the_archive_then_offers_the_form_and_the_folder(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        Put("data/hearthpulse.log", "INFO Started\n");
        var model = Model(wpf);
        await Load(model);
        Assert.Equal("hearthpulse-diagnostics-2026-11-26-1530.zip", (string)model.SuggestedFileName);
        // An older, longer file of the same name: replaced whole.
        var path = Put("out/report.zip", new string('x', 200_000));

        await (Task)model.SaveAsync(path);

        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.Equal(["system.txt", "hearthpulse.log"], zip.Entries.Select(e => e.FullName));
        }

        var size = WpfReport.SizeText(new FileInfo(path).Length);
        Assert.Equal($"Archive saved: report.zip ({size}). Open the form on GitHub and drag the file into it.", (string)model.Status);
        Assert.False((bool)model.StatusIsError);
        Assert.True((bool)model.Saved);
        Assert.True((bool)model.CanOpenFolder);
        Assert.True((bool)model.CanSave);

        model.OpenIssueForm.Execute(null);
        model.OpenFolder.Execute(null);
        Assert.Equal([new Uri(Form)], _browsed);
        Assert.Equal([path], _revealed);

        // A storage that gives only a stream, opened without truncation over a longer file: the tail goes too, the form
        // is offered and the folder is not.
        var stored = Put("out/stored.zip", new string('x', 200_000));
        await Save((object)model, () => Task.FromResult<Stream>(new FileStream(stored, FileMode.OpenOrCreate, FileAccess.Write)), "stored.zip");
        using (var zip = ZipFile.OpenRead(stored))
        {
            Assert.Equal(["system.txt", "hearthpulse.log"], zip.Entries.Select(e => e.FullName));
        }

        Assert.StartsWith("Archive saved: stored.zip (", (string)model.Status, StringComparison.Ordinal);
        Assert.True((bool)model.Saved);
        Assert.False((bool)model.CanOpenFolder);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failed_save_says_what_to_do_removes_the_partial_file_and_offers_nothing(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);
        await Load(model);

        await Save((object)model, () => throw new UnauthorizedAccessException("denied"), "report.zip");
        Assert.Equal("Can’t write to this folder. Pick another folder.", (string)model.Status);
        Assert.True((bool)model.StatusIsError);
        Assert.False((bool)model.Saved);

        // A disk that fills up while the archive is written: the half-written file goes.
        var partial = Path.Combine(_root, "out", "partial.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        model.CreateFile = new Func<string, Stream>(path =>
        {
            File.WriteAllText(path, "partial");
            return new BrokenStream();
        });
        await (Task)model.SaveAsync(partial);
        Assert.Equal("Couldn’t save the archive. Pick another folder and try again.", (string)model.Status);
        Assert.True((bool)model.StatusIsError);
        Assert.False((bool)model.Saved);
        Assert.False(File.Exists(partial));
        Assert.True((bool)model.CanSave);

        // A file that could not be opened (another app holds it) is the player's own: it stays.
        var held = Put("out/held.zip", "someone else's");
        model.CreateFile = new Func<string, Stream>(_ => throw new IOException("in use"));
        await (Task)model.SaveAsync(held);
        Assert.Equal("Couldn’t save the archive. Pick another folder and try again.", (string)model.Status);
        Assert.Equal("someone else's", File.ReadAllText(held));
        Assert.Empty(_browsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_browser_that_does_not_open_leaves_the_forms_address(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf, browserOpens: false);
        await Load(model);

        model.OpenIssueForm.Execute(null);

        Assert.Equal($"Couldn’t open the browser. The form is at {Form}", (string)model.Status);
        Assert.True((bool)model.StatusIsError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_game_logs_switch_holds_still_while_a_save_writes_the_archive(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = WithGameLogs(wpf);
        await Load(model);
        Assert.True((bool)model.CanChangeGameLogs);
        var opening = new TaskCompletionSource<Stream>();
        var output = new MemoryStream();

        var save = Save((object)model, () => opening.Task, "report.zip");
        Assert.False((bool)model.CanChangeGameLogs);
        model.IncludeGameLogs = true;
        Assert.False((bool)model.IncludeGameLogs);
        opening.SetResult(output);
        await save;

        // The archive is the one the list showed (no game logs), and the switch works again.
        using (var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("game-logs/", StringComparison.Ordinal));
        }

        Assert.True((bool)model.Saved);
        Assert.True((bool)model.CanChangeGameLogs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_archive_over_githubs_25_mb_says_to_drop_the_game_logs(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf);
        await Load(model);
        const long limit = 25L * 1024 * 1024;

        await Save((object)model, () => Task.FromResult<Stream>(new SizedStream(limit + 1)), "big.zip");
        Assert.Equal(Loc.F("Report.SavedTooLarge", "big.zip", WpfReport.SizeText(limit + 1)), (string)model.Status);
        Assert.StartsWith("Archive saved: big.zip (", (string)model.Status, StringComparison.Ordinal);
        Assert.Contains("GitHub takes files up to 25 MB", (string)model.Status, StringComparison.Ordinal);
        Assert.True((bool)model.Saved);
        Assert.False((bool)model.StatusIsError);

        // Exactly 25 MB still goes.
        await Save((object)model, () => Task.FromResult<Stream>(new SizedStream(limit)), "fits.zip");
        Assert.Equal(Loc.F("Report.Saved", "fits.zip", WpfReport.SizeText(limit)), (string)model.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_new_choice_of_game_logs_clears_the_result_of_the_last_save(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = WithGameLogs(wpf);
        await Load(model);
        await Save((object)model, () => Task.FromResult<Stream>(new MemoryStream()), "report.zip");
        Assert.True((bool)model.Saved);

        model.IncludeGameLogs = true;

        // The saved archive no longer matches the list: no "Archive saved", no form or folder for it.
        Assert.True((bool)model.IncludeGameLogs);
        Assert.False((bool)model.Saved);
        Assert.Equal(string.Empty, (string)model.Status);
    }

    private dynamic WithGameLogs(bool wpf)
    {
        Put("data/hearthpulse.log", "INFO\n");
        var session = Path.GetDirectoryName(Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", "D CREATE_GAME\n"))!;
        return Model(wpf, new GameLogsLookup(true, session));
    }

    [Fact]
    public void Sizes_read_in_the_interface_language()
    {
        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("812 B", WpfReport.SizeText(812));
        Assert.Equal("4 KB", AvaReport.SizeText(4096));
        Assert.Equal($"{1.3.ToString(CultureInfo.CurrentCulture)} MB", WpfReport.SizeText(1_363_149));
        Loc.Instance.SetLanguage(Loc.Russian);
        Assert.Equal("812 Б", AvaReport.SizeText(812));
        Assert.Equal($"{1.3.ToString(CultureInfo.CurrentCulture)} МБ", WpfReport.SizeText(1_363_149));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_main_model_opens_a_report_on_its_data_folder(bool wpf)
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        dynamic main = wpf ? new WpfMain(history, new AppSettings(), TestApp.Data) : new AvaMain(history, new AppSettings(), TestApp.Data);

        dynamic report = main.ReportProblem();

        Assert.Matches(@"^hearthpulse-diagnostics-\d{4}-\d{2}-\d{2}-\d{4}\.zip$", (string)report.SuggestedFileName);
        Assert.False((bool)report.CanSave);
        report.Detach();
    }

    /// <summary>A destination that keeps nothing and says it holds <paramref name="length"/> bytes.</summary>
    private sealed class SizedStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => length;

        public override long Position { get => _position; set => _position = value; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count) => _position += count;
    }

    /// <summary>A disk that fills up while the archive is written.</summary>
    private sealed class BrokenStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("disk full");

        public override void WriteByte(byte value) => throw new IOException("disk full");
    }
}

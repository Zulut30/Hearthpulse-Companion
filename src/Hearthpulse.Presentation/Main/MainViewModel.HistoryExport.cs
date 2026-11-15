using System.Globalization;
using System.IO;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Main.Match;
#else
using Hearthpulse.UI.Main.Match;
#endif

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// "Export to CSV" on the History page (<c>docs/history-export.md</c>): every game of the mode filter shown, not only
/// the cards listed, read and written off the UI thread; the page's status line says how it ended.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _exportingHistory;
    private Task _historyExport = Task.CompletedTask;

    /// <summary>False while an export runs: the button (<see cref="ReplayLibraryViewModel.Export"/>) waits for it.</summary>
    public bool CanExportHistory => !_exportingHistory;

    /// <summary>The file name the save dialog starts with: the day of the export, the same in every language.</summary>
    public static string HistoryExportFileName(DateTime now) =>
        "hearthpulse-history-" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

    /// <summary>The games as the file's bytes (<see cref="HistoryExport.Bytes"/>); a test hands in its own to see where it runs.</summary>
    internal Func<IReadOnlyList<GameRecord>, Func<string, string>, byte[]> HistoryCsv { get; set; } = HistoryExport.Bytes;

    /// <summary>The last export (tests wait for it).</summary>
    internal Task HistoryExportTask => _historyExport;

    /// <summary>Exports to a file the player picked: written whole or not at all (<see cref="HistoryExport.Save"/>).</summary>
    public Task ExportHistoryAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return StartHistoryExport(csv => Task.Run(() => HistoryExport.Save(path, csv)));
    }

    /// <summary>
    /// Exports to a place that has no path on this PC (a storage provider's file): the stream is opened only once the
    /// file is ready, so a failed read of the history leaves the old file as it was.
    /// </summary>
    public Task ExportHistoryAsync(Func<Task<Stream>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return StartHistoryExport(async csv =>
        {
            var stream = await open().ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                if (stream.CanSeek)
                {
                    // A longer file of the same name is replaced, not left with its old tail.
                    stream.SetLength(0);
                }

                await stream.WriteAsync(csv).ConfigureAwait(false);
            }
        });
    }

    private Task StartHistoryExport(Func<byte[], Task> save)
    {
        if (_exportingHistory)
        {
            return _historyExport;
        }

        SetExportingHistory(true);
        _historyExport = RunHistoryExportAsync(save);
        return _historyExport;
    }

    private async Task RunHistoryExportAsync(Func<byte[], Task> save)
    {
        var history = _history;
        var cards = _cards;
        var filter = HistoryFilter;
        var encode = HistoryCsv;
        try
        {
            // The whole history may be tens of thousands of rows: read, encoded and written on the pool.
            var count = await Task.Run(async () =>
            {
                var games = history.Recent(int.MaxValue).Where(g => FilterMatch(g, filter)).ToList();
                if (games.Count > 0)
                {
                    await save(encode(games, cards.NameOf)).ConfigureAwait(false);
                }

                return games.Count;
            }).ConfigureAwait(true);
            if (count > 0)
            {
                Library.Succeeded(Loc.P("Library.Export.Saved", count));
            }
            else
            {
                // Nothing to write: no empty file is left behind a good name.
                Library.Noted(Loc.T("Library.Export.Empty"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
            or ArgumentException or System.Security.SecurityException or Microsoft.Data.Sqlite.SqliteException)
        {
            Diag.Error("Exporting the history failed", ex);
            Library.Failed(Loc.T("Library.Export.Failed"));
        }
        finally
        {
            SetExportingHistory(false);
        }
    }

    private void SetExportingHistory(bool value)
    {
        if (_exportingHistory != value)
        {
            _exportingHistory = value;
            Raise(nameof(CanExportHistory));
            Library.ExportStateChanged();
        }
    }
}

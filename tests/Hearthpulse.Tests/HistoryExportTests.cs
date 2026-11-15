using System.Globalization;
using System.Text;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// "Export to CSV" on History (<c>docs/history-export.md</c>): the file's format (RFC 4180, a byte order mark, the same
/// numbers and dates under any culture, formulas defused, no names), the whole-or-nothing write, and the view model:
/// every game of the page's mode filter, read and written off the caller's thread, with a status line for each ending.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class HistoryExportTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly string _folder = Directory.CreateDirectory(TestDataFolder.Scratch("hp-export")).FullName;
    private readonly HistoryStore _history;

    public HistoryExportTests()
    {
        _history = new HistoryStore(Path.Combine(_folder, "history.db"));
        Loc.Instance.SetLanguage(Loc.English);
    }

    public void Dispose() => TestDataFolder.TryDelete(_folder);

    private static readonly GameRecord Ranked = new()
    {
        Key = "ranked-1",
        StartedAt = new DateTime(2026, 11, 3, 18, 5, 9, DateTimeKind.Utc),
        EndedAt = new DateTime(2026, 11, 3, 18, 12, 40, DateTimeKind.Utc),
        Mode = "Ranked", GameType = "GT_RANKED", Format = "FT_STANDARD", Result = "Loss",
        PlayerName = "Me#1234", OpponentName = "Foe#5678",
        PlayerClass = "MAGE", HeroCardId = "HERO_08", OpponentClass = "WARRIOR", OpponentHeroCardId = "HERO_01",
        DeckName = "Spell Mage", DeckCode = "AAECAf0EAA==", OpponentArchetype = "Control Warrior",
        Turns = 9, WentFirst = false, Rank = "Legend 1500", RankLegend = 1500, RankStarLevel = 51,
    };

    private static readonly GameRecord Battlegrounds = new()
    {
        Key = "bg-1",
        StartedAt = new DateTime(2026, 11, 4, 20, 0, 0),
        EndedAt = new DateTime(2026, 11, 4, 20, 31, 30),
        Mode = "Battlegrounds", GameType = "GT_BATTLEGROUNDS", Result = "Win", Placement = 3,
        PlayerName = "Me#1234", HeroCardId = "TB_BaconShop_HERO_08",
        Turns = 14, TavernTier = 6, RatingBefore = 7025, RatingAfter = 7000,
    };

    private static string[] Lines(string csv) => csv.Split("\r\n");

    [Fact]
    public void The_header_names_the_columns_in_english_and_a_row_holds_what_the_history_row_shows()
    {
        var lines = Lines(HistoryExport.Csv([Ranked, Battlegrounds], id => id == "TB_BaconShop_HERO_08" ? "Ragnaros" : id));

        Assert.Equal(
            "started_at,ended_at,duration_seconds,mode,game_type,format,result,placement,player_class,hero_card_id,hero,deck_name,deck_code,"
            + "opponent_class,opponent_archetype,went_first,turns,rating_before,rating_after,rating_change,rank,rank_legend,tavern_tier,source",
            lines[0]);
        Assert.Equal(lines[0], string.Join(',', HistoryExport.Columns));
        Assert.Equal(
            "2026-11-03T18:05:09Z,2026-11-03T18:12:40Z,451,Ranked,GT_RANKED,FT_STANDARD,Loss,,MAGE,HERO_08,,Spell Mage,AAECAf0EAA==,"
            + "WARRIOR,Control Warrior,false,9,,,,Legend 1500,1500,,live",
            lines[1]);
        // A time saved without a zone stays without one; a rating that fell is a plain negative number.
        Assert.Equal(
            "2026-11-04T20:00:00,2026-11-04T20:31:30,1890,Battlegrounds,GT_BATTLEGROUNDS,,Win,3,,TB_BaconShop_HERO_08,Ragnaros,,,"
            + ",,,14,7025,7000,-25,,,6,live",
            lines[2]);
        // Every line ends with CRLF, the last one too.
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void Names_and_battletags_never_reach_the_file()
    {
        var csv = HistoryExport.Csv([Ranked, Battlegrounds]);

        Assert.DoesNotContain("Me#1234", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Foe#5678", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(HistoryExport.Columns, c => c.Contains("name", StringComparison.Ordinal) && c != "deck_name");
        // The game key holds account ids in some rows; the opponent's hero skin is not on the History row either.
        Assert.DoesNotContain("ranked-1", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("HERO_01", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Commas_quotes_and_line_breaks_are_quoted_as_rfc_4180_says()
    {
        var game = Ranked with { DeckName = "Big, \"bad\" deck\r\nline two", OpponentArchetype = " padded " };

        var csv = HistoryExport.Csv([game]);

        Assert.Contains(",\"Big, \"\"bad\"\" deck\r\nline two\",", csv, StringComparison.Ordinal);
        Assert.Contains(",\" padded \",", csv, StringComparison.Ordinal);
        // The quoted line break is inside one field: the row still has every column.
        Assert.Equal(HistoryExport.Columns.Count, Fields(csv[(csv.IndexOf("\r\n", StringComparison.Ordinal) + 2)..]).Count);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    [InlineData("\n=1+1", "\"'\n=1+1\"")]
    [InlineData("a=b", "a=b")]
    public void A_text_that_a_spreadsheet_would_run_as_a_formula_gets_an_apostrophe(string deck, string written)
    {
        var csv = HistoryExport.Csv([Ranked with { DeckName = deck, DeckCode = null }]);

        Assert.Contains("HERO_08,," + written + ",,WARRIOR", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_starts_with_a_byte_order_mark_and_is_utf_8()
    {
        var bytes = HistoryExport.Bytes([Ranked with { DeckName = "Маг заклинаний" }]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains(",Маг заклинаний,", text, StringComparison.Ordinal);
        Assert.StartsWith("started_at,", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_and_dates_are_the_same_under_a_russian_culture()
    {
        // ru-RU writes 7 025 with a space and dates as 03.11.2026: none of that may reach the file.
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        string russian;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            russian = HistoryExport.Csv([Ranked, Battlegrounds with { RatingBefore = 12345, RatingAfter = 11000 }]);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(HistoryExport.Csv([Ranked, Battlegrounds with { RatingBefore = 12345, RatingAfter = 11000 }]), russian);
        }
        finally
        {
            CultureInfo.CurrentCulture = before.CurrentCulture;
        }

        Assert.Contains("2026-11-03T18:05:09Z,2026-11-03T18:12:40Z,451,", russian, StringComparison.Ordinal);
        Assert.Contains(",14,12345,11000,-1345,", russian, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_history_is_the_header_alone()
    {
        Assert.Equal(string.Join(',', HistoryExport.Columns) + "\r\n", HistoryExport.Csv([]));
    }

    [Fact]
    public void What_the_row_does_not_know_is_an_empty_field()
    {
        var csv = HistoryExport.Csv([new GameRecord { Key = "bare" }]);

        Assert.Equal(",,,Other,,,Unknown,,,,,,,,,,,,,,,,,live", Lines(csv)[1]);
    }

    [Fact]
    public void Saving_replaces_the_file_whole_and_leaves_no_temporary_file()
    {
        var path = Path.Combine(_folder, "history.csv");
        File.WriteAllText(path, new string('x', 5000));

        HistoryExport.Save(path, HistoryExport.Bytes([Ranked]));

        Assert.Equal(HistoryExport.Bytes([Ranked]), File.ReadAllBytes(path));
        Assert.Equal(["history.csv", "history.db"], Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_failed_save_throws_and_leaves_what_was_there()
    {
        // A folder stands where the file should go: the move fails on every OS.
        var path = Path.Combine(_folder, "taken");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "kept.txt"), "kept");

        Assert.ThrowsAny<Exception>(() => HistoryExport.Save(path, HistoryExport.Bytes([Ranked])));

        Assert.Equal("kept", File.ReadAllText(Path.Combine(path, "kept.txt")));
        Assert.DoesNotContain(Directory.GetFiles(_folder), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // ---- the view model -------------------------------------------------------------------------------------------

    private MainViewModel Model() => new(_history, new AppSettings(), TestDataFolder.App);

    private void AddGames(int ranked, int battlegrounds)
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < ranked + battlegrounds; i++)
        {
            var bg = i >= ranked;
            Assert.True(_history.Add((bg ? Battlegrounds : Ranked) with
            {
                Key = "game-" + i.ToString(CultureInfo.InvariantCulture),
                StartedAt = start.AddMinutes(i * 20),
                EndedAt = start.AddMinutes((i * 20) + 10),
            }));
        }
    }

    [Fact]
    public async Task The_export_takes_every_game_of_the_pages_filter_not_only_the_cards_listed()
    {
        // History lists up to 300 games of the 400 newest; the file has them all.
        AddGames(ranked: 405, battlegrounds: 7);
        var vm = Model();
        await vm.RefreshAsync();
        var all = Path.Combine(_folder, "all.csv");
        var bg = Path.Combine(_folder, "bg.csv");

        await vm.ExportHistoryAsync(all).WaitAsync(Wait);
        Assert.Equal(Loc.P("Library.Export.Saved", 412), vm.Library.Status);
        Assert.Equal("Win", vm.Library.StatusKey);

        vm.SetHistoryFilter.Execute("Battlegrounds");
        Assert.Equal(string.Empty, vm.Library.Status);
        await vm.ExportHistoryAsync(bg).WaitAsync(Wait);

        Assert.Equal(1 + 412 + 1, Lines(File.ReadAllText(all)).Length);
        var rows = Lines(File.ReadAllText(bg));
        Assert.Equal(1 + 7 + 1, rows.Length);
        Assert.All(rows[1..^1], r => Assert.Contains(",Battlegrounds,GT_BATTLEGROUNDS,", r, StringComparison.Ordinal));
        // Newest first, as the page lists them.
        Assert.StartsWith("2026-10-07T05:00:00Z,", rows[1], StringComparison.Ordinal);
        Assert.Equal("Saved 7 games.", vm.Library.Status);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(bg)[..3]);
    }

    [Fact]
    public async Task The_history_is_read_and_written_off_the_callers_thread()
    {
        // Not a stopwatch: the encoder is held, and the call must have returned by then. Were the work done on the
        // caller's thread, the call could not return while the encoder waits, and the thread ids would be the same.
        AddGames(ranked: 3, battlegrounds: 0);
        var vm = Model();
        var path = Path.Combine(_folder, "held.csv");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.HistoryCsv = (games, name) =>
        {
            entered.TrySetResult(Environment.CurrentManagedThreadId);
            release.Wait(Wait);
            return HistoryExport.Bytes(games, name);
        };
        var states = new List<bool>();
        vm.Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Library.CanExport))
            {
                states.Add(vm.Library.CanExport);
            }
        };
        var caller = Environment.CurrentManagedThreadId;

        var export = vm.ExportHistoryAsync(path);
        var returnedBeforeTheWork = !export.IsCompleted;
        var busy = !vm.CanExportHistory && !vm.Library.CanExport;
        // A second press while the first runs starts nothing: it is the same export.
        var again = vm.ExportHistoryAsync(Path.Combine(_folder, "second.csv"));
        var worker = await entered.Task.WaitAsync(Wait);
        var nothingWrittenYet = !File.Exists(path);
        release.Set();
        await export.WaitAsync(Wait);

        Assert.True(returnedBeforeTheWork);
        Assert.True(busy);
        Assert.Same(export, again);
        Assert.NotEqual(caller, worker);
        Assert.True(nothingWrittenYet);
        Assert.True(vm.CanExportHistory);
        Assert.Equal([false, true], states);
        Assert.Equal(1 + 3 + 1, Lines(File.ReadAllText(path)).Length);
        Assert.False(File.Exists(Path.Combine(_folder, "second.csv")));
    }

    [Fact]
    public async Task With_no_games_of_the_mode_no_file_is_written_and_the_page_says_so()
    {
        AddGames(ranked: 2, battlegrounds: 0);
        var vm = Model();
        await vm.RefreshAsync();
        vm.SetHistoryFilter.Execute("Arena");
        var path = Path.Combine(_folder, "none.csv");

        await vm.ExportHistoryAsync(path).WaitAsync(Wait);

        Assert.False(File.Exists(path));
        Assert.Equal(Loc.T("Library.Export.Empty"), vm.Library.Status);
        Assert.Equal(string.Empty, vm.Library.StatusKey);
        Assert.True(vm.CanExportHistory);
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_is_said_and_the_button_comes_back()
    {
        AddGames(ranked: 2, battlegrounds: 0);
        var vm = Model();
        var path = Path.Combine(_folder, "taken");
        Directory.CreateDirectory(path);

        await vm.ExportHistoryAsync(path).WaitAsync(Wait);

        Assert.Equal(Loc.T("Library.Export.Failed"), vm.Library.Status);
        Assert.Equal("Loss", vm.Library.StatusKey);
        Assert.True(vm.CanExportHistory);
        Assert.Contains("Exporting the history failed", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_storage_without_a_path_gets_the_same_bytes_through_its_stream()
    {
        AddGames(ranked: 2, battlegrounds: 1);
        var vm = Model();
        var path = Path.Combine(_folder, "stream.csv");
        // A longer file was there: its tail must not stay.
        File.WriteAllText(path, new string('x', 20000));
        var byPath = Path.Combine(_folder, "path.csv");

        await vm.ExportHistoryAsync(() => Task.FromResult<Stream>(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))).WaitAsync(Wait);
        await vm.ExportHistoryAsync(byPath).WaitAsync(Wait);

        Assert.Equal(File.ReadAllBytes(byPath), File.ReadAllBytes(path));
        Assert.Equal("Saved 3 games.", vm.Library.Status);
    }

    [Fact]
    public async Task The_button_asks_the_view_for_a_file_named_after_the_day_and_waits_while_an_export_runs()
    {
        AddGames(ranked: 1, battlegrounds: 0);
        var vm = Model();
        var asked = new List<string>();
        vm.Library.ExportFileRequested += asked.Add;

        vm.Library.Export.Execute(null);

        Assert.Equal(MainViewModel.HistoryExportFileName(DateTime.Now), Assert.Single(asked));
        Assert.Equal("hearthpulse-history-2026-11-05.csv", MainViewModel.HistoryExportFileName(new DateTime(2026, 11, 5, 23, 59, 0)));

        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.HistoryCsv = (games, name) =>
        {
            entered.TrySetResult();
            release.Wait(Wait);
            return HistoryExport.Bytes(games, name);
        };
        var export = vm.Library.ExportTo(Path.Combine(_folder, "busy.csv"));
        await entered.Task.WaitAsync(Wait);
        vm.Library.Export.Execute(null);
        release.Set();
        await export.WaitAsync(Wait);

        Assert.Single(asked);
    }

    [Fact]
    public void The_status_is_russian_with_its_plural_forms()
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            Assert.Equal("Сохранена 1 игра.", Loc.P("Library.Export.Saved", 1));
            Assert.Equal("Сохранено 3 игры.", Loc.P("Library.Export.Saved", 3));
            Assert.Equal("Сохранено 457 игр.", Loc.P("Library.Export.Saved", 457));
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    /// <summary>The fields of one CSV record (quotes doubled inside quoted fields), as a spreadsheet reads them.</summary>
    private static List<string> Fields(string record)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < record.Length; i++)
        {
            var c = record[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < record.Length && record[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < record.Length && record[i + 1] == '\n')
            {
                break;
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}

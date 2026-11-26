using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

/// <summary>
/// "Report a problem" (roadmap E1, AC6 of TASK-2026-11-26-release-cleanup, docs/report-a-problem.md): what the
/// diagnostics archive holds and what it never holds - no token, key or password, no account, history or collection, no
/// game log unless asked; the user's folder and name cut from Hearthpulse's own files; long logs cut to their end.
/// </summary>
public sealed class DiagnosticsBundleTests : IDisposable
{
    private const string Profile = @"C:\Users\Bob";
    private static readonly DateTime Now = new(2026, 11, 26, 12, 30, 0, DateTimeKind.Utc);
    private static readonly string Home = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";

    private static readonly DiagnosticsContext Context = new("0.16.0", "wpf", "ru",
        [new DisplayInfo(2560, 1440, 1.5, true), new DisplayInfo(1920, 1080, 1.0, false)], 1.5);

    private readonly string _root = Directory.CreateTempSubdirectory("hp-diag-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Data => Path.Combine(_root, "data");

    private void Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>A data folder with everything Hearthpulse keeps, so the test sees what is taken and what is not.</summary>
    private void FullDataFolder(string settings = """{ "Theme": "Frost", "Language": "ru" }""")
    {
        Put("data/hearthpulse.log", "2026-11-26 12:00:00.000 INFO Started\n");
        Put("data/hearthpulse.old.log", "2026-11-25 10:00:00.000 INFO Older\n");
        Put("data/settings.json", settings);
        Put("data/crash-reports/a1.json", """{"v":1,"id":"a1","where":"UI error"}""");
        Put("data/crash-reports/b2.json", """{"v":1,"id":"b2","where":"Task"}""");
        Put("data/account.dat", "account-session-token-0123456789");
        Put("data/history.db", "SQLite format 3 history");
        Put("data/collection.json", """{"cards":{"CORE_001":2}}""");
        Put("data/replays/game.hpr", "replay");
        Put("data/cache/api/meta.json", "{}");
    }

    /// <summary>An install with an older and a newer log session; returns the newer.</summary>
    private string Install()
    {
        Put("hs/Hearthstone.exe", string.Empty);
        Put("hs/Logs/Hearthstone_2026_11_24_10_00_00/Power.log", "OLD SESSION POWER\n");
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", "D 18:02:11.0000000 GameState.DebugPrintPower() - CREATE_GAME\n");
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Decks.log", "I 18:02:12.0000000 Deck ID: 1\n");
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/LoadingScreen.log", "D 18:02:10.0000000 LoadingScreen.OnSceneLoaded()\n");
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/BattleNet.log", "account details the tracker never reads\n");
        return Path.Combine(_root, "hs", "Logs", "Hearthstone_2026_11_25_18_02_11");
    }

    private DiagnosticsBundle Bundle(GameLogsLookup? logs = null, long? appLogCap = null, long? gameLogCap = null) =>
        new(Data, logs ?? GameLogsLookup.NotFound, Context)
        {
            UtcNow = () => Now,
            UserProfile = Profile,
            UserProfileShort = null,
            UserName = "Bob",
            AppLogCap = appLogCap ?? DiagnosticsBundle.DefaultAppLogCap,
            GameLogCap = gameLogCap ?? DiagnosticsBundle.DefaultGameLogCap,
        };

    /// <summary>Writes the archive and reads every entry back as text.</summary>
    private static (IReadOnlyList<DiagnosticsEntry> Written, Dictionary<string, string> Files) Archive(DiagnosticsBundle bundle, bool includeGameLogs)
    {
        using var output = new MemoryStream();
        var written = bundle.Write(output, includeGameLogs);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = reader.ReadToEnd();
        }

        return (written, files);
    }

    [Fact]
    public void The_archive_holds_the_logs_settings_crash_reports_and_system_and_never_the_account_history_or_collection()
    {
        FullDataFolder();
        var bundle = Bundle();

        var plan = bundle.Plan(includeGameLogs: false);
        var (written, files) = Archive(bundle, includeGameLogs: false);

        string[] expected = ["system.txt", "hearthpulse.log", "hearthpulse.old.log", "settings.json", "crash-reports/a1.json", "crash-reports/b2.json"];
        Assert.Equal(expected, plan.Select(e => e.Name));
        Assert.Equal(expected, written.Select(e => e.Name));
        Assert.Equal(expected, files.Keys);
        Assert.Equal(
            [DiagnosticsPart.System, DiagnosticsPart.AppLog, DiagnosticsPart.OldAppLog, DiagnosticsPart.Settings, DiagnosticsPart.CrashReport, DiagnosticsPart.CrashReport],
            plan.Select(e => e.Part));
        // The preview's sizes are what goes in.
        Assert.Equal(plan.Select(e => e.Bytes), written.Select(e => e.Bytes));
        Assert.Equal(written.Select(e => e.Bytes), files.Values.Select(text => (long)Encoding.UTF8.GetByteCount(text)));
        Assert.Contains("INFO Started", files["hearthpulse.log"], StringComparison.Ordinal);
        Assert.Contains("INFO Older", files["hearthpulse.old.log"], StringComparison.Ordinal);
        Assert.Contains("\"where\":\"UI error\"", files["crash-reports/a1.json"], StringComparison.Ordinal);
        Assert.DoesNotContain(files.Values, text => text.Contains("account-session-token", StringComparison.Ordinal)
            || text.Contains("SQLite format", StringComparison.Ordinal) || text.Contains("CORE_001", StringComparison.Ordinal));
    }

    [Fact]
    public void Game_logs_go_in_only_when_asked_and_only_the_newest_sessions_tracker_logs()
    {
        FullDataFolder();
        var session = Install();
        var bundle = Bundle(new GameLogsLookup(true, session));
        Assert.True(bundle.HasGameLogs);

        var (_, without) = Archive(bundle, includeGameLogs: false);
        Assert.DoesNotContain(without.Keys, name => name.StartsWith("game-logs/", StringComparison.Ordinal));
        Assert.Contains("Hearthstone logs: newest session Hearthstone_2026_11_25_18_02_11, not included", without["system.txt"], StringComparison.Ordinal);

        var (written, files) = Archive(bundle, includeGameLogs: true);
        Assert.Equal(["game-logs/Power.log", "game-logs/LoadingScreen.log", "game-logs/Decks.log"],
            written.Where(e => e.Part == DiagnosticsPart.GameLog).Select(e => e.Name));
        Assert.Contains("CREATE_GAME", files["game-logs/Power.log"], StringComparison.Ordinal);
        Assert.DoesNotContain(files.Values, text => text.Contains("OLD SESSION", StringComparison.Ordinal) || text.Contains("account details", StringComparison.Ordinal));
        Assert.Contains("newest session Hearthstone_2026_11_25_18_02_11, included", files["system.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void No_token_key_or_password_reaches_the_archive()
    {
        string[] secrets =
        [
            "pool-secret-value-42", "access-token-value", "refresh-token-value", "api-key-value", "hunter2-password", "nested-panel-key",
            "pool-user:pool-pass", "query-token-value", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.c2lnbmF0dXJl", "bearer-secret-abcdef", "account-session-token",
        ];
        FullDataFolder($$"""
            {
              "Theme": "Frost",
              "ShowSecrets": true,
              "PoolSecret": "{{secrets[0]}}",
              "PoolEndpoint": "https://{{secrets[6]}}@pool.example/api?token={{secrets[7]}}&mode=x",
              "AccessToken": "{{secrets[1]}}",
              "refresh_token": "{{secrets[2]}}",
              "ApiKey": "{{secrets[3]}}",
              "Password": "{{secrets[4]}}",
              "Panels": { "deck-tracker": { "X": 10, "Y": 20, "ApiKey": "{{secrets[5]}}" }, "secrets": { "X": 7, "Y": 9 } },
              "CounterModes": { "secrets-played": "Full" }
            }
            """);
        Put("data/hearthpulse.log", $"INFO session {secrets[8]} restored\nINFO Authorization: Bearer {secrets[9]}\n");
        Put("data/crash-reports/a1.json", $$"""{"v":1,"errors":[{"message":"401 for {{secrets[8]}}"}]}""");

        var (_, files) = Archive(Bundle(), includeGameLogs: false);

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(files, file => file.Value.Contains(secret, StringComparison.Ordinal));
        }

        var settings = files["settings.json"];
        // What is not secret stays, so the copy still says how the app was set up; a removed value shows it was set.
        Assert.Contains("\"Theme\": \"Frost\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"ShowSecrets\": true", settings, StringComparison.Ordinal);
        Assert.Contains("\"PoolSecret\": \"<removed>\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"X\": 10", settings, StringComparison.Ordinal);
        Assert.Contains("pool.example/api?token=<removed>&mode=x", settings, StringComparison.Ordinal);
        // A dictionary's keys are data: the Secrets panel keeps its place and the Secrets counter its mode.
        Assert.Contains("\"secrets\": {", settings, StringComparison.Ordinal);
        Assert.Contains("\"X\": 7", settings, StringComparison.Ordinal);
        Assert.Contains("\"secrets-played\": \"Full\"", settings, StringComparison.Ordinal);
        // Fields this version does not read are left out entirely.
        Assert.DoesNotContain("AccessToken", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh_token", settings, StringComparison.Ordinal);
        Assert.Contains("settings.json: 4 field(s) this version does not read left out", files["system.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_users_folder_name_email_and_battletag_are_cut_from_hearthpulse_files()
    {
        FullDataFolder("""{ "HearthstonePath": "C:\\Users\\Bob\\Games\\Hearthstone", "HdtFolder": "C:\\Users\\Bobby\\HDT" }""");
        Put("data/hearthpulse.log",
            "ERROR Could not read C:\\Users\\Bob\\AppData\\Roaming\\Hearthpulse\\cache\\a.json\n" +
            "INFO Bob signed in as bob@example.com, opponent Rival#12345\n" +
            "INFO c:/users/bob/Documents and C:\\Users\\Bobby\\x stay apart\n");

        var (_, files) = Archive(Bundle(), includeGameLogs: false);

        var log = files["hearthpulse.log"];
        Assert.Contains($"Could not read {Home}\\AppData\\Roaming\\Hearthpulse\\cache\\a.json", log, StringComparison.Ordinal);
        Assert.Contains("INFO <user> signed in as <email>, opponent Player1#1001", log, StringComparison.Ordinal);
        Assert.Contains($"{Home}/Documents", log, StringComparison.Ordinal);
        // Another user's folder is not this user's: only the whole folder name is cut.
        Assert.Contains(@"C:\Users\Bobby\x", log, StringComparison.Ordinal);
        var settings = files["settings.json"];
        Assert.Contains($"\"HearthstonePath\": \"{Home}\\\\Games\\\\Hearthstone\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Users\\Bob\\", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_log_keeps_its_last_whole_lines_and_system_txt_says_how_much()
    {
        FullDataFolder();
        var lines = string.Concat(Enumerable.Range(1, 200).Select(i => $"INFO line {i:000}\n"));
        Put("data/hearthpulse.log", lines);
        var session = Install();
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", string.Concat(Enumerable.Range(1, 500).Select(i => $"D power {i:000}\n")));
        var bundle = Bundle(new GameLogsLookup(true, session), appLogCap: 300, gameLogCap: 400);

        var (written, files) = Archive(bundle, includeGameLogs: true);

        var log = files["hearthpulse.log"];
        Assert.StartsWith("INFO line ", log, StringComparison.Ordinal);
        Assert.EndsWith("INFO line 200\n", log, StringComparison.Ordinal);
        Assert.True(log.Length <= 300, log.Length.ToString());
        var power = files["game-logs/Power.log"];
        Assert.StartsWith("D power ", power, StringComparison.Ordinal);
        Assert.EndsWith("D power 500\n", power, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(power), written.Single(e => e.Name == "game-logs/Power.log").Bytes);
        Assert.Contains("hearthpulse.log: the last 0.3 KB of 2.7 KB", files["system.txt"], StringComparison.Ordinal);
        Assert.Contains("game-logs/Power.log: the last ", files["system.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_file_that_is_not_json_is_left_out_and_system_txt_says_why()
    {
        FullDataFolder("{ \"Theme\": \"Fr");

        var (written, files) = Archive(Bundle(), includeGameLogs: false);

        Assert.DoesNotContain(written, e => e.Part == DiagnosticsPart.Settings);
        Assert.Contains("settings.json: not included, not valid JSON (14 characters)", files["system.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void System_txt_names_the_version_interface_system_displays_and_why_game_logs_are_missing()
    {
        FullDataFolder();

        var (_, files) = Archive(Bundle(), includeGameLogs: true);

        var system = files["system.txt"];
        Assert.StartsWith("Hearthpulse diagnostics", system, StringComparison.Ordinal);
        Assert.Contains("Created: 2026-11-26 12:30:00 UTC", system, StringComparison.Ordinal);
        Assert.Contains("Version: 0.16.0", system, StringComparison.Ordinal);
        Assert.Contains("Interface: wpf", system, StringComparison.Ordinal);
        Assert.Contains("Language: ru;", system, StringComparison.Ordinal);
        Assert.Contains("OS: ", system, StringComparison.Ordinal);
        Assert.Contains("Displays: 2", system, StringComparison.Ordinal);
        Assert.Contains("1. 2560 x 1440, scale 150 %, primary", system, StringComparison.Ordinal);
        Assert.Contains("2. 1920 x 1080, scale 100 %", system, StringComparison.Ordinal);
        Assert.Contains("Main window scale: 150 %", system, StringComparison.Ordinal);
        Assert.Contains("Hearthstone logs: Hearthstone folder not found", system, StringComparison.Ordinal);
        Assert.DoesNotContain(files.Keys, name => name.StartsWith("game-logs/", StringComparison.Ordinal));
        Assert.False(Bundle().HasGameLogs);
    }

    [Fact]
    public void The_lookup_finds_the_newest_session_of_the_configured_install_and_says_when_there_is_none()
    {
        var newest = Install();

        Assert.Equal(new GameLogsLookup(true, newest), GameLogsLookup.Find(Path.Combine(_root, "hs")));

        Put("empty/Hearthstone.exe", string.Empty);
        Assert.Equal(new GameLogsLookup(true, null), GameLogsLookup.Find(Path.Combine(_root, "empty")));
        var (_, files) = Archive(Bundle(new GameLogsLookup(true, null)), includeGameLogs: true);
        Assert.Contains("Hearthstone logs: no log session in the Hearthstone folder", files["system.txt"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Power.log as the game writes it: the shapes of a real log of 2026-11-25 (names, account numbers and cards made up):
    /// both players' accounts in CREATE_GAME and again under PowerTaskList, the local name, the opponent revealed later,
    /// choices and a block naming a player.
    /// </summary>
    private static readonly string[] PowerLines =
    [
        "D 18:02:11.4402560 GameState.DebugPrintPower() - CREATE_GAME",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     GameEntity EntityID=1",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=GAME",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=37660911]",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CONTROLLER value=1",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=52004477]",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CONTROLLER value=2",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     Player EntityID=4 PlayerID=3 GameAccountId=[hi=0 lo=0]",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CONTROLLER value=3",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
        "D 18:02:11.4402560 PowerTaskList.DebugPrintPower() -         Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=37660911]",
        "D 18:02:11.4402560 PowerTaskList.DebugPrintPower() -         Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=52004477]",
        "D 18:02:11.4402560 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=20 CardID=HERO_09",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     tag=ZONE value=PLAY",
        "D 18:02:11.4402560 GameState.DebugPrintPower() -     tag=CONTROLLER value=1",
        "D 18:02:11.4402560 GameState.DebugPrintGame() - GameType=GT_RANKED",
        "D 18:02:11.4402560 GameState.DebugPrintGame() - FormatType=FT_STANDARD",
        "D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=1, PlayerName=Зулу#2345",
        "D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=2, PlayerName=UNKNOWN HUMAN PLAYER",
        "D 18:02:12.1000000 GameState.DebugPrintEntityChoices() - id=1 Player=Зулу#2345 TaskList= ChoiceType=MULLIGAN CountMin=0 CountMax=3",
        "D 18:02:13.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Зулу#2345 tag=MULLIGAN_STATE value=INPUT ",
        "D 18:02:14.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Rival#56789 tag=MULLIGAN_STATE value=INPUT ",
        "D 18:02:15.0000000 GameState.DebugPrintPower() - BLOCK_START BlockType=TRIGGER Entity=Rival#56789 EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=-1 Target=0 SubOption=-1 TriggerKeyword=TAG_NOT_SET",
        "D 18:02:15.0000000 GameState.DebugPrintPower() - BLOCK_END",
        "D 18:02:16.0000000 GameState.DebugPrintEntitiesChosen() - id=1 Player=Зулу#2345 EntitiesCount=2",
        "D 18:02:17.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Зулу#2345 tag=MULLIGAN_STATE value=DONE ",
    ];

    [Fact]
    public void Game_logs_name_players_and_accounts_by_stand_ins_the_same_across_the_archive_and_still_replay()
    {
        FullDataFolder();
        // The same opponent in Hearthpulse's own log: one stand-in for both files.
        Put("data/hearthpulse.log", "INFO Rival#56789 left the lobby\n");
        var session = Install();
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", string.Join("\r\n", PowerLines) + "\r\n");

        var (_, files) = Archive(Bundle(new GameLogsLookup(true, session)), includeGameLogs: true);

        var power = files["game-logs/Power.log"];
        var own = files["hearthpulse.log"];
        foreach (var gone in new[] { "Зулу#2345", "Rival#56789", "37660911", "52004477" })
        {
            Assert.DoesNotContain(gone, power, StringComparison.Ordinal);
            Assert.DoesNotContain(gone, own, StringComparison.Ordinal);
        }

        // Rival is seen first (in Hearthpulse's log): Player1 everywhere; the local player is Player2.
        Assert.Contains("INFO Player1#1001 left the lobby", own, StringComparison.Ordinal);
        Assert.Contains("PlayerName=Player2#1002", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=Player1#1001 tag=MULLIGAN_STATE", power, StringComparison.Ordinal);
        Assert.Contains("id=1 Player=Player2#1002 TaskList=", power, StringComparison.Ordinal);
        // Account numbers by order of first sight, the region part (hi) kept; the game's own line ends kept.
        Assert.Equal(2, Regex.Matches(power, Regex.Escape("GameAccountId=[hi=144115198130930503 lo=1]")).Count);
        Assert.Equal(2, Regex.Matches(power, Regex.Escape("GameAccountId=[hi=144115198130930503 lo=2]")).Count);
        Assert.EndsWith("value=DONE \r\n", power, StringComparison.Ordinal);
        // No account (an AI or the Battlegrounds stand-in) stays no account: lo=0 is kept as it is (review Q7).
        Assert.Contains("Player EntityID=4 PlayerID=3 GameAccountId=[hi=0 lo=0]\r\n", power, StringComparison.Ordinal);

        // The parser reads the same game: players, ids, regions, accounts present, the local player, the entities.
        var before = Parse(PowerLines);
        var after = Parse(power.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(before.Players.Keys.Order(), after.Players.Keys.Order());
        foreach (var (id, player) in before.Players)
        {
            Assert.Equal(player.AccountHi, after.Players[id].AccountHi);
            Assert.Equal(player.HasAccount, after.Players[id].HasAccount);
        }

        Assert.Equal(["Player2#1002", "Player1#1001", string.Empty], after.Players.OrderBy(p => p.Key).Select(p => p.Value.Name));
        Assert.Equal([1UL, 2UL, 0UL], after.Players.OrderBy(p => p.Key).Select(p => p.Value.AccountLo));
        Assert.False(after.Players[3].HasAccount);
        Assert.Equal(before.LocalPlayerId, after.LocalPlayerId);
        Assert.Equal(before.Entities.Count, after.Entities.Count);
    }

    private static GameState Parse(IEnumerable<string> lines)
    {
        var parser = new PowerParser(new DateOnly(2026, 11, 25));
        foreach (var line in lines)
        {
            parser.Feed(line);
        }

        return parser.Game!;
    }

    [Fact]
    public void Two_battletags_with_one_name_get_two_stand_ins_and_the_archive_replays_the_same_game()
    {
        // Alex#1234 the opponent, Alex#5678 the local player (review Q9); a bare "Alex" could be either: a number of its own.
        string[] lines =
        [
            "D 18:02:11.4402560 GameState.DebugPrintPower() - CREATE_GAME",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -     GameEntity EntityID=1",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -     Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=37660911]",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CONTROLLER value=1",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -     Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=52004477]",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CONTROLLER value=2",
            "D 18:02:11.4402560 GameState.DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "D 18:02:11.4402560 GameState.DebugPrintGame() - GameType=GT_RANKED",
            "D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=1, PlayerName=Alex#1234",
            "D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=2, PlayerName=Alex#5678",
            "D 18:02:12.1000000 GameState.DebugPrintEntityChoices() - id=1 Player=Alex#5678 TaskList= ChoiceType=MULLIGAN CountMin=0 CountMax=3",
            "D 18:02:13.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Alex#1234 tag=RESOURCES value=1 ",
            "D 18:02:13.1000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Alex#5678 tag=RESOURCES value=2 ",
            "D 18:02:13.2000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Alex#5678 tag=MULLIGAN_STATE value=DONE ",
            "D 18:02:14.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Alex tag=NUM_TURNS_IN_PLAY value=1 ",
            "D 18:02:15.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Alex#1234 tag=PLAYSTATE value=CONCEDED ",
        ];

        var scrubbed = ThroughArchive(lines);

        Assert.DoesNotContain(scrubbed, line => line.Contains("Alex", StringComparison.Ordinal));
        Assert.Contains("D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=1, PlayerName=Player1#1001", scrubbed);
        Assert.Contains("D 18:02:12.0000000 GameState.DebugPrintGame() - PlayerID=2, PlayerName=Player2#1002", scrubbed);
        Assert.Contains("D 18:02:14.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=Player3 tag=NUM_TURNS_IN_PLAY value=1 ", scrubbed);
        var before = Parse(lines);
        var after = Parse(scrubbed);
        Assert.Equal(2, before.LocalPlayerId);
        Assert.Equal(before.LocalPlayerId, after.LocalPlayerId);
        Assert.Equal(EntityTags(before), EntityTags(after));
    }

    /// <summary>Every entity's tags by id, the names left out: what the parser read of the game.</summary>
    private static List<string> EntityTags(GameState game) => [.. game.Entities.OrderBy(e => e.Key).Select(e => e.Key + ": " +
        string.Join(", ", e.Value.Tags.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + "=" + t.Value)))];

    /// <summary>
    /// Battlegrounds and Duos lines in Power.log's shapes (owner's log of 2026-11-15, a Duos log; names made up): the stand-in
    /// opponent renamed bare before each combat, the teammate, a hero's name as an entity, a short name inside other words,
    /// a card in brackets, the game entity, an entity by number and a META_DATA that names a player.
    /// </summary>
    private static readonly string[] BattlegroundsLines =
    [
        "D 20:36:50.0000000 GameState.DebugPrintPower() - CREATE_GAME",
        "D 20:36:50.0000000 GameState.DebugPrintPower() -     Player EntityID=5 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=37660911]",
        "D 20:36:50.0000000 GameState.DebugPrintPower() -     Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]",
        "D 20:36:50.0000000 GameState.DebugPrintGame() - PlayerID=2, PlayerName=Зулу#2345",
        "D 20:36:50.0000000 GameState.DebugPrintGame() - PlayerID=10, PlayerName=Teammate",
        "D 20:41:12.3000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=Somebody77 tag=BACON_CURRENT_COMBAT_PLAYER_ID value=7 ",
        "D 20:41:12.3000000 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=Somebody77 tag=BACON_CURRENT_COMBAT_PLAYER_ID value=7 ",
        "D 20:41:12.4000000 GameState.DebugPrintPower() - BLOCK_START BlockType=TRIGGER Entity=Somebody77 EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=-1 Target=0 SubOption=-1 TriggerKeyword=TAG_NOT_SET",
        "D 20:41:12.5000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=Star tag=BACON_CURRENT_COMBAT_PLAYER_ID value=3 ",
        "D 20:41:12.6000000 PowerSpellController [taskListId=2094].StartPowerSpell() - FAILED to attach task list to spell Bacon_MinionSwap",
        "D 20:41:12.7000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=Ванесса ван Клиф tag=NUM_TURNS_IN_PLAY value=1 ",
        "D 20:41:12.8000000 GameState.DebugPrintOptions() -     target 8 entity=[entityName=Таверна Боба id=69 zone=PLAY zonePos=0 cardId=TB_BaconShopBob player=10]",
        "D 20:41:12.9000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=TURN value=3 ",
        "D 20:41:12.9000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=6 tag=BACON_DUO_TEAMMATE_PLAYER_ID value=2 ",
        "D 20:41:13.0000000 GameState.DebugPrintPower() -     TAG_CHANGE Entity=Somebody77 tag=PLAYSTATE value=LOST ",
        "D 20:41:13.1000000 GameState.DebugPrintPower() -                 Source = Somebody77 ",
        "D 20:41:13.2000000 GameState.DebugPrintEntityChoices() - id=4 Player=Teammate TaskList= ChoiceType=GENERAL CountMin=1 CountMax=1",
        "D 20:57:14.4107603 GameState.DebugPrintPower() -         META_DATA - Meta=CONTROLLER_AND_ZONE_CHANGE Data=0 InfoCount=4",
        "D 20:57:14.4107603 GameState.DebugPrintPower() -                     Info[0] = [entityName=Вертлявый разведчик id=389 zone=PLAY zonePos=1 cardId=BG32_330 player=9]",
        "D 20:57:14.4107603 GameState.DebugPrintPower() -                     Info[1] = 9",
        "D 20:57:14.4107603 GameState.DebugPrintPower() -                     Info[2] = GameEntity",
        "D 20:57:14.4107603 GameState.DebugPrintPower() -                     Info[3] = Ванесса ван Клиф",
    ];

    /// <summary>The values where a line puts a player by name (the places the parser reads, a block's source, a META_DATA's Info).</summary>
    private static IEnumerable<string> PlayerValues(IEnumerable<string> lines) => lines.SelectMany(line => PlayerPlace.Matches(line))
        .Select(m => m.Groups["name"].Value.TrimEnd())
        .Where(v => v.Length > 0 && v[0] != '[' && v != "GameEntity" && v != GameState.UnknownPlayerName && !v.All(char.IsAsciiDigit));

    private static readonly Regex PlayerPlace = new(
        @"(?:\bTAG_CHANGE Entity=|\bBLOCK_START BlockType=\w+ Entity=)(?<name>.+?)(?= tag=| EffectCardId=)|\bPlayerName=(?<name>[^\r\n]+)" +
        @"|\bid=\d+ Player=(?<name>.+?)(?= TaskList=| EntitiesCount=)|\bSource = (?<name>[^\r\n]+)|\bInfo\[\d+\] = (?<name>[^\r\n]+)");

    /// <summary>The game log as the archive writes it, through a session folder of this test's install.</summary>
    private List<string> ThroughArchive(IEnumerable<string> lines)
    {
        FullDataFolder();
        var session = Install();
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", string.Join("\r\n", lines) + "\r\n");
        var (_, files) = Archive(Bundle(new GameLogsLookup(true, session)), includeGameLogs: true);
        return [.. files["game-logs/Power.log"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];
    }

    [Fact]
    public void Battlegrounds_and_duos_names_without_a_number_become_stand_ins_where_the_log_names_a_player()
    {
        FullDataFolder();
        // The lobby player in Hearthpulse's own log, as a BattleTag: the same number as its bare form in the game's log.
        Put("data/hearthpulse.log", "INFO Somebody77#4242 in the lobby\n");
        var session = Install();
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Power.log", string.Join("\r\n", BattlegroundsLines) + "\r\n");

        var (_, files) = Archive(Bundle(new GameLogsLookup(true, session)), includeGameLogs: true);

        var power = files["game-logs/Power.log"];
        var lines = power.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        string[] names = ["Somebody77", "Teammate", "Star", "Ванесса ван Клиф", "Зулу#2345"];
        Assert.Equal(names.Order(), PlayerValues(BattlegroundsLines).Distinct().Order());
        Assert.DoesNotContain(PlayerValues(lines), names.Contains);
        Assert.Contains("INFO Player1#1001 in the lobby", files["hearthpulse.log"], StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=Player1 tag=BACON_CURRENT_COMBAT_PLAYER_ID value=7 ", power, StringComparison.Ordinal);
        Assert.Contains("BLOCK_START BlockType=TRIGGER Entity=Player1 EffectCardId=", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=Player1 tag=PLAYSTATE value=LOST ", power, StringComparison.Ordinal);
        Assert.Contains("Source = Player1 \r\n", power, StringComparison.Ordinal);
        Assert.Contains("PlayerName=Player2#1002", power, StringComparison.Ordinal);
        Assert.Contains("PlayerName=Player3\r\n", power, StringComparison.Ordinal);
        Assert.Contains("id=4 Player=Player3 TaskList=", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=Player4 tag=BACON_CURRENT_COMBAT_PLAYER_ID value=3 ", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=Player5 tag=NUM_TURNS_IN_PLAY", power, StringComparison.Ordinal);
        // A META_DATA's Info line names a player too (review Q10); an entity, a number and the game entity there stay.
        Assert.Contains("Info[3] = Player5\r\n", power, StringComparison.Ordinal);
        Assert.Contains("Info[0] = [entityName=Вертлявый разведчик id=389", power, StringComparison.Ordinal);
        Assert.Contains("Info[1] = 9\r\n", power, StringComparison.Ordinal);
        Assert.Contains("Info[2] = GameEntity\r\n", power, StringComparison.Ordinal);
        // A short name is replaced only where the log names a player: other text, cards, the game entity and numbers stay.
        Assert.Contains("[taskListId=2094].StartPowerSpell() - FAILED", power, StringComparison.Ordinal);
        Assert.Contains("entity=[entityName=Таверна Боба id=69", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=GameEntity tag=TURN value=3 ", power, StringComparison.Ordinal);
        Assert.Contains("TAG_CHANGE Entity=6 tag=BACON_DUO_TEAMMATE_PLAYER_ID value=2 ", power, StringComparison.Ordinal);
        Assert.Contains("Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]\r\n", power, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battlegrounds_game_replays_the_same_combats_opponents_and_places_with_stand_in_names()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BattlegroundsGame");
        var original = BattlegroundsFixtureTests.Lines().ToList();
        var names = PlayerValues(original).ToHashSet(StringComparer.Ordinal);
        // The real game's forms: the player's BattleTag, the stand-in opponent's bare names, a hero's name as an entity.
        Assert.Superset(new HashSet<string>(["Player#1000", "Innkeeper", "Opponent", "Ванесса ван Клиф"], StringComparer.Ordinal), names);

        var scrubbed = ThroughArchive(original);

        Assert.DoesNotContain(PlayerValues(scrubbed), names.Contains);
        var text = string.Join("\n", scrubbed);
        Assert.DoesNotContain("Opponent", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Innkeeper", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Player#1000", text, StringComparison.Ordinal);
        CardDb cards;
        using (var gz = new GZipStream(File.OpenRead(Path.Combine(folder, "cards.json.gz")), CompressionMode.Decompress))
        {
            cards = CardDb.Parse("ruRU", gz);
        }

        var before = BattlegroundsReplay.Run(original, new DateOnly(2026, 10, 23), cards);
        var after = BattlegroundsReplay.Run(scrubbed, new DateOnly(2026, 10, 23), cards);

        Assert.NotNull(after.Final);
        Assert.NotEmpty(after.Final!.Combats);
        Assert.Equal(7, after.Final.Opponents.Count);
        Assert.Equal(Shape(before.Final), Shape(after.Final));
        Assert.Equal(Shape(before.HeroPick), Shape(after.HeroPick));
    }

    [Fact]
    public void A_duos_game_replays_the_same_with_its_teammate_and_the_opponents_as_stand_ins()
    {
        // The Duos golden log, its stand-in opponent named bare as the game names it before a combat.
        var original = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Replay", "Golden", "duos-partner.log"))
            .Select(line => line.Replace("Rival#4321", "Rival", StringComparison.Ordinal)).ToList();
        var names = PlayerValues(original).ToHashSet(StringComparer.Ordinal);
        Assert.Superset(new HashSet<string>(["Me#1234", "Pal#5555", "Rival"], StringComparer.Ordinal), names);

        var scrubbed = ThroughArchive(original);

        Assert.DoesNotContain(PlayerValues(scrubbed), names.Contains);
        var before = BattlegroundsReplay.Run(original, new DateOnly(2026, 11, 1), CardDb.Empty);
        var after = BattlegroundsReplay.Run(scrubbed, new DateOnly(2026, 11, 1), CardDb.Empty);
        Assert.NotNull(after.Final);
        Assert.True(after.Final!.IsDuos);
        Assert.Equal(Shape(before.Final), Shape(after.Final));
    }

    /// <summary>What a Battlegrounds view says about the game, without the names: turn, place, hero, combats, opponents.</summary>
    private static string Shape(BattlegroundsView? view) => view is null ? "none" : string.Join(" | ",
        $"turn {view.Turn}, place {view.Place}, hero {view.HeroCardId}, health {view.Health}, duos {view.IsDuos}, options {view.HeroOptions.Count}",
        string.Join("; ", view.Combats.Select(c => $"{c.Turn}/{c.OpponentPlayerId}/{c.OpponentHeroCardId}/{c.Result}/{c.DamageTaken}/{c.DamageDealt}")),
        string.Join("; ", view.Opponents.OrderBy(o => o.PlayerId)
            .Select(o => $"{o.PlayerId}/{o.HeroCardId}/{o.Health}/{o.Place}/{o.Dead}/{o.LastBoard.Count}/{o.LastResult}")));

    [Fact]
    public void Achievements_log_names_the_account_by_a_stand_in_number()
    {
        var session = Install();
        Put("hs/Logs/Hearthstone_2026_11_25_18_02_11/Achievements.log",
            "D 18:10:00.0000000 OnAchieveNotification PlayerID=482913 ID=110 Complete=False New=False Remove=False Amount=1\r\n" +
            "D 18:12:00.0000000 OnAchieveNotification PlayerID=482913 ID=218 Complete=False New=False Remove=False Amount=1\r\n");
        FullDataFolder();

        var (_, files) = Archive(Bundle(new GameLogsLookup(true, session)), includeGameLogs: true);

        var achievements = files["game-logs/Achievements.log"];
        Assert.DoesNotContain("482913", achievements, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(achievements, Regex.Escape("OnAchieveNotification PlayerID=1 ID=")).Count);
        Assert.Contains("ID=218 Complete=False New=False Remove=False Amount=1\r\n", achievements, StringComparison.Ordinal);
    }

    [Fact]
    public void The_8_3_form_of_the_users_folder_is_cut_too()
    {
        FullDataFolder();
        Put("data/hearthpulse.log", "ERROR Update failed: C:\\Users\\IVANPE~1\\AppData\\Local\\Temp\\hp-update\\x.zip\n");
        var bundle = new DiagnosticsBundle(Data, GameLogsLookup.NotFound, Context)
        {
            UtcNow = () => Now,
            UserProfile = @"C:\Users\Ivan Petrov",
            UserProfileShort = @"C:\Users\IVANPE~1",
            UserName = "Ivan Petrov",
        };

        var (_, files) = Archive(bundle, includeGameLogs: false);

        Assert.Contains($"Update failed: {Home}\\AppData\\Local\\Temp\\hp-update\\x.zip", files["hearthpulse.log"], StringComparison.Ordinal);
        Assert.DoesNotContain("IVANPE", files["hearthpulse.log"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PoolSecret", true)]
    [InlineData("AccessToken", true)]
    [InlineData("refresh_token", true)]
    [InlineData("ApiKey", true)]
    [InlineData("api_key", true)]
    [InlineData("apiKey", true)]
    [InlineData("Password", true)]
    [InlineData("OAuthToken", true)]
    [InlineData("SessionKey", true)]
    [InlineData("Pin", true)]
    [InlineData("auth", true)]
    [InlineData("Keyboard", false)]
    [InlineData("AuthorName", false)]
    [InlineData("MonkeyMode", false)]
    [InlineData("PinnedPanels", false)]
    [InlineData("Signal", false)]
    [InlineData("Theme", false)]
    [InlineData("HearthstonePath", false)]
    [InlineData("ClientSecret", true)]
    [InlineData("ShowSecrets", false)]
    [InlineData("secrets", false)]
    [InlineData("secrets-played", false)]
    public void A_secret_field_is_known_by_its_words_not_by_its_letters(string name, bool secret) =>
        Assert.Equal(secret, DiagnosticsBundle.IsSecretName(name));

    [Fact]
    public void The_form_is_the_repositorys_template_chooser_and_the_file_name_carries_the_time()
    {
        Assert.Equal("https://github.com/Zulut30/Hearthpulse-Companion/issues/new/choose", DiagnosticsBundle.IssueFormUrl);
        Assert.Equal("hearthpulse-diagnostics-2026-11-26-0905.zip", DiagnosticsBundle.FileName(new DateTime(2026, 11, 26, 9, 5, 0, DateTimeKind.Local)));
        Assert.Equal(25L * 1024 * 1024, DiagnosticsBundle.AttachmentLimit);
    }
}

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Power;

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

        // The parser reads the same game: players, ids, regions, accounts present, the local player, the entities.
        var before = Parse(PowerLines);
        var after = Parse(power.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(before.Players.Keys.Order(), after.Players.Keys.Order());
        foreach (var (id, player) in before.Players)
        {
            Assert.Equal(player.AccountHi, after.Players[id].AccountHi);
            Assert.Equal(player.HasAccount, after.Players[id].HasAccount);
        }

        Assert.Equal(["Player2#1002", "Player1#1001"], after.Players.OrderBy(p => p.Key).Select(p => p.Value.Name));
        Assert.Equal([1UL, 2UL], after.Players.OrderBy(p => p.Key).Select(p => p.Value.AccountLo));
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

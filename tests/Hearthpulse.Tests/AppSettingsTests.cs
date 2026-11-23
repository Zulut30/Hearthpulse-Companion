using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthpulse.Core;

namespace Hearthpulse.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hp-settings-").FullName;
    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly JsonSerializerOptions ReflectionJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(PickHints.Off)]
    [InlineData(PickHints.StatsOnly)]
    [InlineData(PickHints.Highlight)]
    public void Settings_file_round_trips_the_reflection_format(PickHints hints)
    {
        var at = new DateTime(2025, 11, 1, 12, 34, 56, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            PickHints = hints,
            Locale = "ruRU",
            Language = "ru",
            SettingsTab = "Stream",
            OverlayScale = 1.2,
            OverlayOpacity = 0.8,
            CardPoolBrowserWidth = AppSettings.MinCardPoolBrowserWidth,
            LadderSessionBreakHours = 3,
            TwitchDelaySeconds = 3,
            MetaFormat = "wild",
            MetaRank = "legend",
            MulliganRank = "diamond",
            MetaPeriod = "past_month",
            StreamWindowBackground = StreamBackgrounds.Green,
            LeaderboardTop = 0.3,
            LeaderboardSlotHeight = 0.08,
            LeaderboardWidth = 0.2,
            TimestampLeadInSeconds = 4,
            Theme = "Shadow",
            ThemeAccentHue = 100,
            ThemeBaseHue = 90,
            ThemeBaseTint = 0.5,
            ThemeAccent = "Amber",
            BackgroundHero = "HERO_01",
            BackgroundIntensity = 0.5,
            CornerStyle = ThemeLook.Sharp,
            Density = ThemeLook.Compact,
            InterfaceScale = 1.1,
            ClientLocale = "deDE",
            HearthstonePath = "game",
            CompFocus = "MURLOC",
            HdtFolder = "migration",
            LastMigration = new() { At = at, Added = 1, Already = 2, Skipped = 3, Decks = 4, Replays = 5 },
            StreamSessionStart = at,
            PoolConsent = true,
            PoolEndpoint = "https://example.invalid",
            PoolSecret = "fixture",
            CounterModes = { ["test"] = "off" },
            Panels = { ["deck"] = new() { X = 0.25, Y = 0.75, Scale = 1.2 } },
            PanelLooks = { ["deck"] = new() { Compact = true, Opacity = 0.75 } },
            StreamWindows = { ["session"] = new() { X = -20, Y = 30, Width = 400, Height = 500 } },
            TrackedAchievements = [12, 7],
            LeaderboardGeometryVersion = 2,
        };

        // Exercise each persisted switch with a value its constructor would not restore by default.
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.PropertyType == typeof(bool) && property.CanWrite && !property.IsDefined(typeof(JsonIgnoreAttribute), false))
            {
                property.SetValue(settings, !((bool)property.GetValue(settings)!));
            }
        }

        var expected = JsonSerializer.Serialize(settings, ReflectionJson);
        Assert.Equal(expected, settings.ToJson());
        File.WriteAllText(FilePath, expected);
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(expected, JsonSerializer.Serialize(loaded, ReflectionJson));
        Assert.Equal(expected, loaded.ToJson());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"stAtsoNLy\"")]
    public void Generated_reader_preserves_enum_forms_and_last_duplicate_property(string hints)
    {
        var json = $$$"""
            {"PickHints":{{{hints}}},"PanelLooks":{"deck":{"Compact":true}},"PanelLooks":{},
             "SettingsTab":"Tracker","SettingsTab":"Data","LeaderboardGeometryVersion":2,
             "ClientLocale":{"ignored":true},"unknown":[1,{"nested":true}]}
            """;
        var reference = JsonSerializer.Deserialize<AppSettings>(json, ReflectionJson);
        Assert.Equal(JsonSerializer.Serialize(reference, ReflectionJson), AppSettings.FromJson(json).ToJson());
    }

    [Fact]
    public void Older_file_is_normalized_and_migrated_before_saving_in_the_reflection_format()
    {
        File.WriteAllText(FilePath, """
            {"ShowHeroPick":false,"Locale":null,"LeaderboardGeometryVersion":0,
             "Panels":{"deck":{"X":-1,"Y":0.75,"Scale":1},"mulligan-strip":{"Scale":1}}}
            """);
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal("auto", loaded.Locale);
        Assert.Equal(PickHints.Off, loaded.PickHints);
        Assert.Null(loaded.ShowHeroPick);
        Assert.Equal(2, loaded.LeaderboardGeometryVersion);
        Assert.Equal(0, Assert.Single(loaded.Panels).Value.X);
        var expected = JsonSerializer.Serialize(loaded, ReflectionJson);
        loaded.Save(FilePath);
        Assert.Equal(expected, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Null_collections_and_required_strings_recover_without_losing_other_settings()
    {
        var settings = AppSettings.FromJson("""
            {"Panels":null,"CounterModes":null,"Locale":null,"Language":null,"SettingsTab":null,
             "MetaFormat":null,"MetaRank":null,"MetaPeriod":null,"Theme":null,"OverlayEnabled":false}
            """);

        Assert.Empty(settings.Panels);
        Assert.Empty(settings.CounterModes);
        Assert.Equal(("auto", "auto", "General", "standard", "all", "past_week", "Tavern"),
            (settings.Locale, settings.Language, settings.SettingsTab, settings.MetaFormat, settings.MetaRank, settings.MetaPeriod, settings.Theme));
        Assert.False(settings.OverlayEnabled);
        Assert.Equal(AppSettings.DefaultLeaderboardTop, settings.LeaderboardTop);
    }

    [Fact]
    public void Null_entries_are_removed_while_valid_and_unknown_panel_keys_survive_migration()
    {
        var settings = AppSettings.FromJson("""
            {"Panels":{"broken":null,"future-panel":{"X":0.25,"Y":0.75,"Scale":1.2},"mulligan-strip":null},
             "CounterModes":{"broken":null,"future-counter":"Off"},"Theme":"Frost","Language":"ru"}
            """);

        var panel = Assert.Single(settings.Panels);
        Assert.Equal("future-panel", panel.Key);
        Assert.Equal((0.25, 0.75, 1.2), (panel.Value.X, panel.Value.Y, panel.Value.Scale));
        Assert.Equal("Off", Assert.Single(settings.CounterModes).Value);
        Assert.Equal(("Frost", "ru"), (settings.Theme, settings.Language));
        Assert.Equal("ruRU", settings.CardLocale());
    }

    [Fact]
    public void Invalid_enums_and_geometry_recover_to_supported_values()
    {
        var settings = AppSettings.FromJson("""
            {"PickHints":999,"CompMode":999,"OverlayScale":-10,"OverlayOpacity":2,
             "CardPoolBrowserWidth":99999,"LadderSessionBreakHours":0,"ThemeBaseTint":-5,
             "Panels":{"deck":{"X":-1,"Y":2,"Scale":100}}}
            """);

        Assert.Equal(PickHints.StatsOnly, settings.PickHints);
        Assert.Equal((0.6, 1.0, 640.0, 1.0, 0.0),
            (settings.OverlayScale, settings.OverlayOpacity, settings.CardPoolBrowserWidth, settings.LadderSessionBreakHours, settings.ThemeBaseTint));
        var panel = settings.Panels["deck"];
        Assert.Equal((0.0, 1.0, 1.6), (panel.X, panel.Y, panel.Scale));
    }

    [Fact]
    public void Panel_looks_load_from_old_files_and_keep_only_what_differs_from_the_default()
    {
        // A file from before panel looks: no PanelLooks at all.
        var old = AppSettings.FromJson("""{"Panels":{"deck":{"X":0.9,"Y":0.1,"Scale":1.1}}}""");
        Assert.Empty(old.PanelLooks);
        Assert.Equal(1.1, old.Panels["deck"].Scale);

        var settings = AppSettings.FromJson("""
            {"PanelLooks":{"deck":{"Opacity":0.7,"Compact":true},"opponent":{"Opacity":0.05},"counters":{"Opacity":1,"Compact":false},
             "broken":null,"lobby":{"Opacity":2,"Compact":true}}}
            """);

        Assert.Equal(new[] { "deck", "lobby", "opponent" }, settings.PanelLooks.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((0.7, true), (settings.PanelLooks["deck"].Opacity, settings.PanelLooks["deck"].Compact));
        Assert.Equal(PanelLook.MinOpacity, settings.PanelLooks["opponent"].Opacity);
        Assert.Equal((1.0, true), (settings.PanelLooks["lobby"].Opacity, settings.PanelLooks["lobby"].Compact));

        // And back: the look survives a save and load, IsDefault is not written.
        var json = settings.ToJson();
        Assert.DoesNotContain("IsDefault", json, StringComparison.Ordinal);
        Assert.Equal(0.7, AppSettings.FromJson(json).PanelLooks["deck"].Opacity);
    }

    [Fact]
    public void Settings_of_the_removed_game_plan_still_load()
    {
        // Saved by a version with the game plan panel: the file loads, its placement is kept, the comp mode and the chosen
        // comp are dropped at the next save.
        var settings = AppSettings.FromJson("""
            {"CompMode":"Auto","ChosenCompId":"murlocs","CompFocus":"MURLOC","OverlayEnabled":true,
             "Panels":{"plan":{"X":0.8,"Y":0.1,"Scale":1.1},"deck":{"X":0.9,"Y":0.1,"Scale":1}}}
            """);

        Assert.True(settings.OverlayEnabled);
        Assert.Equal(new[] { "deck", "plan" }, settings.Panels.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("ChosenCompId", settings.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("CompMode", settings.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Non_finite_numbers_do_not_reach_layout_or_time_span_construction()
    {
        var settings = AppSettings.FromJson("""
            {"OverlayScale":1e999,"LadderSessionBreakHours":1e999,"ThemeAccentHue":1e999,
             "Panels":{"deck":{"X":1e999}}}
            """);

        Assert.Equal(1, settings.OverlayScale);
        Assert.Equal(TimeSpan.FromHours(2), TimeSpan.FromHours(settings.LadderSessionBreakHours));
        Assert.Equal(42, settings.ThemeAccentHue);
        Assert.Empty(settings.Panels);
        Assert.NotEmpty(settings.ToJson());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"Panels":[]} """)]
    [InlineData("""{"Panels":{"deck":5}}""")]
    [InlineData("""{"OverlayScale":"wide"}""")]
    public void Unreadable_files_are_preserved_before_defaults_can_replace_them(string original)
    {
        File.WriteAllText(FilePath, original);

        var settings = AppSettings.Load(FilePath);

        Assert.True(settings.OverlayEnabled);
        Assert.Empty(settings.Panels);
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.Equal(original, File.ReadAllText(FilePath + ".invalid"));
        settings.OverlayEnabled = false;
        settings.Save(FilePath);
        Assert.False(AppSettings.Load(FilePath).OverlayEnabled);
        Assert.Equal(original, File.ReadAllText(FilePath + ".invalid"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"wide\"")]
    [InlineData("\"Sideways\"")]
    public void One_unreadable_value_does_not_reset_the_other_settings(string value)
    {
        var original = $$"""{"OverlayEnabled":false,"Theme":"Frost","OverlayScale":{{value}},"PickHints":{{value}}}""";
        File.WriteAllText(FilePath, original);

        var settings = AppSettings.Load(FilePath);

        Assert.False(settings.OverlayEnabled);
        Assert.Equal("Frost", settings.Theme);
        Assert.Equal(1, settings.OverlayScale);
        Assert.Equal(PickHints.StatsOnly, settings.PickHints);
        Assert.Equal(original, File.ReadAllText(FilePath + ".invalid"));
    }

    [Fact]
    public void A_previous_diagnostic_copy_is_never_overwritten()
    {
        File.WriteAllText(FilePath + ".invalid", "first corrupt file");
        const string original = "{ second corrupt file: кириллица\n";
        File.WriteAllText(FilePath, original);

        AppSettings.Load(FilePath).Save(FilePath);

        Assert.Equal("first corrupt file", File.ReadAllText(FilePath + ".invalid"));
        var newBackup = Assert.Single(Directory.EnumerateFiles(_directory),
            path => Path.GetFileName(path).StartsWith("settings.json.invalid.", StringComparison.Ordinal));
        Assert.Equal(original, File.ReadAllText(newBackup));
        Assert.True(AppSettings.Load(FilePath).OverlayEnabled);
    }

    [Fact]
    public void A_failed_backup_prevents_overwriting_and_saving_recovers_after_storage_is_fixed()
    {
        const string original = "not json";
        File.WriteAllText(FilePath, original);
        Directory.CreateDirectory(FilePath + ".invalid");

        var settings = AppSettings.Load(FilePath);
        settings.OverlayEnabled = false;
        settings.Save(FilePath);
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));

        Directory.Delete(FilePath + ".invalid");
        settings.Save(FilePath);
        Assert.Equal(original, File.ReadAllText(FilePath + ".invalid"));
        Assert.False(AppSettings.Load(FilePath).OverlayEnabled);
    }

    [WindowsFact]
    public void A_valid_file_held_open_for_a_moment_at_startup_is_read_not_reset()
    {
        // An antivirus scan or a cloud sync can hold the file for a moment: it was taken for unreadable, the app started
        // with defaults and later wrote them over the valid file.
        var saved = new AppSettings { OverlayEnabled = false };
        saved.Save(FilePath);
        var held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var pauses = 0;

        // The other program lets go during the first pause between attempts.
        var settings = AppSettings.Load(FilePath, () =>
        {
            pauses++;
            held.Dispose();
        });

        Assert.Equal(1, pauses);
        Assert.False(settings.OverlayEnabled);
        Assert.False(File.Exists(FilePath + ".invalid"));
    }

    [Fact]
    public void Missing_and_valid_files_need_no_diagnostic_copy()
    {
        var settings = AppSettings.Load(FilePath);
        Assert.Empty(Directory.GetFiles(_directory));
        settings.Language = "ru";
        settings.Panels["deck"] = new PanelPlacement { X = 0.3, Y = 0.2, Scale = 1.1 };
        settings.Save(FilePath);

        var loaded = AppSettings.Load(FilePath);
        Assert.Equal("ru", loaded.Language);
        Assert.Equal((0.3, 0.2, 1.1), (loaded.Panels["deck"].X, loaded.Panels["deck"].Y, loaded.Panels["deck"].Scale));
        Assert.Single(Directory.GetFiles(_directory));
    }
}

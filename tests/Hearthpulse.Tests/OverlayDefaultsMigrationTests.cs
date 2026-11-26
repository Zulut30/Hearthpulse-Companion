using Hearthpulse.Core;

namespace Hearthpulse.Tests;

/// <summary>
/// Panels that repeat what the game already shows went off by default in 0.16.0: the turn timers, the end-of-game toast
/// and the Battlegrounds lobby list. A settings file stores every switch, so the new defaults reach an existing install
/// only through the one-time migration (<see cref="AppSettings.OverlayDefaultsVersion"/>); a switch the player turns back
/// on afterwards stays on.
/// </summary>
public sealed class OverlayDefaultsMigrationTests
{
    [Fact]
    public void A_new_install_starts_with_the_three_panels_off()
    {
        var settings = new AppSettings();
        Assert.Equal((false, false, false), (settings.ShowTurnTimer, settings.ShowGameToasts, settings.ShowLobbyPanel));

        // A first start without a file runs the same migration: the version is recorded, nothing else changes.
        var path = Path.Combine(Directory.CreateTempSubdirectory("hp-defaults-").FullName, "settings.json");
        try
        {
            var loaded = AppSettings.Load(path);
            Assert.Equal((false, false, false, 1), (loaded.ShowTurnTimer, loaded.ShowGameToasts, loaded.ShowLobbyPanel, loaded.OverlayDefaultsVersion));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void An_older_file_turns_the_three_panels_off_once()
    {
        // An install from before 0.16.0 wrote every switch, "on" included, and no version.
        var settings = AppSettings.FromJson("""
            { "ShowTurnTimer": true, "ShowGameToasts": true, "ShowLobbyPanel": true, "ShowDeckTracker": true, "ShowSessionPanel": true }
            """);

        Assert.Equal((false, false, false), (settings.ShowTurnTimer, settings.ShowGameToasts, settings.ShowLobbyPanel));
        Assert.Equal(1, settings.OverlayDefaultsVersion);
        // Only those three: the player's other switches stay as they were.
        Assert.True(settings.ShowDeckTracker);
        Assert.True(settings.ShowSessionPanel);
        Assert.Contains("\"OverlayDefaultsVersion\": 1", settings.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_panel_turned_back_on_after_the_migration_stays_on()
    {
        var migrated = AppSettings.FromJson("""{ "ShowTurnTimer": true, "ShowLobbyPanel": true }""");
        migrated.ShowTurnTimer = true;
        migrated.ShowGameToasts = true;

        var again = AppSettings.FromJson(migrated.ToJson());

        Assert.Equal((true, true, false, 1), (again.ShowTurnTimer, again.ShowGameToasts, again.ShowLobbyPanel, again.OverlayDefaultsVersion));
    }

    [Fact]
    public void A_file_with_the_version_keeps_its_switches()
    {
        var settings = AppSettings.FromJson("""{ "OverlayDefaultsVersion": 1, "ShowTurnTimer": true, "ShowGameToasts": true, "ShowLobbyPanel": true }""");

        Assert.Equal((true, true, true), (settings.ShowTurnTimer, settings.ShowGameToasts, settings.ShowLobbyPanel));
    }
}

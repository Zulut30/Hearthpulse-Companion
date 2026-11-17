using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.SourceCapture;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Game records, journals and the stats pool's shared games (docs/game-stats.md): on by default with no notice in the
/// main window; the two Settings → Data switches are the only control, and a refusal is always kept, in both apps'
/// view models.
/// </summary>
public sealed class GameStatsSharingTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    private static dynamic Model(bool wpf, AppSettings settings) =>
        wpf ? new WpfMain(History(), settings, TestApp.Data) : new AvaMain(History(), settings, TestApp.Data);

    /// <summary>The one-time notice is gone from both view models: nothing to show, nothing to answer.</summary>
    [Theory]
    [InlineData(typeof(WpfMain))]
    [InlineData(typeof(AvaMain))]
    public void There_is_no_notice_to_show_or_answer(Type model)
    {
        foreach (var name in new[] { "ShowGameStatsNotice", "GameStatsNoticeDetails", "AnswerGameStats" })
        {
            Assert.Null(model.GetProperty(name));
        }
    }

    /// <summary>A fresh install: everything is on and goes to the default pool, and the status line names no notice.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_fresh_install_shares_by_default(bool wpf)
    {
        var settings = new AppSettings();
        var model = Model(wpf, settings);

        Assert.True((bool)model.GameStatsAvailable);
        Assert.True((bool)model.GameStatsSharing);
        Assert.True((bool)model.PoolSharing);
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.True(PoolClient.Sharing(settings));
        // The view model makes no install secret: the first record or shared game does, while sharing is on.
        Assert.Null(settings.PoolSecret);

        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceReady"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_a_pool_nothing_is_shared_and_the_switches_hide(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "off" };
        var model = Model(wpf, settings);

        Assert.False((bool)model.GameStatsAvailable);
        Assert.False((bool)model.PoolAvailable);
        Assert.False(GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));
        Assert.False(PoolClient.Sharing(settings));
    }

    /// <summary>
    /// "Turn off" on the notice earlier versions showed saved both refusals: they hold now that the notice is gone,
    /// and nothing turns them back on but the switches.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_refusal_given_on_the_old_notice_is_kept(bool wpf)
    {
        var settings = AppSettings.FromJson(
            """{"PoolEndpoint":"https://pool.test","GameStatsNoticeShown":true,"PoolConsentVersion":5,"GameStatsOff":true,"PoolConsent":false}""");
        var model = Model(wpf, settings);

        Assert.False((bool)model.GameStatsSharing);
        Assert.False((bool)model.PoolSharing);
        Assert.False(GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));
        Assert.False(PoolClient.Sharing(settings));
        Assert.Null(settings.PoolSecret);
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceOff"), (string)model.SourceCaptureStatusText);
    }

    /// <summary>"Got it" on the old notice, or a player who updated without ever seeing it: sharing goes on.</summary>
    [Theory]
    [InlineData(true, """{"PoolEndpoint":"https://pool.test","GameStatsNoticeShown":true,"GameStatsNoticeVersion":5,"PoolConsentVersion":5,"SourceCaptureNoticeVersion":1}""")]
    [InlineData(false, """{"PoolEndpoint":"https://pool.test","GameStatsNoticeShown":true,"GameStatsNoticeVersion":5,"PoolConsentVersion":5,"SourceCaptureNoticeVersion":1}""")]
    [InlineData(true, """{"PoolEndpoint":"https://pool.test","GameStatsNoticeShown":true,"PoolConsentVersion":4}""")]
    [InlineData(false, """{"PoolEndpoint":"https://pool.test"}""")]
    public void A_player_who_did_not_refuse_shares(bool wpf, string json)
    {
        var settings = AppSettings.FromJson(json);
        var model = Model(wpf, settings);

        Assert.True((bool)model.GameStatsSharing);
        Assert.True((bool)model.PoolSharing);
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.True(PoolClient.Sharing(settings));
    }

    /// <summary>
    /// The game records switch: off is kept and makes no secret; turned back on, records and journals go again. It
    /// does not undo the shared games' own refusal.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_game_records_switch_turns_records_and_journals_off_and_on(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false };
        var model = Model(wpf, settings);
        var raised = Raised(model);
        var changes = new List<bool>();
        model.GameStatsChanged += new Action<bool>(changes.Add);

        model.GameStatsSharing = false;

        Assert.True(settings.GameStatsOff);
        Assert.False(GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));
        Assert.Null(settings.PoolSecret);
        Assert.Contains("GameStatsSharing", raised);
        Assert.Contains("SourceCaptureStatusText", raised);
        Assert.Equal([false], changes);

        model.GameStatsSharing = true;

        Assert.False(settings.GameStatsOff);
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.False(PoolClient.Sharing(settings));
        Assert.False(settings.PoolConsent);
        Assert.Equal([false, true], changes);
    }

    /// <summary>The Settings → Data pool switch: off is the refusal, on shares again; every dependent property is told.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_pool_switch_turns_shared_games_off_and_on(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        var raised = Raised(model);
        var pool = 0;
        model.PoolSharingChanged += new Action(() => pool++);
        Assert.True(PoolClient.Sharing(settings));

        model.PoolSharing = false;

        Assert.False(settings.PoolConsent);
        Assert.False(PoolClient.Sharing(settings));
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.Contains("PoolSharing", raised);
        Assert.Equal(1, pool);

        model.PoolSharing = false;
        Assert.Equal(1, pool);

        model.PoolSharing = true;

        Assert.True(settings.PoolConsent);
        Assert.True(PoolClient.Sharing(settings));
        Assert.Equal(2, pool);
    }

    private static List<string?> Raised(dynamic model)
    {
        var raised = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)model).PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler((_, e) => raised.Add(e.PropertyName));
        return raised;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spool_storage_failure_is_not_displayed_as_an_empty_ready_queue(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        model.UpdateSourceCaptureState(new SourceCaptureState("disk_error", 0, 0, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceUnavailable"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pending_deletion_is_displayed_until_server_confirmation(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false, GameStatsOff = true };
        var model = Model(wpf, settings);
        model.UpdateSourceCaptureState(new SourceCaptureState("deleting", 0, 0, 1));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceDeleting"), (string)model.SourceCaptureStatusText);
        model.UpdateSourceCaptureState(new SourceCaptureState("disabled", 0, 0, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceOff"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Data_kept_for_another_pool_address_is_a_state_of_its_own_apart_from_off_offline_and_empty(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        var ready = Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceReady");
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0));
        Assert.Equal(ready, (string)model.SourceCaptureStatusText);

        // Game records of another pool alone change the line, in both apps through the shared view model.
        model.UpdateGameStatsState(new GameStatsState(0, 7, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 0), (string)model.SourceCaptureStatusText);
        model.UpdateSourceCaptureState(new SourceCaptureState("offline", 0, 0, 0, HeldCaptures: 2));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 2), (string)model.SourceCaptureStatusText);
        // The open spool itself is another pool's: its captures count as kept, not as "waiting to be sent".
        model.UpdateSourceCaptureState(new SourceCaptureState("other_pool", 0, 1, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 1), (string)model.SourceCaptureStatusText);

        // A deletion request for another pool shows even while sharing is off: it is not "off", it waits.
        settings.GameStatsOff = true;
        model.UpdateGameStatsState(new GameStatsState(0, 0, 1));
        model.UpdateSourceCaptureState(new SourceCaptureState("disabled", 0, 0, 0, HeldDeletions: 1));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldDeletions", 2), (string)model.SourceCaptureStatusText);

        model.UpdateGameStatsState(new GameStatsState(0, 0, 0));
        model.UpdateSourceCaptureState(new SourceCaptureState("disabled", 0, 0, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceOff"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_state_report_that_was_overtaken_on_its_way_does_not_replace_a_newer_one(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);

        // The senders number their reports; here the older one of each arrives last.
        model.UpdateGameStatsState(new GameStatsState(0, 7, 0) { Revision = 2 });
        model.UpdateGameStatsState(new GameStatsState(0, 0, 0) { Revision = 1 });
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 0), (string)model.SourceCaptureStatusText);
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, HeldCaptures: 3, Revision: 5));
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, Revision: 4));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 3), (string)model.SourceCaptureStatusText);

        // The next report of each replaces it as usual.
        model.UpdateGameStatsState(new GameStatsState(0, 0, 0) { Revision = 3 });
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, Revision: 6));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceReady"), (string)model.SourceCaptureStatusText);
    }
}

using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.GameStats;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.SourceCapture;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Game records (docs/game-stats.md): on by default, nothing sent before the one-time notice in the main window, whose
/// buttons keep them, turn them off or open the policy, in both apps' view models.
/// </summary>
public sealed class GameStatsNoticeTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    private static dynamic Model(bool wpf, AppSettings settings) =>
        wpf ? new WpfMain(History(), settings, TestApp.Data) : new AvaMain(History(), settings, TestApp.Data);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_notice_shows_once_with_a_pool_and_ok_starts_sending(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false };
        var model = Model(wpf, settings);
        var changes = new List<bool>();
        model.GameStatsChanged += new Action<bool>(changes.Add);

        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.True((bool)model.GameStatsSharing);
        Assert.False(GameStatsClient.Sharing(settings));

        model.AnswerGameStats.Execute("ok");

        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.Equal(64, settings.PoolSecret?.Length);
        Assert.Equal([true], changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_on_the_notice_turns_records_off(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false };
        var model = Model(wpf, settings);
        model.AnswerGameStats.Execute("off");

        Assert.True(settings.GameStatsOff);
        Assert.False((bool)model.GameStatsSharing);
        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.False(GameStatsClient.Sharing(settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_a_pool_there_is_no_notice(bool wpf) =>
        Assert.False((bool)Model(wpf, new AppSettings { PoolEndpoint = "off" }).ShowGameStatsNotice);

    /// <summary>The shipped default is the production pool: a fresh install sees the notice before anything is sent.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_fresh_install_sees_the_notice_first(bool wpf)
    {
        var settings = new AppSettings();
        Assert.True((bool)Model(wpf, settings).ShowGameStatsNotice);
        Assert.False(Hearthpulse.Core.Pool.PoolClient.Sharing(settings));
        Assert.False(Hearthpulse.Core.GameStats.GameStatsClient.Sharing(settings));
    }

    /// <summary>
    /// Shared games of the stats pool are on by default (the owner's decision, 2026-11-15): there is no question, the
    /// notice tells of them, and nothing goes to the pool before it was shown.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_notice_also_tells_of_the_pools_shared_games_and_ok_starts_them(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        var pool = 0;
        model.PoolSharingChanged += new Action(() => pool++);

        Assert.True((bool)model.PoolAvailable);
        Assert.True((bool)model.PoolSharing);
        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.False(PoolClient.Sharing(settings));

        model.AnswerGameStats.Execute("ok");

        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.True(PoolClient.Sharing(settings));
        Assert.Null(settings.PoolConsent);
        Assert.Equal(PoolClient.NoticeVersion, settings.PoolNoticeVersion);
        Assert.Equal(1, pool);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_on_the_notice_turns_the_pools_shared_games_off_too(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        var raised = Raised(model);
        var pool = 0;
        model.PoolSharingChanged += new Action(() => pool++);

        model.AnswerGameStats.Execute("off");

        Assert.False(settings.PoolConsent);
        Assert.True(settings.GameStatsOff);
        Assert.False((bool)model.PoolSharing);
        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.False(PoolClient.Sharing(settings));
        Assert.Contains("PoolSharing", raised);
        Assert.Equal(1, pool);
    }

    /// <summary>The policy button is not an answer: nothing is marked as shown and nothing starts.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void More_on_the_notice_leaves_it_unanswered(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);

        model.AnswerGameStats.Execute("more");

        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.False(PoolClient.Sharing(settings));
        Assert.Equal(0, settings.PoolNoticeVersion);
    }

    /// <summary>
    /// A player who saw the earlier notice (or answered the question older versions asked) sees the current one before
    /// anything goes to the pool. Game records wait for it as well: the earlier notice may have named another pool
    /// (docs/game-stats.md, "Notice contract").
    /// </summary>
    [Theory]
    [InlineData(true, null, 0)]
    [InlineData(true, true, 4)]
    [InlineData(false, null, 0)]
    [InlineData(false, true, 4)]
    public void Players_who_saw_the_earlier_text_see_the_wider_notice_before_the_pool_gets_anything(bool wpf, bool? consent, int seen)
    {
        var settings = new AppSettings
        {
            PoolEndpoint = "https://pool.test",
            PoolConsent = consent,
            PoolNoticeVersion = seen,
            GameStatsNoticeShown = true,
            SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion,
        };
        var model = Model(wpf, settings);

        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.False(PoolClient.Sharing(settings));
        Assert.False(GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));

        model.AnswerGameStats.Execute("ok");

        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.True(PoolClient.Sharing(settings));
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.True(SourceCaptureClient.Sharing(settings));
    }

    /// <summary>A refusal given earlier ("no" to the old question, or the switch) holds: no notice for it, and "ok" does not undo it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_earlier_refusal_of_the_pool_is_kept_by_the_notice(bool wpf)
    {
        var refusedAll = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false, GameStatsNoticeShown = true, GameStatsOff = true };
        Assert.False((bool)Model(wpf, refusedAll).ShowGameStatsNotice);

        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false };
        var model = Model(wpf, settings);
        Assert.True((bool)model.ShowGameStatsNotice);

        model.AnswerGameStats.Execute("ok");

        Assert.False(settings.PoolConsent);
        Assert.False((bool)model.PoolSharing);
        Assert.False(PoolClient.Sharing(settings));
        Assert.True(GameStatsClient.Sharing(settings));
    }

    /// <summary>Game records turned off earlier stay off when the wider notice is answered with "ok".</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Game_records_turned_off_earlier_stay_off_when_the_wider_notice_is_answered(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", GameStatsNoticeShown = true, GameStatsOff = true };
        var model = Model(wpf, settings);
        Assert.True((bool)model.ShowGameStatsNotice);

        model.AnswerGameStats.Execute("ok");

        Assert.True(settings.GameStatsOff);
        Assert.False(GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));
        Assert.True(PoolClient.Sharing(settings));
    }

    /// <summary>The Settings → Data switch: off is the refusal, on shares again; every dependent property is told.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_pool_switch_turns_shared_games_off_and_on_and_tells_what_depends_on_it(bool wpf)
    {
        var settings = new AppSettings
        {
            PoolEndpoint = "https://pool.test",
            PoolNoticeVersion = PoolClient.NoticeVersion,
            GameStatsNoticeShown = true,
            SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion,
        };
        var model = Model(wpf, settings);
        var raised = Raised(model);
        var pool = 0;
        model.PoolSharingChanged += new Action(() => pool++);
        Assert.True(PoolClient.Sharing(settings));

        model.PoolSharing = false;

        Assert.False(settings.PoolConsent);
        Assert.False(PoolClient.Sharing(settings));
        Assert.Contains("PoolSharing", raised);
        Assert.Contains("ShowGameStatsNotice", raised);
        Assert.Equal(1, pool);

        model.PoolSharing = false;
        Assert.Equal(1, pool);

        model.PoolSharing = true;

        Assert.True(settings.PoolConsent);
        Assert.True(PoolClient.Sharing(settings));
        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.Equal(2, pool);
    }

    /// <summary>Turned on in Settings before the notice was ever answered: the notice still comes first.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Turning_the_pool_switch_on_does_not_stand_in_for_the_notice(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false, GameStatsNoticeShown = true, GameStatsOff = true };
        var model = Model(wpf, settings);

        model.PoolSharing = true;

        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.False(PoolClient.Sharing(settings));
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
    public void Existing_users_see_the_expanded_notice_before_source_capture(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false, GameStatsNoticeShown = true };
        var model = Model(wpf, settings);
        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.False(SourceCaptureClient.Sharing(settings));
        model.AnswerGameStats.Execute("ok");
        Assert.Equal(SourceCaptureClient.NoticeVersion, settings.SourceCaptureNoticeVersion);
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.False((bool)model.ShowGameStatsNotice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_existing_opt_out_is_preserved_until_the_user_explicitly_enables_sending(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = false, GameStatsNoticeShown = true, GameStatsOff = true };
        var model = Model(wpf, settings);
        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.False(SourceCaptureClient.Sharing(settings));
        model.GameStatsSharing = true;
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.True(GameStatsClient.Sharing(settings));
        Assert.Equal(SourceCaptureClient.NoticeVersion, settings.SourceCaptureNoticeVersion);
        // The switch speaks for game records and journals only: it neither undoes the refusal of the shared games
        // nor counts as their notice.
        Assert.False(PoolClient.Sharing(settings));
        Assert.Equal(0, settings.PoolNoticeVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spool_storage_failure_is_not_displayed_as_an_empty_ready_queue(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, GameStatsNoticeVersion = PoolClient.NoticeVersion };
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
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", GameStatsNoticeShown = true, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, PoolNoticeVersion = PoolClient.NoticeVersion };
        var model = Model(wpf, settings);
        var ready = Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceReady");
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0));
        Assert.Equal(ready, (string)model.SourceCaptureStatusText);

        // Game records of another pool alone change the line, in both apps through the shared view model.
        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 7, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 0), (string)model.SourceCaptureStatusText);
        model.UpdateSourceCaptureState(new SourceCaptureState("offline", 0, 0, 0, HeldCaptures: 2));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 2), (string)model.SourceCaptureStatusText);
        // The open spool itself is another pool's: its captures count as kept, not as "waiting to be sent".
        model.UpdateSourceCaptureState(new SourceCaptureState("other_pool", 0, 1, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 1), (string)model.SourceCaptureStatusText);

        // A deletion request for another pool shows even while sharing is off: it is not "off", it waits.
        settings.GameStatsOff = true;
        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 0, 1));
        model.UpdateSourceCaptureState(new SourceCaptureState("disabled", 0, 0, 0, HeldDeletions: 1));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldDeletions", 2), (string)model.SourceCaptureStatusText);

        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 0, 0));
        model.UpdateSourceCaptureState(new SourceCaptureState("disabled", 0, 0, 0));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceOff"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_state_report_that_was_overtaken_on_its_way_does_not_replace_a_newer_one(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", GameStatsNoticeShown = true, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion, PoolNoticeVersion = PoolClient.NoticeVersion };
        var model = Model(wpf, settings);

        // The senders number their reports; here the older one of each arrives last.
        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 7, 0) { Revision = 2 });
        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 0, 0) { Revision = 1 });
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 0), (string)model.SourceCaptureStatusText);
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, HeldCaptures: 3, Revision: 5));
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, Revision: 4));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.F("Settings.Data.HeldData", 7, 3), (string)model.SourceCaptureStatusText);

        // The next report of each replaces it as usual.
        model.UpdateGameStatsState(new Hearthpulse.Core.GameStats.GameStatsState(0, 0, 0) { Revision = 3 });
        model.UpdateSourceCaptureState(new SourceCaptureState("idle", 0, 0, 0, Revision: 6));
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceReady"), (string)model.SourceCaptureStatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Older_flags_do_not_stand_in_for_the_current_notice_and_a_refusal_survives_it(bool wpf)
    {
        // An install that used a pool of its own with an earlier version: the old flag, the source notice, the old
        // question's version, and a "no" to the shared games.
        var settings = new AppSettings
        {
            PoolEndpoint = "https://pool.test", GameStatsNoticeShown = true, SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion,
            PoolNoticeVersion = PoolClient.NoticeVersion - 1, PoolConsent = false,
        };
        var model = Model(wpf, settings);

        // Nothing is sent before the notice of the current version, and the notice shows although shared games are off:
        // game records and journals still wait for it.
        Assert.False(Hearthpulse.Core.GameStats.GameStatsClient.Sharing(settings));
        Assert.False(SourceCaptureClient.Sharing(settings));
        Assert.False(PoolClient.Sharing(settings));
        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.Equal(Hearthpulse.Core.Localization.Loc.T("Settings.Data.SourceNotice"), (string)model.SourceCaptureStatusText);

        model.AnswerGameStats.Execute("ok");

        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.True(Hearthpulse.Core.GameStats.GameStatsClient.Sharing(settings));
        Assert.True(SourceCaptureClient.Sharing(settings));
        Assert.False(PoolClient.Sharing(settings));
        Assert.False(settings.PoolConsent);
    }
}

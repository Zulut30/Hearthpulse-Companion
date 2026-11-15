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
        Assert.False((bool)Model(wpf, new AppSettings()).ShowGameStatsNotice);

    /// <summary>One banner at a time: the pool's consent question first, the game records notice once it is answered.</summary>
    [Theory]
    [InlineData(true, "yes")]
    [InlineData(true, "no")]
    [InlineData(false, "yes")]
    [InlineData(false, "no")]
    public void The_notice_waits_for_the_answer_to_the_pools_question(bool wpf, string answer)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test" };
        var model = Model(wpf, settings);
        var raised = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)model).PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler((_, e) => raised.Add(e.PropertyName));
        var consents = 0;
        model.PoolConsentChanged += new Action(() => consents++);
        // Without a pool set up by the host there is no question, and the notice does not wait for one.
        Assert.False((bool)model.AskPoolConsent);
        Assert.True((bool)model.ShowGameStatsNotice);

        model.PoolAvailable = PoolClient.Available(settings);

        Assert.True((bool)model.AskPoolConsent);
        Assert.False((bool)model.ShowGameStatsNotice);
        Assert.Contains("ShowGameStatsNotice", raised);
        Assert.False(GameStatsClient.Sharing(settings));

        raised.Clear();
        model.AnswerPool.Execute(answer);

        Assert.False((bool)model.AskPoolConsent);
        Assert.True((bool)model.ShowGameStatsNotice);
        Assert.Contains("AskPoolConsent", raised);
        Assert.Contains("ShowGameStatsNotice", raised);
        Assert.Equal(answer == "yes", settings.PoolConsent);
        Assert.Equal(answer == "yes", (bool)model.PoolSharing);
        Assert.Equal(PoolClient.ConsentVersion, settings.PoolConsentVersion);
        Assert.Equal(1, consents);
        // Still nothing sent as a game record: its own notice has not been answered.
        Assert.False(GameStatsClient.Sharing(settings));
    }

    /// <summary>A yes to an older list is asked again, and the notice waits for that answer too.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_answer_to_an_older_list_is_asked_again_before_the_notice(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", PoolConsent = true, PoolConsentVersion = PoolClient.ConsentVersion - 1 };
        var model = Model(wpf, settings);
        model.PoolAvailable = true;

        Assert.True((bool)model.AskPoolConsent);
        Assert.False((bool)model.ShowGameStatsNotice);

        model.PoolSharing = false;

        Assert.False((bool)model.AskPoolConsent);
        Assert.True((bool)model.ShowGameStatsNotice);
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
        Assert.Equal(SourceCaptureClient.NoticeVersion, settings.SourceCaptureNoticeVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spool_storage_failure_is_not_displayed_as_an_empty_ready_queue(bool wpf)
    {
        var settings = new AppSettings { PoolEndpoint = "https://pool.test", SourceCaptureNoticeVersion = SourceCaptureClient.NoticeVersion };
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
}

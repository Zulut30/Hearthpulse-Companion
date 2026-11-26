using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// TASK-2026-11-26 AC7 in both compiled apps: the "Read data from the game client" switch and what it changes on the
/// pages and in the overlay's lobby line, and the switch's card in the WPF Data tab.
/// </summary>
public sealed class GameMemoryContractTests
{
    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    private static dynamic Model(bool wpf, AppSettings settings) => wpf
        ? new WpfMain(History(), settings, TestApp.Data) { CollectionReadable = true, AchievementsReadable = true }
        : new AvaMain(History(), settings, TestApp.Data) { CollectionReadable = true, AchievementsReadable = true };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_apps_say_reading_is_off_where_a_value_from_the_client_is_missing(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var model = Model(wpf, new AppSettings { ReadGameMemory = false });
        var changes = 0;
        model.GameMemoryChanged += new Action(() => changes++);

        Assert.True((bool)model.GameMemoryOff);
        Assert.False((bool)model.ReadGameMemory);
        Assert.Equal(Loc.T("Collection.Status.Off"), (string)model.CollectionStatus);
        Assert.Equal(Loc.T("Achievements.Empty.Off"), (string)model.AchievementsEmpty);
        Assert.Equal(Loc.T("Stats.Bg.RatingOff"), (string)model.BgRatingEmpty);
        Assert.Equal(Loc.T("Stats.Ct.RankOff"), (string)model.CtRankEmpty);
        Assert.Equal(Loc.T("Stats.Bg.RatingOff"), (string)model.RatingOffReason);
        Assert.Equal(Loc.T("Stats.Ct.RankOff"), (string)model.RankOffReason);
        Assert.EndsWith("(Settings → Data).", (string)model.AchievementsEmpty, StringComparison.Ordinal);

        model.ReadGameMemory = true;

        Assert.Equal(1, changes);
        Assert.Equal(Loc.T("Collection.Status.None"), (string)model.CollectionStatus);
        Assert.Equal(Loc.T("Achievements.Empty.None"), (string)model.AchievementsEmpty);
        Assert.Equal(Loc.T("Stats.Bg.RatingEmpty"), (string)model.BgRatingEmpty);
        Assert.Equal(Loc.T("Stats.Ct.RankEmpty"), (string)model.CtRankEmpty);
        Assert.Equal((string.Empty, string.Empty), ((string)model.RatingOffReason, (string)model.RankOffReason));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_lobby_line_says_reading_is_off_instead_of_reading_the_lobby(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        dynamic overlay = wpf ? new WpfOverlay() : new AvaOverlay();
        var settings = new AppSettings { ShowBgLobbyPanel = true };
        var lobby = new BattlegroundsView(7, 3, 6, "HERO_ME", 20, 0, 2, false, false, false, [], [], false, [], [], [], [], null, 0, [], []);
        var snapshot = LiveSnapshot.Empty with { Battlegrounds = lobby, InGame = true, HearthstoneRunning = true };

        overlay.Update(snapshot, CardDb.Empty, settings, null, null);
        Assert.Equal(Loc.T("Overlay.Bar.ReadingLobby"), (string)overlay.LobbyWaitText);

        settings.ReadGameMemory = false;
        overlay.Update(snapshot, CardDb.Empty, settings, null, null);
        Assert.Equal("Client reading is off", (string)overlay.LobbyWaitText);
    }

    [Fact]
    public void The_wpf_data_tab_has_the_game_client_switch_first_and_bound_both_ways()
    {
        var failure = default(Exception);
        var thread = new Thread(() =>
        {
            try
            {
                Loc.Instance.SetLanguage(Loc.English);
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
                var settings = new AppSettings();
                var model = new WpfMain(History(), settings, TestApp.Data);
                var changes = 0;
                model.GameMemoryChanged += () => changes++;
                var tab = new Hearthpulse.App.Main.SettingsTabs.DataSettings { DataContext = model };
                tab.Measure(new Size(780, double.PositiveInfinity));
                tab.Arrange(new Rect(tab.DesiredSize));
                tab.UpdateLayout();

                // Windows reads the client: the card shows, above the data status.
                var card = Assert.IsAssignableFrom<FrameworkElement>(LogicalTreeHelper.FindLogicalNode(tab, "GameClientCard"));
                var health = Assert.IsAssignableFrom<FrameworkElement>(LogicalTreeHelper.FindLogicalNode(tab, "DataHealthCard"));
                Assert.Equal(Visibility.Visible, card.Visibility);
                Assert.True(card.TranslatePoint(default, tab).Y < health.TranslatePoint(default, tab).Y);
                var box = Assert.IsType<CheckBox>(LogicalTreeHelper.FindLogicalNode(tab, "GameMemorySwitch"));
                Assert.Equal("Read data from the game client", box.Content);
                Assert.True(box.IsChecked);
                Assert.Contains(LogicalTreeHelper.GetChildren((StackPanel)((Border)card).Child).OfType<TextBlock>(),
                    t => t.Text == Loc.T("Settings.Data.GameMemoryNote") && t.TextWrapping == TextWrapping.Wrap);

                box.IsChecked = false;
                Assert.False(settings.ReadGameMemory);
                Assert.Equal(1, changes);

                model.ReadGameMemory = true;
                Assert.True(box.IsChecked);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Game client switch check failed", failure);
        }
    }
}

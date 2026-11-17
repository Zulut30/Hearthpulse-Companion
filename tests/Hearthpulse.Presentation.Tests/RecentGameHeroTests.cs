using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// A game row on Home and History always has a portrait: a record that kept only the class (an Arena or Underground
/// Arena game without its hero card) shows the class's default hero, in both apps' view models.
/// </summary>
public sealed class RecentGameHeroTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_game_without_its_hero_shows_the_class_hero(bool wpf)
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppPaths.Root, "recent-hero-" + Guid.NewGuid().ToString("N"))).FullName;
        var history = new HistoryStore(Path.Combine(folder, "history.db"));
        var started = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(history.Add(new GameRecord
        {
            Key = "recent-hero-underground",
            StartedAt = started,
            EndedAt = started.AddMinutes(10),
            Mode = "Arena",
            GameType = "GT_UNDERGROUND_ARENA",
            Format = "FT_WILD",
            Result = "Win",
            Turns = 9,
            PlayerClass = "WARRIOR",
            OpponentClass = "PRIEST",
        }));
        Assert.True(history.Add(new GameRecord
        {
            Key = "recent-hero-ranked",
            StartedAt = started.AddMinutes(-30),
            EndedAt = started.AddMinutes(-20),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_STANDARD",
            Result = "Loss",
            Turns = 8,
            PlayerClass = "MAGE",
            HeroCardId = "HERO_08a",
        }));

        dynamic model = wpf ? new WpfMain(history, new AppSettings(), TestApp.Data) : new AvaMain(history, new AppSettings(), TestApp.Data);
        await model.RefreshAsync();

        var rows = ((IEnumerable<object>)model.Recent).Select(r => (string)((dynamic)r).HeroCardId).ToList();
        Assert.Equal(["HERO_01", "HERO_08a"], rows);
    }
}

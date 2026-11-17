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

    /// <summary>
    /// The owner's bug: an Underground Arena card in the replay library had an empty portrait, no opponent portrait and
    /// "Wild" in its caption (the log's format for Arena). It shows both classes' heroes and no format; a Ranked card
    /// keeps its own heroes and its format.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_library_card_shows_the_class_heroes_and_a_format_only_where_one_was_picked(bool wpf)
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppPaths.Root, "library-hero-" + Guid.NewGuid().ToString("N"))).FullName;
        var history = new HistoryStore(Path.Combine(folder, "history.db"));
        var started = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(history.Add(new GameRecord
        {
            Key = "library-hero-underground",
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
            Key = "library-hero-ranked",
            StartedAt = started.AddMinutes(-30),
            EndedAt = started.AddMinutes(-20),
            Mode = "Ranked",
            GameType = "GT_RANKED",
            Format = "FT_WILD",
            Result = "Loss",
            Turns = 8,
            PlayerClass = "MAGE",
            OpponentClass = "ROGUE",
            HeroCardId = "HERO_08a",
            OpponentHeroCardId = "HERO_03b",
        }));

        dynamic model = wpf ? new WpfMain(history, new AppSettings(), TestApp.Data) : new AvaMain(history, new AppSettings(), TestApp.Data);
        await model.RefreshAsync();

        var cards = ((IEnumerable<object>)model.Library.Cards).Select(c => (dynamic)c).ToList();
        Assert.Equal(["HERO_01", "HERO_08a"], cards.Select(c => (string)c.HeroCardId));
        Assert.Equal(["HERO_09", "HERO_03b"], cards.Select(c => (string)c.OpponentHeroCardId));
        var wild = Hearthpulse.Core.Localization.Loc.T("Match.Format.FT_WILD");
        Assert.DoesNotContain(wild, (string)cards[0].Caption, StringComparison.Ordinal);
        Assert.Contains(Hearthpulse.Core.Localization.Loc.T("Main.Mode.Underground"), (string)cards[0].Caption, StringComparison.Ordinal);
        Assert.Contains(wild, (string)cards[1].Caption, StringComparison.Ordinal);
    }
}

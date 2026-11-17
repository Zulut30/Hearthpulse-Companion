using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Replay;

namespace Hearthpulse.Tests;

/// <summary>
/// The match page header of a game recorded with only its classes (Arena, Underground Arena): the classes' default heroes
/// instead of empty portraits, the Underground Arena by its name, and a format only where the player picked one.
/// </summary>
public sealed class MatchHeaderTests
{
    private static readonly DateTime Start = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Local);

    private static readonly GameRecord Underground = new()
    {
        Key = "header-underground",
        StartedAt = Start,
        EndedAt = Start.AddMinutes(10),
        Mode = "Arena",
        GameType = "GT_UNDERGROUND_ARENA",
        Format = "FT_WILD",
        Result = "Win",
        Turns = 9,
        PlayerClass = "WARRIOR",
        OpponentClass = "PRIEST",
    };

    [Fact]
    public void An_arena_game_without_its_heroes_shows_the_class_heroes_and_no_format()
    {
        var header = MatchPage.Load(Underground, (string?)null, CardDb.Empty, null).Header;

        Assert.Equal(("HERO_01", "HERO_09"), (header.Local.HeroCardId, header.Opponent.HeroCardId));
        Assert.StartsWith(Loc.T("Main.Mode.Underground") + " · ", header.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain(Loc.T("Match.Format.FT_WILD"), header.Subtitle, StringComparison.Ordinal);

        var arena = MatchPage.Load(Underground with { GameType = "GT_ARENA" }, (string?)null, CardDb.Empty, null).Header;
        Assert.StartsWith(Loc.T("Main.Mode.Arena") + " · ", arena.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain(Loc.T("Match.Format.FT_WILD"), arena.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ranked_game_keeps_its_own_heroes_and_its_format()
    {
        var ranked = Underground with { Mode = "Ranked", GameType = "GT_RANKED", HeroCardId = "HERO_01c", OpponentHeroCardId = "HERO_09b" };

        var header = MatchPage.Load(ranked, (string?)null, CardDb.Empty, null).Header;

        Assert.Equal(("HERO_01c", "HERO_09b"), (header.Local.HeroCardId, header.Opponent.HeroCardId));
        Assert.Contains(" · " + Loc.T("Match.Format.FT_WILD") + " · ", header.Subtitle, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ranked", true)]
    [InlineData("Casual", true)]
    [InlineData("Friendly", true)]
    [InlineData("Arena", false)]
    [InlineData("Brawl", false)]
    [InlineData("Practice", false)]
    [InlineData("Battlegrounds", false)]
    [InlineData("Other", false)]
    public void Only_a_game_in_a_picked_format_names_it(string mode, bool named) =>
        Assert.Equal(named, Hearthpulse.Core.Stats.StatsFilter.HasFormat(Underground with { Mode = mode }));
}

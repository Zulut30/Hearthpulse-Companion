using System.Globalization;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.Tests;

/// <summary>An achievement description as the player reads it: the quota written in, plural forms picked by it, italics dropped (<see cref="AchievementText"/>).</summary>
public sealed class AchievementTextTests
{
    private const string Russian = "Соберите $q |4(разную карту,разные карты,разных карт).";

    [Theory]
    [InlineData(1, "Соберите 1 разную карту.")]
    [InlineData(3, "Соберите 3 разные карты.")]
    [InlineData(5, "Соберите 5 разных карт.")]
    [InlineData(11, "Соберите 11 разных карт.")]
    [InlineData(21, "Соберите 21 разную карту.")]
    [InlineData(112, "Соберите 112 разных карт.")]
    [InlineData(0, "Соберите 0 разных карт.")]
    public void Three_forms_follow_the_russian_plural_rules(int quota, string expected) =>
        Assert.Equal(expected, AchievementText.Format(Russian, quota));

    [Theory]
    [InlineData(1, "Win 1 game.")]
    [InlineData(2, "Win 2 games.")]
    [InlineData(0, "Win 0 games.")]
    public void Two_forms_follow_the_english_plural_rules(int quota, string expected) =>
        Assert.Equal(expected, AchievementText.Format("Win $q |4(game,games).", quota));

    [Fact]
    public void Italics_are_dropped_and_other_markup_stays()
    {
        Assert.Equal("Win with a Legendary minion.", AchievementText.Format("Win with a <i>Legendary</i> minion.", 1));
        Assert.Equal("<b>Bold</b> |4(a,b,c,d) |5(x,y)", AchievementText.Format("<b>Bold</b> |4(a,b,c,d) |5(x,y)", 2));
        Assert.Equal("No quota here.", AchievementText.Format("No quota here.", 7));
    }

    [Fact]
    public void Empty_text_stays_empty()
    {
        Assert.Equal(string.Empty, AchievementText.Format(string.Empty, 3));
        Assert.Equal(string.Empty, AchievementText.Format(null!, 3));
    }

    [Fact]
    public void The_quota_is_a_number_of_the_current_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal($"Нанесите {20000.ToString("N0", CultureInfo.CurrentCulture)} урона.", AchievementText.Format("Нанесите $q урона.", 20000));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Describe_uses_the_achievement_quota()
    {
        var achievement = new AchievementDef(1, 1, 1, "Name", Russian, Hidden: false, Quota: 22, Points: 10, NextTierId: 0);

        Assert.Equal("Соберите 22 разные карты.", AchievementText.Describe(achievement));
    }
}

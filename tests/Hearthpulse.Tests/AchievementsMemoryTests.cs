using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.Tests;

/// <summary>
/// The achievements read from the client (<see cref="HearthstoneMemory.GetAchievements(Func{bool}?, AchievementBook?)"/>,
/// docs/collection-cosmetics.md) on a hand-built image (<see cref="ClientImage"/>): the tables and the player state map
/// become the book by its rules, a read with the session's catalog takes the states only, and anything that does not
/// fit together gives no book.
/// </summary>
public sealed class AchievementsMemoryTests
{
    private static readonly DateTime Nov1 = DateTimeOffset.FromUnixTimeSeconds(ClientImage.Nov1).UtcDateTime;

    [Fact]
    public void The_book_holds_enabled_unretired_achievements_of_linked_sections_with_their_states()
    {
        var before = DateTime.UtcNow;
        using var memory = ClientImage.Attach(new ClientImage().Build());

        var book = memory.GetAchievements();

        Assert.NotNull(book);
        Assert.Null(memory.LastAchievementsMiss);
        Assert.Equal([1, 2, 3, 4, 8], book.Achievements.Keys.Order());
        Assert.Equal([1, 2], book.Categories.Select(c => c.Id));
        Assert.Equal(new AchievementCategory(1, "Общие", "general", 1), book.Categories[0]);
        Assert.Equal("Общие", book.Subcategories.Single(s => s.Id == 11).Name);
        Assert.Equal(new AchievementDef(1, 100, 1, "A I", "Соберите $q |4(карту,карты,карт).", false, 10, 5, 2), book.Achievements[1]);
        Assert.True(book.Achievements[4].Hidden);
        Assert.Equal(string.Empty, book.Achievements[4].Name);
        Assert.InRange(book.ReadAt, before, DateTime.UtcNow);
        Assert.False(book.Confirmed);
    }

    [Fact]
    public void Statuses_dates_and_progress_follow_the_rules()
    {
        using var memory = ClientImage.Attach(new ClientImage().Build());

        var book = memory.GetAchievements()!;

        Assert.Equal(new AchievementState(1, AchievementStatus.Completed, 10, Nov1), book.States[1]);
        Assert.Equal(new AchievementState(2, AchievementStatus.InProgress, 34, null), book.States[2]);
        Assert.Equal(AchievementStatus.NotStarted, book.States[3].Status);
        Assert.Equal(new AchievementState(8, AchievementStatus.Completed, 1, Nov1.AddHours(1)), book.States[8]);
        Assert.Equal(DateTimeKind.Utc, book.States[1].CompletedAt!.Value.Kind);
        // The retired achievement's state is not kept.
        Assert.DoesNotContain(5, book.States.Keys);
        Assert.Equal((34, 50, 1), (book.Chain(1)!.Progress, book.Chain(1)!.Quota, book.Chain(1)!.CompletedTiers));
    }

    [Fact]
    public void A_read_with_the_sessions_catalog_takes_the_states_only()
    {
        using var first = ClientImage.Attach(new ClientImage().Build());
        var catalog = first.GetAchievements()!;
        var newer = new ClientImage { States = [(1, 4, 10, ClientImage.Nov1), (2, 4, 50, ClientImage.Nov1 + 60), (3, 1, 41, 0)] };
        using var second = ClientImage.Attach(newer.Build());

        var book = second.GetAchievements(catalog: catalog);

        Assert.NotNull(book);
        Assert.Same(catalog.Achievements, book.Achievements);
        Assert.Same(catalog.Categories, book.Categories);
        Assert.Equal(41, book.States[3].Progress);
        Assert.Equal(AchievementStatus.Completed, book.States[2].Status);
        Assert.DoesNotContain(8, book.States.Keys);
        Assert.Equal((41, 100, 2), (book.Chain(1)!.Progress, book.Chain(1)!.Quota, book.Chain(1)!.CompletedTiers));
        Assert.True(book.ReadAt >= catalog.ReadAt);
        Assert.False(book.Confirmed);
    }

    [Fact]
    public void A_client_without_its_achievement_manager_gives_no_book()
    {
        using var memory = ClientImage.Attach(new ClientImage { WithAchievementManager = false }.Build());

        Assert.Null(memory.GetAchievements());
        Assert.Contains("achievement manager", memory.LastAchievementsMiss, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ClientDamage.AchievementOfAnotherClass, "another class")]
    [InlineData(ClientDamage.UnreadableDescription, "unreadable")]
    [InlineData(ClientDamage.KeyNotItsAchievement, "key")]
    [InlineData(ClientDamage.MapCountsMore, "fewer states")]
    [InlineData(ClientDamage.StateOfAnotherClass, "another class")]
    [InlineData(ClientDamage.DateOutOfRange, "date")]
    [InlineData(ClientDamage.LocOffsetOutOfRange, "offset out of range")]
    public void Any_damage_gives_no_book_rather_than_a_part_and_says_why(ClientDamage damage, string reason)
    {
        using var memory = ClientImage.Attach(new ClientImage { Damage = damage }.Build());

        Assert.Null(memory.GetAchievements());
        Assert.Contains(reason, memory.LastAchievementsMiss, StringComparison.Ordinal);
    }

    [Fact]
    public void States_that_change_while_they_are_read_give_no_book()
    {
        var client = new ClientImage();
        var image = client.Build();
        using var memory = ClientImage.Attach(new CosmeticsMemoryTests.ChangingSource(image, client.MapGenerationAddress, 18));

        Assert.Null(memory.GetAchievements());
        Assert.Contains("changed while", memory.LastAchievementsMiss, StringComparison.Ordinal);
    }

    [Fact]
    public void A_game_that_begins_in_the_middle_of_the_read_stops_it()
    {
        var many = Enumerable.Range(1000, 300).Select(id => (id, 100, true, false, "A", string.Empty, 0, 1, 1, 0)).ToArray();
        using var memory = ClientImage.Attach(new ClientImage { Achievements = many }.Build());
        var asked = 0;

        Assert.Null(memory.GetAchievements(() => ++asked > 1));

        Assert.Equal(2, asked);
        Assert.Contains("no longer wanted", memory.LastAchievementsMiss, StringComparison.Ordinal);
        Assert.Equal(300, memory.GetAchievements(() => false)!.Achievements.Count);
    }

    [Fact]
    public void A_read_out_of_budget_or_no_longer_wanted_gives_no_book_and_nothing_of_it_sticks()
    {
        using var memory = ClientImage.Attach(new ClientImage().Build());

        Assert.Null(memory.GetAchievements(null, null, maxReads: 60, TimeSpan.FromSeconds(30)));
        Assert.Contains("budget", memory.LastAchievementsMiss, StringComparison.Ordinal);
        Assert.Null(memory.GetAchievements(() => true));
        Assert.Contains("no longer wanted", memory.LastAchievementsMiss, StringComparison.Ordinal);

        Assert.NotNull(memory.GetAchievements(() => false));
        Assert.Null(memory.LastAchievementsMiss);
        Assert.Contains("Achievements not read: the read was no longer wanted", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }
}

using Hearthpulse.Core.Progression;

namespace Hearthpulse.Tests;

/// <summary>
/// The rules of the achievement book (docs/collection-cosmetics.md, "Achievements"): what a read of the client keeps
/// (<see cref="AchievementBook.FromClient"/>), chains as one row with the current tier, progress capped at the quota,
/// no quota as done or not, "completed" over progress, secret achievements, retired ones nowhere, points and counts by
/// tier, close-to-done and recent lists. All made-up data.
/// </summary>
public sealed class AchievementBookTests
{
    private static readonly DateTime ReadAt = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly long Nov1 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static readonly AchievementCategory[] Categories =
    [
        new(2, "Игровой процесс", "gameplay", 2),
        new(1, "Общие", "general", 1),
    ];

    private static readonly AchievementSubcategory[] Subcategories =
    [
        new(10, 1, "Прогресс", 1),
        new(11, 1, string.Empty, 2),
        new(20, 2, "Бой", 1),
        new(99, 7, "Orphan", 1),
    ];

    private static readonly ClientSection[] Sections =
    [
        new(100, "Путь"), new(110, string.Empty), new(200, "Бой"), new(300, "Unlinked"), new(990, "Of an orphan subcategory"),
    ];

    private static readonly ClientSectionItem[] Items =
    [
        new(10, 100, 1), new(11, 110, 1), new(20, 200, 1), new(99, 990, 1),
    ];

    private static ClientAchievement A(int id, int section, int quota, int points, int next = 0, int visibility = 0, bool enabled = true,
                                       bool retired = false, string? name = null, string description = "Do $q") =>
        new(id, section, id, enabled, retired, name ?? $"A{id}", description, visibility, quota, points, next);

    /// <summary>The book of AC10: a chain 10/50/100 with tier 1 done and 34 in tier 2, and the odd cases beside it.</summary>
    private static AchievementBook Book(params ClientAchievementState[] extraStates)
    {
        ClientAchievement[] achievements =
        [
            A(1, 100, 10, 5, next: 2), A(2, 100, 50, 10, next: 3), A(3, 100, 100, 20),
            A(4, 100, 1, 15, visibility: 1, name: string.Empty, description: string.Empty),   // secret, not done
            A(5, 100, 1, 15, visibility: 1, name: string.Empty, description: string.Empty),   // secret, done
            A(6, 110, 0, 5),                                                                  // no quota
            A(7, 110, 100, 10),                                                               // done below its quota
            A(8, 110, 10, 50, retired: true),                                                 // retired
            A(9, 200, 20, 10),
            A(10, 200, 100, 10),                                                              // progress read over its quota
            A(11, 300, 5, 1000),                                                              // its section is in no subcategory
            A(12, 200, 5, 1000, enabled: false),                                              // disabled
            A(13, 990, 5, 1000),                                                              // its subcategory's category does not exist
        ];
        ClientAchievementState[] states =
        [
            new(1, 4, 10, Nov1), new(2, 1, 34, 0),
            new(4, 1, 1, 0), new(5, 2, 1, Nov1 + 3600),
            new(7, 3, 60, Nov1 + 7200),
            new(8, 4, 10, Nov1),
            new(9, 1, 15, 0), new(10, 1, 150, 0),
            new(11, 4, 5, Nov1),
            .. extraStates,
        ];
        return AchievementBook.FromClient(Categories, Subcategories, Sections, Items, achievements, states, ReadAt)!;
    }

    // ------------------------------------------------------------------------------------------- what is kept

    [Fact]
    public void The_book_keeps_enabled_unretired_achievements_of_linked_sections_only()
    {
        var book = Book();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 9, 10], book.Achievements.Keys.Order());
        Assert.Equal([1, 2], book.Categories.Select(c => c.Id));
        Assert.Equal([10, 20, 11], book.Subcategories.Select(s => s.Id));
        Assert.Equal([100, 110, 200], book.Sections.Select(s => s.Id).Order());
        Assert.DoesNotContain(8, book.States.Keys);
        Assert.DoesNotContain(11, book.States.Keys);
        Assert.False(book.Confirmed);
        Assert.Equal(ReadAt, book.ReadAt);
    }

    [Fact]
    public void An_unnamed_subcategory_takes_its_category_name_and_a_section_keeps_no_heading()
    {
        var book = Book();

        Assert.Equal("Общие", book.Subcategories.Single(s => s.Id == 11).Name);
        Assert.Equal(string.Empty, book.Sections.Single(s => s.Id == 110).Name);
        Assert.Equal(1, book.Sections.Single(s => s.Id == 110).SortOrder);
    }

    [Theory]
    [InlineData(0, AchievementStatus.NotStarted)]
    [InlineData(1, AchievementStatus.InProgress)]
    [InlineData(2, AchievementStatus.Completed)]
    [InlineData(3, AchievementStatus.Completed)]
    [InlineData(4, AchievementStatus.Completed)]
    [InlineData(5, AchievementStatus.NotStarted)]
    [InlineData(-1, AchievementStatus.NotStarted)]
    public void Client_statuses_map_by_the_enum_order(int status, AchievementStatus expected) =>
        Assert.Equal(expected, AchievementBook.StatusOf(status));

    [Fact]
    public void A_completion_date_is_unix_seconds_utc_and_zero_is_unknown()
    {
        var book = Book();

        Assert.Equal(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), book.States[1].CompletedAt);
        Assert.Equal(DateTimeKind.Utc, book.States[1].CompletedAt!.Value.Kind);
        Assert.Null(book.States[2].CompletedAt);
        // Progress is kept as read, over the quota too.
        Assert.Equal(150, book.States[10].Progress);
    }

    [Fact]
    public void A_repeated_id_or_a_date_out_of_range_gives_no_book()
    {
        Assert.NotNull(Book());
        Assert.Null(Book(new ClientAchievementState(1, 4, 10, Nov1)));
        Assert.Null(Book(new ClientAchievementState(3, 4, 100, -5)));
        Assert.Null(Book(new ClientAchievementState(3, 4, 100, 4_102_444_800)));
        Assert.Null(AchievementBook.FromClient(Categories, Subcategories, Sections, Items, [A(1, 100, 1, 1), A(1, 100, 1, 1)], [], ReadAt));
        Assert.Null(AchievementBook.FromClient([.. Categories, Categories[0]], Subcategories, Sections, Items, [], [], ReadAt));
    }

    // ------------------------------------------------------------------------------------------------ chains

    [Fact]
    public void A_chain_is_one_row_at_its_first_tier_not_done()
    {
        var chain = Book().Chain(1);

        Assert.NotNull(chain);
        Assert.Single(Book().Chains, c => c.Tiers.Any(t => t.Id == 2));
        Assert.Null(Book().Chain(2));
        Assert.Equal([1, 2, 3], chain.Tiers.Select(t => t.Id));
        Assert.Equal(2, chain.Current.Id);
        Assert.Equal((34, 50), (chain.Progress, chain.Quota));
        Assert.Equal(1, chain.CompletedTiers);
        Assert.False(chain.Completed);
        Assert.Equal((1, 10, 100), (chain.CategoryId, chain.SubcategoryId, chain.SectionId));
        Assert.Equal(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), chain.CompletedAt);
        Assert.Equal((5, 35), (chain.PointsEarned, chain.PointsTotal));
    }

    [Fact]
    public void Progress_is_capped_at_the_quota_and_never_negative()
    {
        var book = Book(new ClientAchievementState(6, 1, -4, 0));

        Assert.Equal((100, 100), (book.Chain(10)!.Progress, book.Chain(10)!.Quota));
        Assert.Equal((15, 20), (book.Chain(9)!.Progress, book.Chain(9)!.Quota));
        Assert.Equal(0, book.Chain(6)!.Progress);
    }

    [Fact]
    public void An_achievement_without_a_quota_is_done_or_not()
    {
        var notDone = Book().Chain(6)!;
        var done = Book(new ClientAchievementState(6, 4, 0, Nov1)).Chain(6)!;

        Assert.Equal((0, 1, false), (notDone.Progress, notDone.Quota, notDone.Completed));
        Assert.Equal((1, 1, true), (done.Progress, done.Quota, done.Completed));
    }

    [Fact]
    public void Completed_wins_over_a_progress_below_the_quota()
    {
        var chain = Book().Chain(7)!;

        Assert.True(chain.Completed);
        Assert.Equal((100, 100), (chain.Progress, chain.Quota));
        Assert.Equal(10, chain.PointsEarned);
    }

    [Fact]
    public void A_secret_achievement_not_done_is_hidden_and_never_close_to_done()
    {
        var book = Book();
        var secret = book.Chain(4)!;

        Assert.True(secret.Hidden);
        Assert.False(secret.Completed);
        Assert.DoesNotContain(book.NearlyDone(100), c => c.HeadId == 4);
    }

    [Fact]
    public void A_secret_achievement_done_is_no_longer_hidden_even_without_a_name()
    {
        var book = Book();
        var done = book.Chain(5)!;

        Assert.False(done.Hidden);
        Assert.True(done.Completed);
        Assert.Equal(string.Empty, done.Current.Name);
        Assert.Contains(book.RecentlyCompleted(), c => c.Achievement.Id == 5);
    }

    [Fact]
    public void A_retired_achievement_counts_nowhere()
    {
        var book = Book();

        Assert.Null(book.Chain(8));
        Assert.DoesNotContain(book.Chains, c => c.Tiers.Any(t => t.Id == 8));
        Assert.DoesNotContain(book.RecentlyCompleted(100), c => c.Achievement.Id == 8);
        Assert.Equal(9, book.Summary.Total);
    }

    [Fact]
    public void A_fully_done_chain_shows_its_last_tier()
    {
        var chain = Book(new ClientAchievementState(3, 4, 100, Nov1 + 86400)).Chain(1);
        // The state of tier 2 says 34 in progress, so mark it done through a book of its own.
        var book = AchievementBook.FromClient(Categories, Subcategories, Sections, Items,
            [A(1, 100, 10, 5, next: 2), A(2, 100, 50, 10, next: 3), A(3, 100, 100, 20)],
            [new(1, 4, 10, Nov1), new(2, 4, 50, Nov1 + 60), new(3, 4, 100, Nov1 + 120)], ReadAt)!;
        var done = book.Chain(1)!;

        Assert.Equal(2, chain!.Current.Id);
        Assert.True(done.Completed);
        Assert.Equal(3, done.Current.Id);
        Assert.Equal((3, 100, 100), (done.CompletedTiers, done.Progress, done.Quota));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Nov1 + 120).UtcDateTime, done.CompletedAt);
        Assert.Equal((35, 35), (done.PointsEarned, done.PointsTotal));
    }

    [Fact]
    public void Tiers_that_point_at_each_other_still_make_one_chain()
    {
        var book = AchievementBook.FromClient(Categories, Subcategories, Sections, Items,
            [A(1, 100, 10, 5, next: 2), A(2, 100, 10, 5, next: 1), A(3, 100, 10, 5, next: 3)], [], ReadAt)!;

        Assert.Equal(2, book.Chains.Count);
        Assert.Equal([1, 2], book.Chain(1)!.Tiers.Select(t => t.Id));
        Assert.Equal([3], book.Chain(3)!.Tiers.Select(t => t.Id));
    }

    // ---------------------------------------------------------------------------------------------- summaries

    [Fact]
    public void Points_and_counts_are_summed_by_tier()
    {
        var summary = Book().Summary;

        // Done: 1 (5), 5 (15), 7 (10). Every kept tier: 5+10+20+15+15+5+10+10+10.
        Assert.Equal((30, 100), (summary.PointsEarned, summary.PointsTotal));
        Assert.Equal((3, 9), (summary.Completed, summary.Total));
        Assert.Equal([1, 2], summary.Categories.Select(c => c.Category.Id));
        Assert.Equal(new CategorySummary(Categories[1], 30, 80, 3, 7), summary.Categories[0]);
        Assert.Equal(new CategorySummary(Categories[0], 0, 20, 0, 2), summary.Categories[1]);
    }

    [Fact]
    public void Chains_follow_category_subcategory_and_section_order()
    {
        var book = Book();

        Assert.Equal([1, 4, 5, 6, 7, 9, 10], book.Chains.Select(c => c.HeadId));
    }

    [Fact]
    public void Close_to_done_are_unfinished_visible_chains_with_progress_by_share()
    {
        var book = Book();

        // 9: 15/20 = 0.75; 1: 34/50 = 0.68; 10: 150 of 100 is "in progress", capped: 1.0. 6 has no quota, 4 is secret.
        Assert.Equal([10, 9, 1], book.NearlyDone().Select(c => c.HeadId));
        Assert.Equal([10], book.NearlyDone(1).Select(c => c.HeadId));
        Assert.Empty(book.NearlyDone(0));
    }

    [Fact]
    public void Recent_completions_are_dated_tiers_newest_first_and_a_period_takes_its_own()
    {
        var book = Book();

        Assert.Equal([7, 5, 1], book.RecentlyCompleted().Select(c => c.Achievement.Id));
        Assert.Equal((1, 1), (book.RecentlyCompleted()[2].HeadId, book.RecentlyCompleted()[2].TierNumber));
        var from = DateTimeOffset.FromUnixTimeSeconds(Nov1 + 3600).UtcDateTime;
        Assert.Equal([5], book.CompletedBetween(from, from.AddHours(1)).Select(c => c.Achievement.Id));
        Assert.Empty(book.CompletedBetween(from.AddHours(5), from.AddHours(6)));
    }

    [Fact]
    public void A_copy_with_other_states_works_its_chains_out_again()
    {
        var book = Book();
        Assert.Equal(34, book.Chain(1)!.Progress);

        var states = book.States.ToDictionary(p => p.Key, p => p.Value);
        states[2] = new AchievementState(2, AchievementStatus.InProgress, 41, null);
        var next = book with { States = states, Confirmed = true };

        Assert.Equal(41, next.Chain(1)!.Progress);
        Assert.True(next.Confirmed);
        Assert.Equal(34, book.Chain(1)!.Progress);
    }

    [Fact]
    public void The_made_up_scene_has_every_case_the_pages_show()
    {
        var now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        var book = AchievementsScene.Build(now, confirmed: true);
        var tiered = book.Chain(AchievementsScene.TieredHead)!;

        Assert.True(book.Confirmed);
        Assert.Equal(5, book.Summary.Categories.Count);
        Assert.Equal((34, 50, 1), (tiered.Progress, tiered.Quota, tiered.CompletedTiers));
        Assert.Contains(book.Chains, c => c.Hidden);
        Assert.Contains(book.Chains, c => c.Completed && c.Current.Hidden && c.Current.Name.Length == 0);
        Assert.Contains(book.Chains, c => c.Current.Quota <= 0);
        Assert.Contains(book.Chains, c => c.Current.Name == AchievementsScene.LongName + " I");
        Assert.Contains(book.Sections, s => s.Name.Length == 0);
        Assert.Equal(12, book.NearlyDone().Count);
        Assert.Equal(12, book.RecentlyCompleted().Count);
        Assert.Equal(new AchievementGain(0, 7, now.AddHours(-2)), book.Gains[AchievementsScene.TieredHead]);
        Assert.Equal(1, book.Gains[AchievementsScene.TierGainHead].Tiers);
        Assert.True(AchievementLimits.Fits(book));
    }
}

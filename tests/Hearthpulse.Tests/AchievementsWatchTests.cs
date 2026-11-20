using Hearthpulse.Core.Progression;

namespace Hearthpulse.Tests;

/// <summary>
/// The achievements' watch (<see cref="AchievementsWatch"/>, docs/collection-cosmetics.md): the catalog is read once per
/// session of the client, each read is joined with the book known before it into the chains' gains, and the gains
/// survive a restart in the file. The timing rules are the cosmetics' (<see cref="CosmeticsWatchTests"/>).
/// </summary>
public sealed class AchievementsWatchTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = TestDataFolder.Scratch("hp-achievements-watch");

    public AchievementsWatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => TestDataFolder.TryDelete(_dir);

    private string File_ => Path.Combine(_dir, "achievements.json");

    private static readonly long Nov1 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>A chain 10/50/100 (head 1) and a single achievement of quota 5 (id 9), with the states given, read at <paramref name="readAt"/>.</summary>
    private static AchievementBook Book(DateTime readAt, params ClientAchievementState[] states) => AchievementBook.FromClient(
        [new AchievementCategory(1, "Общие", "general", 1)],
        [new AchievementSubcategory(10, 1, "Прогресс", 1)],
        [new ClientSection(100, string.Empty)],
        [new ClientSectionItem(10, 100, 1)],
        [
            new ClientAchievement(1, 100, 1, true, false, "A I", string.Empty, 0, 10, 5, 2),
            new ClientAchievement(2, 100, 2, true, false, "A II", string.Empty, 0, 50, 10, 3),
            new ClientAchievement(3, 100, 3, true, false, "A III", string.Empty, 0, 100, 20, 0),
            new ClientAchievement(9, 100, 9, true, false, "B", string.Empty, 0, 5, 5, 0),
        ],
        states,
        readAt)!;

    private static AchievementBook At34(DateTime readAt) => Book(readAt, new(1, 4, 10, Nov1), new(2, 1, 34, 0), new(9, 1, 2, 0));

    [Fact]
    public void The_catalog_is_read_once_per_client_session()
    {
        var watch = new AchievementsWatch(null);
        watch.ClientStarted();
        Assert.Null(watch.SessionCatalog);

        watch.Apply(At34(Noon), watch.Ticket(Noon, inGame: false), Noon);
        Assert.Same(watch.Current, watch.SessionCatalog);

        // A new session of the client (another account, maybe) reads the catalog again.
        watch.ClientStopped();
        Assert.Null(watch.SessionCatalog);
        watch.ClientStarted();
        Assert.Null(watch.SessionCatalog);
        watch.SceneChanged("HUB", "LOGIN");
        Assert.Null(watch.SessionCatalog);
    }

    [Fact]
    public async Task A_saved_book_is_no_catalog_of_this_session()
    {
        new AchievementsStore(File_).Save(At34(Noon) with { Confirmed = true });
        var watch = new AchievementsWatch(new AchievementsStore(File_));
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(watch.Current);
        Assert.False(watch.Current.Confirmed);
        Assert.Null(watch.SessionCatalog);
    }

    [Fact]
    public async Task Progress_since_the_saved_read_is_the_chains_gain_and_survives_a_restart()
    {
        // The last run read tier 2 at 34 of 50.
        new AchievementsStore(File_).Save(At34(Noon));
        var watch = new AchievementsWatch(new AchievementsStore(File_));
        watch.Start();
        await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));

        // This run reads 41.
        var later = Noon.AddHours(2);
        watch.ClientStarted();
        watch.Apply(Book(later, new(1, 4, 10, Nov1), new(2, 1, 41, 0), new(9, 1, 2, 0)), watch.Ticket(later, inGame: false), later);

        Assert.Equal(new AchievementGain(0, 7, later), watch.Current!.Gains[1]);
        Assert.DoesNotContain(9, watch.Current.Gains.Keys);
        await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));

        var next = new AchievementsWatch(new AchievementsStore(File_));
        next.Start();
        await next.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(new AchievementGain(0, 7, later), next.Current!.Gains[1]);
    }

    [Fact]
    public void A_new_tier_is_counted_in_tiers_and_an_unchanged_chain_keeps_its_gain()
    {
        var watch = new AchievementsWatch(null);
        watch.ClientStarted();
        watch.Apply(At34(Noon), watch.Ticket(Noon, inGame: false), Noon);

        // Tier 2 and tier 3 completed by the next read; the other achievement moved from 2 to 4.
        var t1 = Noon.AddMinutes(5);
        watch.SceneChanged("GAMEPLAY", "HUB");
        watch.Apply(Book(t1, new(1, 4, 10, Nov1), new(2, 4, 50, Nov1 + 100), new(3, 4, 100, Nov1 + 200), new(9, 1, 4, 0)),
            watch.Ticket(t1.AddMinutes(1), inGame: false), t1);
        Assert.Equal(new AchievementGain(2, 0, t1), watch.Current!.Gains[1]);
        Assert.Equal(new AchievementGain(0, 2, t1), watch.Current.Gains[9]);

        // A read with nothing new keeps the last gains and their times.
        var t2 = t1.AddMinutes(5);
        watch.SceneChanged("GAMEPLAY", "HUB");
        watch.Apply(Book(t2, new(1, 4, 10, Nov1), new(2, 4, 50, Nov1 + 100), new(3, 4, 100, Nov1 + 200), new(9, 1, 4, 0)),
            watch.Ticket(t2.AddMinutes(1), inGame: false), t2);
        Assert.Equal(new AchievementGain(2, 0, t1), watch.Current.Gains[1]);
        Assert.Equal(new AchievementGain(0, 2, t1), watch.Current.Gains[9]);
    }

    [Fact]
    public void A_book_with_fewer_tiers_is_another_accounts_it_gains_nothing_and_the_earlier_gains_go()
    {
        var watch = new AchievementsWatch(null);
        watch.ClientStarted();
        watch.Apply(At34(Noon), watch.Ticket(Noon, inGame: false), Noon);
        var t1 = Noon.AddMinutes(5);
        watch.SceneChanged("GAMEPLAY", "HUB");
        watch.Apply(Book(t1, new(1, 4, 10, Nov1), new(2, 1, 40, 0), new(9, 1, 2, 0)), watch.Ticket(t1.AddMinutes(1), inGame: false), t1);
        Assert.Equal(new AchievementGain(0, 6, t1), watch.Current!.Gains[1]);

        // Signed in again: chain 1 has no tier done any more, the other achievement moved on. Another account's book.
        var t2 = t1.AddMinutes(5);
        watch.SceneChanged("HUB", "LOGIN");
        watch.Apply(Book(t2, new(1, 1, 3, 0), new(9, 1, 4, 0)), watch.Ticket(t2, inGame: false), t2);

        Assert.True(watch.Current.Confirmed);
        Assert.Empty(watch.Current.Gains);
        Assert.True(watch.Current.LostTiersSince(At34(Noon)));
        Assert.False(At34(Noon).LostTiersSince(watch.Current));
        Assert.Contains("fewer tiers than the known ones (another account or region): gains start again", TestDataFolder.ReadLog(), StringComparison.Ordinal);

        // Its own next read counts from it again.
        var t3 = t2.AddMinutes(5);
        watch.SceneChanged("GAMEPLAY", "HUB");
        watch.Apply(Book(t3, new(1, 1, 5, 0), new(9, 1, 4, 0)), watch.Ticket(t3.AddMinutes(1), inGame: false), t3);
        Assert.Equal(new AchievementGain(0, 2, t3), watch.Current.Gains[1]);
    }

    [Fact]
    public void A_read_of_states_only_keeps_the_catalog_and_gains()
    {
        var catalog = At34(Noon);

        var read = catalog.WithClientStates([new(1, 4, 10, Nov1), new(2, 1, 41, 0), new(9, 1, 2, 0), new(500, 4, 1, Nov1)], Noon.AddHours(1));

        Assert.NotNull(read);
        Assert.Same(catalog.Achievements, read.Achievements);
        Assert.Equal(41, read.States[2].Progress);
        Assert.DoesNotContain(500, read.States.Keys);
        Assert.Equal(Noon.AddHours(1), read.ReadAt);
        Assert.Equal(new AchievementGain(0, 7, Noon.AddHours(1)), read.WithGainsSince(catalog).Gains[1]);
        Assert.Null(catalog.WithClientStates([new(2, 1, 41, 0), new(2, 1, 41, 0)], Noon));
        Assert.Null(catalog.WithClientStates([new(2, 4, 50, -1)], Noon));
    }
}

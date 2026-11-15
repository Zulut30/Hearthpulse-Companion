using System.Net;
using System.Text;
using Hearthpulse.Core;
using Hearthpulse.Core.Pool;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests;

/// <summary>
/// <see cref="OwnBgHeroes"/>, the loader of the own Battlegrounds hero statistics (roadmap
/// 2026-11-15-hsreplay-remediation, C1): each of its states, the 30-minute copy on disk, the earlier copy with its own
/// date when the pool does not answer, and addresses that are never asked. No network: a fake handler answers, on the
/// fixture of <see cref="AnalyticsClientTests"/> and on <c>bg-heroes-render-small.json</c>
/// (tools/analytics/render_fixture.py).
/// </summary>
public sealed class OwnBgHeroesTests : IDisposable
{
    private const string Endpoint = "https://pool.example/stats";
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _cache = TestDataFolder.Scratch("hp-own-bg");
    private readonly FakeTimeProvider _time = new(Start);

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    private static string Fixture(string name = "bg-heroes.json") => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Analytics", name));

    private static string Unavailable(string reason) =>
        $$"""{"v":2,"metric":"bg.hero.offer_pick_placement","rulesVersion":"bg-hero-1","mode":"solo","available":false,"reason":"{{reason}}","published":null,"stale":false,"source":"source_capture","period":null,"groups":[]}""";

    private async Task<OwnBgHeroesResult> Load(FakePool pool, string endpoint = Endpoint, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(pool, disposeHandler: false);
        return await OwnBgHeroes.LoadAsync(http, endpoint, _cache, _time, cancellationToken);
    }

    // ---- the states -------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_pool_address_the_state_is_not_configured_and_nothing_is_asked()
    {
        var pool = new FakePool(Fixture());
        using var http = new HttpClient(pool);

        // The shipped default: no address at all.
        var result = await OwnBgHeroes.LoadAsync(http, new AppSettings(), _cache, _time);

        Assert.Equal(OwnBgHeroesState.NotConfigured, result.State);
        Assert.Null(result.Snapshot);
        Assert.Empty(pool.Requests);
        Assert.False(File.Exists(Path.Combine(_cache, OwnBgHeroes.CacheName)));
    }

    [Theory]
    [InlineData("http://pool.example/stats")]
    [InlineData("ftp://pool.example/stats")]
    [InlineData("https://user:secret@pool.example/stats")]
    [InlineData("pool.example/stats")]
    [InlineData("")]
    public async Task An_address_that_is_not_plain_https_is_not_asked(string endpoint)
    {
        var pool = new FakePool(Fixture());
        using var http = new HttpClient(pool);

        var bySettings = await OwnBgHeroes.LoadAsync(http, new AppSettings { PoolEndpoint = endpoint }, _cache, _time);
        var byAddress = await OwnBgHeroes.LoadAsync(http, endpoint, _cache, _time);

        Assert.Equal((OwnBgHeroesState.NotConfigured, OwnBgHeroesState.NotConfigured), (bySettings.State, byAddress.State));
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task The_settings_address_is_asked_once_with_get()
    {
        var pool = new FakePool(Fixture());
        using var http = new HttpClient(pool);

        var result = await OwnBgHeroes.LoadAsync(http, new AppSettings { PoolEndpoint = Endpoint + "/" }, _cache, _time);

        Assert.Equal(OwnBgHeroesState.Ok, result.State);
        Assert.Equal(["GET https://pool.example/stats/v2/analytics/bg/heroes?mode=solo"], pool.Requests);
    }

    [Fact]
    public async Task No_answer_and_no_copy_is_offline()
    {
        foreach (var pool in new[] { new FakePool(null), new FakePool("oops", HttpStatusCode.NotFound), new FakePool("<html>"), new FakePool("""{"v":1}""") })
        {
            var result = await Load(pool);

            Assert.Equal(OwnBgHeroesState.Offline, result.State);
            Assert.Null(result.Snapshot);
            Assert.False(result.FromOldCopy);
            Assert.Single(pool.Requests);
        }

        Assert.False(File.Exists(Path.Combine(_cache, OwnBgHeroes.CacheName)));
    }

    [Theory]
    [InlineData("not_published", OwnBgHeroesState.NotPublished)]
    [InlineData("rebuilding", OwnBgHeroesState.Rebuilding)]
    [InlineData("unsupported_mode", OwnBgHeroesState.Unsupported)]
    public async Task A_not_available_answer_keeps_its_own_state_and_has_no_rows(string reason, OwnBgHeroesState state)
    {
        var result = await Load(new FakePool(Unavailable(reason)));

        Assert.Equal(state, result.State);
        Assert.Null(result.Group);
        Assert.Equal(reason, result.Snapshot!.Reason);
        Assert.Equal(Start.UtcDateTime, result.FetchedAt);
    }

    [Fact]
    public async Task Published_rows_without_any_complete_hero_are_small_sample()
    {
        var result = await Load(new FakePool(Fixture("bg-heroes-render-small.json")));

        Assert.Equal(OwnBgHeroesState.SmallSample, result.State);
        Assert.Equal(4, result.Group!.Heroes.Count);
        Assert.All(result.Group.Heroes, h => Assert.Equal("small_sample", h.Quality));
    }

    [Fact]
    public async Task A_published_snapshot_is_ok_on_its_newest_build_only()
    {
        var result = await Load(new FakePool(Fixture()));

        Assert.Equal(OwnBgHeroesState.Ok, result.State);
        Assert.Equal((223456, 120, 14, 3), (result.Group!.Build, result.Group.Captures, result.Group.Sources, result.Group.Suppressed));
        Assert.Equal(3, result.Group.Heroes.Count);
        Assert.Equal((Start.UtcDateTime, false), (result.FetchedAt, result.FromOldCopy));

        // Builds are never merged: the newest one is shown whatever the order of the groups, the unknown build last.
        var reordered = OwnBgHeroesResult.From(result.Snapshot! with { Groups = [.. result.Snapshot.Groups.Reverse()] }, Start.UtcDateTime);
        Assert.Equal(223456, reordered.Group!.Build);
        var older = result.Snapshot.Groups[0] with { Build = 100, Captures = 999 };
        Assert.Equal(223456, OwnBgHeroesResult.From(result.Snapshot with { Groups = [older, result.Snapshot.Groups[0]] }, Start.UtcDateTime).Group!.Build);
    }

    [Fact]
    public void A_published_snapshot_without_groups_is_not_published()
    {
        var snapshot = OwnBgHeroesResult.FromJson(Fixture(), Start.UtcDateTime)!.Snapshot!;

        Assert.Equal(OwnBgHeroesState.NotPublished, OwnBgHeroesResult.From(snapshot with { Groups = [] }, Start.UtcDateTime).State);
        Assert.Null(OwnBgHeroesResult.FromJson("not json", Start.UtcDateTime));
        Assert.Null(OwnBgHeroesResult.FromJson("""{"v":2}""", Start.UtcDateTime));
    }

    // ---- the copy on disk ---------------------------------------------------------------------------

    [Fact]
    public async Task A_copy_younger_than_thirty_minutes_answers_without_a_request()
    {
        var pool = new FakePool(Fixture());
        // A clock between two milliseconds: the answer and its copy still carry one date.
        _time.Advance(TimeSpan.FromTicks(1234));
        var first = await Load(pool);
        Assert.Equal(Start.UtcDateTime, first.FetchedAt);
        _time.Advance(OwnBgHeroes.MaxAge - TimeSpan.FromSeconds(1));

        var cached = await Load(pool);

        Assert.Single(pool.Requests);
        Assert.Equal((OwnBgHeroesState.Ok, Start.UtcDateTime, false), (cached.State, cached.FetchedAt, cached.FromOldCopy));
        Assert.Equal(3, cached.Group!.Heroes.Count);

        _time.Advance(TimeSpan.FromSeconds(1));
        var fresh = await Load(pool);

        Assert.Equal(2, pool.Requests.Count);
        Assert.Equal(Start.UtcDateTime + OwnBgHeroes.MaxAge, fresh.FetchedAt);
    }

    [Fact]
    public async Task Offline_the_earlier_copy_is_shown_with_its_own_date()
    {
        await Load(new FakePool(Fixture()));
        _time.Advance(TimeSpan.FromDays(3));
        var down = new FakePool(null);

        var result = await Load(down);

        Assert.Single(down.Requests);
        Assert.Equal(OwnBgHeroesState.Ok, result.State);
        Assert.True(result.FromOldCopy);
        Assert.Equal(Start.UtcDateTime, result.FetchedAt);
        Assert.Equal(14, result.Group!.Sources);
    }

    [Fact]
    public async Task An_earlier_nothing_yet_answer_says_nothing_about_today()
    {
        await Load(new FakePool(Unavailable("not_published")));
        _time.Advance(TimeSpan.FromHours(1));

        var result = await Load(new FakePool(null));

        Assert.Equal((OwnBgHeroesState.Offline, false), (result.State, result.FromOldCopy));
    }

    [Fact]
    public async Task Withdrawn_numbers_replace_the_copy_and_do_not_come_back_offline()
    {
        await Load(new FakePool(Fixture()));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await Load(new FakePool(Unavailable("rebuilding")))).State);
        _time.Advance(TimeSpan.FromHours(1));

        var result = await Load(new FakePool(null));

        Assert.Equal(OwnBgHeroesState.Offline, result.State);
        Assert.Null(result.Group);
    }

    [Fact]
    public async Task The_copy_of_another_pool_is_not_shown()
    {
        await Load(new FakePool(Fixture()));

        var other = new FakePool(null);
        var result = await Load(other, "https://other.example/stats");

        Assert.Single(other.Requests);
        Assert.Equal(OwnBgHeroesState.Offline, result.State);
    }

    [Fact]
    public async Task A_damaged_copy_is_the_same_as_none_and_is_replaced_whole()
    {
        Directory.CreateDirectory(_cache);
        var file = Path.Combine(_cache, OwnBgHeroes.CacheName);
        await File.WriteAllTextAsync(file, "{\"endpoint\":\"https://pool.example/stats\",\"fetched\":");

        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null))).State);
        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(Fixture()))).State);

        // Written through a temporary file: the folder holds the finished copy and nothing else.
        Assert.Equal([OwnBgHeroes.CacheName], Directory.GetFiles(_cache).Select(Path.GetFileName));
        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(null))).State);
    }

    [Fact]
    public async Task A_copy_from_the_future_is_not_trusted_as_fresh()
    {
        await Load(new FakePool(Fixture()));
        var pool = new FakePool(Unavailable("not_published"));
        using var http = new HttpClient(pool);

        // The clock was set back two hours: the copy is "from the future".
        var result = await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, new FakeTimeProvider(Start.AddHours(-2)));

        Assert.Single(pool.Requests);
        Assert.Equal(OwnBgHeroesState.NotPublished, result.State);
    }

    // ---- withdrawn numbers stay withdrawn ----------------------------------------------------------

    private async Task<OwnBgHeroesResult> Load(HttpMessageHandler pool, OwnBgHeroesFiles files)
    {
        using var http = new HttpClient(pool, disposeHandler: false);
        return await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, _time, files);
    }

    private string CopyFile => Path.Combine(_cache, OwnBgHeroes.CacheName);

    /// <summary>Real files, except that the named ones cannot be written (and, when asked, not deleted either).</summary>
    private static OwnBgHeroesFiles Failing(Func<string, bool> fails, bool deleteFails = false)
    {
        var real = new OwnBgHeroesFiles();
        return new OwnBgHeroesFiles
        {
            Write = (file, text, token) => fails(file) ? Task.FromResult(false) : real.Write(file, text, token),
            Delete = file => !(deleteFails && fails(file)) && real.Delete(file),
        };
    }

    [Fact]
    public async Task A_withdrawal_whose_copy_could_not_be_replaced_still_hides_the_numbers_after_a_restart()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));

        // The copy cannot be replaced (another program holds it): the answer is shown, the old numbers stay on disk.
        var stuck = Failing(file => file == CopyFile);
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await Load(new FakePool(Unavailable("rebuilding")), stuck)).State);
        Assert.Contains("\"available\": true", await File.ReadAllTextAsync(CopyFile), StringComparison.Ordinal);
        Assert.True(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));

        // Offline in the same run, and in a new one (nothing in memory: only the marker on disk).
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), stuck)).State);
        var restarted = await Load(new FakePool(null), new OwnBgHeroesFiles());
        Assert.Equal((OwnBgHeroesState.Offline, false), (restarted.State, restarted.FromOldCopy));
        Assert.Null(restarted.Group);
    }

    [Theory]
    [InlineData("not_published")]
    [InlineData("rebuilding")]
    [InlineData("unsupported_mode")]
    public async Task Every_not_available_answer_withdraws_the_earlier_copy(string reason)
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));
        await Load(new FakePool(Unavailable(reason)), Failing(file => file == CopyFile));
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [WindowsFact]
    public async Task A_copy_held_open_by_another_program_does_not_bring_withdrawn_numbers_back()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));

        // Held for reading: it can be read, not renamed over and not deleted.
        await using (new FileStream(CopyFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(OwnBgHeroesState.Rebuilding, (await Load(new FakePool(Unavailable("rebuilding")), new OwnBgHeroesFiles())).State);
        }

        Assert.Contains("\"available\": true", await File.ReadAllTextAsync(CopyFile), StringComparison.Ordinal);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task A_new_publication_shows_numbers_again_and_removes_the_marker()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));
        await Load(new FakePool(Unavailable("rebuilding")), Failing(file => file == CopyFile));
        _time.Advance(TimeSpan.FromHours(1));

        var published = await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());

        Assert.Equal(OwnBgHeroesState.Ok, published.State);
        Assert.False(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));
        _time.Advance(TimeSpan.FromHours(1));
        var offline = await Load(new FakePool(null), new OwnBgHeroesFiles());
        Assert.Equal((OwnBgHeroesState.Ok, true, published.FetchedAt), (offline.State, offline.FromOldCopy, offline.FetchedAt));
    }

    [Fact]
    public async Task A_new_publication_that_cannot_be_kept_is_shown_now_and_the_old_copy_stays_hidden()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));
        var stuck = Failing(file => file == CopyFile);
        await Load(new FakePool(Unavailable("rebuilding")), stuck);
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(Fixture()), stuck)).State);

        // The copy on disk is still the one from before the withdrawal.
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task When_nothing_can_be_written_the_process_hides_the_numbers_and_writes_the_marker_later()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));
        var broken = true;
        var files = Failing(_ => broken, deleteFails: true);

        // Neither the copy nor the marker can be written and the copy cannot be deleted: no exception, no old numbers.
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await Load(new FakePool(Unavailable("rebuilding")), files)).State);
        Assert.False(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));
        Assert.Contains("\"available\": true", await File.ReadAllTextAsync(CopyFile), StringComparison.Ordinal);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), files)).State);

        // The disk works again: the next load of the same run writes the marker, and a new run honours it.
        broken = false;
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), files)).State);
        Assert.True(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task When_only_deleting_works_the_withdrawn_copy_is_deleted()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        _time.Advance(TimeSpan.FromHours(1));

        await Load(new FakePool(Unavailable("not_published")), Failing(_ => true));

        Assert.False(File.Exists(CopyFile));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task A_marker_that_cannot_be_read_hides_every_copy_until_a_new_publication()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        await File.WriteAllTextAsync(OwnBgHeroes.MarkerOf(CopyFile), "{\"endpoint\":");
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(Fixture()), new OwnBgHeroesFiles())).State);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task The_marker_of_another_pool_does_not_hide_the_copy_of_this_one()
    {
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());
        await File.WriteAllTextAsync(OwnBgHeroes.MarkerOf(CopyFile), "{\"endpoint\":\"https://other.example/stats\",\"revoked\":99999999999999}");
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    // ---- no clock in the decision, answers in the order of their requests -----------------------------

    [Fact]
    public async Task A_withdrawal_under_a_clock_set_back_still_hides_the_copy_written_before_it()
    {
        // The copy with numbers carries 12:00.
        await Load(new FakePool(Fixture()), new OwnBgHeroesFiles());

        // The clock goes back to 11:00; the pool withdraws the numbers and the copy cannot be replaced.
        var files = Failing(file => file == CopyFile);
        using (var http = new HttpClient(new FakePool(Unavailable("rebuilding"))))
        {
            var early = new FakeTimeProvider(Start.AddHours(-1));
            Assert.Equal(OwnBgHeroesState.Rebuilding, (await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, early, files)).State);
        }

        Assert.Contains("\"available\": true", await File.ReadAllTextAsync(CopyFile), StringComparison.Ordinal);

        // Offline afterwards, whatever the clock says, in the same run and in a new one: "12:00 is after 11:00" is not asked.
        foreach (var at in new[] { Start.AddHours(-1), Start, Start.AddMinutes(5), Start.AddDays(2) })
        {
            using var http = new HttpClient(new FakePool(null));
            var clock = new FakeTimeProvider(at);
            Assert.Equal(OwnBgHeroesState.Offline, (await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, clock, files)).State);
            Assert.Equal(OwnBgHeroesState.Offline, (await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, clock, new OwnBgHeroesFiles())).State);
        }
    }

    [Fact]
    public async Task A_slow_answer_with_numbers_does_not_undo_the_withdrawal_a_later_request_applied()
    {
        var stuck = true;
        var files = Failing(file => stuck && file == CopyFile);
        stuck = false;
        await Load(new FakePool(Fixture()), files);
        stuck = true;
        _time.Advance(TimeSpan.FromHours(1));
        var pool = new HeldPool();

        // Two loads at once; the first one's answer (numbers, sent before the withdrawal) is on its way.
        var first = Load(pool, files);
        await pool.Asked(1);
        var second = Load(pool, files);
        await pool.Asked(2);

        pool.Answer(2, Unavailable("rebuilding"));
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await second).State);
        Assert.True(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));

        // The old answer arrives: it changes nothing, and its load reports what the later one found.
        stuck = false;
        pool.Answer(1, Fixture());
        var late = await first;
        Assert.Equal(OwnBgHeroesState.Rebuilding, late.State);
        Assert.Null(late.Group);
        Assert.True(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), files)).State);
        Assert.Equal(OwnBgHeroesState.Offline, (await Load(new FakePool(null), new OwnBgHeroesFiles())).State);
    }

    [Fact]
    public async Task A_publication_of_a_later_request_follows_an_earlier_withdrawal_and_removes_the_marker()
    {
        var stuck = false;
        var files = Failing(file => stuck && file == CopyFile);
        await Load(new FakePool(Fixture()), files);
        _time.Advance(TimeSpan.FromHours(1));
        var pool = new HeldPool();
        var first = Load(pool, files);
        await pool.Asked(1);
        var second = Load(pool, files);
        await pool.Asked(2);

        // The earlier request brings the withdrawal (the copy cannot be replaced: a marker)...
        stuck = true;
        pool.Answer(1, Unavailable("rebuilding"));
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await first).State);
        Assert.True(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));

        // ...and the later one a new publication: the numbers are back and kept.
        stuck = false;
        pool.Answer(2, Fixture());
        var published = await second;
        Assert.Equal(OwnBgHeroesState.Ok, published.State);
        Assert.False(File.Exists(OwnBgHeroes.MarkerOf(CopyFile)));

        _time.Advance(TimeSpan.FromHours(1));
        var offline = await Load(new FakePool(null), new OwnBgHeroesFiles());
        Assert.Equal((OwnBgHeroesState.Ok, true), (offline.State, offline.FromOldCopy));
    }

    [Fact]
    public async Task A_cancelled_request_among_parallel_ones_leaves_the_gate_free()
    {
        var files = new OwnBgHeroesFiles();
        var pool = new HeldPool();
        using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(pool, disposeHandler: false);
        var cancelled = OwnBgHeroes.LoadAsync(http, Endpoint, _cache, _time, files, cancel.Token);
        await pool.Asked(1);
        var waiting = Load(pool, files);
        await pool.Asked(2);

        await cancel.CancelAsync();
        Assert.Equal(OwnBgHeroesState.Offline, (await cancelled).State);

        // The other request and a new one go through.
        pool.Answer(2, Fixture());
        Assert.Equal(OwnBgHeroesState.Ok, (await waiting).State);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Rebuilding, (await Load(new FakePool(Unavailable("rebuilding")), files)).State);

        // A token cancelled before the load starts does not take the gate with it either.
        Assert.Equal(OwnBgHeroesState.Offline, (await OwnBgHeroes.LoadAsync(http, Endpoint, _cache, _time, files, cancel.Token)).State);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(OwnBgHeroesState.Ok, (await Load(new FakePool(Fixture()), files)).State);
    }

    /// <summary>A pool whose answers the test hands out: each request waits for its own, in any order.</summary>
    private sealed class HeldPool : HttpMessageHandler
    {
        private readonly Lock _sync = new();
        private readonly List<TaskCompletionSource<string>> _answers = [];
        private readonly List<TaskCompletionSource> _asked = [];
        private int _requests;

        /// <summary>Completes when the request number <paramref name="number"/> (from 1) has reached the pool.</summary>
        public Task Asked(int number) => Slot(_asked, number, NewSignal).Task;

        public void Answer(int number, string body) => Slot(_answers, number, NewBody).SetResult(body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref _requests);
            Slot(_asked, number, NewSignal).SetResult();
            var body = await Slot(_answers, number, NewBody).Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource<string> NewBody() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private T Slot<T>(List<T> list, int number, Func<T> create)
        {
            lock (_sync)
            {
                while (list.Count < number)
                {
                    list.Add(create());
                }

                return list[number - 1];
            }
        }
    }

    // ---- never throws -------------------------------------------------------------------------------

    [Fact]
    public async Task A_cancelled_or_timed_out_request_ends_as_offline_not_as_an_exception()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var stopped = await Load(new FakePool(Fixture()), cancellationToken: cancelled.Token);
        var timedOut = await Load(new FakePool(Fixture()) { Fails = new TaskCanceledException("timeout") });

        Assert.Equal((OwnBgHeroesState.Offline, OwnBgHeroesState.Offline), (stopped.State, timedOut.State));
    }

    [Fact]
    public async Task A_cache_folder_that_cannot_be_written_still_gives_the_answer()
    {
        // A file where the folder should be: the copy cannot be written, the numbers are shown all the same.
        await File.WriteAllTextAsync(_cache, "not a folder");

        var result = await Load(new FakePool(Fixture()));

        Assert.Equal(OwnBgHeroesState.Ok, result.State);
    }

    private sealed class FakePool(string? body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public Exception? Fails { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            cancellationToken.ThrowIfCancellationRequested();
            if (Fails is not null)
            {
                throw Fails;
            }

            return body is null
                ? throw new HttpRequestException("No route to host")
                : Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

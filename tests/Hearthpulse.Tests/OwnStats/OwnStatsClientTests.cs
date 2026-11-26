using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Hearthpulse.Core;
using Hearthpulse.Core.Pool.Own;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests.OwnStats;

/// <summary>
/// <see cref="OwnStatsClient"/>, the one reader of Hearthpulse's own numbers (task pro-collection, package A5, AC19),
/// over a fake pool: the state of every answer, the six-hour copy and what takes its place (no older copy while the
/// pool is down, a clock set back, a failing disk, a withdrawal), requests that are never sent, the 4 MB and ten-second
/// limits, the number objects of contract K4, the log lines, and the app's wiring. The answers are synthetic, in the
/// envelope of contract K4; the clock is the test's.
/// </summary>
public sealed class OwnStatsClientTests : IDisposable
{
    private const string Pool = "https://pool.test/stats";
    private const string View = "constructed-mulligan";
    private const string Url = Pool + "/v2/analytics/records/constructed-mulligan?cls=MAGE&coin=any&fmt=standard&rank=d4-1&window=patch";

    // The builds deliberately out of order: the period names the first and the last.
    private const string Period = """{"line":"constructed","patch":"34.2.0","builds":[231577,231000],"window":"patch","fromDay":20400,"toDay":20418}""";
    private const string Windows = """[{"patch":"34.2.0","window":"patch"},{"patch":"34.2.0","window":"7d"},{"patch":"unknown","window":"30d"}]""";
    private const string SliceData = """{"games":412,"installs":7,"base":{"num":230,"den":412,"value":0.558,"low":0.51,"high":0.606,"rankable":true},"suppressed":0,"cards":[]}""";

    private static readonly DateTimeOffset Noon = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The parameters of the test view, deliberately not in name order.</summary>
    private static readonly KeyValuePair<string, string>[] Query =
        [new("window", "patch"), new("fmt", "standard"), new("rank", "d4-1"), new("cls", "MAGE"), new("coin", "any")];

    private readonly string _cache = TestDataFolder.Scratch("ownstats");
    private readonly FakeTimeProvider _time = new(Noon);

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Fact]
    public async Task A_published_answer_is_ok_with_its_period_windows_and_the_views_own_data()
    {
        var pool = new FakePool(Published());

        var answer = await Load(Client(pool));

        Assert.Equal(OwnStatsState.Ok, answer.State);
        Assert.Equal((View, "mulligan.card", "mull-card-1", 1794740400000L, false), (answer.View, answer.Metric, answer.RulesVersion, answer.Published, answer.Stale));
        Assert.Equal(new OwnPeriod("constructed", "34.2.0", 231000, 231577, "patch", 20400, 20418), answer.Period);
        Assert.Equal([new OwnWindow("34.2.0", "patch"), new OwnWindow("34.2.0", "7d"), new OwnWindow("unknown", "30d")], answer.Windows);
        Assert.Equal(new Slice(412, new OwnRate(230, 412, 0.558, 0.51, 0.606, true)), answer.Data);
        Assert.Equal(Noon.UtcDateTime, answer.FetchedAt);
        Assert.Equal(["GET " + Url], pool.Requests);
        Assert.StartsWith("own-constructed-mulligan-", Path.GetFileName(Assert.Single(Files())), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not_published", OwnStatsState.NotPublished)]
    [InlineData("rebuilding", OwnStatsState.Rebuilding)]
    [InlineData("no_window", OwnStatsState.NoWindow)]
    [InlineData("unsupported_mode", OwnStatsState.Unsupported)]
    [InlineData("a_reason_of_a_later_pool", OwnStatsState.NotPublished)]
    public async Task Each_reason_the_pool_gives_for_no_numbers_is_a_state_of_its_own(string reason, OwnStatsState state)
    {
        var answer = await Load(Client(new FakePool(Unavailable(reason))));

        Assert.Equal(state, answer.State);
        Assert.Null(answer.Data);
        Assert.Null(answer.Period);
        // "No window" serves the snapshot's time and the windows it has (what the block can offer instead); an answer that
        // serves no snapshot has "published": null, read as never published, not as an error.
        var noWindow = reason == "no_window";
        Assert.Equal(noWindow ? 1794740400000L : 0, answer.Published);
        Assert.Equal(noWindow ? 3 : 0, answer.Windows.Count);
        Assert.Equal(("mulligan.card", "mull-card-1", Noon.UtcDateTime), (answer.Metric, answer.RulesVersion, answer.FetchedAt));
        Assert.Single(Files());
    }

    public static TheoryData<int, string> NotTheViewsAnswer => new()
    {
        { 404, """{"error":"unknown_view"}""" },
        { 400, """{"error":"invalid_query"}""" },
        { 500, Published() },
        { 503, string.Empty },
        { 200, "not json" },
        { 200, string.Empty },
        { 200, "[]" },
        { 200, "null" },
        { 200, "{}" },
        { 200, Published().Replace("\"v\":2", "\"v\":1", StringComparison.Ordinal) },
        { 200, Published().Replace("\"v\":2", "\"v\":\"2\"", StringComparison.Ordinal) },
        { 200, Published().Replace("game_records", "source_captures", StringComparison.Ordinal) },
        { 200, Envelope(SliceData, view: "constructed-cards") },
        { 200, Published().Replace("\"available\":true,", string.Empty, StringComparison.Ordinal) },
        { 200, Published().Replace("\"available\":true", "\"available\":\"yes\"", StringComparison.Ordinal) },
        { 200, Envelope("null") },
        { 200, Envelope("\"games\"") },
        { 200, Published().Replace(",\"data\":" + SliceData, string.Empty, StringComparison.Ordinal) },
        { 200, Envelope(SliceData, period: "null") },
        { 200, Envelope(SliceData, period: Period.Replace(",\"window\":\"patch\"", string.Empty, StringComparison.Ordinal)) },
        { 200, Envelope(SliceData, period: Period.Replace("\"fromDay\":20400", "\"fromDay\":\"20400\"", StringComparison.Ordinal)) },
        // Data the view cannot read: no games, games as text, a list where an object belongs.
        { 200, Envelope("""{"cards":[]}""") },
        { 200, Envelope("""{"games":"412","base":null}""") },
        { 200, Envelope("[]") },
    };

    [Theory]
    [MemberData(nameof(NotTheViewsAnswer))]
    public async Task An_error_status_or_an_answer_that_is_not_the_views_is_offline_and_nothing_is_kept(int status, string body)
    {
        var pool = new FakePool(body) { Status = (HttpStatusCode)status };

        var answer = await Load(Client(pool));

        Assert.Equal(OwnStatsState.Offline, answer.State);
        Assert.Null(answer.Data);
        Assert.Null(answer.Period);
        Assert.Equal(default, answer.FetchedAt);
        Assert.Single(pool.Requests);
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData("""
        "builds":["231000","x"],
        """)]
    [InlineData("""
        "builds":[231000,"x"],
        """)]
    [InlineData("""
        "builds":[],
        """)]
    [InlineData("""
        "builds":null,
        """)]
    [InlineData("""
        "builds":231000,
        """)]
    [InlineData("")]
    public async Task Builds_that_are_not_all_whole_numbers_name_none_and_the_answer_stands(string builds)
    {
        var period = Period.Replace("\"builds\":[231577,231000],", builds, StringComparison.Ordinal);
        Assert.NotEqual(Period, period);

        var answer = await Load(Client(new FakePool(Envelope(SliceData, period: period))));

        Assert.Equal(OwnStatsState.Ok, answer.State);
        Assert.Equal(new OwnPeriod("constructed", "34.2.0", null, null, "patch", 20400, 20418), answer.Period);
    }

    [Fact]
    public async Task A_view_that_reads_nothing_from_the_data_is_offline()
    {
        var answer = await Client(new FakePool(Published())).LoadAsync<Slice>(View, Query, _ => null!);

        Assert.Equal(OwnStatsState.Offline, answer.State);
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://pool.test/stats")]
    [InlineData("pool.test/stats")]
    [InlineData("ftp://pool.test/stats")]
    [InlineData("https://user:secret@pool.test/stats")]
    [InlineData("https://pool.test/stats?key=1")]
    [InlineData("https://pool.test/stats#top")]
    public async Task Without_a_plain_https_pool_nothing_is_asked_and_the_block_is_not_configured(string? endpoint)
    {
        var pool = new FakePool(Published());
        var client = new OwnStatsClient(new HttpClient(pool), () => endpoint, _cache, _time);

        var answer = await Load(client);

        Assert.Equal(OwnStatsState.NotConfigured, answer.State);
        Assert.Equal(string.Empty, client.Address);
        Assert.Empty(pool.Requests);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task A_trailing_slash_of_the_pools_address_is_not_doubled()
    {
        var pool = new FakePool(Published());
        var client = new OwnStatsClient(new HttpClient(pool), () => Pool + "/", _cache, _time);

        Assert.Equal(OwnStatsState.Ok, (await Load(client)).State);
        Assert.Equal(Pool, client.Address);
        Assert.Equal(["GET " + Url], pool.Requests);
    }

    public static TheoryData<string, string, string> NotKeys => new()
    {
        { "Constructed-Mulligan", "fmt", "standard" },
        { "constructed/../decks", "fmt", "standard" },
        { "constructed mulligan", "fmt", "standard" },
        { "constructed-", "fmt", "standard" },
        { string.Empty, "fmt", "standard" },
        { new string('a', 49), "fmt", "standard" },
        { View, "fmt&cls", "standard" },
        { View, "Fmt", "standard" },
        { View, string.Empty, "standard" },
        { View, "fmt", string.Empty },
        { View, "fmt", new string('a', 65) },
    };

    [Theory]
    [MemberData(nameof(NotKeys))]
    public async Task A_view_or_a_parameter_that_is_not_a_key_never_reaches_the_pool(string view, string name, string value)
    {
        var pool = new FakePool(Published());

        var answer = await Client(pool).LoadAsync(view, [new(name, value)], ParseSlice);

        Assert.Equal(OwnStatsState.Unsupported, answer.State);
        Assert.Empty(pool.Requests);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task A_value_with_a_control_or_surrogate_character_never_reaches_the_pool()
    {
        var pool = new FakePool(Published());
        var client = Client(pool);

        // Kept out of the theory's data: test names carry it badly. No format, rank, class, archetype or patch has one.
        foreach (var value in new[] { "stan\ndard", "standard\0", "Spell Mage \u0085", "Spell Mage \ud800", "Spell Mage 😀" })
        {
            Assert.Equal(OwnStatsState.Unsupported, (await client.LoadAsync(View, [new("arch", value)], ParseSlice)).State);
        }

        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task A_parameter_given_twice_never_reaches_the_pool()
    {
        var pool = new FakePool(Published());

        var answer = await Client(pool).LoadAsync(View, [new("fmt", "standard"), new("cls", "MAGE"), new("fmt", "wild")], ParseSlice);

        Assert.Equal(OwnStatsState.Unsupported, answer.State);
        Assert.Empty(pool.Requests);
    }

    [Fact]
    public async Task Values_are_escaped_and_the_order_of_the_parameters_makes_no_other_request()
    {
        var pool = new FakePool(Envelope(SliceData, view: "bg-heroes"));
        var client = Client(pool);
        KeyValuePair<string, string>[] query = [new("mode", "solo"), new("mmr", "8000+"), new("window", "7d"), new("patch", "b231577")];

        Assert.Equal(OwnStatsState.Ok, (await client.LoadAsync("bg-heroes", query, ParseSlice)).State);
        Assert.Equal(OwnStatsState.Ok, (await client.LoadAsync("bg-heroes", [.. Enumerable.Reverse(query)], ParseSlice)).State);

        // One request, the parameters in name order; a plus is not read as a space.
        Assert.Equal([$"GET {Pool}/v2/analytics/records/bg-heroes?mmr=8000%2B&mode=solo&patch=b231577&window=7d"], pool.Requests);

        // An archetype's name reaches the pool as it is, and cannot add a parameter of its own.
        pool.Body = Published();
        Assert.Equal(OwnStatsState.Ok, (await client.LoadAsync(View, [new("fmt", "wild"), new("arch", "Spell Mage's & Co=1+")], ParseSlice)).State);
        var sent = HttpUtility.ParseQueryString(new Uri(pool.Requests[^1]["GET ".Length..]).Query);
        Assert.Equal("arch,fmt", string.Join(',', sent.AllKeys));
        Assert.Equal(("Spell Mage's & Co=1+", "wild"), (sent["arch"], sent["fmt"]));
    }

    [Fact]
    public async Task The_copy_answers_for_six_hours_and_after_that_there_is_no_older_copy_while_the_pool_is_down()
    {
        Assert.Equal(TimeSpan.FromHours(6), OwnStatsClient.MaxAge);
        var pool = new FakePool(Published());
        var client = Client(pool);
        Assert.Equal(OwnStatsState.Ok, (await Load(client)).State);

        _time.Advance(OwnStatsClient.MaxAge);
        var copy = await Load(client);
        Assert.Equal(OwnStatsState.Ok, copy.State);
        // The copy's own date, not the time it was read.
        Assert.Equal(Noon.UtcDateTime, copy.FetchedAt);
        Assert.Equal(new Slice(412, new OwnRate(230, 412, 0.558, 0.51, 0.606, true)), copy.Data);
        Assert.Single(pool.Requests);
        Assert.Single(Files());
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));

        // A minute later the copy is too old and the pool is down: offline, and the copy is gone for good, so no state of
        // the clock or the network brings it back.
        _time.Advance(TimeSpan.FromMinutes(1));
        pool.Status = null;
        Assert.Equal(OwnStatsState.Offline, (await Load(client)).State);
        Assert.Equal(2, pool.Requests.Count);
        Assert.Empty(Files());
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool, Noon))).State);
    }

    [Fact]
    public async Task A_copy_dated_in_the_future_is_deleted_before_the_pool_is_asked()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);

        // The clock went back an hour: the copy's age cannot be told. It is deleted, and with the pool down there is nothing.
        pool.Status = null;
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool, Noon - TimeSpan.FromHours(1)))).State);
        Assert.Empty(Files());

        // Back at the right time the pool is asked again.
        pool.Status = HttpStatusCode.OK;
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        Assert.Equal(3, pool.Requests.Count);
    }

    [Theory]
    [InlineData("rebuilding", OwnStatsState.Rebuilding, 10)]
    [InlineData("not_published", OwnStatsState.NotPublished, 10)]
    [InlineData("no_window", OwnStatsState.NoWindow, 360)]
    [InlineData("unsupported_mode", OwnStatsState.Unsupported, 360)]
    public async Task Numbers_about_to_come_are_asked_for_again_after_ten_minutes_other_answers_after_six_hours(string reason, OwnStatsState state, int minutes)
    {
        var pool = new FakePool(Unavailable(reason));
        var client = Client(pool);
        Assert.Equal(state, (await Load(client)).State);

        _time.Advance(TimeSpan.FromMinutes(minutes));
        Assert.Equal(state, (await Load(client)).State);
        Assert.Single(pool.Requests);

        // Past that the pool is asked, and numbers published meanwhile are there at once.
        _time.Advance(TimeSpan.FromMinutes(1));
        pool.Body = Published();
        Assert.Equal(OwnStatsState.Ok, (await Load(client)).State);
        Assert.Equal(2, pool.Requests.Count);
    }

    [Fact]
    public async Task A_withdrawal_takes_the_place_of_the_numbers_on_disk_so_no_restart_or_clock_brings_them_back()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);

        // A player had their games deleted: once the copy is past its age, the pool says the numbers are being rebuilt.
        _time.Advance(OwnStatsClient.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Unavailable("rebuilding");
        Assert.Equal(OwnStatsState.Rebuilding, (await Load(Client(pool))).State);
        Assert.DoesNotContain("\"games\":412", File.ReadAllText(Assert.Single(Files())), StringComparison.Ordinal);

        // The pool goes down. Right away the copy of that answer speaks; after a restart with the clock back at a time
        // the numbers were young, the copy on disk is the withdrawal (from the future for that clock, so deleted).
        pool.Status = null;
        Assert.Equal(OwnStatsState.Rebuilding, (await Load(Client(pool))).State);
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool, Noon + TimeSpan.FromHours(1)))).State);
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool))).State);
    }

    [Fact]
    public async Task An_unreadable_copy_or_one_of_another_request_is_never_read_and_the_pool_is_asked()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        var copy = Assert.Single(Files());
        var kept = File.ReadAllText(copy);

        // Each is young by the clock and still not read: the pool answers again and its answer takes the copy's place.
        var damaged = new[]
        {
            "not json", "[]",
            kept.Replace("{\"fetched\":", "{\"fetched\":\"", StringComparison.Ordinal).Replace(",\"url\":", "\",\"url\":", StringComparison.Ordinal),
            kept.Replace("cls=MAGE", "cls=PRIEST", StringComparison.Ordinal),
            kept.Replace("\"view\":\"constructed-mulligan\"", "\"view\":\"constructed-cards\"", StringComparison.Ordinal),
            kept.Replace("\"games\":412", "\"games\":\"412\"", StringComparison.Ordinal),
        };
        foreach (var text in damaged)
        {
            Assert.NotEqual(kept, text);
            File.WriteAllText(copy, text);
            Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
            Assert.Equal(kept, File.ReadAllText(copy));
        }

        Assert.Equal(1 + damaged.Length, pool.Requests.Count);
    }

    [Fact]
    public async Task An_answer_that_cannot_be_kept_is_still_shown_and_leaves_no_temporary_file()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        // A folder where the copy goes: the copy cannot be moved into place (any OS).
        var copy = Assert.Single(Files());
        File.Delete(copy);
        Directory.CreateDirectory(copy);

        var answer = await Load(Client(pool));

        Assert.Equal(OwnStatsState.Ok, answer.State);
        Assert.Equal(412, answer.Data!.Games);
        Assert.Empty(Directory.GetFiles(_cache, "*.tmp"));
        Assert.Equal(2, pool.Requests.Count);
    }

    [Fact]
    public async Task A_disk_that_refuses_the_answer_leaves_no_older_numbers_behind()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);

        _time.Advance(OwnStatsClient.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Unavailable("rebuilding");
        var broken = new OwnStatsClient(new HttpClient(pool), () => Pool, _cache, _time) { WriteFile = (_, _) => false };
        Assert.Equal(OwnStatsState.Rebuilding, (await Load(broken)).State);
        Assert.Empty(Files());

        // Offline now, after a restart, and with the clock back where the old copy would have been young.
        pool.Status = null;
        Assert.Equal(OwnStatsState.Offline, (await Load(broken)).State);
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool))).State);
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool, Noon + TimeSpan.FromHours(1)))).State);
    }

    [Fact]
    public async Task A_copy_that_can_be_neither_replaced_nor_deleted_is_never_read_again()
    {
        var pool = new FakePool(Published());
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        _time.Advance(OwnStatsClient.MaxAge + TimeSpan.FromMinutes(1));
        pool.Body = Unavailable("rebuilding");
        var broken = new OwnStatsClient(new HttpClient(pool), () => Pool, _cache, _time) { WriteFile = (_, _) => false, DeleteFile = _ => false };

        Assert.Equal(OwnStatsState.Rebuilding, (await Load(broken)).State);
        Assert.Single(Files());

        // The old numbers are still on disk and, by a clock set back, young again: nobody in this process reads them.
        pool.Status = null;
        Assert.Equal(OwnStatsState.Offline, (await Load(broken)).State);
        Assert.Equal(OwnStatsState.Offline, (await Load(Client(pool, Noon + TimeSpan.FromHours(1)))).State);

        // The next answer that does reach the disk is read as usual.
        (pool.Status, pool.Body) = (HttpStatusCode.OK, Published());
        var asked = pool.Requests.Count;
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        Assert.Equal(OwnStatsState.Ok, (await Load(Client(pool))).State);
        Assert.Equal(asked + 1, pool.Requests.Count);
    }

    [Fact]
    public async Task A_later_load_of_the_same_request_waits_for_the_answer_in_flight_and_another_request_does_not()
    {
        var pool = new FakePool(Published()) { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously), HoldOnlyFirst = true };
        var client = Client(pool);
        var first = Load(client);
        Assert.True(await pool.Received.WaitAsync(TimeSpan.FromMinutes(1)));

        // The same request again waits its turn instead of racing, so two answers cannot land on disk out of order.
        var second = Load(client);
        // Another request has a copy and a turn of its own: it is answered while the first one is held.
        var other = await client.LoadAsync(View, [new("fmt", "wild"), new("cls", "PRIEST")], ParseSlice);
        Assert.Equal(OwnStatsState.Ok, other.State);
        Assert.False(second.IsCompleted);
        Assert.Equal(2, pool.Requests.Count);

        pool.Hold!.SetResult();
        Assert.Equal(OwnStatsState.Ok, (await first).State);
        // It reads what the first one just fetched, and asks the pool for nothing.
        Assert.Equal(OwnStatsState.Ok, (await second).State);
        Assert.Equal(2, pool.Requests.Count);
    }

    [Fact]
    public async Task A_load_given_up_is_offline_throws_nothing_and_keeps_no_one_waiting()
    {
        var pool = new FakePool(Published()) { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var client = Client(pool);

        // Given up while the pool is asked.
        using (var asking = new CancellationTokenSource())
        {
            var load = Load(client, asking.Token);
            Assert.True(await pool.Received.WaitAsync(TimeSpan.FromMinutes(1)));
            await asking.CancelAsync();
            Assert.Equal(OwnStatsState.Offline, (await load).State);
        }

        // Given up while waiting its turn behind a load in flight.
        var held = Load(client);
        Assert.True(await pool.Received.WaitAsync(TimeSpan.FromMinutes(1)));
        using var waiting = new CancellationTokenSource();
        var behind = Load(client, waiting.Token);
        await waiting.CancelAsync();
        Assert.Equal(OwnStatsState.Offline, (await behind).State);

        pool.Hold!.SetResult();
        Assert.Equal(OwnStatsState.Ok, (await held).State);
        Assert.Equal(2, pool.Requests.Count);
        Assert.Single(Files());
    }

    [Fact]
    public async Task A_pool_that_does_not_answer_within_ten_seconds_is_offline()
    {
        var pool = new FakePool(Published()) { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var load = Load(Client(pool));
        Assert.True(await pool.Received.WaitAsync(TimeSpan.FromMinutes(1)));

        _time.Advance(OwnStatsClient.Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(load.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(OwnStatsState.Offline, (await load).State);
        Assert.Empty(Files());
        pool.Hold!.SetResult();
    }

    [Theory]
    [InlineData(false, 1, OwnStatsState.Offline)]
    [InlineData(true, 1, OwnStatsState.Offline)]
    [InlineData(false, 0, OwnStatsState.Ok)]
    [InlineData(true, 0, OwnStatsState.Ok)]
    public async Task An_answer_over_four_megabytes_is_offline_whether_or_not_its_length_is_told(bool unknownLength, int over, OwnStatsState state)
    {
        // Whitespace after the envelope keeps it valid JSON: only the size decides.
        var body = Published();
        body += new string(' ', OwnStatsClient.MaxBytes - Encoding.UTF8.GetByteCount(body) + over);
        var pool = new FakePool(body) { UnknownLength = unknownLength };

        Assert.Equal(state, (await Load(Client(pool))).State);
        Assert.Equal(state == OwnStatsState.Ok ? 1 : 0, Files().Length);
    }

    [Fact]
    public void Rates_are_read_by_kind_and_one_that_cannot_be_true_is_hidden()
    {
        Assert.Equal(new OwnRate(230, 412, 0.558, 0.51, 0.606, true), Rate("""{"num":230,"den":412,"value":0.558,"low":0.51,"high":0.606,"rankable":true}"""));
        // Below the minimum of games the counts stay without a value; below the installations there is nothing at all.
        Assert.Equal(new OwnRate(3, 8, null, null, null, false), Rate("""{"num":3,"den":8,"value":null,"low":null,"high":null,"rankable":false}"""));
        Assert.Equal(new OwnRate(3, 8, null, null, null, false), Rate("""{"num":3,"den":8}"""));
        Assert.Null(Rate("null"));
        Assert.Null(OwnStatsClient.Rate(default));
        foreach (var impossible in new[]
        {
            """{"num":9,"den":8}""", """{"num":-1,"den":8}""", """{"num":2.5,"den":8}""", """{"num":"3","den":8}""", """{"den":8}""",
            """{"num":3,"den":8,"value":1.5}""", """{"num":3,"den":8,"value":-0.1}""", """{"num":3,"den":8,"value":"0.4"}""",
            """{"num":3,"den":8,"value":0.4,"low":0.6,"high":0.4}""", "[3,8]", "\"0.4\"",
        })
        {
            Assert.Null(Rate(impossible));
        }
    }

    [Fact]
    public void Means_and_placements_are_read_by_kind_and_one_that_cannot_be_true_is_hidden()
    {
        Assert.Equal(new OwnMean(40, 6.5, 6.1, 6.9, true), Mean("""{"n":40,"mean":6.5,"low":6.1,"high":6.9,"rankable":true}"""));
        Assert.Equal(new OwnMean(1, 7, null, null, false), Mean("""{"n":1,"mean":7,"low":null,"high":null,"rankable":false}"""));
        foreach (var impossible in new[] { "null", """{"mean":6.5}""", """{"n":40,"mean":"6.5"}""", """{"n":40,"mean":6.5,"low":7,"high":6}""", "[40]" })
        {
            Assert.Null(Mean(impossible));
        }

        var solo = Placement("""{"n":40,"mean":3.9,"low":3.2,"high":4.6,"dist":[8,7,6,5,5,4,3,2],"rankable":false}""")!;
        Assert.Equal(new OwnPlacement(40, 3.9, 3.2, 4.6, null, false), solo with { Distribution = null });
        Assert.Equal([8, 7, 6, 5, 5, 4, 3, 2], solo.Distribution!);
        Assert.Equal([5, 4, 3, 2], Placement("""{"n":14,"mean":2.1,"low":1.6,"high":2.6,"dist":[5,4,3,2],"rankable":true}""")!.Distribution!);
        // Below the minimum of games: no mean and no distribution, the count stays.
        Assert.Equal(new OwnPlacement(4, null, null, null, null, false), Placement("""{"n":4,"mean":null,"low":null,"high":null,"dist":null,"rankable":false}"""));
        foreach (var impossible in new[]
        {
            "null", """{"mean":3.9}""", """{"n":40,"mean":9}""", """{"n":40,"mean":0.5}""", """{"n":40,"mean":3.9,"low":4.6,"high":3.2}""",
            """{"n":40,"mean":3.9,"dist":[8,7,6,5,5]}""", """{"n":40,"mean":3.9,"dist":[8,7,6,5,5,4,3,"2"]}""", """{"n":40,"mean":3.9,"dist":[8,7,6,-5]}""",
            """{"n":40,"mean":3.9,"dist":{"1":8}}""",
        })
        {
            Assert.Null(Placement(impossible));
        }
    }

    [Fact]
    public async Task Only_copies_past_their_age_are_removed_when_an_answer_is_kept()
    {
        Directory.CreateDirectory(_cache);
        string Kept(string name, TimeSpan age)
        {
            var path = Path.Combine(_cache, name);
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, (Noon - age).UtcDateTime);
            return path;
        }

        var expired = Kept("own-bg-heroes-0123456789abcdef.json", OwnStatsClient.MaxAge + TimeSpan.FromMinutes(1));
        var young = Kept("own-bg-comps-0123456789abcdef.json", TimeSpan.FromHours(1));
        var pairs = Kept("mulligan-pairs-0123abcd-standard-mage.json", TimeSpan.FromDays(30));

        Assert.Equal(OwnStatsState.Ok, (await Load(Client(new FakePool(Published())))).State);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(young));
        // Not a copy of this reader: left to its own.
        Assert.True(File.Exists(pairs));
        Assert.Equal(3, Files().Length);
    }

    [Fact]
    public async Task Log_lines_name_the_view_and_the_event_never_a_value_or_the_body()
    {
        const string view = "zqlog-view";
        var pool = new FakePool("""{"error":"Zqownbody5150"}""") { Status = HttpStatusCode.InternalServerError };
        KeyValuePair<string, string>[] query = [new("fmt", "standard"), new("arch", "Zqownvalue Mage")];

        Assert.Equal(OwnStatsState.Offline, (await Client(pool).LoadAsync(view, query, ParseSlice)).State);
        (pool.Status, pool.Body) = (HttpStatusCode.OK, """{"v":2,"note":"Zqownbody5150"}""");
        Assert.Equal(OwnStatsState.Offline, (await Client(pool).LoadAsync(view, query, ParseSlice)).State);

        var log = TestDataFolder.ReadLog();
        Assert.Contains($"Own statistics: {view}: the pool answered 500", log, StringComparison.Ordinal);
        Assert.Contains($"Own statistics: {view}: the answer is not the view's", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Zqownvalue", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Zqownbody", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public Task The_app_reads_its_own_numbers_from_the_pool_named_in_the_settings_and_keeps_the_copies_in_cache_pool()
    {
        var settings = new AppSettings { PoolEndpoint = Pool + "/" };
        return AppServicesTests.With(settings, (services, _) =>
        {
            Assert.Equal(Path.Combine(services.Data.Root, "cache", "pool"), services.OwnStats.CacheDirectory);
            Assert.Equal(Pool, services.OwnStats.Address);

            // Read at every load: another pool in the settings is the one asked next, and one that is not https is none.
            settings.PoolEndpoint = "https://other.test/stats";
            Assert.Equal("https://other.test/stats", services.OwnStats.Address);
            settings.PoolEndpoint = "http://pool.test/stats";
            Assert.Equal(string.Empty, services.OwnStats.Address);
            return Task.CompletedTask;
        });
    }

    /// <summary>What the test view's data holds: its games and the base rate (how a mulligan slice begins).</summary>
    private sealed record Slice(int Games, OwnRate? Base);

    private static Slice ParseSlice(JsonElement data) => new(data.GetProperty("games").GetInt32(), OwnStatsClient.Rate(data.GetProperty("base")));

    /// <summary>A reader of the pool with the test's clock, or with a clock of its own (a restart at another time).</summary>
    private OwnStatsClient Client(FakePool pool, DateTimeOffset? now = null) =>
        new(new HttpClient(pool), () => Pool, _cache, now is { } at ? new FakeTimeProvider(at) : _time);

    private static Task<OwnStatsAnswer<Slice>> Load(OwnStatsClient client, CancellationToken cancellationToken = default) =>
        client.LoadAsync(View, Query, ParseSlice, cancellationToken);

    private string[] Files() => Directory.Exists(_cache) ? Directory.GetFiles(_cache, "*.json") : [];

    private static OwnRate? Rate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return OwnStatsClient.Rate(doc.RootElement);
    }

    private static OwnMean? Mean(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return OwnStatsClient.Mean(doc.RootElement);
    }

    private static OwnPlacement? Placement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return OwnStatsClient.Placement(doc.RootElement);
    }

    /// <summary>A published answer of contract K4 (the shape <c>server/pool/node/records</c> writes).</summary>
    private static string Envelope(string data, string view = View, string period = Period) =>
        $$"""{"v":2,"source":"game_records","view":"{{view}}","metric":"mulligan.card","rulesVersion":"mull-card-1","derivers":{"deck":"deck-1","archetypes":"arch-20261126"},"available":true,"reason":null,"published":1794740400000,"stale":false,"thresholds":{"minInstalls":5,"minGames":10},"period":{{period}},"windows":{{Windows}},"data":{{data}}}""";

    private static string Published() => Envelope(SliceData);

    /// <summary>
    /// An answer without numbers as the pool gives it (docs/analytics-records.md, "Route and envelope"): "no window" with
    /// the snapshot's time and windows, any other reason with no snapshot behind it.
    /// </summary>
    private static string Unavailable(string reason) =>
        $$"""{"v":2,"source":"game_records","view":"{{View}}","metric":"mulligan.card","rulesVersion":"mull-card-1","derivers":{"deck":"deck-1","archetypes":"arch-20261126"},"available":false,"reason":"{{reason}}","published":{{(reason == "no_window" ? "1794740400000" : "null")}},"stale":false,"thresholds":{"minInstalls":5,"minGames":10},"period":null,"windows":{{(reason == "no_window" ? Windows : "[]")}},"data":null}""";

    /// <summary>
    /// Answers every request with the status and body it had when the request arrived; a null status throws like a
    /// machine without network. With <see cref="Hold"/> the answer waits until the test lets it go (or the request is
    /// cancelled); <see cref="UnknownLength"/> sends the body without its length, as a chunked answer.
    /// </summary>
    private sealed class FakePool(string body) : HttpMessageHandler
    {
        private int _arrived;

        public HttpStatusCode? Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = body;

        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Only the first request waits for <see cref="Hold"/>.</summary>
        public bool HoldOnlyFirst { get; init; }

        public bool UnknownLength { get; init; }

        /// <summary>Released once for every request that arrives.</summary>
        public SemaphoreSlim Received { get; } = new(0);

        /// <summary>Method and address of each request, the address as sent (escaped).</summary>
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, text) = (Status, Body);
            int order;
            lock (Requests)
            {
                Requests.Add($"{request.Method} {request.RequestUri!.AbsoluteUri}");
                order = ++_arrived;
            }

            Received.Release();
            if (Hold is { } hold && (!HoldOnlyFirst || order == 1))
            {
                await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (status is not { } code)
            {
                throw new HttpRequestException("No route to host");
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            return new HttpResponseMessage(code) { Content = UnknownLength ? new StreamContent(new Unseekable(bytes)) : new ByteArrayContent(bytes) };
        }
    }

    /// <summary>A body whose length cannot be told before it is read (no Content-Length).</summary>
    private sealed class Unseekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

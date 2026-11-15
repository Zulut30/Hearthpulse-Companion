using System.Net;
using System.Text.Json.Nodes;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests;

/// <summary>
/// The data API's freshness summary (Core/Api/DataHealth.cs) over a fake server answering with the real answers
/// (Fixtures/DataHealth: <c>GET https://api.kolodahearthstone.com/v1/system/sources</c> cut to the sources the app maps
/// plus a switched-off and a waiting one, and <c>/v1/system/health</c>, both read on 2026-11-15): the five states, the
/// section a late source belongs to and no other, offline without a false "stale", and the 30-minute disk cache.
/// </summary>
public sealed class DataHealthTests : IDisposable
{
    private readonly string _cache = TestDataFolder.Scratch("datahealth");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 21, 30, 0, TimeSpan.Zero));

    public void Dispose() => TestDataFolder.TryDelete(_cache);

    [Fact]
    public async Task The_live_answer_is_fresh_everywhere_and_names_each_sections_date()
    {
        var server = new Server();

        var report = await Health(server).LoadAsync();

        Assert.False(report.Offline);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, report.CheckedAt);
        Assert.All(DataSections.All.Where(s => DataSections.Sources(s).Count > 0), s => Assert.Equal(DataFreshness.Fresh, report.Section(s).State));
        // Heroes are joined from two sources: the date is the older one's (the details, collected every 120 hours).
        Assert.Equal(new DateTime(2026, 11, 12, 3, 35, 3, DateTimeKind.Utc).AddTicks(1266080), report.Section(DataSection.BgHeroes).DataDate);
        Assert.Equal([DataHealth.SourcesPath, DataHealth.HealthPath], server.Requests);
    }

    [Fact]
    public async Task Every_mapped_source_id_is_one_the_server_lists()
    {
        var report = await Health(new Server()).LoadAsync();

        var mapped = DataSections.All.SelectMany(DataSections.Sources).Distinct(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, id => Assert.True(report.Sources.ContainsKey(id), id));
        // The card catalog has no collector in the summary: its section stays unknown instead of borrowing another's state.
        Assert.Empty(DataSections.Sources(DataSection.CardPool));
        Assert.Equal(DataFreshness.Unknown, report.Section(DataSection.CardPool).State);
    }

    [Fact]
    public async Task A_stale_source_marks_its_own_section_and_no_other()
    {
        var server = new Server();
        server.Edit("hsguru_brawl", row =>
        {
            row["freshness_state"] = "stale";
            row["stale"] = true;
            row["age_hours"] = 40.5;
            row["last_success_at"] = "2026-11-14T05:00:00+00:00";
        });
        // hsguru_brawl is in the health answer's hard-failed list: without that, late is "stale".
        server.Health = "{\"data\":{\"hard_failed_sources\":[]}}";

        var report = await Health(server).LoadAsync();

        var brawl = report.Section(DataSection.Brawl);
        Assert.Equal(DataFreshness.Stale, brawl.State);
        Assert.True(brawl.Delayed);
        Assert.Equal(new DateTime(2026, 11, 14, 5, 0, 0, DateTimeKind.Utc), brawl.DataDate);
        Assert.All(DataSections.All.Where(s => s != DataSection.Brawl), s => Assert.False(report.Section(s).Delayed, s.ToString()));
    }

    [Fact]
    public async Task An_age_past_the_threshold_is_stale_even_when_the_server_did_not_flag_it()
    {
        var server = new Server();
        server.Edit("hsreplay_card_choices", row => row["age_hours"] = 36.5); // stale_after_hours is 36, "stale" false

        var report = await Health(server).LoadAsync();

        Assert.Equal(DataFreshness.Stale, report.Section(DataSection.CardChoices).State);
    }

    [Fact]
    public async Task One_late_source_of_several_decides_the_section_and_gives_its_date()
    {
        var server = new Server();
        server.Edit("hsreplay_battlegrounds_hero_details", row =>
        {
            row["stale"] = true;
            row["age_hours"] = 130.0;
        });

        var heroes = (await Health(server).LoadAsync()).Section(DataSection.BgHeroes);

        Assert.Equal(DataFreshness.Stale, heroes.State);
        Assert.Equal(12, heroes.DataDate!.Value.Day); // the details' date, not the list's of the 15th
    }

    [Theory]
    [InlineData("hsreplay_battlegrounds_trinkets_lesser")]
    [InlineData("hsreplay_battlegrounds_trinkets_greater")]
    [InlineData("hsreplay_battlegrounds_trinkets_top_5_percent_last_7_days")]
    public async Task The_trinket_section_is_late_when_only_one_of_its_slices_is(string source)
    {
        // The default slice, top 1 %, is built from the lesser and greater lists (the source_id of its rows in
        // GET api/bg/trinkets?mmr=TOP_1_PERCENT&timeRange=LAST_7_DAYS, 2026-11-15); the wider slices have their own.
        var server = new Server();
        server.Edit(source, row =>
        {
            row["stale"] = true;
            row["age_hours"] = 50.0;
            row["last_success_at"] = "2026-11-13T19:00:00+00:00";
        });

        var report = await Health(server).LoadAsync();

        var trinkets = report.Section(DataSection.BgTrinkets);
        Assert.Equal(DataFreshness.Stale, trinkets.State);
        Assert.Equal(new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc), trinkets.DataDate);
        Assert.All(DataSections.All.Where(s => s != DataSection.BgTrinkets), s => Assert.False(report.Section(s).Delayed, s.ToString()));
    }

    [Fact]
    public async Task A_check_now_during_a_refresh_waits_for_it_so_the_older_answer_never_lands_last()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var health = Health(server);

        // The 30-minute refresh is on the wire with the answer of that moment (everything fresh)...
        var refresh = health.LoadAsync();
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        // ...when the server's picture changes and the player presses "Check now".
        server.Edit("hsguru_brawl", row =>
        {
            row["stale"] = true;
            row["age_hours"] = 40.0;
        });
        server.Health = "{\"data\":{\"hard_failed_sources\":[]}}";
        var check = health.LoadAsync(force: true);
        Assert.Equal([DataHealth.SourcesPath], server.Requests); // the check has not gone out beside the refresh

        server.Hold.SetResult();
        var older = await refresh;
        var newer = await check;

        Assert.Equal(DataFreshness.Fresh, older.Section(DataSection.Brawl).State);
        Assert.Equal(DataFreshness.Stale, newer.Section(DataSection.Brawl).State);
        Assert.True(older.Sequence > 0 && newer.Sequence > older.Sequence);
        // One load at a time: the refresh's two requests, then the check's two.
        Assert.Equal([DataHealth.SourcesPath, DataHealth.HealthPath, DataHealth.SourcesPath, DataHealth.HealthPath], server.Requests);
        // The disk holds the newer answer: another reader gets it without asking.
        using var reader = Health(server);
        Assert.Equal(DataFreshness.Stale, (await reader.LoadAsync()).Section(DataSection.Brawl).State);
        Assert.Equal(4, server.Requests.Count);

        // The screen too, whichever order the two answers reach it in.
        Loc.Instance.SetLanguage(Loc.English);
        var vm = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App);
        vm.SetDataHealth(newer);
        vm.SetDataHealth(older);
        Assert.NotEqual(string.Empty, vm.BrawlDataDelay);
        Assert.Contains(vm.DataHealthRows, r => r.Attention);
        Assert.False(older.Supersedes(newer));
        Assert.True(newer.Supersedes(older));
    }

    [Fact]
    public async Task A_cancelled_load_leaves_the_next_one_free_to_run()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var health = Health(server);
        using var cancel = new CancellationTokenSource();

        var first = health.LoadAsync(cancellationToken: cancel.Token);
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = health.LoadAsync(force: true); // waits behind the first
        await cancel.CancelAsync();

        var cancelled = await first;
        Assert.True(cancelled.Offline);
        Assert.Empty(cancelled.Sources);
        Assert.Empty(Directory.Exists(_cache) ? Directory.GetFiles(_cache) : []); // nothing of the cancelled load on disk

        server.Hold.SetResult();
        var report = await second.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(report.Offline);
        Assert.Equal(DataFreshness.Fresh, report.Section(DataSection.Arena).State);

        // A load cancelled while it waited its turn does not take the gate with it either.
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();
        Assert.True((await health.LoadAsync(force: true, gone.Token)).Offline);
        Assert.False((await health.LoadAsync(force: true).WaitAsync(TimeSpan.FromSeconds(30))).Offline);
    }

    [Fact]
    public async Task Closing_cancels_the_running_load_and_asks_nothing_afterwards()
    {
        var server = new Server { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var health = Health(server);

        var running = health.LoadAsync();
        await server.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var waiting = health.LoadAsync(force: true);
        health.Dispose();
        health.Dispose(); // twice is once

        Assert.True((await running.WaitAsync(TimeSpan.FromSeconds(30))).Offline);
        Assert.True((await waiting.WaitAsync(TimeSpan.FromSeconds(30))).Offline);
        server.Hold.SetResult();
        var asked = server.Requests.Count;

        var after = await health.LoadAsync(force: true);
        Assert.True(after.Offline);
        Assert.Empty(after.Sources);
        Assert.Equal(asked, server.Requests.Count);
        Assert.Equal(1, asked); // only the request that was on the wire when the app closed
    }

    [Fact]
    public async Task Waiting_for_the_origin_is_not_a_delay()
    {
        var server = new Server();
        server.Edit("hsguru_meta_matrix", row =>
        {
            // What the server says of vicious_syndicate_radars in the live answer.
            row["freshness_state"] = "upstream_pending";
            row["stale"] = true;
            row["age_hours"] = 93.8;
        });

        var report = await Health(server).LoadAsync();

        Assert.Equal(DataFreshness.Pending, report.Sources["vicious_syndicate_radars"].State);
        var meta = report.Section(DataSection.ConstructedMeta);
        Assert.Equal(DataFreshness.Pending, meta.State);
        Assert.False(meta.Delayed);
    }

    [Fact]
    public async Task A_source_that_never_collected_or_failed_past_its_threshold_is_failed()
    {
        var server = new Server();
        server.Edit("hsreplay_arena_cards_advanced", row =>
        {
            row["freshness_state"] = "missing";
            row.Remove("age_hours");
            row.Remove("last_success_at");
        });
        server.Edit("hsguru_brawl", row =>
        {
            row["stale"] = true;
            row["age_hours"] = 20.0;
        });

        var report = await Health(server).LoadAsync();

        var arena = report.Section(DataSection.Arena);
        Assert.Equal(DataFreshness.Failed, arena.State);
        Assert.True(arena.Delayed);
        Assert.Null(arena.DataDate);
        Assert.Equal(DataFreshness.Failed, report.Section(DataSection.Brawl).State); // late and in hard_failed_sources
    }

    [Fact]
    public async Task A_failed_last_run_with_numbers_still_within_the_threshold_is_fresh()
    {
        // The live answers: hsguru_brawl is in hard_failed_sources while its numbers are 0.38 h old (threshold 12 h).
        var report = await Health(new Server()).LoadAsync();

        Assert.Equal(DataFreshness.Fresh, report.Section(DataSection.Brawl).State);
    }

    [Fact]
    public async Task A_switched_off_source_is_unknown_not_late()
    {
        var report = await Health(new Server()).LoadAsync();

        Assert.Equal(DataFreshness.Unknown, report.Sources["firestone_standard"].State);
    }

    [Fact]
    public async Task Offline_without_a_copy_knows_nothing_and_calls_nothing_stale()
    {
        var report = await Health(new Server { Offline = true }).LoadAsync();

        Assert.True(report.Offline);
        Assert.Null(report.CheckedAt);
        Assert.All(DataSections.All, s =>
        {
            Assert.Equal(DataFreshness.Unknown, report.Section(s).State);
            Assert.False(report.Section(s).Delayed);
            Assert.Null(report.Section(s).DataDate);
        });
    }

    [Fact]
    public async Task Offline_the_kept_copy_gives_dates_but_no_state_however_old_it_is()
    {
        var server = new Server();
        server.Edit("hsguru_brawl", row =>
        {
            row["stale"] = true;
            row["age_hours"] = 40.0;
        });
        var first = await Health(server).LoadAsync();
        Assert.True(first.Section(DataSection.Brawl).Delayed);

        // Three days later the server does not answer: every age in the copy is three days out of date.
        server.Offline = true;
        _time.Advance(TimeSpan.FromDays(3));
        var report = await Health(server).LoadAsync();

        Assert.True(report.Offline);
        Assert.Equal(first.CheckedAt, report.CheckedAt);
        Assert.All(DataSections.All, s =>
        {
            Assert.Equal(DataFreshness.Unknown, report.Section(s).State);
            Assert.False(report.Section(s).Delayed);
        });
        Assert.Equal(first.Section(DataSection.Arena).DataDate, report.Section(DataSection.Arena).DataDate);
    }

    [Fact]
    public async Task The_summary_is_kept_for_thirty_minutes_then_asked_again_and_check_now_skips_the_wait()
    {
        var server = new Server();
        var health = Health(server);

        var first = await health.LoadAsync();
        _time.Advance(TimeSpan.FromMinutes(29));
        var kept = await Health(server).LoadAsync(); // another instance: the copy is on disk
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(first.CheckedAt, kept.CheckedAt);
        Assert.False(kept.Offline);
        Assert.Equal(first.Sources.Count, kept.Sources.Count);
        Assert.Equal(first.Section(DataSection.BgHeroes), kept.Section(DataSection.BgHeroes));

        await health.LoadAsync(force: true);
        Assert.Equal(4, server.Requests.Count);

        _time.Advance(TimeSpan.FromMinutes(31));
        var later = await health.LoadAsync();
        Assert.Equal(6, server.Requests.Count);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, later.CheckedAt);
        // Written through a temporary file that is gone afterwards.
        Assert.Equal(["data-health.json"], Directory.GetFiles(_cache).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_broken_copy_or_a_wrong_answer_never_throws()
    {
        Directory.CreateDirectory(_cache);
        await File.WriteAllTextAsync(Path.Combine(_cache, "data-health.json"), "\0\0\0");
        var server = new Server();

        var asked = await Health(server).LoadAsync();
        Assert.False(asked.Offline);
        Assert.Equal(DataFreshness.Fresh, asked.Section(DataSection.Arena).State);

        // Not a list (a proxy's page, a changed contract): the copy stands in, marked offline.
        server.Sources = "{\"data\":{\"detail\":\"maintenance\"}}";
        _time.Advance(TimeSpan.FromHours(1));
        var wrong = await Health(server).LoadAsync();
        Assert.True(wrong.Offline);
        Assert.Equal(asked.CheckedAt, wrong.CheckedAt);

        // Rows of other shapes are skipped, not fatal.
        server.Sources = "{\"data\":[7,{\"site\":\"x\"},{\"id\":\"hsguru_brawl\",\"freshness_state\":\"fresh\",\"age_hours\":\"soon\"}]}";
        var odd = await Health(server).LoadAsync(force: true);
        Assert.False(odd.Offline);
        Assert.Equal("hsguru_brawl", Assert.Single(odd.Sources).Key);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.True((await Health(new Server()).LoadAsync(force: true, cancelled.Token)).Offline);
    }

    [Fact]
    public async Task Without_the_health_answer_the_sources_own_states_stand()
    {
        var server = new Server { Health = null };
        server.Edit("hsguru_brawl", row =>
        {
            row["stale"] = true;
            row["age_hours"] = 20.0;
        });

        var report = await Health(server).LoadAsync();

        Assert.False(report.Offline);
        Assert.Equal(DataFreshness.Stale, report.Section(DataSection.Brawl).State);
    }

    private DataHealth Health(Server server) => new(new HearthpulseApi(new HttpClient(server), "https://api.test/"), _cache, _time);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DataHealth", name));

    /// <summary>Answers the two system routes with the fixtures (editable per test), or not at all.</summary>
    private sealed class Server : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        /// <summary>While set and not completed, every answer waits for it (or for the request's cancellation).</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Completes once a held request is waiting.</summary>
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Offline { get; set; }

        public string Sources { get; set; } = Fixture("system-sources.json");

        /// <summary>Null answers 503.</summary>
        public string? Health { get; set; } = Fixture("system-health.json");

        /// <summary>Changes one source's row of the sources answer.</summary>
        public void Edit(string id, Action<JsonObject> change)
        {
            var root = JsonNode.Parse(Sources)!;
            change(root["data"]!.AsArray().Select(r => r!.AsObject()).Single(r => (string?)r["id"] == id));
            Sources = root.ToJsonString();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Offline)
            {
                throw new HttpRequestException("No such host is known.");
            }

            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            lock (Requests)
            {
                Requests.Add(path);
            }

            // The answer is what the server had when it was asked, however long it is held on the wire.
            var body = path == DataHealth.SourcesPath ? Sources : path == DataHealth.HealthPath ? Health : null;
            if (Hold is { Task.IsCompleted: false } hold)
            {
                Waiting.TrySetResult();
                await hold.Task.WaitAsync(cancellationToken);
            }

            return body is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}

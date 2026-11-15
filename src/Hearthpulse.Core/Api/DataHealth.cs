using System.Globalization;
using System.Text.Json;

namespace Hearthpulse.Core.Api;

/// <summary>What is known about how current a collected source, or an app section built on several, is.</summary>
public enum DataFreshness
{
    /// <summary>Nothing is known: no answer from the server (offline), or the summary does not list the source.</summary>
    Unknown,

    /// <summary>Collected within the source's own threshold.</summary>
    Fresh,

    /// <summary>Older than the threshold: the numbers stay on screen with a note.</summary>
    Stale,

    /// <summary>The collector is waiting for the origin to publish; not an error.</summary>
    Pending,

    /// <summary>The source never collected anything, or its collection failed and what it has is past the threshold.</summary>
    Failed,
}

/// <summary>The parts of the app that show numbers from the data API; each maps to source ids in <see cref="DataSections"/>.</summary>
public enum DataSection
{
    ConstructedMeta,
    ConstructedArchetypes,
    ConstructedMatchups,
    Mulligan,
    BgHeroes,
    BgHeroComps,
    BgMinions,
    BgTrinkets,
    BgComps,
    Arena,
    Brawl,
    CardChoices,
    CardPool,
}

/// <summary>One source of <c>v1/system/sources</c>, as the server described it when the summary was read.</summary>
/// <param name="AgeHours">Hours since the last successful collection, by the server's clock; null when it never succeeded.</param>
/// <param name="StaleAfterHours">The source's own threshold (30 h for daily ones, 120 h for the slow ones).</param>
public sealed record DataSourceHealth(string Id, DataFreshness State, double? AgeHours, double? StaleAfterHours, DateTime? LastSuccess, DateTime? NextRun);

/// <summary>A section's state: the worst of its sources, and the date of the source that decides it.</summary>
/// <param name="DataDate">When the deciding source last collected (UTC); null when unknown.</param>
public sealed record DataSectionHealth(DataSection Section, DataFreshness State, DateTime? DataDate, DateTime? NextRun)
{
    /// <summary>The section's numbers are late: the page says so next to its source line.</summary>
    public bool Delayed => State is DataFreshness.Stale or DataFreshness.Failed;
}

/// <summary>
/// Which source ids stand behind each section. The ids are the server's own (seen in <c>v1/system/sources</c> and in
/// the <c>meta.source_id</c> / <c>latest_run.source</c> / <c>list_source_id</c> of the routes the app calls on
/// 2026-11-15); the one table to edit when a route or a collector changes.
/// </summary>
public static class DataSections
{
    /// <summary>Every section in the order the settings card lists them.</summary>
    public static IReadOnlyList<DataSection> All { get; } = Enum.GetValues<DataSection>();

    private static readonly Dictionary<DataSection, string[]> Ids = new()
    {
        // v1/hsguru/meta, v1/hsguru/archetypes
        [DataSection.ConstructedMeta] = ["hsguru_meta_matrix"],
        // api/db/archetypes and its /decks, /matchups
        [DataSection.ConstructedArchetypes] = ["hsreplay_archetypes"],
        // v1/hsguru/matchups
        [DataSection.ConstructedMatchups] = ["hsguru_matchups"],
        // v1/hsguru/mulligan and class-matchups, v1/hsguru/archetypes/analysis, api/db/archetypes/{id}/mulligan,
        // the per-deck v1/hsreplay/mulligan and /decks
        [DataSection.Mulligan] = ["hsguru_mulligan", "hsguru_archetype_analysis", "hsreplay_archetypes", "hsreplay_mulligan"],
        // api/bg/heroes: the list and the per-hero details it is joined with
        [DataSection.BgHeroes] = ["hsreplay_battlegrounds_heroes", "hsreplay_battlegrounds_hero_details"],
        // v1/battlegrounds/heroes/compositions: the comps each hero ends with, named from the comps list
        [DataSection.BgHeroComps] = ["hsreplay_battlegrounds_hero_details", "hsreplay_battlegrounds_compositions"],
        // v1/battlegrounds/minions/stats; the Cards tab's api/bg/library/minion-stats
        [DataSection.BgMinions] = ["hsreplay_battlegrounds_minion_stats", "hsreplay_battlegrounds_minions"],
        // api/bg/tier-lists?list=trinkets&timeRange=LAST_7_DAYS for the slices the server collects
        [DataSection.BgTrinkets] =
        [
            "hsreplay_battlegrounds_trinkets_top_50_percent_last_7_days",
            "hsreplay_battlegrounds_trinkets_top_20_percent_last_7_days",
            "hsreplay_battlegrounds_trinkets_top_5_percent_last_7_days",
        ],
        // api/bg-comps: only the admitted origin's comps are taken (BgSiteParser drops the other list), so only its
        // collector counts
        [DataSection.BgComps] = ["hsreplay_battlegrounds_comps"],
        // datasets/hsreplay_arena_cards_advanced, v1/arena/classes
        [DataSection.Arena] = ["hsreplay_arena_cards_advanced", "hsreplay_arena_class_pages_firecrawl"],
        // v1/hsguru/brawl/meta
        [DataSection.Brawl] = ["hsguru_brawl"],
        // v1/hsreplay/card-choices and /cards
        [DataSection.CardChoices] = ["hsreplay_card_choices"],
        // api/v1/constructed-cards is the site's catalog: the summary lists no collector for it.
        [DataSection.CardPool] = [],
    };

    /// <summary>The source ids of a section; empty when the summary has no source for it.</summary>
    public static IReadOnlyList<string> Sources(DataSection section) => Ids.TryGetValue(section, out var ids) ? ids : [];
}

/// <summary>
/// The freshness summary as of <see cref="CheckedAt"/>. An <see cref="Offline"/> one is the last copy the app kept: it
/// still names the dates, but every section's state is <see cref="DataFreshness.Unknown"/> - an age measured hours ago
/// says nothing about now, and "stale" must not be shown on a guess.
/// </summary>
public sealed class DataHealthReport
{
    private readonly Dictionary<string, DataSourceHealth> _sources;

    public DataHealthReport(IEnumerable<DataSourceHealth> sources, DateTime? checkedAt, bool offline = false)
    {
        _sources = sources.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        CheckedAt = checkedAt;
        Offline = offline;
    }

    /// <summary>No summary at all: the server did not answer and nothing was kept.</summary>
    public static DataHealthReport None { get; } = new([], null, offline: true);

    /// <summary>When the server gave this summary (UTC); null when there is none.</summary>
    public DateTime? CheckedAt { get; }

    /// <summary>The server did not answer the last check: this is the kept copy (or nothing).</summary>
    public bool Offline { get; }

    public IReadOnlyDictionary<string, DataSourceHealth> Sources => _sources;

    /// <summary>
    /// A section's state. The worst source decides: failed, then stale, then waiting for the origin; fresh only when
    /// every listed source it has is fresh. Sources the summary does not list are left out, and a section with none
    /// is unknown.
    /// </summary>
    public DataSectionHealth Section(DataSection section)
    {
        var known = DataSections.Sources(section).Select(id => _sources.GetValueOrDefault(id)).OfType<DataSourceHealth>().ToList();
        if (known.Count == 0)
        {
            return new DataSectionHealth(section, DataFreshness.Unknown, null, null);
        }

        var state = known.Select(s => s.State).MaxBy(Severity);
        // The date is that of the oldest source in the deciding state: "data from" names what is late, not what is fine.
        var deciding = known.Where(s => s.State == state && s.LastSuccess is not null).MinBy(s => s.LastSuccess);
        var next = known.Where(s => s.NextRun is not null).Select(s => s.NextRun).Min();
        return new DataSectionHealth(section, Offline ? DataFreshness.Unknown : state, deciding?.LastSuccess, next);
    }

    private static int Severity(DataFreshness state) => state switch
    {
        DataFreshness.Failed => 4,
        DataFreshness.Stale => 3,
        DataFreshness.Pending => 2,
        DataFreshness.Unknown => 1,
        _ => 0,
    };
}

/// <summary>
/// Reads the data API's freshness summary (<c>v1/system/sources</c>, with the failed list of <c>v1/system/health</c>)
/// and keeps it on disk for 30 minutes. Offline, the kept copy is returned marked <see cref="DataHealthReport.Offline"/>.
/// Runs on the thread pool and never throws.
/// </summary>
/// <param name="cacheDirectory">Where the summary is kept; the app's API cache by default (tests pass a scratch one).</param>
public sealed class DataHealth(HearthpulseApi api, string? cacheDirectory = null, TimeProvider? time = null)
{
    public const string SourcesPath = "v1/system/sources";

    public const string HealthPath = "v1/system/health";

    /// <summary>How long a summary is used before the server is asked again.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private const string CacheName = "data-health.json";

    private readonly string _file = Path.Combine(cacheDirectory ?? Path.Combine(AppPaths.Cache, "api"), CacheName);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// The summary: the kept one while it is younger than <see cref="MaxAge"/> (unless <paramref name="force"/>, the
    /// "Check now" button), else the server's, else the kept copy marked offline.
    /// </summary>
    public Task<DataHealthReport> LoadAsync(bool force = false, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(force, cancellationToken), CancellationToken.None);

    private async Task<DataHealthReport> LoadCoreAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            var kept = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            var now = _time.GetUtcNow().UtcDateTime;
            if (!force && kept?.CheckedAt is { } at && now >= at && now - at <= MaxAge)
            {
                return kept;
            }

            var sources = await api.GetAsync(SourcesPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!sources.Ok || sources.Data.ValueKind != JsonValueKind.Array)
            {
                Diag.Info($"Data health: no summary ({sources.Error?.Code ?? "BAD_RESPONSE"}), kept copy: {(kept is null ? "none" : "yes")}");
                return kept is null ? DataHealthReport.None : new DataHealthReport(kept.Sources.Values, kept.CheckedAt, offline: true);
            }

            // The failed list is a second answer; without it the sources' own states still stand.
            var health = await api.GetAsync(HealthPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            var failed = health.Ok ? Strings(health.Data, "hard_failed_sources") : [];
            var rows = Parse(sources.Data, failed);
            var report = new DataHealthReport(rows, now, offline: false);
            await HearthpulseData.WriteCacheAsync(_file, Serialize(report), cancellationToken).ConfigureAwait(false);
            Diag.Info(string.Create(CultureInfo.InvariantCulture,
                $"Data health: {rows.Count} source(s), {Count(rows, DataFreshness.Stale)} stale, {Count(rows, DataFreshness.Pending)} pending, {Count(rows, DataFreshness.Failed)} failed"));
            return report;
        }
        catch (OperationCanceledException)
        {
            return DataHealthReport.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            Diag.Error("Data health unreadable", ex);
            return DataHealthReport.None;
        }
    }

    private static int Count(List<DataSourceHealth> rows, DataFreshness state) => rows.Count(r => r.State == state);

    /// <summary>The rows of <c>v1/system/sources</c>; a row without an id is skipped.</summary>
    internal static List<DataSourceHealth> Parse(JsonElement list, IReadOnlyCollection<string> hardFailed)
    {
        var rows = new List<DataSourceHealth>();
        foreach (var row in list.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || Text(row, "id") is not { Length: > 0 } id)
            {
                continue;
            }

            var age = Number(row, "age_hours");
            var limit = Number(row, "stale_after_hours");
            rows.Add(new DataSourceHealth(id, StateOf(row, age, limit, hardFailed.Contains(id)), age, limit, Date(row, "last_success_at"), Date(row, "next_run_at")));
        }

        return rows;
    }

    /// <summary>
    /// The server's words into the app's five states. A switched-off source is unknown, not late. A source whose last
    /// run failed but whose numbers are still within the threshold is fresh: the numbers on screen are.
    /// </summary>
    private static DataFreshness StateOf(JsonElement row, double? age, double? limit, bool hardFailed)
    {
        var state = Text(row, "freshness_state") ?? string.Empty;
        if (state == "disabled" || (row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False))
        {
            return DataFreshness.Unknown;
        }

        if (state == "upstream_pending")
        {
            return DataFreshness.Pending;
        }

        if (state is "missing" or "failed" or "error" or "hard_failed")
        {
            return DataFreshness.Failed;
        }

        var late = state == "stale"
                   || (row.TryGetProperty("stale", out var stale) && stale.ValueKind == JsonValueKind.True)
                   || (age is { } hours && limit is { } after && hours > after);
        return late ? hardFailed ? DataFreshness.Failed : DataFreshness.Stale
            : state == "fresh" || age is not null ? DataFreshness.Fresh
            : DataFreshness.Unknown;
    }

    private static string Serialize(DataHealthReport report) => JsonSerializer.Serialize(new
    {
        checked_at = report.CheckedAt,
        sources = report.Sources.Values.Select(s => new
        {
            id = s.Id,
            state = s.State.ToString(),
            age_hours = s.AgeHours,
            stale_after_hours = s.StaleAfterHours,
            last_success_at = s.LastSuccess,
            next_run_at = s.NextRun,
        }),
    });

    private async Task<DataHealthReport?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_file))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(_file, cancellationToken).ConfigureAwait(false));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Date(root, "checked_at") is not { } at
                || !root.TryGetProperty("sources", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var rows = new List<DataSourceHealth>();
            foreach (var row in list.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object && Text(row, "id") is { Length: > 0 } id
                    && Enum.TryParse<DataFreshness>(Text(row, "state"), out var state))
                {
                    rows.Add(new DataSourceHealth(id, state, Number(row, "age_hours"), Number(row, "stale_after_hours"), Date(row, "last_success_at"), Date(row, "next_run_at")));
                }
            }

            return new DataHealthReport(rows, at, offline: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken copy is no copy: the server is asked, and its answer replaces the file.
            Diag.Error("Data health cache unreadable", ex);
            return null;
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    private static DateTime? Date(JsonElement row, string name) =>
        Text(row, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : null;

    private static HashSet<string> Strings(JsonElement data, string name)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
                {
                    set.Add(text);
                }
            }
        }

        return set;
    }
}

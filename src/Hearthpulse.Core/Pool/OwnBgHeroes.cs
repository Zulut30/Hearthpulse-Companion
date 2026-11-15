using System.Text.Json;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Data;

namespace Hearthpulse.Core.Pool;

/// <summary>What the screen can say about Hearthpulse's own Battlegrounds hero statistics; each value is its own state.</summary>
public enum OwnBgHeroesState
{
    /// <summary>No stats pool is set up (or its address is not https): nothing was asked, the block is hidden.</summary>
    NotConfigured,

    /// <summary>The pool did not answer and there is no earlier copy with numbers.</summary>
    Offline,

    /// <summary>The pool answered: nothing was ever published.</summary>
    NotPublished,

    /// <summary>The pool answered: the numbers were withdrawn after a deletion and the next build is pending.</summary>
    Rebuilding,

    /// <summary>The pool answered that the metric does not exist for the mode: the block is hidden.</summary>
    Unsupported,

    /// <summary>Published, but no hero of the newest build has a value to show (rows without values, or no row at all).</summary>
    SmallSample,

    /// <summary>Published, and at least one hero of the newest build has every value.</summary>
    Ok,
}

/// <summary>
/// One load of the own hero statistics. <see cref="Group"/> is the newest game build of the snapshot (builds are
/// never merged), set for <see cref="OwnBgHeroesState.Ok"/> and <see cref="OwnBgHeroesState.SmallSample"/>.
/// <see cref="FetchedAt"/> (UTC) is when the pool gave this answer; <see cref="FromOldCopy"/>: the pool did not answer
/// now and the numbers are an earlier copy from disk.
/// </summary>
public sealed record OwnBgHeroesResult(OwnBgHeroesState State, BgHeroAnalytics? Snapshot, BgHeroAnalyticsGroup? Group, DateTime FetchedAt, bool FromOldCopy)
{
    /// <summary>The state alone, without an answer behind it.</summary>
    public static OwnBgHeroesResult Of(OwnBgHeroesState state) => new(state, null, null, default, false);

    /// <summary>
    /// The state of a pool answer given as its JSON text (a kept copy, a render fixture); null when the text is not
    /// such an answer.
    /// </summary>
    public static OwnBgHeroesResult? FromJson(string json, DateTime fetchedAt, bool fromOldCopy = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return AnalyticsClient.Parse(doc.RootElement) is { } snapshot ? From(snapshot, fetchedAt, fromOldCopy) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The state of a pool answer: which of the "not available" reasons, or what the newest build holds.</summary>
    public static OwnBgHeroesResult From(BgHeroAnalytics snapshot, DateTime fetchedAt, bool fromOldCopy = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Available)
        {
            var state = snapshot.Reason switch
            {
                "rebuilding" => OwnBgHeroesState.Rebuilding,
                "unsupported_mode" => OwnBgHeroesState.Unsupported,
                _ => OwnBgHeroesState.NotPublished,
            };
            return new(state, snapshot, null, fetchedAt, fromOldCopy);
        }

        // The newest build; the group without a build number only when it is the only one.
        var group = snapshot.Groups.Where(g => g.Build is not null).MaxBy(g => g.Build) ?? snapshot.Groups.FirstOrDefault();
        if (group is null)
        {
            return new(OwnBgHeroesState.NotPublished, snapshot, null, fetchedAt, fromOldCopy);
        }

        var complete = group.Heroes.Any(h => string.Equals(h.Quality, "ok", StringComparison.Ordinal));
        return new(complete ? OwnBgHeroesState.Ok : OwnBgHeroesState.SmallSample, snapshot, group, fetchedAt, fromOldCopy);
    }
}

/// <summary>
/// Loads Hearthpulse's own Battlegrounds hero statistics (metric <c>bg.hero.offer_pick_placement</c>,
/// docs/analytics-api.md) for a screen: the pool's address from the settings, a copy on disk for 30 minutes, and an
/// earlier copy with numbers when the pool does not answer. Never throws; all work is asynchronous.
/// </summary>
public static class OwnBgHeroes
{
    /// <summary>A copy on disk younger than this is shown without asking the pool.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    internal const string CacheName = "pool-bg-heroes-solo.json";

    /// <param name="cacheDirectory">Where the copy is kept; <c>cache/api</c> of the data folder when null.</param>
    /// <param name="time">The clock, for tests.</param>
    public static Task<OwnBgHeroesResult> LoadAsync(HttpClient http, AppSettings settings, string? cacheDirectory = null, TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return PoolClient.Available(settings)
            ? LoadAsync(http, PoolClient.Endpoint(settings), cacheDirectory, time, cancellationToken)
            : Task.FromResult(OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured));
    }

    /// <summary>As the overload with settings, for a pool base address; anything but https is "not configured" and is not asked.</summary>
    public static async Task<OwnBgHeroesResult> LoadAsync(HttpClient http, string endpoint, string? cacheDirectory = null, TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured);
        }

        endpoint = endpoint.TrimEnd('/');
        var file = Path.Combine(cacheDirectory ?? Path.Combine(AppPaths.Cache, "api"), CacheName);
        var now = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        try
        {
            var copy = await ReadCopyAsync(file, endpoint, cancellationToken).ConfigureAwait(false);
            // A copy from the future (the clock was set back) is not young: the pool is asked.
            if (copy is { } young && now >= young.FetchedAt && now - young.FetchedAt < MaxAge)
            {
                return OwnBgHeroesResult.From(young.Snapshot, young.FetchedAt);
            }

            if (await AnalyticsClient.FetchBgHeroesAsync(http, endpoint, cancellationToken).ConfigureAwait(false) is { } answer)
            {
                // Every answer replaces the copy, "withdrawn" included: numbers the pool took back do not outlive it here.
                await HearthpulseData.WriteCacheAsync(file, Envelope(endpoint, now, answer.Body), cancellationToken).ConfigureAwait(false);
                return OwnBgHeroesResult.From(answer.Snapshot, now);
            }

            // No answer. An earlier copy with numbers is shown with its own date; an earlier "nothing yet" says nothing
            // about today.
            return copy is { Snapshot.Available: true } old
                ? OwnBgHeroesResult.From(old.Snapshot, old.FetchedAt, fromOldCopy: true)
                : OwnBgHeroesResult.Of(OwnBgHeroesState.Offline);
        }
        catch (OperationCanceledException)
        {
            return OwnBgHeroesResult.Of(OwnBgHeroesState.Offline);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or NotSupportedException)
        {
            Diag.Error("Own Battlegrounds hero statistics unavailable", ex);
            return OwnBgHeroesResult.Of(OwnBgHeroesState.Offline);
        }
    }

    private static string Envelope(string endpoint, DateTime fetchedAt, string body) =>
        "{\"endpoint\":" + JsonSerializer.Serialize(endpoint) + ",\"fetched\":" +
        new DateTimeOffset(fetchedAt).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"body\":" + body + "}";

    /// <summary>The copy on disk, when it is this pool's and readable; a damaged file is the same as none.</summary>
    private static async Task<(BgHeroAnalytics Snapshot, DateTime FetchedAt)?> ReadCopyAsync(string file, string endpoint, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!string.Equals(root.GetProperty("endpoint").GetString(), endpoint, StringComparison.Ordinal))
            {
                return null;
            }

            var fetched = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("fetched").GetInt64()).UtcDateTime;
            return AnalyticsClient.Parse(root.GetProperty("body")) is { } snapshot ? (snapshot, fetched) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException
            or KeyNotFoundException or ArgumentOutOfRangeException)
        {
            Diag.Error("Own Battlegrounds hero statistics: the copy on disk is unreadable", ex);
            return null;
        }
    }
}

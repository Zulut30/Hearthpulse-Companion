using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Pool;

/// <summary>
/// Hearthpulse's own opening-hand pair statistics (metric <c>mulligan.pair_winrate</c>, rules <c>mull-pairs-1</c>;
/// docs/analytics-metrics.md): the stats pool's <c>GET /v1/events/mulligan-pairs</c> for one format and class, cached
/// on disk. Never throws and never waits long: no pool, an error or a timeout is an empty list, and the guide simply
/// shows no synergy. These are our own numbers ("Hearthpulse statistics"), never mixed into the external data.
/// </summary>
/// <param name="endpoint">The pool's base address right now (<see cref="PoolClient.Endpoint"/>); empty when no pool is set up.</param>
/// <param name="cacheDirectory">Where answers are cached; the app's cache folder by default (tests pass a scratch one).</param>
public sealed partial class MulliganPairs(HttpClient http, Func<string> endpoint, string? cacheDirectory = null)
{
    /// <summary>The metric and the rules this client understands: another computation is not read as this one.</summary>
    public const string Metric = "mulligan.pair_winrate";

    public const string Rules = "mull-pairs-1";

    /// <summary>The pool publishes nothing made of fewer installations; a row below it is dropped here as well.</summary>
    public const int MinInstallations = 5;

    /// <summary>A fresh answer is not asked for again for this long.</summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>Offline, a copy older than this is not shown: the numbers describe a 30-day window that has moved on.</summary>
    internal static readonly TimeSpan MaxStale = TimeSpan.FromDays(7);

    /// <summary>The guide is on screen for a minute at most: the pool gets this long, then the guide goes without pairs.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const int MaxBytes = 1024 * 1024;

    private readonly string _cacheDirectory = cacheDirectory ?? Path.Combine(AppPaths.Cache, "pool");

    [GeneratedRegex("^[A-Z]{4,14}$")]
    private static partial Regex ClassName();

    [GeneratedRegex("^[A-Za-z0-9_]{3,64}$")]
    private static partial Regex CardId();

    /// <summary>
    /// The published pairs of a format ("standard" or "wild") and a class ("MAGE"). Empty when the pool is not set up
    /// or not https (no request is made), when it has no pair above its thresholds, or when neither the pool nor a
    /// cached copy answers.
    /// </summary>
    public async Task<IReadOnlyList<GuidePair>> LoadAsync(string format, string playerClass, CancellationToken cancellationToken = default)
    {
        var cls = playerClass.ToUpperInvariant();
        var address = endpoint().TrimEnd('/');
        if (format is not ("standard" or "wild") || !ClassName().IsMatch(cls)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return [];
        }

        try
        {
            // One file per pool, format and class: numbers of a test pool never answer for another one.
            var pool = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address)))[..8];
            var file = Path.Combine(_cacheDirectory, $"mulligan-pairs-{pool}-{format}-{cls.ToLowerInvariant()}.json");
            var age = File.Exists(file) ? DateTime.UtcNow - File.GetLastWriteTimeUtc(file) : TimeSpan.MaxValue;
            if (age <= MaxAge && await ReadAsync(file, format, cls, cancellationToken).ConfigureAwait(false) is { } fresh)
            {
                return fresh;
            }

            if (await FetchAsync($"{address}/v1/events/mulligan-pairs?fmt={format}&cls={cls}", cancellationToken).ConfigureAwait(false) is { } text
                && Parse(text, format, cls) is { } pairs)
            {
                await HearthpulseData.WriteCacheAsync(file, text, cancellationToken).ConfigureAwait(false);
                return pairs;
            }

            return age <= MaxStale ? await ReadAsync(file, format, cls, cancellationToken).ConfigureAwait(false) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Diag.Info($"Mulligan pairs: not loaded ({ex.GetType().Name})");
            return [];
        }
    }

    /// <summary>The answer's text, or null on a network error, a timeout, a non-success status or an oversized body.</summary>
    private async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Diag.Info($"Mulligan pairs: the pool answered {(int)response.StatusCode}");
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            await using var streamScope = stream.ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(block, limit.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(block.AsMemory(0, read), limit.Token).ConfigureAwait(false);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            Diag.Info($"Mulligan pairs: the pool did not answer ({ex.GetType().Name})");
            return null;
        }
    }

    private static async Task<IReadOnlyList<GuidePair>?> ReadAsync(string file, string format, string cls, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(file) ? Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), format, cls) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The pairs of an answer; null when it is not this metric's answer for this format and class (bad JSON, another
    /// version or rules). A row that is malformed, below the pool's own thresholds or impossible (more wins than
    /// games) is dropped: the client does not trust the pool to have filtered.
    /// </summary>
    internal static IReadOnlyList<GuidePair>? Parse(string text, string format, string cls)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var v) || v != 1
                || Text(root, "metric") != Metric || Text(root, "rules") != Rules || Text(root, "fmt") != format || Text(root, "cls") != cls
                || !root.TryGetProperty("pairs", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var pairs = new List<GuidePair>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || Text(row, "a") is not { } a || Text(row, "b") is not { } b || a == b
                    || !CardId().IsMatch(a) || !CardId().IsMatch(b)
                    || !row.TryGetProperty("installs", out var count) || !count.TryGetInt32(out var installs) || installs < MinInstallations
                    || Cell(row, "both") is not { Games: >= MulliganGuide.MinPairGames } both)
                {
                    continue;
                }

                pairs.Add(new GuidePair(a, b, installs, both, Cell(row, "onlyA"), Cell(row, "onlyB"), Cell(row, "none")));
            }

            return pairs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A <c>[games, wins]</c> cell; null when the pool hid it (too few installations or games) or it is malformed.</summary>
    private static PairCell? Cell(JsonElement row, string name) =>
        row.TryGetProperty(name, out var cell) && cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() == 2
        && cell[0].TryGetInt32(out var games) && cell[1].TryGetInt32(out var wins) && games > 0 && wins >= 0 && wins <= games
            ? new PairCell(games, wins)
            : null;
}

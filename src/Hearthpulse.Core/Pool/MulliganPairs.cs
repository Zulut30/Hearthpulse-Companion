using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Core.Pool;

/// <summary>
/// Hearthpulse's own opening-hand pair statistics (metric <c>mulligan.pair_winrate</c>, rules <c>mull-pairs-1</c>;
/// docs/analytics-metrics.md): the stats pool's <c>GET /v1/events/mulligan-pairs</c> for one format and class. Never
/// throws and never waits long: no pool, an error or a timeout is an empty list, and the guide shows no pair line.
/// These are our own numbers ("Hearthpulse statistics"), never mixed into the external data.
/// <para>
/// The pool withdraws a pair when a player has their games deleted, so the copy on disk is a short convenience and
/// nothing more: it answers for <see cref="MaxAge"/> after it was fetched, and there is no older copy for the time the
/// pool is down. A copy that is past that age, or dated in the future (the clock was set back), is deleted before the
/// pool is asked; one that cannot be deleted is never read again in this process. One fetch at a time per file, in
/// the order asked, so an answer never lands over a later one.
/// </para>
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

    /// <summary>An answer is not asked for again, and is shown, for this long after it was fetched; never longer.</summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>The pool gets this long; then the guide goes without pairs.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const int MaxBytes = 1024 * 1024;

    /// <summary>One fetch at a time per cache file, across every instance of the process.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// <summary>Files holding an expired copy that could not be deleted: not read again until a new answer replaces them.</summary>
    private static readonly ConcurrentDictionary<string, bool> Unreadable = new(StringComparer.Ordinal);

    private readonly string _cacheDirectory = cacheDirectory ?? Path.Combine(AppPaths.Cache, "pool");

    internal TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Writes a cache file whole or not at all; false when it could not. Replaced by tests of a failing disk.</summary>
    internal Func<string, string, bool> WriteFile { get; init; } = WriteAtomically;

    /// <summary>Deletes a cache file; false when it is still there. Replaced by tests of a failing disk.</summary>
    internal Func<string, bool> DeleteFile { get; init; } = Delete;

    [GeneratedRegex("^[A-Z]{4,14}$")]
    private static partial Regex ClassName();

    [GeneratedRegex("^[A-Za-z0-9_]{3,64}$")]
    private static partial Regex CardId();

    /// <summary>
    /// The pool this instance reads right now, without a trailing slash; empty when none is set up or it is not plain
    /// https. Whoever keeps pairs in memory keeps them for this address only.
    /// </summary>
    public string Address
    {
        get
        {
            var address = endpoint().TrimEnd('/');
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? address : string.Empty;
        }
    }

    /// <summary>
    /// The published pairs of a format ("standard" or "wild") and a class ("MAGE"). Empty when the pool is not set up
    /// or not https (no request is made), when it has no pair above its thresholds, and whenever it does not answer
    /// and the copy on disk is older than <see cref="MaxAge"/>.
    /// </summary>
    public async Task<IReadOnlyList<GuidePair>> LoadAsync(string format, string playerClass, CancellationToken cancellationToken = default)
    {
        var cls = playerClass.ToUpperInvariant();
        var address = Address;
        if (format is not ("standard" or "wild") || !ClassName().IsMatch(cls) || address.Length == 0)
        {
            return [];
        }

        // One file per pool, format and class: numbers of a test pool never answer for another one.
        var pool = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address)))[..8];
        var file = Path.GetFullPath(Path.Combine(_cacheDirectory, $"mulligan-pairs-{pool}-{format}-{cls.ToLowerInvariant()}.json"));
        var gate = Gates.GetOrAdd(file, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        try
        {
            if (!Unreadable.ContainsKey(file) && File.Exists(file))
            {
                if (ReadFresh(file, format, cls) is { } fresh)
                {
                    return fresh;
                }

                // Past its age, from the future or unreadable: gone before the pool is asked, so no later state of the
                // clock, the disk or the network can bring it back.
                Discard(file);
            }

            if (await FetchAsync($"{address}/v1/events/mulligan-pairs?fmt={format}&cls={cls}", cancellationToken).ConfigureAwait(false) is not { } text
                || Parse(text, format, cls) is not { } pairs)
            {
                return [];
            }

            var stamped = "{\"fetched\":" + Time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + ",\"answer\":" + text + "}";
            if (WriteFile(file, stamped))
            {
                Unreadable.TryRemove(file, out _);
            }

            return pairs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up while the pool was asked: nothing, as for a caller that gave up waiting its turn above;
            // not logged as a pool that did not answer.
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private void Discard(string file)
    {
        if (!DeleteFile(file))
        {
            Unreadable[file] = true;
            Diag.Info("Mulligan pairs: an expired copy could not be deleted and is ignored");
        }
    }

    /// <summary>The copy's pairs while it is at most <see cref="MaxAge"/> old by its own stamp; null for anything else.</summary>
    private IReadOnlyList<GuidePair>? ReadFresh(string file, string format, string cls)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Int64(root, "fetched") is not { } fetched
                || !root.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var age = Time.GetUtcNow().ToUnixTimeMilliseconds() - fetched;
            return age >= 0 && age <= (long)MaxAge.TotalMilliseconds ? Parse(answer, format, cls) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool WriteAtomically(string file, string text)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, text);
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Info($"Mulligan pairs: the answer was not cached ({ex.GetType().Name})");
            Delete(temp);
            return false;
        }
    }

    private static bool Delete(string file)
    {
        try
        {
            File.Delete(file);
            return !File.Exists(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The answer's text, or null on a network error, a timeout, a non-success status or an oversized body.</summary>
    private async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Timeout, Time);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
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
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Diag.Info($"Mulligan pairs: the pool did not answer ({ex.GetType().Name})");
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
            return Parse(doc.RootElement, format, cls);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every value is read by its kind first: a string where a number belongs is a dropped row, never an exception.</summary>
    private static List<GuidePair>? Parse(JsonElement root, string format, string cls)
    {
        if (root.ValueKind != JsonValueKind.Object || Int32(root, "v") != 1
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
                || Int32(row, "installs") is not { } installs || installs < MinInstallations
                || Cell(row, "both") is not { Games: >= MulliganGuide.MinPairGames } both)
            {
                continue;
            }

            pairs.Add(new GuidePair(a, b, installs, both, Cell(row, "onlyA"), Cell(row, "onlyB"), Cell(row, "none")));
        }

        return pairs;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? Int32(value) : null;

    private static int? Int32(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static long? Int64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    /// <summary>A <c>[games, wins]</c> cell; null when the pool hid it (too few installations or games) or it is malformed.</summary>
    private static PairCell? Cell(JsonElement row, string name) =>
        row.TryGetProperty(name, out var cell) && cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() == 2
        && Int32(cell[0]) is { } games && Int32(cell[1]) is { } wins && games > 0 && wins >= 0 && wins <= games
            ? new PairCell(games, wins)
            : null;
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hearthpulse.Core.Pool.Own;

/// <summary>
/// The one reader of Hearthpulse's own numbers, every view the stats pool computes from game records:
/// <c>GET /v2/analytics/records/&lt;view&gt;?window=…&amp;patch=…&amp;…</c>, envelope <c>v: 2</c>, <c>source:
/// "game_records"</c>. A view package gives the key, the parameters and how to read <c>data</c>; this class asks the
/// pool (plain https, at most 4 MB, <see cref="Timeout"/>), reads the envelope into an <see cref="OwnStatsState"/> and
/// keeps a copy on disk. Our own numbers ("Hearthpulse statistics"), never mixed into the external data. Never throws
/// for anything the pool, the network or the disk does.
/// <para>
/// A deletion of a player's games must reach the screen: a copy answers for <see cref="MaxAge"/> after it was fetched,
/// never later, and there is no older copy while the pool is down. A copy past its age, dated in the future or
/// unreadable is deleted before the pool is asked; one that cannot be deleted is never read again in this process.
/// "Not published" and "rebuilding" are asked again after <see cref="PendingAge"/>. One fetch at a time per request, in
/// the order asked. Each pool address and request has its copy (<c>own-&lt;view&gt;-&lt;hash&gt;.json</c>); copies older
/// than <see cref="MaxAge"/> go whenever a new one is kept.
/// </para>
/// <para>
/// A request names the view and its parameters (format, rank bucket, class or archetype, coin, MMR range, Arena kind,
/// window, patch), no id of the player or the installation. Log lines name the view and the event, never a value or a body.
/// </para>
/// </summary>
public sealed partial class OwnStatsClient
{
    /// <summary>An answer is not asked for again, and is shown, for this long after it was fetched; never longer.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>
    /// A "not published" or "rebuilding" answer is asked for again after this: the pool publishes within minutes of
    /// either, and a page opened at that moment should not wait six hours for the numbers.
    /// </summary>
    internal static readonly TimeSpan PendingAge = TimeSpan.FromMinutes(10);

    /// <summary>The pool gets this long to answer; then the block is offline.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>A larger answer is not read: the block is offline.</summary>
    internal const int MaxBytes = 4 * 1024 * 1024;

    private const string Route = "/v2/analytics/records/";
    private const int MaxViewLength = 48;
    private const int MaxValueLength = 64;

    /// <summary>One fetch at a time per copy file, across every instance of the process.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// <summary>Files holding an expired copy that could not be deleted: not read again until a new answer replaces them.</summary>
    private static readonly ConcurrentDictionary<string, bool> Unreadable = new(StringComparer.Ordinal);

    private readonly HttpClient _http;
    private readonly Func<string?> _endpoint;
    private readonly TimeProvider _time;

    /// <param name="http">The app's shared client.</param>
    /// <param name="endpoint">The pool's base address right now (<see cref="PoolClient.Endpoint"/>), read at every load; null or empty: none.</param>
    /// <param name="cacheDirectory">Where the copies are kept: <c>cache/pool</c> of the data folder in the app.</param>
    /// <param name="time">The clock; the system's when null.</param>
    public OwnStatsClient(HttpClient http, Func<string?> endpoint, string cacheDirectory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);
        _http = http;
        _endpoint = endpoint;
        CacheDirectory = Path.GetFullPath(cacheDirectory);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The pool this instance reads right now, without a trailing slash; empty when none is set up or it is not plain
    /// https (no user name, query or fragment). Whoever keeps answers in memory keeps them for this address only.
    /// </summary>
    public string Address
    {
        get
        {
            var address = (_endpoint() ?? string.Empty).TrimEnd('/');
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? address : string.Empty;
        }
    }

    /// <summary>The folder of the copies, as a full path.</summary>
    internal string CacheDirectory { get; }

    /// <summary>Writes a copy whole or not at all; false when it could not. Replaced by tests of a failing disk.</summary>
    internal Func<string, string, bool> WriteFile { get; init; } = WriteAtomically;

    /// <summary>Deletes a copy; false when it is still there. Replaced by tests of a failing disk.</summary>
    internal Func<string, bool> DeleteFile { get; init; } = Delete;

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")]
    private static partial Regex ViewKey();

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,31}$")]
    private static partial Regex ParameterName();

    /// <summary>
    /// One view (<c>constructed-mulligan</c>, <c>bg-heroes</c>…) for one set of parameters, as the view takes them
    /// (<c>window</c>, <c>patch</c>, <c>fmt</c>, <c>rank</c>, <c>cls</c> or <c>arch</c>, <c>coin</c>, <c>mode</c>,
    /// <c>mmr</c>, <c>kind</c>, <c>by</c>), sent escaped and in name order. <paramref name="parseData"/> reads the
    /// view's <c>data</c> into the package's own type and copies what it keeps (the element is valid only during the
    /// call); when it throws on a shape it does not know, or returns null, the answer is not the view's and the block
    /// is offline. A view or a parameter that is not a key is not sent (<see cref="OwnStatsState.Unsupported"/>). Check
    /// <see cref="OwnStatsAnswer{T}.RulesVersion"/> against the rules the view knows before trusting the data. Until its
    /// first wait the load runs on the calling thread (a young copy is read and parsed there): a page calls it through its
    /// background work, as it does for other reads.
    /// </summary>
    public async Task<OwnStatsAnswer<T>> LoadAsync<T>(string view, IReadOnlyList<KeyValuePair<string, string>> query,
        Func<JsonElement, T> parseData, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parseData);
        var address = Address;
        if (address.Length == 0)
        {
            return Unanswered<T>(OwnStatsState.NotConfigured, view);
        }

        if (PathOf(view, query) is not { } path)
        {
            Diag.Info("Own statistics: a request that is not a key was not sent");
            return Unanswered<T>(OwnStatsState.Unsupported, view);
        }

        var url = address + path;
        var file = CopyOf(view, url);
        var gate = Gates.GetOrAdd(file, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Unanswered<T>(OwnStatsState.Offline, view);
        }

        try
        {
            if (!Unreadable.ContainsKey(file) && File.Exists(file))
            {
                if (ReadFresh(file, url, view, parseData) is { } fresh)
                {
                    return fresh;
                }

                // Past its age, from the future or unreadable: gone before the pool is asked, so nothing brings it back.
                Discard(file, view);
            }

            if (await FetchAsync(url, view, cancellationToken).ConfigureAwait(false) is not { } text)
            {
                return Unanswered<T>(OwnStatsState.Offline, view);
            }

            var fetched = _time.GetUtcNow().ToUnixTimeMilliseconds();
            if (Read(text, view, parseData, fetched) is not { } answer)
            {
                Diag.Info($"Own statistics: {view}: the answer is not the view's");
                return Unanswered<T>(OwnStatsState.Offline, view);
            }

            // Every answer of the view is kept, "no numbers" too: it takes the place of the copy with numbers.
            var stamped = "{\"fetched\":" + fetched.ToString(CultureInfo.InvariantCulture) + ",\"url\":" + JsonSerializer.Serialize(url)
                + ",\"answer\":" + text + "}";
            if (WriteFile(file, stamped))
            {
                Unreadable.TryRemove(file, out _);
                Prune(file);
            }

            return answer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up while the pool was asked: as when it gave up waiting its turn, and not logged.
            return Unanswered<T>(OwnStatsState.Offline, view);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The request's path and query, the parameters in name order (one copy whatever order they came in); null when
    /// the view or a parameter name is not a key, a name repeats, or a value is empty, longer than 64 characters or
    /// holds a control or surrogate character (no format, rank, class, archetype or patch has one).
    /// </summary>
    internal static string? PathOf(string view, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        if (view.Length > MaxViewLength || !ViewKey().IsMatch(view))
        {
            return null;
        }

        var sorted = query.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var text = new StringBuilder(Route).Append(view);
        for (var i = 0; i < sorted.Count; i++)
        {
            var (name, value) = (sorted[i].Key, sorted[i].Value);
            if (name is null || !ParameterName().IsMatch(name) || (i > 0 && string.Equals(name, sorted[i - 1].Key, StringComparison.Ordinal))
                || string.IsNullOrEmpty(value) || value.Length > MaxValueLength || value.Any(c => char.IsControl(c) || char.IsSurrogate(c)))
            {
                return null;
            }

            text.Append(i == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        return text.ToString();
    }

    private static OwnStatsAnswer<T> Unanswered<T>(OwnStatsState state, string view) where T : class =>
        new(state, view, null, null, 0, false, null, [], null, default);

    /// <summary>The copy of one request to one pool: the address and the request together pick the file.</summary>
    private string CopyOf(string view, string url) =>
        Path.Combine(CacheDirectory, "own-" + view + "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16] + ".json");

    /// <summary>
    /// The copy's answer while it is young by its own stamp (<see cref="PendingAge"/> for "numbers to come",
    /// <see cref="MaxAge"/> for the rest); null for anything else: past its age, from the future, another request's,
    /// unreadable, or no longer an answer of the view.
    /// </summary>
    private OwnStatsAnswer<T>? ReadFresh<T>(string file, string url, string view, Func<JsonElement, T> parseData) where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Int64(root, "fetched") is not { } fetched || Text(root, "url") != url
                || !root.TryGetProperty("answer", out var answer) || StateOf(answer, view) is not { } state)
            {
                return null;
            }

            var age = _time.GetUtcNow().ToUnixTimeMilliseconds() - fetched;
            var limit = state is OwnStatsState.NotPublished or OwnStatsState.Rebuilding ? PendingAge : MaxAge;
            return age >= 0 && age <= (long)limit.TotalMilliseconds ? Read(answer, view, parseData, fetched) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static OwnStatsAnswer<T>? Read<T>(string text, string view, Func<JsonElement, T> parseData, long fetched) where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Read(doc.RootElement, view, parseData, fetched);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The answer an envelope gives; null when it is not this view's: another version, source or view, no
    /// availability, or a published answer without its period, without data or with data the view cannot read.
    /// </summary>
    private static OwnStatsAnswer<T>? Read<T>(JsonElement root, string view, Func<JsonElement, T> parseData, long fetched) where T : class
    {
        if (StateOf(root, view) is not { } state)
        {
            return null;
        }

        var fetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(fetched).UtcDateTime;
        var (metric, rules, published) = (Text(root, "metric"), Text(root, "rulesVersion"), Int64(root, "published") ?? 0);
        var stale = root.TryGetProperty("stale", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (state != OwnStatsState.Ok)
        {
            return new(state, view, metric, rules, published, stale, null, WindowsOf(root), null, fetchedAt);
        }

        return PeriodOf(root) is { } period && root.TryGetProperty("data", out var data) && data.ValueKind is (JsonValueKind.Object or JsonValueKind.Array)
            && Data(data, parseData) is { } parsed
                ? new(OwnStatsState.Ok, view, metric, rules, published, stale, period, WindowsOf(root), parsed, fetchedAt)
                : null;
    }

    /// <summary>What an envelope of this view says about its numbers; null when it is not one (version, source, view, availability).</summary>
    private static OwnStatsState? StateOf(JsonElement root, string view)
    {
        if (root.ValueKind != JsonValueKind.Object || Int32(root, "v") != 2 || Text(root, "source") != "game_records" || Text(root, "view") != view
            || !root.TryGetProperty("available", out var available) || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return available.ValueKind == JsonValueKind.True ? OwnStatsState.Ok : Text(root, "reason") switch
        {
            "rebuilding" => OwnStatsState.Rebuilding,
            "no_window" => OwnStatsState.NoWindow,
            "unsupported_mode" => OwnStatsState.Unsupported,
            // "not_published", and a reason this version does not know yet: nothing to show now, asked again soon.
            _ => OwnStatsState.NotPublished,
        };
    }

    /// <summary>The view's own reading of <c>data</c>; null when it throws on a shape it does not know, or gives nothing.</summary>
    private static T? Data<T>(JsonElement data, Func<JsonElement, T> parseData) where T : class
    {
        try
        {
            return parseData(data);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException
            or OverflowException or ArgumentException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The answer's <c>period</c>; null when it is missing or not of its shape. Builds that are not all whole numbers name none.</summary>
    private static OwnPeriod? PeriodOf(JsonElement root)
    {
        if (!root.TryGetProperty("period", out var period) || period.ValueKind != JsonValueKind.Object
            || Text(period, "line") is not { } line || Text(period, "patch") is not { } patch || Text(period, "window") is not { } window
            || Int32(period, "fromDay") is not { } fromDay || Int32(period, "toDay") is not { } toDay)
        {
            return null;
        }

        int? first = null, last = null;
        if (period.TryGetProperty("builds", out var builds) && builds.ValueKind == JsonValueKind.Array)
        {
            foreach (var build in builds.EnumerateArray())
            {
                if (Count(build) is not { } number)
                {
                    (first, last) = (null, null);
                    break;
                }

                (first, last) = (Math.Min(first ?? number, number), Math.Max(last ?? number, number));
            }
        }

        return new OwnPeriod(line, patch, first, last, window, fromDay, toDay);
    }

    /// <summary>The windows the pool publishes for the view; an entry that is not <c>{patch, window}</c> is left out.</summary>
    private static List<OwnWindow> WindowsOf(JsonElement root)
    {
        var windows = new List<OwnWindow>();
        if (root.TryGetProperty("windows", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && Text(entry, "patch") is { } patch && Text(entry, "window") is { } window)
                {
                    windows.Add(new OwnWindow(patch, window));
                }
            }
        }

        return windows;
    }

    /// <summary>The answer's text, or null on a network error, a timeout, an error status or a body over <see cref="MaxBytes"/>.</summary>
    private async Task<string?> FetchAsync(string url, string view, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Timeout, _time);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Diag.Info($"Own statistics: {view}: the pool answered {(int)response.StatusCode}");
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                Diag.Info($"Own statistics: {view}: the answer is over 4 MB");
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
                    Diag.Info($"Own statistics: {view}: the answer is over 4 MB");
                    return null;
                }

                await buffer.WriteAsync(block.AsMemory(0, read), limit.Token).ConfigureAwait(false);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Diag.Info($"Own statistics: {view}: the pool did not answer ({ex.GetType().Name})");
            return null;
        }
    }

    private void Discard(string file, string view)
    {
        if (!DeleteFile(file))
        {
            Unreadable[file] = true;
            Diag.Info($"Own statistics: {view}: an expired copy could not be deleted and is ignored");
        }
    }

    /// <summary>Removes copies older than <see cref="MaxAge"/> by file time but the one just kept; one whose load is running is left to it.</summary>
    private void Prune(string kept)
    {
        var oldest = _time.GetUtcNow().UtcDateTime - MaxAge;
        try
        {
            foreach (var file in Directory.EnumerateFiles(CacheDirectory, "own-*.json"))
            {
                var gate = Gates.GetOrAdd(file, static _ => new SemaphoreSlim(1, 1));
                if (string.Equals(file, kept, StringComparison.Ordinal) || !gate.Wait(0))
                {
                    continue;
                }

                try
                {
                    if (File.GetLastWriteTimeUtc(file) < oldest)
                    {
                        DeleteFile(file);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next kept answer tries again.
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
            Diag.Info($"Own statistics: an answer was not kept ({ex.GetType().Name})");
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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static long? Int64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
}

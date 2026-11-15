using System.Text.Json;
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
    public static Task<OwnBgHeroesResult> LoadAsync(HttpClient http, string endpoint, string? cacheDirectory = null, TimeProvider? time = null,
        CancellationToken cancellationToken = default) =>
        LoadAsync(http, endpoint, cacheDirectory, time, OwnBgHeroesFiles.Shared, cancellationToken);

    /// <summary>
    /// The load itself. <paramref name="files"/> writes and deletes the files and keeps what the process knows (the
    /// withdrawals, the order of the requests); tests pass their own (a new one is "the app started again").
    ///
    /// <para><b>Withdrawn numbers are never shown again.</b> When the pool answers "not available" (not published,
    /// rebuilding, unsupported), every copy with numbers is dead, whatever happens to the files afterwards and
    /// whatever the clock says: no time is compared. The ladder, each step taken only when the one before failed:</para>
    /// <list type="number">
    /// <item>the withdrawal is remembered in the process, before any file is touched;</item>
    /// <item>the copy is replaced by the answer (then no numbers are left on disk);</item>
    /// <item>a marker file is written beside the copy: while it is there no copy is read at all, in this run or
    /// the next;</item>
    /// <item>the copy is deleted;</item>
    /// <item>nothing could be written: the process still refuses the copy, and every later load writes the marker
    /// again until it succeeds.</item>
    /// </list>
    /// <para>A marker that exists and cannot be read counts as a marker. Only a new publication takes it away, and
    /// only after that publication is on disk: the copy first, the marker second, so a failure between the two
    /// hides good numbers and never shows withdrawn ones.</para>
    ///
    /// <para><b>Answers are applied in the order their requests started.</b> Each load takes a number when it begins;
    /// the files and the withdrawal are changed under one gate per copy, and an answer whose request began before
    /// the one applied last changes nothing: that load returns what the later one found. So a slow answer with
    /// numbers, sent before a withdrawal, cannot undo it. The request itself runs outside the gate, and the gate
    /// is released on every way out, cancellation included.</para>
    /// </summary>
    internal static async Task<OwnBgHeroesResult> LoadAsync(HttpClient http, string endpoint, string? cacheDirectory, TimeProvider? time,
        OwnBgHeroesFiles files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured);
        }

        endpoint = endpoint.TrimEnd('/');
        var file = Path.Combine(cacheDirectory ?? Path.Combine(AppPaths.Cache, "api"), CacheName);
        // Whole milliseconds, as the copy on disk keeps the time: a fresh answer and its copy carry the same date.
        var now = DateTimeOffset.FromUnixTimeMilliseconds((time ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds()).UtcDateTime;
        var request = files.Begin(file);
        try
        {
            using (await files.EnterAsync(file, cancellationToken).ConfigureAwait(false))
            {
                // The clock decides only whether to ask again (a copy from the future is not young), never whether
                // a copy may be shown.
                if (await UsableCopyAsync(file, endpoint, files, cancellationToken).ConfigureAwait(false) is { } young
                    && now >= young.FetchedAt && now - young.FetchedAt < MaxAge)
                {
                    return OwnBgHeroesResult.From(young.Snapshot, young.FetchedAt);
                }
            }

            var answer = await AnalyticsClient.FetchBgHeroesAsync(http, endpoint, cancellationToken).ConfigureAwait(false);

            using (await files.EnterAsync(file, cancellationToken).ConfigureAwait(false))
            {
                if (answer is { } got)
                {
                    if (files.Superseded(file, request) is { } later)
                    {
                        return later;
                    }

                    var result = OwnBgHeroesResult.From(got.Snapshot, now);
                    files.Applied(file, request, result);
                    var envelope = Envelope(endpoint, now, got.Body);
                    if (got.Snapshot.Available)
                    {
                        // A new publication: the copy first, the marker only once the copy is on disk.
                        if (await files.Write(file, envelope, cancellationToken).ConfigureAwait(false) && files.Delete(MarkerOf(file)))
                        {
                            files.Forget(file);
                        }
                    }
                    else
                    {
                        await WithdrawAsync(file, endpoint, now, envelope, files, cancellationToken).ConfigureAwait(false);
                    }

                    return result;
                }

                // No answer. An earlier copy with numbers is shown with its own date, read again now: another load
                // may have withdrawn it while this one waited. An earlier "nothing yet" says nothing about today.
                return await UsableCopyAsync(file, endpoint, files, cancellationToken).ConfigureAwait(false) is { Snapshot.Available: true } old
                    ? OwnBgHeroesResult.From(old.Snapshot, old.FetchedAt, fromOldCopy: true)
                    : OwnBgHeroesResult.Of(OwnBgHeroesState.Offline);
            }
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

    internal static string MarkerOf(string file) => Path.ChangeExtension(file, ".revoked");

    /// <summary>The copy, unless the pool withdrew its numbers: then no copy is read at all. Called under the gate.</summary>
    private static async Task<(BgHeroAnalytics Snapshot, DateTime FetchedAt)?> UsableCopyAsync(string file, string endpoint, OwnBgHeroesFiles files,
        CancellationToken cancellationToken) =>
        await WithdrawnAsync(file, endpoint, files, cancellationToken).ConfigureAwait(false)
            ? null
            : await ReadCopyAsync(file, endpoint, cancellationToken).ConfigureAwait(false);

    /// <summary>The pool withdrew its numbers: the ladder described on the load. Called under the gate.</summary>
    private static async Task WithdrawAsync(string file, string endpoint, DateTime now, string envelope, OwnBgHeroesFiles files, CancellationToken cancellationToken)
    {
        files.Remember(file);
        try
        {
            if (await files.Write(file, envelope, cancellationToken).ConfigureAwait(false))
            {
                // No numbers are left on disk; a marker of an earlier withdrawal has nothing more to guard.
                if (files.Delete(MarkerOf(file)))
                {
                    files.Forget(file);
                }

                return;
            }

            if (await files.Write(MarkerOf(file), Marker(endpoint, now), cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!files.Delete(file))
            {
                Diag.Info("Own Battlegrounds hero statistics: withdrawn numbers could not be removed from disk; they stay hidden and the marker is written again on the next load");
            }
        }
        catch (OperationCanceledException)
        {
            // Remembered in the process; the next load writes the marker.
        }
    }

    /// <summary>
    /// Whether a withdrawal stands: the process remembers one, or the marker file is there (this pool's, or one that
    /// cannot be read). A withdrawal the process knows and the disk does not is written again here.
    /// </summary>
    private static async Task<bool> WithdrawnAsync(string file, string endpoint, OwnBgHeroesFiles files, CancellationToken cancellationToken)
    {
        var marker = MarkerOf(file);
        var onDisk = false;
        if (File.Exists(marker))
        {
            try
            {
                var text = await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(text);
                // The marker of another pool says nothing about this one (the copy it guards is not read for this pool anyway).
                onDisk = string.Equals(doc.RootElement.GetProperty("endpoint").GetString(), endpoint, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // There was a withdrawal and whose it was is lost: no copy is trusted.
                Diag.Error("Own Battlegrounds hero statistics: the withdrawal marker is unreadable", ex);
                onDisk = true;
            }
        }

        if (!files.Remembered(file))
        {
            return onDisk;
        }

        if (!onDisk)
        {
            try
            {
                await files.Write(marker, Marker(endpoint, DateTime.UtcNow), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The process still knows; the next load tries again.
            }
        }

        return true;
    }

    /// <summary>The marker's text: whose withdrawal it is; the time is for a reader of the file, no decision reads it.</summary>
    private static string Marker(string endpoint, DateTime revokedAt) =>
        "{\"endpoint\":" + JsonSerializer.Serialize(endpoint) + ",\"revoked\":" +
        new DateTimeOffset(revokedAt).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

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

/// <summary>
/// The file operations of <see cref="OwnBgHeroes"/> and what this process knows about each copy: the withdrawals, the
/// order in which the requests started, the gate under which the files change. The app uses <see cref="Shared"/>; a
/// test passes its own to make a write fail or to stand for a new start of the app.
/// </summary>
internal sealed class OwnBgHeroesFiles
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Copy> _copies = new(StringComparer.OrdinalIgnoreCase);

    public static OwnBgHeroesFiles Shared { get; } = new();

    /// <summary>Writes a file whole (through a temporary file); false when it could not be written.</summary>
    public Func<string, string, CancellationToken, Task<bool>> Write { get; init; } = WriteWholeAsync;

    /// <summary>Deletes a file; true when it is gone afterwards (or was never there).</summary>
    public Func<string, bool> Delete { get; init; } = DeleteFile;

    /// <summary>The number of a load that begins now: later loads get greater ones.</summary>
    public long Begin(string file) => Interlocked.Increment(ref Of(file).Started);

    /// <summary>
    /// Waits for the gate of the copy; dispose the result to leave. A cancelled wait never held the gate, and a
    /// holder leaves it through <c>using</c> whatever happens inside.
    /// </summary>
    public async Task<IDisposable> EnterAsync(string file, CancellationToken cancellationToken)
    {
        var gate = Of(file).Gate;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Leave(gate);
    }

    /// <summary>Under the gate: what a load that began later already found, when its answer was applied first; else null.</summary>
    public OwnBgHeroesResult? Superseded(string file, long request)
    {
        var copy = Of(file);
        return copy.AppliedRequest > request ? copy.AppliedResult : null;
    }

    /// <summary>Under the gate: the answer of <paramref name="request"/> is the one in force.</summary>
    public void Applied(string file, long request, OwnBgHeroesResult result)
    {
        var copy = Of(file);
        (copy.AppliedRequest, copy.AppliedResult) = (request, result);
    }

    public void Remember(string file) => Of(file).Withdrawn = true;

    public bool Remembered(string file) => Of(file).Withdrawn;

    public void Forget(string file) => Of(file).Withdrawn = false;

    private Copy Of(string file) => _copies.GetOrAdd(file, static _ => new Copy());

    /// <summary>One copy file as the process knows it. The gates live as long as the process: there is one per copy.</summary>
    private sealed class Copy
    {
        public long Started;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public long AppliedRequest { get; set; }

        public OwnBgHeroesResult? AppliedResult { get; set; }

        public volatile bool Withdrawn;
    }

    private sealed class Leave(SemaphoreSlim gate) : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The gate belongs to the copy and lives as long as the process; leaving releases it, it does not dispose it.")]
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    private static async Task<bool> WriteWholeAsync(string file, string text, CancellationToken cancellationToken)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(temp, text, cancellationToken).ConfigureAwait(false);
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Error($"Own Battlegrounds hero statistics: {Path.GetFileName(file)} not written", ex);
            return false;
        }
        finally
        {
            // A cancelled or failed write leaves no temporary file behind (after the move there is none).
            DeleteFile(temp);
        }
    }

    private static bool DeleteFile(string file)
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
}

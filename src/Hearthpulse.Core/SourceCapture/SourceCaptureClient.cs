using System.Collections.Frozen;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>Strict receipt validation. No status code alone acknowledges local source or completed deletion.</summary>
public sealed class SourceCaptureClient(HttpClient http)
{
    /// <summary>
    /// Source journals follow the game records switch: on by default, off once the player turned game records off
    /// (<see cref="AppSettings.GameStatsOff"/>), and only with a configured endpoint.
    /// </summary>
    public static bool Sharing(AppSettings settings) => !settings.GameStatsOff && Hearthpulse.Core.Pool.PoolClient.Available(settings);

    /// <summary>Reads technical processing status only; source contents and arbitrary server fields never escape.</summary>
    public async Task<SourceCaptureRemoteStatus?> GetStatusAsync(string endpoint, string iid, string generation, string captureId, CancellationToken cancellationToken = default)
    {
        if (!SourceCaptureTokens.Hex(iid, 32) || !SourceCaptureTokens.Hex(generation, 32) || !SourceCaptureTokens.Hex(captureId, 32))
            throw new ArgumentException("Invalid capture identity.", nameof(captureId));
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Invalid capture endpoint.", nameof(endpoint));
        SourceCaptureRemoteStatus? result = null;
        var answer = await PostAsync(endpoint, "status", new { v = 1, iid, generation, captureId }, root =>
        {
            if (!root.TryGetProperty("captureId", out var id) || id.GetString() != captureId
                || !root.TryGetProperty("status", out var status) || status.GetString() is not ("stored" or "processing" or "parsed" or "partial" or "failed" or "deleted")
                || !Count(root, "records", out var records) || !Count(root, "unsupported", out var unsupported) || !Count(root, "gaps", out var gaps)
                || !root.TryGetProperty("parserVersion", out var parser)) return false;
            var version = parser.ValueKind == JsonValueKind.Null ? null : parser.GetString();
            if (version is not null && (version.Length is 0 or > 80 || !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))) return false;
            result = new(status.GetString()!, version, records, unsupported, gaps);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return answer.Accepted ? result : null;
    }

    private static bool Count(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var number) && number.TryGetInt32(out value) && value >= 0;
    }

    /// <summary>Posts exact persisted bytes, compressed off the caller thread by the spool sender.</summary>
    internal async Task<SourceCaptureAnswer> ChunkAsync(string endpoint, SourceCaptureManifest manifest, SourceCaptureChunk chunk, byte[] bytes, CancellationToken token)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) await gzip.WriteAsync(bytes, token).ConfigureAwait(false);
        return await PostAsync(endpoint, "chunks", new
        {
            v = 1, manifest.Iid, manifest.Generation, manifest.CaptureId, chunk.Index, chunk.Sha256,
            encoding = "gzip", chunk.UncompressedBytes, data = Convert.ToBase64String(compressed.ToArray()),
        }, root => True(root, "stored") && root.TryGetProperty("index", out var index) && index.TryGetInt32(out var value) && value == chunk.Index
            && root.TryGetProperty("sha256", out var hash) && hash.GetString() == chunk.Sha256, token).ConfigureAwait(false);
    }

    internal Task<SourceCaptureAnswer> CompleteAsync(string endpoint, SourceCaptureManifest manifest, CancellationToken token) =>
        PostAsync(endpoint, "complete", new
        {
            v = 1, manifest.Iid, manifest.Generation, manifest.CaptureId, manifest.CaptureKind, manifest.Mode, manifest.Day,
            manifest.SourceVersion, manifest.CaptureStatus, chunks = manifest.Chunks.Select(c => new { c.Index, c.Sha256, c.UncompressedBytes }), manifest.Gaps,
        }, root => root.TryGetProperty("status", out var status) && status.GetString() == "stored"
            && root.TryGetProperty("captureId", out var id) && id.GetString() == manifest.CaptureId, token);

    internal Task<SourceCaptureAnswer> ForgetAsync(SourceCaptureRevocation revocation, CancellationToken token) =>
        PostAsync(revocation.Endpoint, "forget", new { v = 1, revocation.Iid, revocation.Generation },
            root => root.TryGetProperty("status", out var status) && status.GetString() == "deleted", token, deletion: true);

    private async Task<SourceCaptureAnswer> PostAsync(string endpoint, string route, object body, Func<JsonElement, bool> receipt, CancellationToken token, bool deletion = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/v1/captures/" + route);
            request.Content = JsonContent.Create(body, options: SourceCaptureRecord.Json);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, (int)response.StatusCode is 400 or 409 or 410 or 413 ? "rejected" : "offline", Retry(response),
                    (int)response.StatusCode == 400 ? await ErrorCodeAsync(response, token).ConfigureAwait(false) : null);
            // A hostile/misconfigured endpoint cannot force an unbounded response allocation.
            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(block, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 64 * 1024) return new(false, "invalid_receipt", TimeSpan.Zero);
                await buffer.WriteAsync(block.AsMemory(0, read), token).ConfigureAwait(false);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var root = json.RootElement;
            var accepted = True(root, "ok") && receipt(root) && (!deletion || (int)response.StatusCode == 200);
            return new(accepted, accepted ? "stored" : deletion ? "deleting" : "invalid_receipt", Retry(response));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException
            || (ex is OperationCanceledException && !token.IsCancellationRequested))
        {
            return new(false, "offline", TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// The pool's codes for a journal it will never take as it is (server/pool/node/validation.mjs): its records, their
    /// order, the manifest against them. Refusals of an envelope, an identity, a limit or a conflict keep it waiting.
    /// </summary>
    internal static readonly FrozenSet<string> JournalRefusals = new[]
    {
        "invalid_record", "invalid_jsonl", "invalid_utf8", "duplicate_json_key", "json_depth", "mixed_source_version",
        "invalid_sequence", "invalid_combat_view", "invalid_combat", "invalid_blocks", "invalid_choice_progression",
        "concealed_reference_reused", "unobserved_opponent_tag", "manifest_source_mismatch", "incomplete_stream",
        "draft_result", "invalid_manifest",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The pool's fixed error code of a refusal (<c>{"error":"invalid_record"}</c>), or null; never other text.</summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[4096];
        int length = 0, read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false)) > 0) length += read;
        try
        {
            using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String && error.GetString() is { Length: > 0 and <= 40 } code && code.All(c => c is >= 'a' and <= 'z' or '_')
                ? code : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool True(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static TimeSpan Retry(HttpResponseMessage response) => response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date ? TimeSpan.FromSeconds(Math.Clamp((date - DateTimeOffset.UtcNow).TotalSeconds, 1, 3600)) : TimeSpan.FromSeconds(30));
}

/// <param name="Code">The pool's error code of a 400 refusal, when it gave one (see <c>ErrorCodeAsync</c>).</param>
internal sealed record SourceCaptureAnswer(bool Accepted, string Status, TimeSpan RetryAfter, string? Code = null)
{
    /// <summary>A 400 for the journal itself (<see cref="SourceCaptureClient.JournalRefusals"/>): it is never taken as it is.</summary>
    public bool Final => Code is not null && SourceCaptureClient.JournalRefusals.Contains(Code);
}
/// <summary>Aggregate processing status without source records or installation identifiers.</summary>
public sealed record SourceCaptureRemoteStatus(string Status, string? ParserVersion, int Records, int Unsupported, int Gaps);
internal sealed record SourceCaptureRevocation(string Iid, string Generation, string Endpoint);
internal sealed record SourceCaptureChunk(int Index, string Sha256, int UncompressedBytes, bool Acknowledged = false);
/// <summary>A spool manifest. SourceVersion is absent from manifests written before v2 and then reads as 1.</summary>
internal sealed record SourceCaptureManifest(string Iid, string Generation, string CaptureId, string CaptureKind, string Mode, int Day,
    string CaptureStatus, int Gaps, bool Finalized, List<SourceCaptureChunk> Chunks, int SourceVersion = 1);

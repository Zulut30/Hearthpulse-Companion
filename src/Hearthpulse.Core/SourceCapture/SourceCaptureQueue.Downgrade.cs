using System.Text;
using System.Text.Json.Nodes;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>
/// A pool that predates a source version refuses every line of it as an unknown record: the spool sends such a journal one
/// version older, and once the pool takes it, the next ones too until the app restarts: v4 as v3 (docs/source-capture.md,
/// "Source v4"), v3 as v2 without their <c>bg_combat</c> records ("Battlegrounds fights").
/// </summary>
public sealed partial class SourceCaptureQueue
{
    // In memory only: after the next start the pool is asked with the current version again (it may have been updated meanwhile).
    private volatile int _poolVersion = SourceCaptureRecord.CurrentVersion;
    // The newest version the pool took a chunk of in this session: a line refused in it is the journal's fault, not the version's.
    private volatile int _taken;

    /// <summary>
    /// The first chunk of an unsent journal newer than v2, and newer than any version the pool took in this session, was
    /// refused as an unknown record: the pool may not know its version, so the journal is tried one version older. Any
    /// other refusal is about the journal itself (<see cref="RefusedAsync"/>).
    /// </summary>
    private bool UnknownVersion(SourceCaptureManifest capture, SourceCaptureAnswer answer) =>
        answer.Code == "invalid_record" && capture.SourceVersion > Math.Max(2, _taken)
        && _manifests.TryGetValue(capture.CaptureId, out var current) && current.Chunks.TrueForAll(c => !c.Acknowledged);

    /// <summary>
    /// The pool took a chunk in <paramref name="version"/>. When the journal was tried older after a refusal
    /// (<paramref name="stepped"/>), the pool does not know the newer version: the next journals go in this one until the
    /// app restarts, with one log line. A journal refused in every version leaves the version where it was.
    /// </summary>
    private void Taken(int version, bool stepped)
    {
        if (version > _taken) _taken = version;
        if (!stepped || version >= _poolVersion) return;
        _poolVersion = version;
        Diag.Info(version == 2
            ? "Source capture: the pool does not take source v3 yet, journals go as v2 without fights until the app restarts"
            : $"Source capture: the pool does not take source v{version + 1} yet, journals go as v{version} until the app restarts");
    }

    /// <summary>The journal rewritten in <paramref name="version"/> under the disk gate; null when it is no longer this spool's to send.</summary>
    private async Task<SourceCaptureManifest?> DowngradeAsync(SourceCaptureManifest capture, int version, CancellationToken token)
    {
        await _disk.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!CanSend(capture) || !_manifests.TryGetValue(capture.CaptureId, out var current)) return null;
            if (current.SourceVersion <= version) return current;
            if (current.Chunks.Exists(c => c.Acknowledged)) return null;
            Rewrite(current, version);
            return _manifests[capture.CaptureId];
        }
        finally { _disk.Release(); }
    }

    /// <summary>
    /// Writes the journal again in an older wire version: to v3 a v4 journal gets back what v3 counts as a loss and v4
    /// does not (<see cref="AsV3"/>); to v2 the <c>bg_combat</c> records are dropped. The records are renumbered and marked
    /// with the version, then the chunks and the manifest are written again. Every other byte of a record stays as the
    /// recorder wrote it.
    /// </summary>
    private void Rewrite(SourceCaptureManifest manifest, int version)
    {
        var records = new List<JsonObject>();
        foreach (var path in ChunkFiles(manifest.CaptureId))
        {
            foreach (var line in Encoding.UTF8.GetString(ReadChunk(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                records.Add(JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Invalid source line."));
        }

        if (manifest.SourceVersion >= 4) AsV3(records);
        var lines = new List<byte[]>();
        var seq = 0;
        foreach (var record in records)
        {
            if (version < 3 && (string?)record["kind"] == "bg_combat") continue;
            record["v"] = version;
            record["seq"] = seq++;
            lines.Add(Encoding.UTF8.GetBytes(record.ToJsonString() + "\n"));
        }

        foreach (var path in ChunkFiles(manifest.CaptureId)) File.Delete(path);
        var index = 0;
        long size = 0;
        FileStream? file = null;
        try
        {
            foreach (var bytes in lines)
            {
                if (file is null || size + bytes.Length > TargetChunkBytes)
                {
                    file?.Flush(flushToDisk: true);
                    file?.Dispose();
                    file = new FileStream(ChunkPath(manifest.CaptureId, index++), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    size = 0;
                }

                file.Write(bytes);
                size += bytes.Length;
            }

            file?.Flush(flushToDisk: true);
        }
        finally { file?.Dispose(); }
        FinalizeCapture(manifest with { SourceVersion = version });
    }

    /// <summary>
    /// What v3 counts as a loss and v4 does not: every Battlegrounds game names the <c>combat_view</c> gap in its header,
    /// and an OTHER block is an unsupported observation. A journal that now holds either ends incomplete, as the v3
    /// recorder sealed it.
    /// </summary>
    private static void AsV3(List<JsonObject> records)
    {
        if (records.Count == 0 || (string?)records[0]["kind"] != "start") return;
        var start = records[0]["data"];
        var battlegrounds = (string?)start?["captureKind"] == "game" && (string?)start?["mode"] is "bg" or "duos";
        var other = records.Exists(r => (string?)r["kind"] == "block_start" && (string?)r["data"]?["block"] == "OTHER");
        if (battlegrounds)
            records.Insert(1, JsonNode.Parse(new SourceCaptureRecord(1, "gap", 0, "none", 0, null, 0, new { reason = "combat_view", count = 1 }, 3).ToJson())!.AsObject());
        if ((battlegrounds || other) && (string?)records[^1]["kind"] == "end" && records[^1]["data"] is JsonObject end) end["status"] = "incomplete";
    }
}

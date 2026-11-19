using System.Text;
using System.Text.Json.Nodes;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>
/// A pool that predates source v3 (docs/source-capture.md, "Battlegrounds fights") refuses every v3 line as an unknown
/// record. Rather than hold the queue until it is updated, the spool sends such journals as v2 without their
/// <c>bg_combat</c> records, and keeps doing so until the app restarts.
/// </summary>
public sealed partial class SourceCaptureQueue
{
    // In memory only: after the next start the pool is asked with v3 again (it may have been updated meanwhile).
    private volatile bool _v2Only;

    /// <summary>
    /// The first chunk of an unsent v3 journal was refused as an unknown record: the pool does not know v3. Any other
    /// refusal keeps the journal waiting, as before.
    /// </summary>
    private bool UnknownV3(SourceCaptureManifest capture, SourceCaptureAnswer answer) =>
        capture.SourceVersion == 3 && answer.Code == "invalid_record"
        && _manifests.TryGetValue(capture.CaptureId, out var current) && current.Chunks.TrueForAll(c => !c.Acknowledged);

    /// <summary>The journal rewritten as v2 under the disk gate; null when it is no longer this spool's to send.</summary>
    private async Task<SourceCaptureManifest?> DowngradeAsync(SourceCaptureManifest capture, CancellationToken token)
    {
        await _disk.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!CanSend(capture) || !_manifests.TryGetValue(capture.CaptureId, out var current)) return null;
            if (current.SourceVersion != 3) return current;
            if (current.Chunks.Exists(c => c.Acknowledged)) return null;
            RewriteAsV2(current);
            return _manifests[capture.CaptureId];
        }
        finally { _disk.Release(); }
    }

    /// <summary>
    /// Drops the <c>bg_combat</c> records, renumbers the others and marks them v:2, then writes the chunks and the manifest
    /// again. Every other byte of a record stays as the recorder wrote it.
    /// </summary>
    private void RewriteAsV2(SourceCaptureManifest manifest)
    {
        var lines = new List<byte[]>();
        var seq = 0;
        foreach (var path in ChunkFiles(manifest.CaptureId))
        {
            foreach (var line in Encoding.UTF8.GetString(ReadChunk(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var record = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Invalid source line.");
                if ((string?)record["kind"] == "bg_combat") continue;
                record["v"] = 2;
                record["seq"] = seq++;
                lines.Add(Encoding.UTF8.GetBytes(record.ToJsonString() + "\n"));
            }
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
        FinalizeCapture(manifest with { SourceVersion = 2 });
    }
}

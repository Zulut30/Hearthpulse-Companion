using System.Text.Json;
using Hearthpulse.Core.Pool;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>What the source spools hold for pools other than the current one: kept, never sent to the current pool.</summary>
public sealed record SourceCaptureHeld(int Captures, int Deletions);

/// <summary>
/// The layout of the <c>source-capture/</c> folder: one spool per pool address in
/// <c>&lt;hash of the address&gt;/</c>, and <c>unknown-*</c> for a spool whose address could not be read (never sent).
/// These helpers work on folders no <see cref="SourceCaptureQueue"/> has open.
/// </summary>
public sealed partial class SourceCaptureQueue
{
    private const string UnknownPrefix = "unknown-";
    private const string StoreFile = "state.json";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    /// <summary>The spool folder of a pool address (<see cref="PoolDestination.Normalize"/>) under <paramref name="root"/>.</summary>
    public static string Folder(string root, string destination) => Path.Combine(root, PoolDestination.Key(destination));

    /// <summary>
    /// Moves a spool written before spools had a folder per address (state and sources directly in
    /// <paramref name="root"/>). Its state names the pool its generation was made for, so the sources go to that pool's
    /// folder, and every deletion request to the folder of the pool it is addressed to. Unreadable state means an
    /// unknown pool: the spool is set aside and never sent.
    /// </summary>
    internal static void MigrateLegacy(string root)
    {
        if (!Directory.Exists(root)) return;
        var statePath = Path.Combine(root, StoreFile);
        var captures = CaptureFolders(root);
        if (!File.Exists(statePath) && captures.Count == 0) return;
        try
        {
            StoreState? store = null;
            try
            {
                if (File.Exists(statePath)) store = Read<StoreState>(statePath);
                if (store is not null && store.Revocations is null) throw new InvalidDataException("Invalid spool metadata.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                var aside = Path.Combine(root, UnknownPrefix + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(aside);
                File.Move(statePath, Path.Combine(aside, StoreFile), overwrite: true);
                foreach (var capture in captures) Directory.Move(capture, Path.Combine(aside, Path.GetFileName(capture)));
                Diag.Info($"Source capture: earlier queue has unreadable state, set aside, {captures.Count} capture(s) not sent");
                return;
            }

            var own = PoolDestination.Normalize(store?.Endpoint);
            var revocations = store?.Revocations ?? [];
            var undeliverable = 0;
            foreach (var group in revocations.GroupBy(r => PoolDestination.Normalize(r.Endpoint), StringComparer.Ordinal))
            {
                if (group.Key is not { } destination) { undeliverable += group.Count(); continue; }
                if (destination == own) continue;
                // The pool was left by an opt-out: its folder keeps the install key, so returning to it needs an explicit enable.
                Merge(Folder(root, destination), new(group.First().Iid, null, destination, false, 0, []), group);
            }

            if (own is not null)
            {
                var folder = Folder(root, own);
                Merge(folder, store! with { Endpoint = own, Revocations = [] }, revocations.Where(r => PoolDestination.Normalize(r.Endpoint) == own));
                foreach (var capture in captures)
                {
                    var target = Path.Combine(folder, Path.GetFileName(capture));
                    if (Directory.Exists(target)) Directory.Delete(capture, recursive: true);
                    else Directory.Move(capture, target);
                }
            }
            else
            {
                // No generation was ever made: these sources belong to none and the old loader deleted them too.
                foreach (var capture in captures) Directory.Delete(capture, recursive: true);
            }

            File.Delete(statePath);
            Diag.Info(own is not null
                ? $"Source capture: earlier queue kept for its own pool, {captures.Count} capture(s), {revocations.Count - undeliverable} deletion request(s), {undeliverable} without a pool dropped"
                : $"Source capture: earlier queue had no pool, {captures.Count} capture(s) deleted, {revocations.Count - undeliverable} deletion request(s) kept, {undeliverable} without a pool dropped");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // What stays directly in the root is no spool any more: it is not sent, and the next start tries again.
            Diag.Info($"Source capture: earlier queue could not be moved, not sent ({ex.GetType().Name})");
        }
    }

    /// <summary>Writes a spool's state, keeping the state and deletion requests an earlier attempt already left there.</summary>
    private static void Merge(string folder, StoreState fresh, IEnumerable<SourceCaptureRevocation> revocations)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, StoreFile);
        var state = File.Exists(path) ? Read<StoreState>(path) : fresh;
        Atomic(path, state with { Revocations = (state.Revocations ?? []).Union(revocations).ToList() });
    }

    /// <summary>What waits for pools other than <paramref name="current"/> (null: no pool is set up now).</summary>
    internal static SourceCaptureHeld Held(string root, string? current)
    {
        var captures = 0;
        var deletions = 0;
        foreach (var folder in OtherFolders(root, current, unknown: true))
        {
            try
            {
                captures += CaptureFolders(folder).Count;
                var path = Path.Combine(folder, StoreFile);
                if (PoolDestination.IsKey(Path.GetFileName(folder)) && File.Exists(path)) deletions += Read<StoreState>(path).Revocations?.Count ?? 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // Unreadable state holds no deliverable request; its sources were counted above.
            }
        }

        return new(captures, deletions);
    }

    /// <summary>
    /// Applies the 14 day bound to sources kept for other pools (an open spool prunes its own): returns how many went.
    /// A folder set aside without an address goes with its last source.
    /// </summary>
    internal static int SweepOthers(string root, string? current, TimeProvider time)
    {
        var expired = 0;
        foreach (var folder in OtherFolders(root, current, unknown: true))
        {
            try
            {
                foreach (var capture in CaptureFolders(folder))
                {
                    if (time.GetUtcNow() - new DateTimeOffset(Directory.GetCreationTimeUtc(capture)) <= MaxAge) continue;
                    Directory.Delete(capture, recursive: true);
                    expired++;
                }

                if (!PoolDestination.IsKey(Path.GetFileName(folder)) && CaptureFolders(folder).Count == 0) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"Source capture: a queue kept for another pool could not be trimmed ({ex.GetType().Name})");
            }
        }

        if (expired > 0) Diag.Info($"Source capture: dropped {expired} expired capture(s) kept for another pool");
        return expired;
    }

    /// <summary>Spool folders other than the current pool's: other addresses, and with <paramref name="unknown"/> what was set aside.</summary>
    internal static List<string> OtherFolders(string root, string? current, bool unknown)
    {
        if (!Directory.Exists(root)) return [];
        var own = current is null ? null : PoolDestination.Key(current);
        return Directory.GetDirectories(root)
            .Where(d => Path.GetFileName(d) is { } name && name != own
                && (PoolDestination.IsKey(name) || unknown && name.StartsWith(UnknownPrefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>Deletes the sources of a spool folder whose state cannot be read (no deletion request can be made for it).</summary>
    internal static int DeleteSources(string folder)
    {
        var captures = CaptureFolders(folder);
        foreach (var capture in captures) Directory.Delete(capture, recursive: true);
        return captures.Count;
    }

    private static List<string> CaptureFolders(string folder) => Directory.Exists(folder)
        ? Directory.GetDirectories(folder).Where(d => SourceCaptureTokens.Hex(Path.GetFileName(d), 32)).ToList()
        : [];
}

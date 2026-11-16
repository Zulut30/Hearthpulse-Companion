using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthpulse.Core.Pool;

namespace Hearthpulse.Core.GameStats;

/// <summary>What the game record queues hold for pools other than the current one: kept, never sent to the current pool.</summary>
public readonly record struct GameStatsHeld(int Records, int Deletions);

/// <summary>
/// The game record queues of the data folder (<c>game-stats/</c>), one per pool address: a record or a deletion
/// request queued for one pool waits in that pool's folder (<c>game-stats/&lt;hash of the address&gt;/</c>) and is sent
/// only there, also after the address in the settings changed or the app restarted. A queue whose address cannot be
/// read is set aside (<c>unknown-*</c>) and never sent. docs/game-stats.md, "Queue destination".
/// </summary>
public sealed class GameStatsQueues
{
    private const string UnknownPrefix = "unknown-";
    private readonly string _root;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, GameStatsQueue> _open = new(StringComparer.Ordinal);

    public GameStatsQueues(string root) => _root = root;

    /// <summary>UTC clock; tests move it.</summary>
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// The queue of one pool address (<see cref="PoolDestination.Normalize"/>). Null when its folder holds something
    /// with an unreadable address that could not be set aside: then nothing is queued or sent for it.
    /// </summary>
    public GameStatsQueue? For(string destination)
    {
        lock (_lock)
        {
            var folder = Path.Combine(_root, PoolDestination.Key(destination));
            try
            {
                if (Directory.Exists(folder) && !Owned(folder, destination))
                {
                    _open.Remove(destination);
                    var (records, deletions) = SetAside(folder);
                    Diag.Info($"Game stats: queue with an unreadable destination set aside, {records} record(s) not sent, {deletions} deletion request(s) dropped");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"Game stats: queue with an unreadable destination stays unsent ({ex.GetType().Name})");
                return null;
            }

            if (!_open.TryGetValue(destination, out var queue))
            {
                _open[destination] = queue = new GameStatsQueue(folder, destination, _lock) { UtcNow = UtcNow };
            }

            return queue;
        }
    }

    /// <summary>
    /// Moves a queue written before queues had a destination (files directly in <c>game-stats/</c>). It may have been
    /// filled for any pool, so it goes to <paramref name="explicitDestination"/>, the address the settings name by
    /// themselves, and with none it is set aside and never sent: the default pool did not exist when it was filled.
    /// </summary>
    public void MigrateLegacy(string? explicitDestination)
    {
        lock (_lock)
        {
            var records = GameStatsQueue.RecordFiles(_root).Select(f => f.FullName).ToList();
            var forgetFile = Path.Combine(_root, GameStatsQueue.ForgetFile);
            var forget = File.Exists(forgetFile);
            if (records.Count == 0 && !forget)
            {
                return;
            }

            var deletions = forget ? 1 : 0;
            try
            {
                if (explicitDestination is not null && For(explicitDestination) is { } queue)
                {
                    queue.Adopt(records, forget);
                    File.Delete(forgetFile);
                    Diag.Info($"Game stats: earlier queue kept for the pool named in the settings, {records.Count} record(s), {deletions} deletion request(s)");
                    return;
                }

                var aside = Path.Combine(_root, UnknownPrefix + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(aside);
                foreach (var file in records)
                {
                    File.Move(file, Path.Combine(aside, Path.GetFileName(file)), overwrite: true);
                }

                File.Delete(forgetFile);
                Diag.Info($"Game stats: earlier queue has no known pool, set aside, {records.Count} record(s) not sent, {deletions} deletion request(s) dropped");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // What stays directly in game-stats/ is no queue any more: it is not sent, and the next start tries again.
                Diag.Info($"Game stats: earlier queue could not be moved, not sent ({ex.GetType().Name})");
            }
        }
    }

    /// <summary>What waits for pools other than <paramref name="current"/> (null: no pool is set up now).</summary>
    public GameStatsHeld Held(string? current)
    {
        lock (_lock)
        {
            var records = 0;
            var deletions = 0;
            foreach (var folder in Others(current))
            {
                records += GameStatsQueue.RecordFiles(folder).Count;
                deletions += File.Exists(Path.Combine(folder, GameStatsQueue.ForgetFile)) ? 1 : 0;
            }

            return new(records, deletions);
        }
    }

    /// <summary>
    /// The player turned sharing off: what waits for other pools is deleted here as well. A pool whose address is
    /// known gets a deletion request that waits for that address; a queue set aside without one is only deleted.
    /// </summary>
    public void WithdrawOthers(string? current)
    {
        lock (_lock)
        {
            var records = 0;
            var deletions = 0;
            foreach (var folder in Others(current))
            {
                try
                {
                    records += GameStatsQueue.RecordFiles(folder).Count;
                    if (DestinationOf(folder) is { } destination && For(destination) is { } queue)
                    {
                        queue.Clear(forget: true);
                        deletions++;
                    }
                    else if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diag.Info($"Game stats: a queue kept for another pool could not be cleared ({ex.GetType().Name})");
                }
            }

            if (records > 0 || deletions > 0)
            {
                Diag.Info($"Game stats: sharing turned off, deleted {records} record(s) kept for other pools, {deletions} deletion request(s) wait for their pools");
            }
        }
    }

    /// <summary>
    /// Applies the age bound to what waits for other pools (the current pool's queue trims itself): records older than
    /// <see cref="GameStatsQueue.MaxAge"/> go, and a folder left with neither a record nor a deletion request goes too.
    /// </summary>
    public void Sweep(string? current)
    {
        lock (_lock)
        {
            var cutoff = UtcNow() - GameStatsQueue.MaxAge;
            var expired = 0;
            foreach (var folder in Others(current))
            {
                try
                {
                    var left = 0;
                    foreach (var file in GameStatsQueue.RecordFiles(folder))
                    {
                        if (file.LastWriteTimeUtc < cutoff)
                        {
                            file.Delete();
                            expired++;
                        }
                        else
                        {
                            left++;
                        }
                    }

                    if (left == 0 && !File.Exists(Path.Combine(folder, GameStatsQueue.ForgetFile)))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diag.Info($"Game stats: a queue kept for another pool could not be trimmed ({ex.GetType().Name})");
                }
            }

            if (expired > 0)
            {
                Diag.Info($"Game stats: dropped {expired} expired record(s) kept for another pool");
            }
        }
    }

    /// <summary>Queue folders other than the current pool's: other addresses and what was set aside.</summary>
    private List<string> Others(string? current)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var own = current is null ? null : PoolDestination.Key(current);
        return Directory.GetDirectories(_root)
            .Where(d => Path.GetFileName(d) is { } name && name != own
                && (PoolDestination.IsKey(name) || name.StartsWith(UnknownPrefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>The folder is this destination's queue: its file says so, or it holds nothing yet.</summary>
    private static bool Owned(string folder, string destination) =>
        File.Exists(Path.Combine(folder, GameStatsQueue.DestinationFile))
            ? DestinationOf(folder) == destination
            : GameStatsQueue.RecordFiles(folder).Count == 0 && !File.Exists(Path.Combine(folder, GameStatsQueue.ForgetFile));

    /// <summary>The address a queue folder names, when it is readable and is the one the folder is named after.</summary>
    private static string? DestinationOf(string folder)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(folder, GameStatsQueue.DestinationFile));
            return JsonNode.Parse(text) is JsonObject meta
                && meta["endpoint"] is JsonValue value && value.TryGetValue<string>(out var endpoint)
                && PoolDestination.Normalize(endpoint) == endpoint
                && PoolDestination.Key(endpoint) == Path.GetFileName(folder)
                ? endpoint
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Renames a folder with an unknown address so it is never sent; its deletion request cannot be delivered.</summary>
    private (int Records, int Deletions) SetAside(string folder)
    {
        var aside = Path.Combine(_root, UnknownPrefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.Move(folder, aside);
        var forget = Path.Combine(aside, GameStatsQueue.ForgetFile);
        var deletions = File.Exists(forget) ? 1 : 0;
        File.Delete(forget);
        File.Delete(Path.Combine(aside, GameStatsQueue.DestinationFile));
        return (GameStatsQueue.RecordFiles(aside).Count, deletions);
    }
}

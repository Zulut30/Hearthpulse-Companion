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
/// read is set aside (<c>unknown-*</c>) and never sent; so is every queue written before queues had a destination.
/// docs/game-stats.md, "Queue destination".
/// </summary>
public sealed class GameStatsQueues
{
    private const string UnknownPrefix = "unknown-";
    private const string LayoutFile = "layout";
    private readonly string _root;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, GameStatsQueue> _open = new(StringComparer.Ordinal);
    private int _generation;

    public GameStatsQueues(string root) => _root = root;

    /// <summary>UTC clock; tests move it.</summary>
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// What these queues' log lines start with: game records unless another sender keeps its queues here (the shared
    /// games of <see cref="PoolOutbox"/>), so a line says whose queue it is about.
    /// </summary>
    internal string Label { get; init; } = GameStatsQueue.DefaultLabel;

    /// <summary>
    /// Counts the withdrawals of this run. A record is built in the background: it carries the number read when its
    /// game ended, and <see cref="Add"/> refuses it once a withdrawal came in between.
    /// </summary>
    public int Generation => Volatile.Read(ref _generation);

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
                    SetAside(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"{Label}: queue with an unreadable destination stays unsent ({ex.GetType().Name})");
                return null;
            }

            if (!_open.TryGetValue(destination, out var queue))
            {
                _open[destination] = queue = new GameStatsQueue(folder, destination, _lock, Label) { UtcNow = UtcNow };
            }

            return queue;
        }
    }

    /// <summary>
    /// Queues a record built for a game that ended while <paramref name="generation"/> was current. False when sharing
    /// was withdrawn since (also when it is back on by now): what was withdrawn does not come back.
    /// </summary>
    public bool Add(string destination, JsonObject record, int generation)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                Diag.Info($"{Label}: a record built across a withdrawal was not queued");
                return false;
            }

            if (For(destination) is not { } queue)
            {
                return false;
            }

            queue.Add(record);
            return true;
        }
    }

    /// <summary>
    /// Marks the pool an earlier version may already have sent to before committing the <c>layout</c> file.
    /// False on a filesystem failure: the sender retries rather than losing deletion provenance.
    /// </summary>
    public bool PrepareLayout(string? legacyDestination)
    {
        lock (_lock)
        {
            var path = Path.Combine(_root, LayoutFile);
            try
            {
                if (File.Exists(path))
                {
                    return true;
                }

                Directory.CreateDirectory(_root);
                // Persist deletion provenance first. If this fails, leave layout absent so the next pass retries.
                if (legacyDestination is not null)
                {
                    if (For(legacyDestination) is not { } queue)
                    {
                        return false;
                    }

                    queue.MarkUsed();
                }

                File.WriteAllText(path, "2");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"{Label}: the queue layout mark could not be written ({ex.GetType().Name})");
                return false;
            }
        }
    }

    /// <summary>
    /// Takes a queue written before queues had a destination (files directly in <c>game-stats/</c>) out of the way. It
    /// names no pool and may have been filled for any, so it is never given to the pool the settings name now: its
    /// records are set aside unsent, or deleted when sharing is off, and its deletion request cannot be delivered.
    /// Returns false when it could not be moved; the caller tries again, and nothing in the root is ever sent.
    /// </summary>
    public bool MigrateLegacy(bool sharingOff)
    {
        lock (_lock)
        {
            var records = GameStatsQueue.RecordFiles(_root).Select(f => f.FullName).ToList();
            var forgetFile = Path.Combine(_root, GameStatsQueue.ForgetFile);
            var forget = File.Exists(forgetFile);
            if (records.Count == 0 && !forget)
            {
                return true;
            }

            try
            {
                if (sharingOff)
                {
                    DeleteLegacy();
                    return true;
                }

                var aside = Path.Combine(_root, UnknownPrefix + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(aside);
                foreach (var file in records)
                {
                    File.Move(file, Path.Combine(aside, Path.GetFileName(file)), overwrite: true);
                }

                File.Delete(forgetFile);
                Diag.Info($"{Label}: earlier queue names no pool, set aside, {records.Count} record(s) not sent");
                if (forget)
                {
                    Diag.Info($"{Label}: earlier deletion request names no pool, cannot be delivered, dropped");
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"{Label}: earlier queue could not be moved, not sent ({ex.GetType().Name})");
                return false;
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
                // Only a request that can still reach a pool waits; one without a readable address never will.
                deletions += File.Exists(Path.Combine(folder, GameStatsQueue.ForgetFile)) && DestinationOf(folder) is not null ? 1 : 0;
            }

            return new(records, deletions);
        }
    }

    /// <summary>
    /// Sharing is off: every waiting record goes, in the current pool's queue, in the queues kept for other pools, in
    /// what was set aside and in what an earlier version left in the root, whether or not that was moved yet. A pool
    /// that may hold something of this install (<see cref="GameStatsQueue.Used"/>) gets a deletion request that waits
    /// for its own address; a pool nothing was sent to gets none. Safe to repeat.
    /// </summary>
    public void Withdraw(string? current)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _generation);
            var records = 0;
            var deletions = 0;
            if (current is not null && For(current) is { } own)
            {
                own.Withdraw();
            }

            foreach (var folder in Others(current))
            {
                try
                {
                    records += GameStatsQueue.RecordFiles(folder).Count;
                    if (DestinationOf(folder) is { } destination && For(destination) is { } queue)
                    {
                        deletions += queue.Withdraw() ? 1 : 0;
                    }
                    else if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diag.Info($"{Label}: a queue kept for another pool could not be cleared ({ex.GetType().Name})");
                }
            }

            try
            {
                DeleteLegacy();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Info($"{Label}: earlier queue could not be deleted ({ex.GetType().Name})");
            }

            if (records > 0)
            {
                Diag.Info($"{Label}: sharing is off, deleted {records} record(s) kept for other pools, {deletions} deletion request(s) wait for their pools");
            }
        }
    }

    /// <summary>
    /// Applies the age bound to what waits for other pools (the current pool's queue trims itself): records older than
    /// <see cref="GameStatsQueue.MaxAge"/> go, and a folder left with nothing to send or to remember goes too. A folder
    /// whose address cannot be read is set aside, so its deletion request does not count as waiting for ever.
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
                    var name = Path.GetFileName(folder);
                    if (PoolDestination.IsKey(name) && DestinationOf(folder) is null)
                    {
                        SetAside(folder);
                        continue;
                    }

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

                    if (left == 0 && !File.Exists(Path.Combine(folder, GameStatsQueue.ForgetFile)) && !File.Exists(Path.Combine(folder, GameStatsQueue.SentFile)))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diag.Info($"{Label}: a queue kept for another pool could not be trimmed ({ex.GetType().Name})");
                }
            }

            if (expired > 0)
            {
                Diag.Info($"{Label}: dropped {expired} expired record(s) kept for another pool");
            }
        }
    }

    /// <summary>Deletes what an earlier version left directly in the root; its deletion request names no pool.</summary>
    private void DeleteLegacy()
    {
        var records = GameStatsQueue.RecordFiles(_root);
        var forgetFile = Path.Combine(_root, GameStatsQueue.ForgetFile);
        var forget = File.Exists(forgetFile);
        foreach (var file in records)
        {
            file.Delete();
        }

        File.Delete(forgetFile);
        if (records.Count > 0)
        {
            Diag.Info($"{Label}: sharing is off, deleted {records.Count} record(s) of the earlier queue");
        }

        if (forget)
        {
            Diag.Info($"{Label}: earlier deletion request names no pool, cannot be delivered, dropped");
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

    /// <summary>
    /// Renames a folder with an unknown address so it is never sent. Its deletion request cannot be delivered and goes
    /// with the marks that only meant something for a known pool.
    /// </summary>
    private void SetAside(string folder)
    {
        var aside = Path.Combine(_root, UnknownPrefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.Move(folder, aside);
        var forget = Path.Combine(aside, GameStatsQueue.ForgetFile);
        var deletions = File.Exists(forget) ? 1 : 0;
        File.Delete(forget);
        File.Delete(Path.Combine(aside, GameStatsQueue.DestinationFile));
        File.Delete(Path.Combine(aside, GameStatsQueue.SentFile));
        var records = GameStatsQueue.RecordFiles(aside).Count;
        if (records == 0)
        {
            Directory.Delete(aside, recursive: true);
        }

        Diag.Info($"{Label}: queue with an unreadable destination set aside, {records} record(s) not sent, {deletions} deletion request(s) dropped");
    }
}

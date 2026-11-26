using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hearthpulse.Core.GameStats;

/// <summary>
/// Game records waiting to be sent: one small file per record in the data folder (<c>game-stats/</c>), so a record
/// survives a restart or a day offline. Bounded: at most <see cref="MaxRecords"/> records, <see cref="MaxBytes"/> in
/// all and <see cref="MaxAge"/> old; past a bound the oldest go first. A <c>forget</c> file marks a deletion request
/// the pool has not confirmed yet. A queue belongs to one pool address (<see cref="Destination"/>, written beside the
/// records in the <c>destination</c> file): <see cref="GameStatsQueues"/> keeps one folder per address.
/// </summary>
public sealed class GameStatsQueue
{
    public const int MaxRecords = 300;

    public const long MaxBytes = 4 * 1024 * 1024;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    internal const string ForgetFile = "forget";

    /// <summary>The file naming the pool this queue is for; without a <c>.json</c> ending, so it is never read as a record.</summary>
    internal const string DestinationFile = "destination";

    /// <summary>
    /// The mark that a batch was handed to this queue's pool (written before the first request, kept until the pool
    /// confirmed a deletion). Only a queue with it asks its pool to delete: a refusal before anything was sent makes no
    /// request at all. Local metadata, like the destination.
    /// </summary>
    internal const string SentFile = "sent";

    /// <summary>What the log lines of game record queues start with; other queues name themselves (<see cref="GameStatsQueues.Label"/>).</summary>
    internal const string DefaultLabel = "Game stats";

    private readonly string _folder;
    private readonly Lock _lock;
    private readonly string _label;

    public GameStatsQueue(string folder)
        : this(folder, null, new Lock(), DefaultLabel)
    {
    }

    internal GameStatsQueue(string folder, string? destination, Lock gate, string label)
    {
        _folder = folder;
        Destination = destination;
        _lock = gate;
        _label = label;
    }

    /// <summary>
    /// The pool address (<see cref="Pool.PoolDestination"/>) these records and the deletion request were queued for,
    /// and the only one they are sent to. Null for a bare folder that <see cref="GameStatsQueues"/> does not manage.
    /// </summary>
    public string? Destination { get; }

    /// <summary>UTC clock; tests move it.</summary>
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return Files().Count;
            }
        }
    }

    /// <summary>Whether a deletion request waits to be sent.</summary>
    public bool ForgetPending => File.Exists(Path.Combine(_folder, ForgetFile));

    /// <summary>Adds a record (its <c>id</c> names the file, so a record added twice is kept once), then trims the queue.</summary>
    public void Add(JsonObject record)
    {
        if (record["id"]?.GetValue<string>() is not { Length: 32 } id || !id.All(char.IsAsciiHexDigitLower))
        {
            return;
        }

        lock (_lock)
        {
            EnsureFolder();
            var path = Path.Combine(_folder, id + ".json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, record.ToJsonString());
            File.Move(temp, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, UtcNow());
            Trim();
        }
    }

    /// <summary>The oldest records, up to <paramref name="max"/>; an unreadable file is deleted.</summary>
    public IReadOnlyList<JsonObject> Take(int max)
    {
        lock (_lock)
        {
            Trim();
            var list = new List<JsonObject>();
            foreach (var file in Files().OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal))
            {
                if (list.Count >= max)
                {
                    break;
                }

                try
                {
                    if (JsonNode.Parse(File.ReadAllText(file.FullName)) is JsonObject record)
                    {
                        list.Add(record);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // Damaged: dropped below.
                }

                TryDelete(file.FullName);
            }

            return list;
        }
    }

    /// <summary>Removes records that were sent or refused.</summary>
    public void Remove(IEnumerable<JsonObject> records)
    {
        lock (_lock)
        {
            foreach (var record in records)
            {
                if (record["id"]?.GetValue<string>() is { Length: 32 } id && id.All(char.IsAsciiHexDigitLower))
                {
                    TryDelete(Path.Combine(_folder, id + ".json"));
                }
            }
        }
    }

    /// <summary>Drops every waiting record; with <paramref name="forget"/> also marks a deletion request to send.</summary>
    public void Clear(bool forget)
    {
        lock (_lock)
        {
            foreach (var file in Files())
            {
                TryDelete(file.FullName);
            }

            if (forget)
            {
                EnsureFolder();
                File.WriteAllText(Path.Combine(_folder, ForgetFile), string.Empty);
            }
        }
    }

    /// <summary>The folder, with the destination written before the first record: a record never lies there unaddressed.</summary>
    private void EnsureFolder()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, DestinationFile);
        if (Destination is null || File.Exists(path))
        {
            return;
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, new JsonObject { ["v"] = 1, ["endpoint"] = Destination }.ToJsonString());
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The pool confirmed the deletion: nothing of this install is left there, so the queue counts as unused again.</summary>
    public void Forgotten()
    {
        lock (_lock)
        {
            TryDelete(Path.Combine(_folder, ForgetFile));
            TryDelete(Path.Combine(_folder, SentFile));
        }
    }

    /// <summary>Whether something was handed to this queue's pool since its last confirmed deletion.</summary>
    public bool Used => File.Exists(Path.Combine(_folder, SentFile));

    /// <summary>Called before a batch leaves: from here on a refusal has something to delete at this pool.</summary>
    public void MarkUsed()
    {
        lock (_lock)
        {
            if (!Used)
            {
                EnsureFolder();
                File.WriteAllText(Path.Combine(_folder, SentFile), string.Empty);
            }
        }
    }

    /// <summary>
    /// The player turned sharing off: every waiting record goes, and a deletion request is marked only when this pool
    /// may hold something (<see cref="Used"/>). Returns whether a request waits now.
    /// </summary>
    public bool Withdraw()
    {
        lock (_lock)
        {
            Clear(forget: Used);
            return ForgetPending;
        }
    }

    /// <summary>A deletion request that can no longer be made (the install id is gone) is dropped with its mark.</summary>
    internal void DropForget() => Forgotten();

    private List<FileInfo> Files() => RecordFiles(_folder);

    /// <summary>The record files of a queue folder.</summary>
    internal static List<FileInfo> RecordFiles(string folder) =>
        Directory.Exists(folder) ? new DirectoryInfo(folder).GetFiles("*.json").ToList() : [];

    private void Trim()
    {
        var files = Files().OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        var cutoff = UtcNow() - MaxAge;
        long bytes = 0;
        for (var i = 0; i < files.Count; i++)
        {
            bytes += files[i].Length;
            if (i >= MaxRecords || bytes > MaxBytes || files[i].LastWriteTimeUtc < cutoff)
            {
                TryDelete(files[i].FullName);
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Info($"{_label}: could not delete {Path.GetFileName(path)} ({ex.GetType().Name})");
        }
    }
}

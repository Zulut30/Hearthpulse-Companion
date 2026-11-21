namespace Hearthpulse.Core.Collection;

/// <summary>
/// Decides when something of the player's is read from the client between games, and keeps what was read
/// (docs/collection-cosmetics.md, "When it is read"): the card backs and coins (<see cref="CosmeticsWatch"/>) and the
/// achievements (<c>Progression.AchievementsWatch</c>). The rules beside the collection's (<see cref="CollectionWatch"/>):
/// <list type="bullet">
/// <item>once when a session of the client starts (the client started, was found running at the app's start, began a
/// new log folder, or went back to its login screen);</item>
/// <item>again when a game ended (the scene left <c>GAMEPLAY</c>) or the collection or pack opening screen closed, not
/// more often than <see cref="MinInterval"/>: rewards come after games and from the shop and the collection;</item>
/// <item>never during a game; a read that gave nothing is tried again after 10 s, 20 s… up to a minute.</item>
/// </list>
/// The read itself runs on the memory thread; this hands out a ticket for it and takes the result. Calls come from the
/// tracking thread; <see cref="Current"/> may be read from any thread. What is known is
/// confirmed only between a read of the running client and the end of that client's session; the saved file and an
/// earlier session's read are shown as unconfirmed.
/// </summary>
/// <typeparam name="T">The record read: its <c>Confirmed</c> flag is set through <see cref="Confirmed(T, bool)"/>.</typeparam>
internal abstract class ClientReadWatch<T>
    where T : class
{
    /// <summary>The scene of a game in LoadingScreen.log.</summary>
    internal const string GameScene = "GAMEPLAY";

    /// <summary>The shortest time between two reads that gave something.</summary>
    internal static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    /// <summary>A read that gave nothing is tried again after this, then twice this, up to <see cref="RetryMax"/>.</summary>
    internal static readonly TimeSpan RetryStep = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan RetryMax = TimeSpan.FromMinutes(1);

    /// <summary>A save that failed is tried again after this.</summary>
    internal static readonly TimeSpan SaveRetry = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Lock _saveGate = new();
    private T? _current;
    private T? _unsaved;
    private volatile bool _saveFailed;
    private DateTime _saveRetryAt = DateTime.MaxValue;
    private int _started;

    // Tracking thread only. Every wish for a read has a number; a session of the client starts at _sessionFloor.
    private int _ticket;
    private int _sessionFloor;
    private bool _wanted;
    private int _misses;
    private DateTime _notBefore = DateTime.MinValue;

    /// <summary>The newest record known: read from the client, else the saved one; null before any.</summary>
    public T? Current => Volatile.Read(ref _current);

    /// <summary>
    /// <see cref="Current"/> changed: loaded from the file, read from the client, or no longer confirmed. It carries
    /// nothing on purpose: a subscriber reads <see cref="Current"/>. Raised on a background or the tracking thread,
    /// never on the UI thread; a subscriber that throws is logged.
    /// </summary>
    public event Action? Changed;

    /// <summary>The start-up load of the saved file, for tests to wait on.</summary>
    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>The last save, for tests to wait on.</summary>
    internal Task Saved { get; private set; } = Task.CompletedTask;

    /// <summary>What is read, for the log ("cosmetics", "achievements").</summary>
    protected abstract string What { get; }

    /// <summary>The saved record, or null; never throws. Null without a store.</summary>
    protected abstract T? Load();

    /// <summary>Writes the record to the store; throws what the file system throws. Nothing without a store.</summary>
    protected abstract void Save(T value);

    /// <summary>Removes what a killed save left behind; never throws.</summary>
    protected abstract void RemoveLeftovers();

    /// <summary>The record with its confirmation set.</summary>
    protected abstract T Confirmed(T value, bool confirmed);

    /// <summary>Whether the record is confirmed.</summary>
    protected abstract bool IsConfirmed(T value);

    /// <summary>When the record was read, to keep the newer of two.</summary>
    protected abstract DateTime ReadAt(T value);

    /// <summary>The log line of a read: counts only, never names or ids.</summary>
    protected abstract string Describe(T value);

    /// <summary>A read joined with what was known before it (the achievements' gains); the read itself by default.</summary>
    protected virtual T Merge(T read, T? known) => read;

    /// <summary>
    /// Loads the saved record off the caller's thread; call it after subscribing to <see cref="Changed"/>. Only the
    /// first call does anything. A record read from the client meanwhile wins over the file.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Loaded = Task.Run(() =>
        {
            try
            {
                T? saved;
                lock (_saveGate)
                {
                    // What a save cut off by a crash left behind; under the gate, so never a save's file in the making.
                    RemoveLeftovers();
                    saved = Load();
                }

                if (saved is not null && Interlocked.CompareExchange(ref _current, Confirmed(saved, false), null) is null)
                {
                    Raise();
                }
            }
            catch (Exception ex)
            {
                Diag.Error($"Loading the saved {What} failed", ex);
            }
        });
    }

    /// <summary>The client was started (or found running, or began a new log folder): one read is wanted, what is known is no longer confirmed.</summary>
    public void ClientStarted() => NewSession(wanted: true);

    /// <summary>The client was closed: what is known stays, unconfirmed, and nothing is read.</summary>
    public void ClientStopped() => NewSession(wanted: false);

    /// <summary>The client went from one scene to another, live (not while old log lines are replayed).</summary>
    public void SceneChanged(string from, string to)
    {
        if (to == CollectionWatch.LoginScene)
        {
            // Signed out or reconnected: the next read may be another account's.
            NewSession(wanted: true);
        }
        else if ((from == GameScene && to != GameScene) || (from is CollectionWatch.CollectionScene or CollectionWatch.PackScene && to != from))
        {
            _ticket++;
            _wanted = true;
        }
    }

    /// <summary>The number of the read the next memory scan should do, 0 when none is wanted now (or a game is on).</summary>
    public int Ticket(DateTime now, bool inGame) => _wanted && !inGame && now >= _notBefore ? _ticket : 0;

    /// <summary>Housekeeping of a tick: a save that failed is tried again.</summary>
    public void Tick(DateTime now)
    {
        if (!_saveFailed)
        {
            return;
        }

        if (_saveRetryAt == DateTime.MaxValue)
        {
            _saveRetryAt = now + SaveRetry;
        }
        else if (now >= _saveRetryAt)
        {
            _saveFailed = false;
            _saveRetryAt = DateTime.MaxValue;
            SaveInBackground();
        }
    }

    /// <summary>
    /// Takes the result of the read asked for with <paramref name="ticket"/>: a record, or null when the client gave
    /// none (what is known stays). A result asked for in an earlier session of the client is dropped; one asked for
    /// before a newer wish is taken, and the newer wish stands.
    /// </summary>
    public void Apply(T? read, int ticket, DateTime now)
    {
        if (ticket <= 0 || ticket < _sessionFloor || ticket > _ticket)
        {
            return;
        }

        if (read is null)
        {
            _misses++;
            _notBefore = now + TimeSpan.FromTicks(Math.Min(RetryStep.Ticks * _misses, RetryMax.Ticks));
            return;
        }

        if (ticket == _ticket)
        {
            _wanted = false;
        }

        _misses = 0;
        _notBefore = now + MinInterval;
        T confirmed;
        lock (_gate)
        {
            // Results come in the order they were read; a later one never gives way to an earlier one.
            if (_current is { } known && IsConfirmed(known) && ReadAt(known) > ReadAt(read))
            {
                return;
            }

            confirmed = Confirmed(Merge(read, _current), true);
            Volatile.Write(ref _current, confirmed);
            Volatile.Write(ref _unsaved, confirmed);
        }

        SaveInBackground();
        Diag.Info($"{char.ToUpperInvariant(What[0])}{What[1..]} read from the client: {Describe(confirmed)}");
        Raise();
    }

    private void NewSession(bool wanted)
    {
        _sessionFloor = ++_ticket;
        _wanted = wanted;
        _misses = 0;
        _notBefore = DateTime.MinValue;
        bool changed;
        lock (_gate)
        {
            changed = _current is { } known && IsConfirmed(known);
            if (changed)
            {
                Volatile.Write(ref _current, Confirmed(_current!, false));
            }
        }

        if (changed)
        {
            Raise();
        }
    }

    private void SaveInBackground()
    {
        Saved = Task.Run(() =>
        {
            try
            {
                lock (_saveGate)
                {
                    // Always the newest read: the file never goes back to an older one, and a retry saves what is current.
                    if (Volatile.Read(ref _unsaved) is { } newest)
                    {
                        Save(newest);
                        Interlocked.CompareExchange(ref _unsaved, null, newest);
                    }
                }
            }
            catch (Exception ex)
            {
                Diag.Error($"Saving the {What} failed; it is tried again", ex);
                _saveFailed = true;
            }
        });
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // A view that fails must not stop the tracking thread or lose the save.
            Diag.Error($"A subscriber of the {What} failed", ex);
        }
    }
}

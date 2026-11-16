namespace Hearthpulse.Core.Collection;

/// <summary>
/// Decides when the collection is read from the client, and keeps what was read (docs/collection.md): once per
/// session of the client, when the collection screen opens, and when it or the pack opening screen closes (cards
/// crafted, disenchanted or opened there). Not asked for during a game, and not more often than
/// <see cref="MinInterval"/>. The read itself runs on the memory thread; this only hands out a ticket for it and takes
/// the result. Its calls come from the tracking thread; <see cref="Current"/> may be read from any thread.
/// <para>
/// A collection is <see cref="CardCollection.Confirmed"/> only between a read of the running client and the end of
/// that client's session (closed, started again, back at its login screen: the next one may be another account's).
/// The saved file and every earlier session's read are shown as what they are: unconfirmed.
/// </para>
/// </summary>
internal sealed class CollectionWatch
{
    /// <summary>The scene of the collection screen in LoadingScreen.log (<c>SceneMgr.Mode</c> of the client).</summary>
    internal const string CollectionScene = "COLLECTIONMANAGER";

    /// <summary>The scene of the pack opening screen.</summary>
    internal const string PackScene = "PACKOPENING";

    /// <summary>The client's login screen: after it another account may be signed in.</summary>
    internal const string LoginScene = "LOGIN";

    /// <summary>The shortest time between two reads that gave a collection.</summary>
    internal static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

    /// <summary>A read that gave nothing (the client is still signing in) is tried again after this, then twice this, up to <see cref="RetryMax"/>.</summary>
    internal static readonly TimeSpan RetryStep = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan RetryMax = TimeSpan.FromMinutes(1);

    /// <summary>A save that failed is tried again after this.</summary>
    internal static readonly TimeSpan SaveRetry = TimeSpan.FromMinutes(1);

    private readonly CollectionStore? _store;
    private readonly Lock _gate = new();
    private readonly Lock _saveGate = new();
    private CardCollection? _current;
    private CardCollection? _unsaved;
    private volatile bool _saveFailed;
    private DateTime _saveRetryAt = DateTime.MaxValue;
    private int _started;

    // Tracking thread only. Every wish for a read has a number; a session of the client starts at _sessionFloor.
    private int _ticket;
    private int _sessionFloor;
    private bool _wanted;
    private int _misses;
    private DateTime _notBefore = DateTime.MinValue;

    /// <param name="store">Where the collection is kept between runs; null keeps it in memory only.</param>
    public CollectionWatch(CollectionStore? store) => _store = store;

    /// <summary>The newest collection known: read from the client, else the saved one; null before any. See <see cref="CardCollection.Confirmed"/>.</summary>
    public CardCollection? Current => Volatile.Read(ref _current);

    /// <summary>
    /// <see cref="Current"/> changed: loaded from the file, read from the client, or no longer confirmed. It carries no
    /// collection on purpose: a subscriber reads <see cref="Current"/> and so never ends up with an older one than the
    /// last. Raised on a background or the tracking thread, never on the UI thread; a subscriber that throws is logged.
    /// </summary>
    public event Action? Changed;

    /// <summary>The start-up load of the saved file, for tests to wait on.</summary>
    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>The last save, for tests to wait on.</summary>
    internal Task Saved { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Loads the saved collection off the caller's thread; call it after subscribing to <see cref="Changed"/>. Only the
    /// first call does anything. A collection read from the client meanwhile wins over the file.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || _store is not { } store)
        {
            return;
        }

        Loaded = Task.Run(() =>
        {
            try
            {
                CardCollection? saved;
                lock (_saveGate)
                {
                    // What a save cut off by a crash left behind; under the gate, so never a save's file in the making.
                    store.RemoveLeftovers();
                    saved = store.Load();
                }

                if (saved is not null && Interlocked.CompareExchange(ref _current, saved with { Confirmed = false }, null) is null)
                {
                    Raise();
                }
            }
            catch (Exception ex)
            {
                Diag.Error("Loading the saved collection failed", ex);
            }
        });
    }

    /// <summary>
    /// The client was started (or found running at the app's start, or began a new log session): what is known is of
    /// an earlier session and no longer confirmed, and the collection is read once.
    /// </summary>
    public void ClientStarted() => NewSession(wanted: true);

    /// <summary>The client was closed: what is known stays, unconfirmed, and nothing is read.</summary>
    public void ClientStopped() => NewSession(wanted: false);

    /// <summary>The client went from one scene to another, live (not while old log lines are replayed).</summary>
    public void SceneChanged(string from, string to)
    {
        if (to == LoginScene)
        {
            // Signed out or reconnected: the next collection may be another account's.
            NewSession(wanted: true);
        }
        else if (to == CollectionScene || from is CollectionScene or PackScene)
        {
            _ticket++;
            _wanted = true;
        }
    }

    /// <summary>
    /// The number of the read the next memory scan should do, 0 when none is wanted now. The result comes back to
    /// <see cref="Apply"/> with it.
    /// </summary>
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
    /// Takes the result of the read asked for with <paramref name="ticket"/>: a collection, or null when the client
    /// gave none. A result asked for in an earlier session of the client is dropped; one asked for before a newer wish
    /// (the player went on to open a pack) is taken, and the newer wish stands.
    /// </summary>
    public void Apply(CardCollection? read, int ticket, DateTime now)
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
        var confirmed = read with { Confirmed = true };
        lock (_gate)
        {
            // Results come in the order they were read; a later one never gives way to an earlier one.
            if (_current is { Confirmed: true } known && known.ReadAt > confirmed.ReadAt)
            {
                return;
            }

            Volatile.Write(ref _current, confirmed);
            Volatile.Write(ref _unsaved, confirmed);
        }

        SaveInBackground();
        Diag.Info($"Collection read from the client: {read.Cards.Count} cards");
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
            changed = _current is { Confirmed: true };
            if (changed)
            {
                Volatile.Write(ref _current, _current! with { Confirmed = false });
            }
        }

        if (changed)
        {
            Raise();
        }
    }

    private void SaveInBackground()
    {
        if (_store is not { } store)
        {
            return;
        }

        Saved = Task.Run(() =>
        {
            try
            {
                lock (_saveGate)
                {
                    // Always the newest read: the file never goes back to an older one, and a retry saves what is current.
                    if (Volatile.Read(ref _unsaved) is { } newest)
                    {
                        store.Save(newest);
                        Interlocked.CompareExchange(ref _unsaved, null, newest);
                    }
                }
            }
            catch (Exception ex)
            {
                Diag.Error("Saving the collection failed; it is tried again", ex);
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
            Diag.Error("A collection subscriber failed", ex);
        }
    }
}

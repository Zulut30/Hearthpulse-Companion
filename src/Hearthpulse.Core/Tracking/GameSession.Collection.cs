using Hearthpulse.Core.Collection;

namespace Hearthpulse.Core.Tracking;

/// <summary>The player's collection, read from the client between games and kept locally (docs/collection.md).</summary>
public sealed partial class GameSession
{
    private CollectionWatch _collectionWatch = null!;

    /// <summary>
    /// When the collection is read and where it is kept (<see cref="AppPaths.Collection"/>); a test puts one with a
    /// scratch file here before the first tick.
    /// </summary>
    internal CollectionWatch CollectionWatch
    {
        get => _collectionWatch;
        set
        {
            if (_collectionWatch is { } old)
            {
                old.Changed -= OnCollectionChanged;
            }

            value.Changed += OnCollectionChanged;
            _collectionWatch = value;
        }
    }

    /// <summary>
    /// The player's collection: the last one read from the client, else the one saved by an earlier run; null before
    /// any was read (and, without a saved file, always on macOS and Linux, where the client's memory is not read).
    /// Only a <see cref="CardCollection.Confirmed"/> one is the running client's; anything else is "as of
    /// <see cref="CardCollection.ReadAt"/>" and may be another account's. Safe from any thread.
    /// </summary>
    public CardCollection? Collection => _collectionWatch.Current;

    /// <summary>
    /// <see cref="Collection"/> changed: loaded from the file at start-up, read from the client, or no longer
    /// confirmed (the client was closed or started again). Read <see cref="Collection"/> for the state. Raised off the
    /// UI thread; the host dispatches.
    /// </summary>
    public event Action? CollectionChanged;

    private void OnCollectionChanged() => CollectionChanged?.Invoke();

    /// <summary>The number of the collection read the next memory scan should do, 0 for none.</summary>
    private int CollectionTicket(bool inGame)
    {
        var now = Clock();
        // The game's scene comes seconds before its first Power.log line; a game left unfinished is no game.
        return _collectionWatch.Ticket(now, (inGame && !_recordedAbandoned) || _scene == "GAMEPLAY");
    }

    private void ApplyCollectionMemory(MemoryReading reading)
    {
        if (reading.Request.CollectionTicket != 0)
        {
            _collectionWatch.Apply(reading.Collection, reading.Request.CollectionTicket, Clock());
        }
    }
}

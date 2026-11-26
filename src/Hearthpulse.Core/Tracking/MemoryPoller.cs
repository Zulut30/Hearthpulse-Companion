using System.Diagnostics;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.Core.Tracking;

/// <summary>What the tracking thread wants read from the game on the next memory scan.</summary>
internal sealed record MemoryRequest(
    int Generation,
    bool InGame,
    bool Battlegrounds,
    bool ReadRating,
    bool WantRaces,
    bool WantRanks,
    string? AwaitingRatingKey,
    bool WantDraftChoices = false,
    bool WantLobbyNames = false,
    int CollectionTicket = 0,
    int CosmeticsTicket = 0,
    int AchievementsTicket = 0,
    AchievementBook? AchievementsCatalog = null);

/// <summary>The long read a memory scan makes, at most one (docs/collection-cosmetics.md).</summary>
internal enum LongReadKind
{
    None,
    Collection,
    Cosmetics,
    Achievements,
}

/// <summary>What one memory scan found; null where nothing was read.</summary>
internal sealed record MemoryReading(
    MemoryRequest Request,
    BattlegroundsRating? Rating,
    int? PostGameRating,
    IReadOnlyList<int>? Races,
    BattlegroundsButtonCosts? ButtonCosts,
    (MatchPlayerInfo? Local, MatchPlayerInfo? Opponent)? Players,
    Arena.ArenaDraftOffer? DraftChoices = null,
    IReadOnlyDictionary<int, string>? LobbyNames = null,
    CardCollection? Collection = null,
    CosmeticsCollection? Cosmetics = null,
    AchievementBook? Achievements = null);

/// <summary>
/// Reads the game's memory on its own thread. A scan can take seconds on a slow PC, or when a security tool inspects
/// every read; on the log thread that froze the overlay. The tracking thread posts what it needs and picks up the
/// newest reading, it never waits for a scan.
/// </summary>
internal sealed class MemoryPoller
{
    private readonly HearthstoneMemory _memory;
    private readonly AutoResetEvent _wake = new(false);
    private MemoryRequest? _request;
    private MemoryReading? _reading;
    private Thread? _thread;
    private TimeSpan _longReadTime;
    private int _releaseWanted;

    public MemoryPoller(HearthstoneMemory memory)
    {
        _memory = memory;
    }

    /// <summary>The reader thread let go of the game process after <see cref="Release"/>; raised on that thread (tests).</summary>
    internal event Action? Released;

    /// <summary>What the next scan reads; null while scanning is paused (tests).</summary>
    internal MemoryRequest? Pending => Volatile.Read(ref _request);

    /// <summary>The reader thread was started: a request was posted at least once (tests).</summary>
    internal bool Started => _thread is not null;

    /// <summary>Sets what the next scan reads; null pauses scanning (Hearthstone is not running).</summary>
    public void Post(MemoryRequest? request)
    {
        Volatile.Write(ref _request, request);
        if (request is null)
        {
            return;
        }

        // Reading is wanted again before the reader got to let go: it keeps the process.
        Volatile.Write(ref _releaseWanted, 0);
        if (_thread is null)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Hearthpulse memory reader" };
            _thread.Start();
        }
    }

    /// <summary>
    /// No more scans: reading the client was turned off. The reader thread lets go of the game process before it would
    /// scan again, never in the middle of a scan; a later <see cref="Post"/> of a request attaches anew. Never started,
    /// it never attached and nothing is done.
    /// </summary>
    public void Release()
    {
        Volatile.Write(ref _request, null);
        if (_thread is null)
        {
            return;
        }

        Volatile.Write(ref _releaseWanted, 1);
        _wake.Set();
    }

    /// <summary>Scans right away instead of waiting for the next interval.</summary>
    public void Wake() => _wake.Set();

    /// <summary>The newest reading that was not taken yet, or null.</summary>
    public MemoryReading? Take() => Interlocked.Exchange(ref _reading, null);

    /// <summary>One scan of <paramref name="request"/> on the caller's thread, without the reader thread (tests).</summary>
    internal MemoryReading? ScanOnce(MemoryRequest request)
    {
        Volatile.Write(ref _request, request);
        return Scan(request);
    }

    private void Run()
    {
        while (true)
        {
            var request = Volatile.Read(ref _request);
            var delay = TimeSpan.FromSeconds(3);
            if (request is null && Interlocked.Exchange(ref _releaseWanted, 0) != 0)
            {
                // On this thread, between scans: the handle is never closed under a read of this reader.
                _memory.Detach();
                Released?.Invoke();
            }
            else if (request is not null)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    if (Scan(request) is { } reading)
                    {
                        Volatile.Write(ref _reading, reading);
                    }
                }
                catch (Exception ex)
                {
                    Diag.Error("Memory scan failed", ex);
                }

                // The collection's, the cosmetics' and the achievements' reads are long by nature and rare: no sign of a slow PC.
                var elapsed = Stopwatch.GetElapsedTime(started) - _longReadTime;
                _longReadTime = TimeSpan.Zero;
                if (elapsed > TimeSpan.FromSeconds(1))
                {
                    Diag.Info($"Memory scan took {elapsed.TotalMilliseconds:N0} ms; the next one waits longer");
                    delay = TimeSpan.FromSeconds(10);
                }
                else if (request.AwaitingRatingKey is not null || request.WantDraftChoices)
                {
                    delay = TimeSpan.FromSeconds(1);
                }
            }

            _wake.WaitOne(delay);
        }
    }

    private MemoryReading? Scan(MemoryRequest request)
    {
        if (!_memory.EnsureAttached())
        {
            return null;
        }

        var awaiting = request.AwaitingRatingKey is not null;
        var battlegroundsGame = request.InGame && request.Battlegrounds;
        var rating = request.ReadRating || awaiting ? _memory.GetBattlegroundsRating() : null;
        var postGameRating = awaiting ? _memory.GetPostGameBattlegroundsRating() : null;
        var races = battlegroundsGame && request.WantRaces ? _memory.GetBattlegroundsRaces() : null;
        var buttonCosts = battlegroundsGame ? _memory.GetBattlegroundsButtonCosts() : null;
        var players = request.InGame && request.WantRanks ? _memory.GetMatchPlayers() : null;
        var draft = request.WantDraftChoices ? _memory.GetArenaDraftChoices() : null;
        var lobby = battlegroundsGame && request.WantLobbyNames ? _memory.GetBattlegroundsLobbyNames() : null;
        var (served, collection, cosmetics, achievements) = LongRead(request);
        return new MemoryReading(served, rating, postGameRating, races, buttonCosts, players, draft, lobby, collection, cosmetics, achievements);
    }

    /// <summary>
    /// At most one long read a scan, after the other values (docs/collection-cosmetics.md): the collection if it is
    /// wanted, else the cosmetics, else the achievements (with the session's catalog, the states only). A long read this
    /// scan did not make keeps its wish for the next scan: its ticket is taken off the request the reading answers, so
    /// the watch does not take the empty result for a failed read.
    /// </summary>
    private (MemoryRequest Served, CardCollection? Collection, CosmeticsCollection? Cosmetics, AchievementBook? Achievements) LongRead(MemoryRequest request)
    {
        var kind = NextLongRead(request, Volatile.Read(ref _request));
        var served = Served(request, kind);
        var started = Stopwatch.GetTimestamp();
        switch (kind)
        {
            case LongReadKind.Collection:
                return (served, ReadCollection(request), null, null);
            case LongReadKind.Cosmetics:
                var cosmetics = _memory.GetCosmetics(() => LongReadStale(request.CosmeticsTicket, request, Volatile.Read(ref _request)));
                _longReadTime = Stopwatch.GetElapsedTime(started);
                return (served, null, cosmetics, null);
            case LongReadKind.Achievements:
                var achievements = _memory.GetAchievements(
                    () => LongReadStale(request.AchievementsTicket, request, Volatile.Read(ref _request)), request.AchievementsCatalog);
                _longReadTime = Stopwatch.GetElapsedTime(started);
                return (served, null, null, achievements);
            default:
                return (served, null, null, null);
        }
    }

    /// <summary>The one long read of a scan: the collection, else the cosmetics, else the achievements, each only while it is wanted.</summary>
    internal static LongReadKind NextLongRead(MemoryRequest request, MemoryRequest? newest) =>
        !CollectionReadStale(request, newest) ? LongReadKind.Collection
        : !LongReadStale(request.CosmeticsTicket, request, newest) ? LongReadKind.Cosmetics
        : !LongReadStale(request.AchievementsTicket, request, newest) ? LongReadKind.Achievements
        : LongReadKind.None;

    /// <summary>The request a reading answers: the tickets of the long reads that come after the one made are taken off (they wait for the next scan).</summary>
    internal static MemoryRequest Served(MemoryRequest request, LongReadKind made) => made switch
    {
        LongReadKind.Collection => request with { CosmeticsTicket = 0, AchievementsTicket = 0, AchievementsCatalog = null },
        LongReadKind.Cosmetics => request with { AchievementsTicket = 0, AchievementsCatalog = null },
        _ => request,
    };

    /// <summary>
    /// The collection, read last (after the other values of this scan) and only while it is still wanted: the newest
    /// request is looked at before the read and during it, so a game that began meanwhile stops it. The next scan
    /// still waits for it, at most the read's 3 s budget (docs/collection.md).
    /// </summary>
    private CardCollection? ReadCollection(MemoryRequest request)
    {
        if (CollectionReadStale(request, Volatile.Read(ref _request)))
        {
            return null;
        }

        var started = Stopwatch.GetTimestamp();
        var collection = _memory.GetCollection(() => CollectionReadStale(request, Volatile.Read(ref _request)));
        _longReadTime = Stopwatch.GetElapsedTime(started);
        return collection;
    }

    /// <summary>
    /// Whether a collection read asked for by <paramref name="asked"/> should not run (or go on): it was not asked for,
    /// the client is gone, a game is on, or the Arena draft screen opened since and its offer is waited for.
    /// </summary>
    internal static bool CollectionReadStale(MemoryRequest asked, MemoryRequest? newest) => LongReadStale(asked.CollectionTicket, asked, newest);

    /// <summary>
    /// Whether a long read with <paramref name="ticket"/> asked for by <paramref name="asked"/> should not run (or go
    /// on): it was not asked for, the client is gone, a game is on, or the Arena draft screen opened since.
    /// </summary>
    internal static bool LongReadStale(int ticket, MemoryRequest asked, MemoryRequest? newest) =>
        ticket == 0 || asked.InGame || newest is null || newest.InGame
        || (newest.WantDraftChoices && !asked.WantDraftChoices);
}

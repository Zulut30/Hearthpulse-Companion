using Hearthpulse.Core.Collection;

namespace Hearthpulse.Core.Tracking;

/// <summary>The player's card backs and coins, read from the client between games and kept locally (docs/collection-cosmetics.md).</summary>
public sealed partial class GameSession
{
    private CosmeticsWatch _cosmeticsWatch = null!;

    /// <summary>Where the cosmetics are kept (<see cref="AppPaths.Cosmetics"/>); a test puts one with a scratch file here before the first tick.</summary>
    internal CosmeticsWatch CosmeticsWatch
    {
        get => _cosmeticsWatch;
        set
        {
            if (_cosmeticsWatch is { } old)
            {
                old.Changed -= OnCosmeticsChanged;
            }

            value.Changed += OnCosmeticsChanged;
            _cosmeticsWatch = value;
        }
    }

    /// <summary>
    /// The player's card backs and coins: the last read from the client, else the ones saved by an earlier run; null
    /// before any. Only <see cref="CosmeticsCollection.Confirmed"/> ones are the running client's. Safe from any thread.
    /// </summary>
    public CosmeticsCollection? Cosmetics => _cosmeticsWatch.Current;

    /// <summary>
    /// <see cref="Cosmetics"/> changed: loaded from the file at start-up, read from the client, or no longer confirmed.
    /// Read <see cref="Cosmetics"/> for the state. Raised off the UI thread; the host dispatches.
    /// </summary>
    public event Action? CosmeticsChanged;

    private void OnCosmeticsChanged() => CosmeticsChanged?.Invoke();

    /// <summary>The number of the cosmetics read the next memory scan should do, 0 for none (never in a game or its scene).</summary>
    private int CosmeticsTicket(bool inGame) => _cosmeticsWatch.Ticket(Clock(), (inGame && !_recordedAbandoned) || _scene == "GAMEPLAY");

    private void ApplyCosmeticsMemory(MemoryReading reading)
    {
        if (reading.Request.CosmeticsTicket != 0)
        {
            _cosmeticsWatch.Apply(reading.Cosmetics, reading.Request.CosmeticsTicket, Clock());
        }
    }
}

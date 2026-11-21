using Hearthpulse.Core.Progression;

namespace Hearthpulse.Core.Tracking;

/// <summary>The player's Hearthstone achievements, read from the client between games and kept locally (docs/collection-cosmetics.md).</summary>
public sealed partial class GameSession
{
    private AchievementsWatch _achievementsWatch = null!;

    /// <summary>Where the achievement book is kept (<see cref="AppPaths.Achievements"/>); a test puts one with a scratch file here before the first tick.</summary>
    internal AchievementsWatch AchievementsWatch
    {
        get => _achievementsWatch;
        set
        {
            if (_achievementsWatch is { } old)
            {
                old.Changed -= OnAchievementsChanged;
            }

            value.Changed += OnAchievementsChanged;
            _achievementsWatch = value;
        }
    }

    /// <summary>
    /// The achievement book: the last read from the client, else the one saved by an earlier run; null before any. Only a
    /// <see cref="AchievementBook.Confirmed"/> one is the running client's. Safe from any thread.
    /// </summary>
    public AchievementBook? Achievements => _achievementsWatch.Current;

    /// <summary>
    /// <see cref="Achievements"/> changed: loaded from the file at start-up, read from the client, or no longer confirmed.
    /// Read <see cref="Achievements"/> for the state. Raised off the UI thread; the host dispatches.
    /// </summary>
    public event Action? AchievementsChanged;

    private void OnAchievementsChanged() => AchievementsChanged?.Invoke();

    /// <summary>The number of the achievements read the next memory scan should do, 0 for none (never in a game or its scene).</summary>
    private int AchievementsTicket(bool inGame) => _achievementsWatch.Ticket(Clock(), (inGame && !_recordedAbandoned) || _scene == "GAMEPLAY");

    private void ApplyAchievementsMemory(MemoryReading reading)
    {
        if (reading.Request.AchievementsTicket != 0)
        {
            _achievementsWatch.Apply(reading.Achievements, reading.Request.AchievementsTicket, Clock());
        }
    }
}

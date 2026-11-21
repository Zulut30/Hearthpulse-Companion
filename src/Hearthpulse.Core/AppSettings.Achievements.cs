namespace Hearthpulse.Core;

/// <summary>Settings of the achievements page.</summary>
public sealed partial class AppSettings
{
    /// <summary>The most achievement chains that can be tracked at once.</summary>
    public const int MaxTrackedAchievements = 10;

    private List<int> _trackedAchievements = [];

    /// <summary>
    /// Tracked achievement chains: the id of each chain's first tier, at most <see cref="MaxTrackedAchievements"/>, in the
    /// order they were added. A file written by hand keeps its positive ids, without repeats, up to the limit.
    /// </summary>
    public List<int> TrackedAchievements
    {
        get => _trackedAchievements;
        set => _trackedAchievements = value is null ? [] : [.. value.Where(id => id > 0).Distinct().Take(MaxTrackedAchievements)];
    }
}

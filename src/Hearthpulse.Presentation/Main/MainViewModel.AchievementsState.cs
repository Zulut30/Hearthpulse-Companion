#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The Achievements page's empty state (docs/achievements-page.md): why there is no book, that it is being worked out,
/// or that working it out failed, with the retry.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The page's empty state: why there is no book (another system, not read yet, none yet), or that the first one is
    /// being put together. Empty once the page has its content.
    /// </summary>
    public string AchievementsEmpty => _achievementBook is null
        ? Loc.T(!_achievementsReadable ? "Achievements.Empty.Unsupported" : HearthstoneRunning ? "Achievements.Empty.NotRead" : "Achievements.Empty.None")
        : _achievementsData is not null ? string.Empty
        : Loc.T(_achievementsFailed ? "Achievements.Error" : "Achievements.Loading");

    public string AchievementsEmptyGlyph => AchievementsFailed ? GlyphFailed
        : _achievementBook is not null || (_achievementsReadable && HearthstoneRunning) ? GlyphWaiting
        : !_achievementsReadable ? GlyphUnsupported : GlyphNone;

    /// <summary>
    /// Working the known book out failed: the empty state says so instead of "loading", and <see cref="RetryAchievements"/>
    /// tries again. A newer book or a retry clears it.
    /// </summary>
    public bool AchievementsFailed => _achievementsFailed && _achievementBook is not null && _achievementsData is null;

    /// <summary>The page as a failed working out of the shown book leaves it (render scenes); a retry or a newer book clears it.</summary>
    internal void ShowAchievementsFailed()
    {
        if (_achievementBook is null)
        {
            return;
        }

        _achievementsBuild++;
        _achievementsData = null;
        _achievementsFailed = true;
        RaiseAchievementsStatus();
    }

    /// <summary>Works the known book out again after a failure.</summary>
    public RelayCommand RetryAchievements => _retryAchievements ??= new RelayCommand(() =>
    {
        if (_achievementBook is not null)
        {
            BuildAchievements();
        }
    });

    /// <summary>The empty state stands in for the content: no book, or the first one not worked out yet.</summary>
    public bool AchievementsEmptyShown => _achievementsData is null;
}

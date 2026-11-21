namespace Hearthpulse.App;

/// <summary>
/// The player's Hearthstone achievements for the Achievements and Recap pages (docs/achievements-page.md). In a file of
/// its own: what the client reads is named in no source file that also holds a sending call, and the main window only
/// shows it.
/// </summary>
public partial class App
{
    /// <summary>
    /// Hands the main view model the session's achievement book now and on every change. The session raises the change
    /// on its own threads and without a value: the newest book is read on the UI thread.
    /// </summary>
    partial void ShowAchievements()
    {
        _session.AchievementsChanged += () => _ = Dispatcher.BeginInvoke(() => _mainViewModel.SetAchievements(_session.Achievements));
        _mainViewModel.SetAchievements(_session.Achievements);
    }
}

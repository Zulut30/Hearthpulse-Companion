namespace Hearthpulse.UI;

/// <summary>
/// The library pages' data from the game session (Collection, Achievements): each hook subscribes to its session event
/// and hands the main view model the newest value on the UI thread. Each lives in a file of its own
/// (AppHost.Cosmetics.cs, AppHost.Achievements.cs): what the client reads stays out of any source file that also holds a
/// sending call.
/// </summary>
public sealed partial class AppHost
{
    /// <summary>Hands the library pages their data now and on every change (called once at start, after the collection).</summary>
    private void ShowLibraryPages()
    {
        ShowCosmetics();
        ShowAchievements();
    }

    /// <summary>The card backs and coins for the Collection page (<c>GameSession.CosmeticsChanged</c>).</summary>
    partial void ShowCosmetics();

    /// <summary>The achievements for the Achievements and Recap pages (<c>GameSession.AchievementsChanged</c>).</summary>
    partial void ShowAchievements();
}

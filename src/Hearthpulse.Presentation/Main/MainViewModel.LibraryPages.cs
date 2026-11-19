using Hearthpulse.Core;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The library pages after History (Collection, Achievements, Recap): the hooks the shared navigation, language and
/// game-client code calls. Each page's own partial implements its hooks (Collection: <c>MainViewModel.CollectionPage.cs</c>;
/// Achievements and Recap: their partials); a hook nobody implements costs nothing.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The Collection page was shown (<see cref="PageShown"/>): it builds or refreshes its tab.</summary>
    partial void CollectionPageShown();

    /// <summary>The Achievements page was shown (<see cref="PageShown"/>).</summary>
    partial void AchievementsPageShown();

    /// <summary>The Recap page ("Wrapped") was shown (<see cref="PageShown"/>).</summary>
    partial void WrappedPageShown();

    /// <summary>The language changed: the Collection page rebuilds its set names, groups, captions and states.</summary>
    partial void RebuildCollectionPage();

    /// <summary>The language changed: the Achievements page rebuilds its captions and states.</summary>
    partial void RebuildAchievementsPage();

    /// <summary>The language changed: the Recap page rebuilds its period, captions and states.</summary>
    partial void RebuildWrapped();

    /// <summary>The Hearthstone client started or closed: a status line that names the next step changes its text.</summary>
    partial void HearthstoneRunningChanged();
}

/// <summary>
/// The status line under a library page's title (design §0, Main/Pages/LibraryLook): a glyph, where the numbers come from
/// and of what time, and, for saved data, the "from saved data" mark with its tip. Null where a page shows none (no data:
/// the page's empty state says it all).
/// </summary>
/// <param name="Glyph">MDI glyph before the line: read from the game (check), saved (history).</param>
/// <param name="Text">The line itself, with its date ("Read from the game today at 14:05.").</param>
/// <param name="Mark">The mark under the line, empty when the data is confirmed (no mark).</param>
/// <param name="MarkTip">Why the mark is there.</param>
public sealed record LibraryStatus(string Glyph, string Text, string Mark, string MarkTip) : IImmutableView
{
    /// <summary>The data is a saved copy: the mark shows and the page dims its pictures (never its text).</summary>
    public bool Saved => Mark.Length > 0;
}

using Avalonia.Threading;

namespace Hearthpulse.UI;

/// <summary>
/// The player's card backs and coins for the Collection page (docs/collection-page.md). In a file of its own: what the
/// client reads is named in no source file that also holds a sending call, and the main view only shows it.
/// </summary>
public sealed partial class AppHost
{
    /// <summary>
    /// Hands the main view model the session's cosmetics now and on every change. The session raises the change on its
    /// own threads and without a value: the newest cosmetics are read on the UI thread.
    /// </summary>
    partial void ShowCosmetics()
    {
        Session.CosmeticsChanged += () => Dispatcher.UIThread.Post(() => Main.SetCosmetics(Session.Cosmetics));
        Main.SetCosmetics(Session.Cosmetics);
    }
}

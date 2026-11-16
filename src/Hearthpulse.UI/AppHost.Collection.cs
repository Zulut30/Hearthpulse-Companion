using Avalonia.Threading;

namespace Hearthpulse.UI;

/// <summary>
/// The player's collection for the main view (docs/collection.md). In a file of its own: the collection is named in no
/// source file that also holds a sending call (CollectionStoreTests), and the main view only shows it.
/// </summary>
public sealed partial class AppHost
{
    /// <summary>
    /// Hands the main view model the session's collection now and on every change. The session raises the change on its
    /// own threads and without a value: the newest collection is read on the UI thread.
    /// </summary>
    private void ShowCollection()
    {
        Session.CollectionChanged += () => Dispatcher.UIThread.Post(() => Main.SetCollection(Session.Collection));
        Main.SetCollection(Session.Collection);
    }
}

namespace Hearthpulse.App;

/// <summary>
/// The player's collection for the main window (docs/collection.md). In a file of its own: the collection is named in
/// no source file that also holds a sending call (CollectionStoreTests), and the main window only shows it.
/// </summary>
public partial class App
{
    /// <summary>
    /// Hands the main view model the session's collection now and on every change. The session raises the change on its
    /// own threads and without a value: the newest collection is read on the UI thread.
    /// </summary>
    private void ShowCollection()
    {
        _session.CollectionChanged += () => _ = Dispatcher.BeginInvoke(() => _mainViewModel.SetCollection(_session.Collection));
        _mainViewModel.SetCollection(_session.Collection);
    }
}

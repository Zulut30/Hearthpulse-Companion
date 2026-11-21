namespace Hearthpulse.App;

/// <summary>
/// The player's card backs and coins for the Collection page (docs/collection-page.md). In a file of its own: what the
/// client reads is named in no source file that also holds a sending call, and the main window only shows it.
/// </summary>
public partial class App
{
    /// <summary>
    /// Hands the main view model the session's cosmetics now and on every change. The session raises the change on its
    /// own threads and without a value: the newest cosmetics are read on the UI thread.
    /// </summary>
    partial void ShowCosmetics()
    {
        _session.CosmeticsChanged += () => _ = Dispatcher.BeginInvoke(() => _mainViewModel.SetCosmetics(_session.Cosmetics));
        _mainViewModel.SetCosmetics(_session.Cosmetics);
    }
}

#if HEARTHPULSE_WPF
using Hearthpulse.App.Main.Match;
using Hearthpulse.App.Overlay;
#else
using Hearthpulse.UI.Main.Match;
using Hearthpulse.UI.Overlay;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Stats;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

public sealed partial class MainViewModel
{
    /// <summary>
    /// Uploads under way by <see cref="ReplayUploads.KeyOf"/>: a game goes up once at a time, whether its library card
    /// (Recent or Saved, or a card built again for another tab) or the match page asked.
    /// </summary>
    private readonly HashSet<string> _siteUploads = new(StringComparer.Ordinal);

    /// <summary>The card's link on the site, from the uploads read once for the tab (only own games are uploaded).</summary>
    private static string SiteUrlOf(IReadOnlyDictionary<string, ReplayUploadResult> uploads, LibraryCard card) =>
        card is { CanUpload: true, FileName: { } name } && uploads.GetValueOrDefault(ReplayUploads.KeyOf(card.Shelf, name)) is { } upload
            ? upload.Url
            : string.Empty;

    /// <summary>
    /// Uploads the card's replay to the site (plan 2.2), after the confirmation in the card or the match header: the
    /// Share copy without the post-game part (<see cref="ReplayUpload"/>), with the account's sign-in. The link goes to
    /// the clipboard and its page opens in the browser. While it runs the button cancels it; a second upload of the game
    /// cannot start.
    /// </summary>
    internal void UploadLibraryCard(LibraryCard card, string name)
    {
        if (card.SiteWork != SiteWork.None)
        {
            return;
        }

        if (_siteUploads.Contains(ReplayUploads.KeyOf(card.Shelf, name)))
        {
            Library.Failed(Loc.T("Library.Site.Busy"));
            return;
        }

        _libraryAction = UploadAsync(card, name);
        _libraryAction.Forget("Replay upload");
    }

    /// <summary>The question before an upload (the link is public), answered in the card or in the match header.</summary>
    internal void AskSiteUpload(LibraryCard card, string name) =>
        ReplayLibraryViewModel.Ask(card, Loc.T("Library.Site.ConfirmUpload"), Loc.T("Library.Site.ConfirmUploadYes"), () => UploadLibraryCard(card, name));

    /// <summary>
    /// The match page's "View in browser" (docs/web-replays.md): a game on the site opens at once; one not uploaded yet
    /// is asked about first, then goes up the library's way and opens when it is there. While it goes up the same button
    /// cancels it. Signed out, it says so before asking.
    /// </summary>
    internal void ViewLibraryCardInBrowser(LibraryCard card)
    {
        if (card is { SiteWork: SiteWork.Uploading, SiteCancel: { } cancel })
        {
            cancel.Cancel();
            return;
        }

        if (card is not { CanUpload: true, SiteWork: SiteWork.None, FileName: { } name })
        {
            return;
        }

        if (card.OnSite)
        {
            OpenLibraryCardOnSite(card);
            return;
        }

        _libraryAction = ViewInBrowserAsync(card, name);
        _libraryAction.Forget("Replay in the browser");
    }

    private async Task ViewInBrowserAsync(LibraryCard card, string name)
    {
        // The uploads file is the one record of what is on the site: a card built before it was read (or another card
        // of the same game that uploaded it) learns its link here instead of sending the game again.
        var uploads = LibraryUploads;
        var key = ReplayUploads.KeyOf(card.Shelf, name);
        if (await Task.Run(() => uploads.Get(key)).ConfigureAwait(true) is { } upload)
        {
            ShowOnSite(key, upload.Url, card);
            OpenLibraryCardOnSite(card);
            return;
        }

        if (_siteUploads.Contains(key))
        {
            Library.Failed(Loc.T("Library.Site.Busy"));
        }
        else if (_account is not { SignedIn: true })
        {
            Library.Failed(Loc.T("Library.Site.SignIn"));
        }
        else
        {
            AskSiteUpload(card, name);
        }
    }

    /// <summary>Every card of the game (<paramref name="key"/>) shows its link, or none: the library's and the match header's.</summary>
    private void ShowOnSite(string key, string url, LibraryCard card)
    {
        card.SiteUrl = url;
        foreach (var other in Library.AllCards.Append(_matchPage?.HeaderCard))
        {
            if (other is { FileName: { } file } && other != card && ReplayUploads.KeyOf(other.Shelf, file) == key)
            {
                other.SiteUrl = url;
            }
        }
    }

    private async Task UploadAsync(LibraryCard card, string name)
    {
        if (!card.CanUpload || card.SiteWork != SiteWork.None)
        {
            return;
        }

        if (_account is not { SignedIn: true } account)
        {
            Library.Failed(Loc.T("Library.Site.SignIn"));
            return;
        }

        var key = ReplayUploads.KeyOf(card.Shelf, name);
        if (!_siteUploads.Add(key))
        {
            Library.Failed(Loc.T("Library.Site.Busy"));
            return;
        }

        using var cancel = new CancellationTokenSource();
        card.SiteCancel = cancel;
        card.SiteWork = SiteWork.Uploading;
        try
        {
            var cards = _cards;
            var files = LibraryFiles;
            var uploads = LibraryUploads;
            var shelf = card.Shelf;
            var body = await Task.Run(() => files.Load(shelf, name) is { } text && ReplayUpload.Prepare(text, cards) is { } upload
                ? ReplayUpload.Encode(upload)
                : null, cancel.Token).ConfigureAwait(true);
            if (body is null)
            {
                Library.Failed(Loc.T("Library.ShareFailed"));
                return;
            }

            // The game's profile id links the replay in the site's «Последние игры»: only for a game the profile upload
            // sends, while it is on - the site then already has the game (the id is derived from its start time).
            var gameId = Settings.UploadGamesToProfile && card.HistoryKey is { } historyKey
                && _libraryGames.Concat(_recentGames ?? []).FirstOrDefault(g => g.Key == historyKey) is { } record
                ? Core.Accounts.ProfileEvents.ProfileGameId(record)
                : null;
            var (result, error) = await account.UploadReplayAsync(body, gameId, cancel.Token).ConfigureAwait(true);
            if (result is null)
            {
                Library.Failed(Loc.Or("Library.Site.Error." + error, Loc.T("Library.Site.Error.SERVER_ERROR")));
                return;
            }

            await Task.Run(() => uploads.Set(key, result), CancellationToken.None).ConfigureAwait(true);
            ShowOnSite(key, result.Url, card);
            // The page opens in the browser as well (the link is on the clipboard too): only https on the site's host.
            Library.Uploaded(result.Url, opened: account.OpenSiteLink(result.Url));
            await RefreshAccountCountsAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Library.Failed(Loc.T("Library.Site.Cancelled"));
        }
        finally
        {
            _siteUploads.Remove(key);
            card.SiteCancel = null;
            card.SiteWork = SiteWork.None;
        }
    }

    /// <summary>Deletes the card's replay from the site with the token its upload handed out.</summary>
    internal void RemoveLibraryCardFromSite(LibraryCard card, string name)
    {
        if (card.SiteWork != SiteWork.None)
        {
            return;
        }

        _libraryAction = RemoveFromSiteAsync(card, name);
        _libraryAction.Forget("Replay removal from the site");
    }

    private async Task RemoveFromSiteAsync(LibraryCard card, string name)
    {
        if (card.SiteWork != SiteWork.None || _account is not { } account)
        {
            return;
        }

        card.SiteWork = SiteWork.Removing;
        try
        {
            var uploads = LibraryUploads;
            var key = ReplayUploads.KeyOf(card.Shelf, name);
            if (await Task.Run(() => uploads.Get(key)).ConfigureAwait(true) is not { } upload)
            {
                ShowOnSite(key, string.Empty, card);
                return;
            }

            var error = await account.DeleteReplayAsync(upload.Id, upload.DeleteToken).ConfigureAwait(true);
            if (error is not (null or "NOT_FOUND"))
            {
                // Still on the site: the link and the token stay, so the delete can be tried again.
                Library.Failed(Loc.Or("Library.Site.DeleteError." + error, Loc.T("Library.Site.DeleteError.SERVER_ERROR")));
                return;
            }

            await Task.Run(() => uploads.Set(key, null)).ConfigureAwait(true);
            ShowOnSite(key, string.Empty, card);
            Library.Succeeded(Loc.T(error is null ? "Library.Site.Removed" : "Library.Site.Gone"));
            await RefreshAccountCountsAsync().ConfigureAwait(true);
        }
        finally
        {
            card.SiteWork = SiteWork.None;
        }
    }

    internal void ShareLibraryCard(LibraryCard card, string name)
    {
        _libraryAction = ShareAsync(card.Shelf, name);
        _libraryAction.Forget("Replay share");
    }

    private async Task ShareAsync(ReplayShelf shelf, string name)
    {
        var cards = _cards;
        var files = LibraryFiles;
        var path = await Task.Run(() => files.ShareCopy(shelf, name, cards)).ConfigureAwait(true);
        if (path is null)
        {
            Library.Failed(Loc.T("Library.ShareFailed"));
        }
        else
        {
            Library.RequestShare(path);
        }
    }
}

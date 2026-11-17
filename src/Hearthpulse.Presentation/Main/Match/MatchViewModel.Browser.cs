#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main.Match;
#else
namespace Hearthpulse.UI.Main.Match;
#endif

/// <summary>
/// "View in browser" in the match page's header (<c>docs/web-replays.md</c>): the game's page on hearthpulse.net, the
/// same upload as its library card's (<see cref="HeaderCard"/>, one record in <c>replays\uploads.json</c>).
/// </summary>
public sealed partial class MatchViewModel
{
    private RelayCommand? _viewHeaderInBrowser;

    /// <summary>Only the player's own game with its replay file goes to the site (not an imported one), as in the library.</summary>
    public bool ShowHeaderBrowser => HeaderCard is { CanUpload: true };

    /// <summary>mdi-open-in-new, or mdi-close while the game goes up (the button then cancels it).</summary>
    public string HeaderBrowserGlyph => HeaderCard is { SiteWork: SiteWork.Uploading } ? "\U000F0156" : "\U000F03CC";

    /// <summary>What the button does now: open the page, upload and open it (asked first), or cancel the upload.</summary>
    public string HeaderBrowserTip => HeaderCard switch
    {
        { SiteWork: SiteWork.Uploading } => Loc.T("Library.Site.Uploading"),
        { OnSite: true } => Loc.T("Match.Browser.OpenTip"),
        _ => Loc.T("Match.Browser.UploadTip"),
    };

    /// <summary>
    /// Opens the game on the site; a game not there yet is asked about in the header first (the link is public), then
    /// uploaded and opened. While it goes up, cancels it.
    /// </summary>
    public RelayCommand ViewHeaderInBrowser => _viewHeaderInBrowser ??= new RelayCommand(() =>
    {
        if (HeaderCard is not { } card)
        {
            return;
        }

        if (card.SiteWork == SiteWork.None)
        {
            ClearHeaderStatus();
        }

        MirrorLibraryStatus();
        _owner.ViewLibraryCardInBrowser(card);
    });

    /// <summary>The header card's site state changed: the button's glyph, tip and presence follow.</summary>
    private void RaiseHeaderBrowser()
    {
        Raise(nameof(ShowHeaderBrowser));
        Raise(nameof(HeaderBrowserGlyph));
        Raise(nameof(HeaderBrowserTip));
    }
}

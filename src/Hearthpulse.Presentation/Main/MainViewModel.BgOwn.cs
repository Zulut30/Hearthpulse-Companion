#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Pool;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// One hero of Hearthpulse's own statistics. Every cell is a value over its sample (<see cref="PickRange"/>,
/// <see cref="PlaceDetail"/>, <see cref="Top4Range"/>); a value the pool keeps back reads "not enough data" and its
/// lower line holds only the count the pool did publish, or nothing.
/// </summary>
public sealed record BgOwnRow(string CardId, string Name, string Offered, string Picked, string PickRate, string PickRange,
    string Place, string PlaceDetail, string Top4, string Top4Range, bool IsSmall) : IImmutableView;

/// <summary>
/// The block "Hearthpulse statistics (own games)" around its rows: with rows, <see cref="Summary"/> (build, games,
/// installations, period, publication date) above the table and <see cref="Note"/> under it; without, one empty
/// state in place of the table.
/// </summary>
public sealed record BgOwnView(bool HasRows, string Summary, string Note, string EmptyText, string EmptyGlyph) : IImmutableView;

/// <summary>
/// Hearthpulse's own Battlegrounds hero statistics on the Heroes tab (metric <c>bg.hero.offer_pick_placement</c>,
/// docs/analytics-api.md): the heroes of the newest game build as the stats pool published them. These numbers stand
/// beside the tab's external list and never mix with it: a value the pool keeps back is shown as missing, not filled
/// from the external numbers.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Under this many games in a value's denominator it is shown without decimals (the sample does not carry them).</summary>
    private const int FineSample = 300;

    private OwnBgHeroesResult _bgOwnResult = OwnBgHeroesResult.Of(OwnBgHeroesState.NotConfigured);
    private BgOwnView _bgOwn = new(false, string.Empty, string.Empty, string.Empty, NoticeGlyph);

    /// <summary>The heroes of the newest build, in the pool's order (most offered first).</summary>
    public SyncList<BgOwnRow> BgOwnRows { get; } = [];

    public BgOwnView BgOwn { get => _bgOwn; private set => Set(ref _bgOwn, value); }

    /// <summary>
    /// The block is on the Heroes tab while the solo list is shown (the metric has no Duos numbers), and only when a
    /// stats pool is set up and knows the metric.
    /// </summary>
    public bool ShowBgOwn =>
        _bgTab == "Heroes" && !HeroDuos && _bgOwnResult.State is not (OwnBgHeroesState.NotConfigured or OwnBgHeroesState.Unsupported);

    /// <summary>The own statistics after a load (<see cref="OwnBgHeroes.LoadAsync(HttpClient, AppSettings, string?, TimeProvider?, CancellationToken)"/>); the hosts call it on the UI thread after every refresh.</summary>
    public void SetBgOwn(OwnBgHeroesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _bgOwnResult = result;
        RebuildBgOwn();
    }

    /// <summary>The block again from the last load: the card database or the language changed.</summary>
    private void RebuildBgOwn()
    {
        var result = _bgOwnResult;
        var rows = result.Group is { } group && result.State is OwnBgHeroesState.Ok or OwnBgHeroesState.SmallSample
            ? group.Heroes.Select(OwnRow).ToList()
            : [];
        BgOwnRows.Sync(rows);
        BgOwn = rows.Count > 0
            ? new(true, OwnSummary(result), OwnNote(result.Group!, rows), string.Empty, NoticeGlyph)
            : new(false, string.Empty, string.Empty, OwnEmptyText(result), NoticeGlyph);
        Raise(nameof(ShowBgOwn));
    }

    private BgOwnRow OwnRow(BgHeroAnalyticsRow hero)
    {
        var small = Loc.T("Main.BgOwn.Small");
        var name = _cards.NameOf(hero.HeroCardId);
        var pick = hero.PickRate;
        var place = hero.Placement;
        var top = hero.Top4;
        return new(
            hero.HeroCardId,
            string.IsNullOrEmpty(name) ? hero.HeroCardId : name,
            hero.Offered.ToString("N0", CultureInfo.CurrentCulture),
            hero.Picked?.ToString("N0", CultureInfo.CurrentCulture) ?? small,
            pick?.Value is { } share ? Share(share, pick.Denominator) : small,
            pick is { Value: not null, Low: { } pickLow, High: { } pickHigh } ? Range(Share(pickLow, pick.Denominator), Share(pickHigh, pick.Denominator)) : string.Empty,
            place?.Mean is { } mean ? Place(mean, place.Count) : small,
            // The games behind the place are fewer than "picked" (a game without a result has no place): always named.
            place is null ? string.Empty : Joined(
                place is { Mean: not null, Low: { } low, High: { } high } ? Range(Place(low, place.Count), Place(high, place.Count)) : null,
                Loc.P("Main.BgOwn.Games", place.Count)),
            top?.Value is { } top4 ? Share(top4, top.Denominator) : small,
            top is { Value: not null, Low: { } topLow, High: { } topHigh } ? Range(Share(topLow, top.Denominator), Share(topHigh, top.Denominator)) : string.Empty,
            !string.Equals(hero.Quality, "ok", StringComparison.Ordinal));
    }

    private static string Share(double value, int sample) => value.ToString(sample < FineSample ? "P0" : "P1", CultureInfo.CurrentCulture);

    private static string Place(double value, int sample) => value.ToString(sample < FineSample ? "0.0" : "0.00", CultureInfo.CurrentCulture);

    private static string Range(string low, string high) => low + "–" + high;

    private string OwnSummary(OwnBgHeroesResult result)
    {
        var group = result.Group!;
        var snapshot = result.Snapshot!;
        var days = snapshot.ToDay - snapshot.FromDay;
        return Joined(
            group.Build is { } build ? Loc.F("Main.BgOwn.Build", build.ToString(CultureInfo.InvariantCulture)) : Loc.T("Main.BgOwn.BuildUnknown"),
            Loc.P("Main.BgOwn.Games", group.Captures) + " " + Loc.P("Main.BgOwn.FromInstalls", group.Sources),
            days > 0 ? Loc.P("Main.Bg.RangeDays", days) : null,
            snapshot.Published > 0 ? Loc.F("Main.BgOwn.Published", DateTimeOffset.FromUnixTimeMilliseconds(snapshot.Published).LocalDateTime) : null,
            snapshot.Stale ? Loc.T("Main.BgOwn.Stale") : null,
            result.FromOldCopy ? Loc.F("Main.BgOwn.OldCopy", result.FetchedAt.ToLocalTime()) : null);
    }

    private static string OwnNote(BgHeroAnalyticsGroup group, List<BgOwnRow> rows) => string.Join(
        ' ',
        new[]
        {
            Loc.T("Main.BgOwn.Caption"),
            rows.Exists(r => r.IsSmall) ? Loc.T("Main.BgOwn.SmallNote") : null,
            group.Suppressed > 0 ? Loc.P("Main.BgOwn.Suppressed", group.Suppressed) : null,
        }.Where(part => part is not null));

    private static string OwnEmptyText(OwnBgHeroesResult result) => result.State switch
    {
        OwnBgHeroesState.NotPublished => Loc.T("Main.BgOwn.NotPublished"),
        OwnBgHeroesState.Rebuilding => Loc.T("Main.BgOwn.Rebuilding"),
        OwnBgHeroesState.Offline => Loc.T("Main.BgOwn.Offline"),
        // Published, yet no hero of the newest build was offered to five installations.
        OwnBgHeroesState.SmallSample or OwnBgHeroesState.Ok => Loc.T("Main.BgOwn.NoRows"),
        _ => string.Empty,
    };
}

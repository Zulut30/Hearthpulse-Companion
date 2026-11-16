using System.Globalization;
using System.Text.RegularExpressions;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main.Cards;
#else
namespace Hearthpulse.UI.Main.Cards;
#endif

/// <summary>An archetype whose lists run the card: share of its lists, copies, the archetype's own win rate and games.</summary>
/// <param name="Edge">The archetype's win rate minus 50 %, for the number's colour.</param>
public sealed record CardArchetypeRow(string Name, string ClassName, string HeroId, string Lists, string Share, string Copies, string WinRate, double Edge, string Games)
    : IImmutableView;

/// <summary>The card in one archetype's opening hand: keep rate, win rate in hand, impact and the sample.</summary>
/// <param name="Edge">The impact as a fraction, for the number's colour.</param>
public sealed record CardMulliganRow(string Name, string ClassName, string HeroId, string KeepRate, string WinRate, string Impact, double Edge, string Hands) : IImmutableView;

/// <summary>
/// The card page: one Constructed card in the format of the Standard or Wild page - the card itself, its numbers in
/// ranked games, the archetypes that run it, its mulligan by archetype and the player's own games with it
/// (docs/card-page.md). Every number has its source's date and sample where the source gives them; a number the source
/// does not give has no tile.
/// </summary>
public sealed partial class CardPageViewModel : Observable
{
    private string _cardId = string.Empty;
    private string _name = string.Empty;
    private string _text = string.Empty;
    private IReadOnlyList<string> _facts = [];
    private bool _loading;
    private IReadOnlyList<StatTile> _tiles = [];
    private string _statsSource = string.Empty;
    private string _statsEmpty = string.Empty;
    private bool _statsFailed;
    private IReadOnlyList<CardArchetypeRow> _archetypes = [];
    private string _archetypesEmpty = string.Empty;
    private IReadOnlyList<CardMulliganRow> _mulligan = [];
    private string _mulliganEmpty = string.Empty;
    private string _metaSource = string.Empty;
    private IReadOnlyList<StatTile> _mineTiles = [];
    private string _mineNote = string.Empty;
    private string _mineEmpty = string.Empty;

    public CardPageViewModel(MainViewModel owner)
    {
        Back = new RelayCommand(owner.CloseCard);
        Retry = new RelayCommand(owner.RetryCard);
    }

    public RelayCommand Back { get; }

    /// <summary>Asks for the card's numbers again after a load that failed (<see cref="StatsFailed"/>).</summary>
    public RelayCommand Retry { get; }

    /// <summary>The card shown, for its art; empty while no card is open.</summary>
    public string CardId { get => _cardId; private set => Set(ref _cardId, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>The card text without markup.</summary>
    public string Text { get => _text; private set => Set(ref _text, value); }

    /// <summary>Cost, class, set and format, as chips under the name.</summary>
    public IReadOnlyList<string> Facts { get => _facts; private set => Set(ref _facts, value); }

    /// <summary>The numbers are on their way: the card itself is shown already.</summary>
    public bool Loading { get => _loading; private set => Set(ref _loading, value); }

    /// <summary>The card's numbers in ranked games; a number the source did not give has no tile.</summary>
    public IReadOnlyList<StatTile> Tiles { get => _tiles; private set => Set(ref _tiles, value); }

    /// <summary>"Hearthpulse data · Gold · last 14 days · updated 16 Nov 06:04", with the delay line under it when the source is late.</summary>
    public string StatsSource { get => _statsSource; private set => Set(ref _statsSource, value); }

    /// <summary>Why there are no tiles: the card is not in the list, or the list could not be loaded. Empty with tiles.</summary>
    public string StatsEmpty { get => _statsEmpty; private set => Set(ref _statsEmpty, value); }

    /// <summary>
    /// The card list could not be loaded and there is no older copy (offline, the server too slow): the section says so
    /// and offers <see cref="Retry"/>. Never set while a load runs, so the page does not stay "loading".
    /// </summary>
    public bool StatsFailed { get => _statsFailed; private set => Set(ref _statsFailed, value); }

    public IReadOnlyList<CardArchetypeRow> Archetypes { get => _archetypes; private set => Set(ref _archetypes, value); }

    public string ArchetypesEmpty { get => _archetypesEmpty; private set => Set(ref _archetypesEmpty, value); }

    public IReadOnlyList<CardMulliganRow> Mulligan { get => _mulligan; private set => Set(ref _mulligan, value); }

    public string MulliganEmpty { get => _mulliganEmpty; private set => Set(ref _mulliganEmpty, value); }

    /// <summary>Where the archetype and mulligan rows come from: the loaded meta's source, rank, period and date.</summary>
    public string MetaSource { get => _metaSource; private set => Set(ref _metaSource, value); }

    /// <summary>The player's own games with the card.</summary>
    public IReadOnlyList<StatTile> MineTiles { get => _mineTiles; private set => Set(ref _mineTiles, value); }

    /// <summary>The sample behind <see cref="MineTiles"/>: own games of the format, and those with card data.</summary>
    public string MineNote { get => _mineNote; private set => Set(ref _mineNote, value); }

    public string MineEmpty { get => _mineEmpty; private set => Set(ref _mineEmpty, value); }

    /// <summary>The card at once (name, art, text), before its numbers are in.</summary>
    internal void BeginLoading(Card card, string formatType, CardDb cards)
    {
        ShowCard(card, formatType, CardPages.PlainText(card.Text, cards.Locale));
        Tiles = [];
        StatsSource = string.Empty;
        StatsEmpty = string.Empty;
        StatsFailed = false;
        Archetypes = [];
        ArchetypesEmpty = string.Empty;
        Mulligan = [];
        MulliganEmpty = string.Empty;
        MetaSource = string.Empty;
        MineTiles = [];
        MineNote = string.Empty;
        MineEmpty = string.Empty;
        Loading = true;
    }

    /// <param name="meta">The meta the page was built from (its source, rank and period name the rows' slice), or null.</param>
    /// <param name="statsDelay">The "update is delayed" line of the card list's source, or empty.</param>
    /// <param name="metaDelay">The same for the meta.</param>
    internal void Show(CardPage page, ConstructedMeta? meta, string statsDelay, string metaDelay)
    {
        ShowCard(page.Card, page.FormatType, page.Text);

        Tiles = page.Stats is { } stats ? StatTiles(stats) : [];
        var slice = Joined(
        [
            page.StatsDate is { } date ? Loc.F("Source.Health.Updated", date.ToLocalTime()) : HearthpulseData.Attribution,
            RankLabel(page.RankRange),
            PeriodLabel(page.TimeRange),
        ]);
        StatsSource = Tiles.Count == 0 ? string.Empty : MainViewModel.WithDelay(slice, statsDelay);
        StatsEmpty = Tiles.Count > 0 ? string.Empty : Loc.T(page.StatsLoaded ? "Card.Stats.None" : "Card.Stats.Unavailable");
        StatsFailed = !page.StatsLoaded;

        Archetypes = page.Archetypes.Select(a => new CardArchetypeRow(
            a.Name,
            ConstructedMeta.ClassName(a.Class),
            MainViewModel.ClassHero(a.Class),
            Loc.P("Card.Archetypes.Lists", a.Lists, a.ListsWithCard),
            Percent(a.ListShare, "P0"),
            a.AverageCopies.ToString("0.#", CultureInfo.CurrentCulture),
            Percent(a.WinRate, "P1"),
            a.WinRate - 0.5,
            a.Games.ToString("N0", CultureInfo.CurrentCulture))).ToList();
        ArchetypesEmpty = Archetypes.Count > 0 ? string.Empty : Loc.T(meta is null ? "Card.Archetypes.NoMeta" : "Card.Archetypes.None");

        Mulligan = page.Mulligan.Select(m => new CardMulliganRow(
            m.Name,
            ConstructedMeta.ClassName(m.Class),
            MainViewModel.ClassHero(m.Class),
            Percent(m.KeepRate, "P0"),
            Percent(m.InHandWinRate, "P1"),
            Loc.F("Card.Mulligan.Points", Signed(m.Impact * 100)),
            Math.Round(m.Impact * 100, 1) / 100,
            m.InHand.ToString("N0", CultureInfo.CurrentCulture))).ToList();
        MulliganEmpty = Mulligan.Count > 0 ? string.Empty : Loc.T(meta is null ? "Card.Archetypes.NoMeta" : "Card.Mulligan.None");

        var source = meta is null ? string.Empty : Joined(
        [
            Loc.F("Card.Meta.Source", meta.Source, ConstructedMeta.Label(ConstructedMeta.Ranks, meta.Rank), ConstructedMeta.Label(ConstructedMeta.Periods, meta.Period)),
            page.MetaDate is { } updated ? Loc.F("Card.Updated", updated.ToLocalTime()) : null,
        ]);
        MetaSource = Archetypes.Count == 0 && Mulligan.Count == 0 ? string.Empty : MainViewModel.WithDelay(source, metaDelay);

        ShowMine(page.Mine);
        Loading = false;
    }

    internal void Clear()
    {
        CardId = string.Empty;
        Name = string.Empty;
        Text = string.Empty;
        Facts = [];
        Tiles = [];
        Archetypes = [];
        Mulligan = [];
        MineTiles = [];
        StatsFailed = false;
        Loading = false;
    }

    private void ShowCard(Card card, string formatType, string text)
    {
        CardId = card.Id;
        Name = card.Name;
        Text = text;
        Facts = new[]
        {
            Loc.F("Card.Cost", card.Cost),
            ConstructedMeta.ClassName(card.CardClass),
            SetName(card.Set),
            Loc.T(formatType == "FT_WILD" ? "Main.Nav.Wild" : "Main.Nav.Standard"),
        }.Where(s => s.Length > 0).ToList();
    }

    private static List<StatTile> StatTiles(CardStat s)
    {
        var tiles = new List<StatTile>();
        if (s.DeckShare is { } share)
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.DeckShare"), Percent(share, "P1"), s.AverageCopies is { } copies ? Loc.F("Card.Stats.Copies", copies) : string.Empty));
        }

        if (s.DeckWinRate is { } deck)
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.DeckWinRate"), Percent(deck, "P1"), Loc.T("Card.Stats.DeckWinRateDetail"), MetaTiers.WinRateSign(deck)));
        }

        if (s.DrawnWinRate is { } drawn)
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.Drawn"), Percent(drawn, "P1"), Loc.T("Card.Stats.DrawnDetail"), MetaTiers.WinRateSign(drawn)));
        }

        if (s is { PlayedWinRate: { } played, TimesPlayed: { } times })
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.Played"), Percent(played, "P1"), Loc.P("Card.Stats.PlayedDetail", times), MetaTiers.WinRateSign(played)));
        }

        if (s.KeepRate is { } keep)
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.Kept"), Percent(keep, "P0"),
                s.OpeningHandWinRate is { } hand ? Loc.F("Card.Stats.KeptDetail", Percent(hand, "P1")) : string.Empty));
        }

        if (s.AverageTurnPlayed is { } turn)
        {
            tiles.Add(new StatTile(Loc.T("Card.Stats.Turn"), turn.ToString("0.0", CultureInfo.CurrentCulture), Loc.T("Card.Stats.TurnDetail")));
        }

        return tiles;
    }

    private void ShowMine(CardPageMine mine)
    {
        var seen = mine.WhenDrawn.Games > 0 || mine.WhenPlayed.Games > 0 || mine.Offered > 0;
        MineTiles = !seen ? [] : new[]
        {
            Record("Card.Mine.Drawn", mine.WhenDrawn),
            Record("Card.Mine.Played", mine.WhenPlayed),
            mine.Offered > 0 ? new StatTile(Loc.T("Card.Mine.Kept"), Loc.F("Card.Mine.KeptOf", mine.Kept, mine.Offered), Loc.T("Card.Mine.KeptDetail")) : null,
        }.OfType<StatTile>().ToList();
        MineNote = seen ? Loc.P("Card.Mine.Note", mine.TrackedGames, mine.Games) : string.Empty;
        MineEmpty = seen ? string.Empty : Loc.T(mine.Games == 0 ? "Card.Mine.NoGames" : "Card.Mine.None");
    }

    private static StatTile? Record(string label, WinLoss record) => record.Games == 0
        ? null
        : new StatTile(Loc.T(label), record.WinRate is { } rate ? Percent(rate, "P0") : "—", Loc.P("Card.Mine.Record", record.Games, record.Wins, record.Losses),
            // A handful of own games is no verdict: the number stays neutral until there are enough.
            record.WinRate is { } r && record.Games >= DeckDetails.SmallSample ? MetaTiers.WinRateSign(r) : 0);

    /// <summary>"Gold" for GOLD; empty for a rank the app has no name for (never the server's code).</summary>
    private static string RankLabel(string? rank) => rank is { Length: > 0 } ? Loc.Or("Card.Rank." + rank, string.Empty) : string.Empty;

    /// <summary>"last 14 days" for LAST_14_DAYS, "current patch" for CURRENT_PATCH; empty for anything else.</summary>
    private static string PeriodLabel(string? range) =>
        range is null ? string.Empty
        : Days().Match(range) is { Success: true } days ? Loc.P("Card.Period.Days", int.Parse(days.Groups[1].Value, CultureInfo.InvariantCulture))
        : range == "CURRENT_PATCH" ? Loc.T("Card.Period.Patch")
        : string.Empty;

    /// <summary>The set as the card data names it: card data has the set's code only ("EMERALD_DREAM"), shown as words.</summary>
    private static string SetName(string set) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(set.Replace('_', ' ').ToLowerInvariant());

    private static string Percent(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    private static string Signed(double points)
    {
        var rounded = Math.Round(points, 1);
        var text = Math.Abs(rounded).ToString("0.0", CultureInfo.CurrentCulture);
        return rounded > 0 ? "+" + text : rounded < 0 ? "−" + text : text;
    }

    private static string Joined(IEnumerable<string?> parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    [GeneratedRegex(@"^LAST_(\d{1,3})_DAYS?$")]
    private static partial Regex Days();
}

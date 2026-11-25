#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.ComponentModel;
using System.Globalization;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

public sealed partial class MainViewModel
{
    /// <summary>Games an archetype needs before the tier list shows it (hearthpulse.net's "Minimum games" choices).</summary>
    public static IReadOnlyList<int> MetaMinGameChoices { get; } = [100, 250, 500, 1000, 2500, 5000];

    /// <summary>Community stats by format key ("standard", "wild"), as the host loaded them.</summary>
    private readonly Dictionary<string, ConstructedMeta> _constructedMetas = new(StringComparer.Ordinal);
    private IReadOnlyList<MetaDeckRow> _archetypeDecks = [];
    private IReadOnlyList<MulliganRow> _mulligan = [];
    private IReadOnlyList<MetaChoice> _metaClasses = [];
    private IReadOnlyList<MatchupHead> _matchupHeads = [];
    private IReadOnlyList<MatchupMatrixRow> _matchupMatrix = [];
    private MetaArchetypeRow? _selectedArchetype;
    private string _metaClass = string.Empty;
    private string _metaSort = "WinRate";
    private bool _metaSortAscending;
    private bool _metaTable;
    private int _metaMinGames = MetaMinGameChoices[0];
    private bool _metaLoading = true;
    private bool _metaUnavailable;
    private bool _metaReloading;
    private ConstructedMeta? _earlyGivenUp;
    private string? _matrixFormat;
    private Func<string> _constructedMetaStatus = () => Loc.T("Main.Meta.Loading");
    private string _archetypeSummary = string.Empty;
    private Func<string> _copyNote = () => string.Empty;
    private RelayCommand? _setMetaSort;
    private RelayCommand? _setMetaView;
    private RelayCommand? _setMetaRank;
    private RelayCommand? _setMetaPeriod;
    private RelayCommand? _setMetaClass;
    private RelayCommand? _setMetaMinGames;
    private RelayCommand? _selectArchetype;
    private RelayCommand? _copyDeck;

    /// <summary>
    /// Raised when the page needs stats: a format key ("standard", "wild") the page shows and has none for, or null when
    /// the player changed a meta filter (every format in use loads again). The host calls <see cref="SetConstructedMeta"/>.
    /// </summary>
    public event Action<string?>? ConstructedMetaRequested;

    public IReadOnlyList<MetaChoice> MetaRanks => Choices(ConstructedMeta.Ranks, Settings.MetaRank);

    public IReadOnlyList<MetaChoice> MetaPeriods => Choices(ConstructedMeta.Periods, Settings.MetaPeriod);

    public IReadOnlyList<MetaChoice> MetaMinGames => MetaMinGameChoices
        .Select(g => new MetaChoice(g.ToString(CultureInfo.InvariantCulture), g.ToString("N0", CultureInfo.CurrentCulture), g == _metaMinGames))
        .ToList();

    /// <summary>"All classes" plus every class with an archetype in the current data.</summary>
    public IReadOnlyList<MetaChoice> MetaClasses { get => _metaClasses; private set => Set(ref _metaClasses, value); }

    /// <summary>
    /// The archetypes in table order (the "Table" view), numbered. One list for the life of the page, changed in place:
    /// a new sort, class or minimum of games keeps the rows that stay as they were, and so their containers.
    /// </summary>
    public SyncList<MetaArchetypeRow> MetaArchetypes { get; } = [];

    /// <summary>
    /// The same archetypes by tier, S to D (the "Tiers" view), as one flat list: a <see cref="TierHeadRow"/>, then the
    /// <see cref="MetaArchetypeRow"/>s of that tier; an empty tier is its head alone. Changed in place, as
    /// <see cref="MetaArchetypes"/>, whose row objects it shares.
    /// </summary>
    public SyncList<object> MetaTierRows { get; } = [];

    public bool HasMetaArchetypes => MetaArchetypes.Count > 0;

    /// <summary>Whether the tier list shows one flat sortable table instead of tier groups.</summary>
    public bool MetaTableView { get => _metaTable; private set { if (Set(ref _metaTable, value)) Raise(nameof(MetaTierView)); } }

    public bool MetaTierView => !_metaTable;

    public MetaSortArrows MetaArrows => new(Arrow("Tier"), Arrow("Name"), Arrow("WinRate"), Arrow("Popularity"), Arrow("Games"));

    /// <summary>The page's stats are being downloaded and there is nothing of its format to show yet: the tabs' loading state.</summary>
    public bool MetaLoading { get => _metaLoading; private set { if (Set(ref _metaLoading, value)) RaiseMetaStates(); } }

    /// <summary>
    /// A filter changed and the stats shown are still the old filter's: they stay where they are, dimmed, with a busy
    /// cue in the card's head, until the new ones replace them in one step. Never a loading box under the old rows.
    /// </summary>
    public bool MetaRefreshing => _metaReloading && PageMeta is { } meta && !OfCurrentFilter(meta);

    /// <summary>
    /// The tier list came ahead of the full stats (<see cref="ConstructedMeta.IsEarly"/>): the mulligan numbers and the
    /// matchups are still on their way, so their empty lists mean "not yet", not "none". Over when the full stats come,
    /// or when the load ends without them.
    /// </summary>
    private bool MetaDetailWaiting => PageMeta is { IsEarly: true } meta && !ReferenceEquals(meta, _earlyGivenUp);

    /// <summary>The top decks wait for new stats or for the prices of new lists: the rows shown stay, dimmed.</summary>
    public bool MetaDecksRefreshing => MetaRefreshing || _metaDecksHeld;

    /// <summary>The heat map shown is the old filter's, or waits for the new matrix: it stays, dimmed, with the busy cue.</summary>
    public bool MetaMatchupsRefreshing => HasMatchupMatrix && (MetaRefreshing || MetaDetailWaiting);

    /// <summary>The matchups tab has no matrix to show while one is on its way: its loading state.</summary>
    public bool MetaMatchupsLoading => _metaLoading || (MetaDetailWaiting && !HasMatchupMatrix);

    /// <summary>The page's format could not be loaded: the "unavailable" state shows, not the filters' empty one.</summary>
    public bool MetaUnavailable { get => _metaUnavailable; private set { if (Set(ref _metaUnavailable, value)) RaiseMetaStates(); } }

    /// <summary>Stats are in but nothing passes the filters: the empty state shows.</summary>
    public bool MetaEmpty => MetaStatusShown && MetaArchetypes.Count == 0;

    /// <summary>Stats are in and the top-decks tab has no list.</summary>
    public bool MetaDecksEmpty => MetaStatusShown && MetaDecks.Count == 0 && !CanBuildOnly;

    /// <summary>Stats are in and the matchups tab has no matrix (not while the matrix is still on its way).</summary>
    public bool MetaMatchupsEmpty => MetaStatusShown && !HasMatchupMatrix && !MetaDetailWaiting;

    /// <summary>The status line under each tab (games, source, updated): only with stats in, never a second "Loading".</summary>
    public bool MetaStatusShown => !_metaLoading && !_metaUnavailable;

    /// <summary>The "1st"/"Coin" columns: shown only when the source has those numbers, not as a column of dashes.</summary>
    public bool MetaHasPlaySplit => MetaArchetypes.Any(r => r.HasPlaySplit);

    /// <summary>
    /// Most played decklists, best first among those with enough games. One list for the life of the page, changed in
    /// place: a row that shows what it showed before keeps its container (and its hero art) through a rebuild.
    /// </summary>
    public SyncList<MetaDeckRow> MetaDecks { get; } = [];

    public IReadOnlyList<MetaDeckRow> ArchetypeDecks { get => _archetypeDecks; private set => Set(ref _archetypeDecks, value); }

    public IReadOnlyList<MulliganRow> Mulligan { get => _mulligan; private set { if (Set(ref _mulligan, value)) Raise(nameof(MulliganEmptyShown)); } }

    /// <summary>The heat map's columns: the most played archetypes.</summary>
    public IReadOnlyList<MatchupHead> MatchupHeads { get => _matchupHeads; private set => Set(ref _matchupHeads, value); }

    /// <summary>The heat map's rows, one per column archetype, in the same order.</summary>
    public IReadOnlyList<MatchupMatrixRow> MatchupMatrix
    {
        get => _matchupMatrix;
        private set
        {
            if (Set(ref _matchupMatrix, value))
            {
                Raise(nameof(HasMatchupMatrix));
                Raise(nameof(MetaMatchupsEmpty));
                Raise(nameof(MetaMatchupsRefreshing));
                Raise(nameof(MetaMatchupsLoading));
            }
        }
    }

    public bool HasMatchupMatrix => _matchupMatrix.Count > 0;

    /// <summary>Where the meta numbers come from, or why there are none; kept as a text maker so it follows a language change.</summary>
    /// <summary>The tier list's source line; while its collection is late, the delay line goes under it.</summary>
    public string ConstructedMetaStatus => MetaStatusShown && MetaArchetypes.Count > 0 ? WithDelay(_constructedMetaStatus(), MetaDataDelay) : _constructedMetaStatus();

    public string ArchetypeSummary { get => _archetypeSummary; private set => Set(ref _archetypeSummary, value); }

    public string CopyNote => _copyNote();

    /// <summary>The selected archetype's name over its detail, or a nudge to pick one.</summary>
    public string SelectedArchetypeTitle => _selectedArchetype?.Name ?? Loc.T("Main.Meta.PickArchetype");

    public MetaArchetypeRow? SelectedArchetype
    {
        get => _selectedArchetype;
        set
        {
            var old = _selectedArchetype;
            if (Set(ref _selectedArchetype, value))
            {
                old?.IsSelected = false;
                value?.IsSelected = true;
                Raise(nameof(SelectedArchetypeTitle));
                RebuildArchetypeDetail();
            }
        }
    }

    public RelayCommand SetMetaRank => _setMetaRank ??= new RelayCommand(p => ChangeMetaFilter(() => Settings.MetaRank = ConstructedMeta.Key(ConstructedMeta.Ranks, p as string)));

    public RelayCommand SetMetaPeriod => _setMetaPeriod ??= new RelayCommand(p => ChangeMetaFilter(() => Settings.MetaPeriod = ConstructedMeta.Key(ConstructedMeta.Periods, p as string)));

    public RelayCommand SetMetaClass => _setMetaClass ??= new RelayCommand(p =>
    {
        _metaClass = p as string ?? string.Empty;
        RebuildConstructedMeta();
    });

    /// <summary>A minimum games choice ("100" ... "5000"): archetypes with fewer games leave the list (no new download).</summary>
    public RelayCommand SetMetaMinGames => _setMetaMinGames ??= new RelayCommand(p =>
    {
        if (int.TryParse(p as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var games) && MetaMinGameChoices.Contains(games))
        {
            _metaMinGames = games;
            Raise(nameof(MetaMinGames));
            RebuildConstructedMeta();
        }
    });

    /// <summary>
    /// Sorts by a column ("Tier", "Name", "WinRate", "Popularity", "Games"); the sorted column again turns the order
    /// round. "Popularity" alone is the old "most played" choice.
    /// </summary>
    public RelayCommand SetMetaSort => _setMetaSort ??= new RelayCommand(p =>
    {
        var column = p as string is "Tier" or "Name" or "WinRate" or "Popularity" or "Games" ? (string)p : "WinRate";
        _metaSortAscending = column == _metaSort ? !_metaSortAscending : column == "Name";
        _metaSort = column;
        Raise(nameof(MetaArrows));
        RebuildConstructedMeta();
    });

    /// <summary>"Tiers" (groups S to D) or "Table" (one sortable list).</summary>
    public RelayCommand SetMetaView => _setMetaView ??= new RelayCommand(p => MetaTableView = p as string == "Table");

    public RelayCommand SelectArchetype => _selectArchetype ??= new RelayCommand(p =>
    {
        if (p is MetaArchetypeRow row)
        {
            SelectedArchetype = row;
        }
    });

    public RelayCommand CopyDeck => _copyDeck ??= new RelayCommand(p =>
    {
        if (p is MetaDeckRow deck)
        {
            CopyDeckCode(deck);
        }
    });

    /// <summary>Stats of one format from the host; null when the page's format could not be loaded.</summary>
    public void SetConstructedMeta(ConstructedMeta? meta)
    {
        if (meta is null)
        {
            MetaLoading = false;
            // Stats shown before stay; with none the page says so instead of blaming the filters. They are no longer
            // waited for: the old rows come back to full, and an early tier list's empty parts say what they lack.
            MetaUnavailable = PageMeta is null;
            _metaReloading = false;
            if (PageMeta is { IsEarly: true } early)
            {
                _earlyGivenUp = early;
                RebuildConstructedMeta();
            }

            RaiseMetaStates();
            ShowMetaStatus(() => Loc.T("Main.Meta.Unavailable"));
            return;
        }

        _constructedMetas[meta.Format] = meta;
        if (PageMeta is not null)
        {
            MetaUnavailable = false;
        }

        // The stats of the filter shown came: the wait is over (another format's, or the old filter's, do not end it).
        if (PageMeta is { } shown && OfCurrentFilter(shown))
        {
            _metaReloading = false;
        }

        RebuildConstructedMeta();
        RebuildConstructedStats();

        // Your own decks show the archetype they belong to, and the deck page compares with it.
        RebuildDecks();
        RebuildCard();
    }

    /// <summary>The host copied (or failed to copy) a deck code; the note under the lists says which.</summary>
    public void DeckCodeCopied(string deckName, bool copied) =>
        ShowCopyNote(copied ? () => Loc.F("Main.Meta.Copied", deckName) : () => Loc.T("Main.Meta.ClipboardBusy"));

    /// <summary>The class's default hero, for its portrait.</summary>
    internal static string ClassHero(string heroClass) => heroClass.ToUpperInvariant() switch
    {
        "WARRIOR" => "HERO_01",
        "SHAMAN" => "HERO_02",
        "ROGUE" => "HERO_03",
        "PALADIN" => "HERO_04",
        "HUNTER" => "HERO_05",
        "DRUID" => "HERO_06",
        "WARLOCK" => "HERO_07",
        "MAGE" => "HERO_08",
        "PRIEST" => "HERO_09",
        "DEMONHUNTER" => "HERO_10",
        "DEATHKNIGHT" => "HERO_11",
        _ => string.Empty,
    };

    private static string ConstructedNote(ConstructedMeta? meta) => meta is null ? Loc.T("Stats.Ct.NoteNoMeta") : string.Empty;

    partial void OnConstructedFormatChanged()
    {
        RebuildConstructedMeta();
        Raise(nameof(ConstructedWild));
    }

    // The tier list and top decks follow the page; the overlay keeps the game's format.
    partial void OnConstructedFormatShown() => RequestPageMeta();

    /// <summary>The platform copies the deck code and reports through <see cref="DeckCodeCopied"/>.</summary>
    partial void CopyDeckCode(MetaDeckRow deck);

    private void RaiseMetaStates()
    {
        Raise(nameof(MetaEmpty));
        Raise(nameof(MetaDecksEmpty));
        Raise(nameof(MetaCanBuildEmpty));
        Raise(nameof(MetaPricingShown));
        Raise(nameof(MetaMatchupsEmpty));
        Raise(nameof(MetaStatusShown));
        Raise(nameof(MetaRefreshing));
        Raise(nameof(MetaDecksRefreshing));
        Raise(nameof(MetaMatchupsRefreshing));
        Raise(nameof(MetaMatchupsLoading));
    }

    /// <summary>Whether stats are of the rank and period the filters ask for now (not the old filter's, kept while they load).</summary>
    private bool OfCurrentFilter(ConstructedMeta meta) =>
        meta.Rank == ConstructedMeta.Key(ConstructedMeta.Ranks, Settings.MetaRank) && meta.Period == ConstructedMeta.Key(ConstructedMeta.Periods, Settings.MetaPeriod);

    private void ShowMetaStatus(Func<string> text)
    {
        _constructedMetaStatus = text;
        Raise(nameof(ConstructedMetaStatus));
    }

    private void ShowCopyNote(Func<string> text)
    {
        _copyNote = text;
        Raise(nameof(CopyNote));
    }

    private void ChangeMetaFilter(Action apply)
    {
        apply();
        Raise(nameof(MetaRanks));
        Raise(nameof(MatchupsRankNote));
        Raise(nameof(MetaPeriods));
        MetaUnavailable = false;
        if (PageMeta is null)
        {
            // Nothing of the page's format to keep: the tabs wait with their loading state.
            MetaLoading = true;
            ShowMetaStatus(() => Loc.T("Main.Meta.LoadingShort"));
        }
        else
        {
            // The old filter's rows stay where they are, dimmed, until the new stats replace them in one step.
            _metaReloading = true;
            RaiseMetaStates();
        }

        ConstructedMetaRequested?.Invoke(null);
    }

    /// <summary>Asks the host for the page's format stats while it has none (the tier list waits with "Loading").</summary>
    private void RequestPageMeta()
    {
        if (PageMeta is null && ConstructedMeta.FormatKeyOf(_ctFormat) is { } key)
        {
            MetaLoading = true;
            MetaUnavailable = false;
            ShowMetaStatus(() => Loc.T("Main.Meta.LoadingShort"));
            ConstructedMetaRequested?.Invoke(key);
        }
    }

    /// <summary>The stats of the format the page shows.</summary>
    private ConstructedMeta? PageMeta => ConstructedMetaFor(_ctFormat);

    /// <summary>The stats the meta tabs show now, null without any: for a render scene to put back what it replaced.</summary>
    public ConstructedMeta? ShownConstructedMeta => PageMeta;

    private string Arrow(string column) => column != _metaSort ? string.Empty : _metaSortAscending ? "▲" : "▼";

    private static IReadOnlyList<MetaChoice> Choices(IReadOnlyList<MetaOption> options, string? selected)
    {
        var key = ConstructedMeta.Key(options, selected);
        return options.Select(o => new MetaChoice(o.Key, o.Label, o.Key == key)).ToList();
    }

    /// <summary>"Quest Priest · 58% in the meta" for one of your decks (its format's stats), or empty when it matches no archetype.</summary>
    private string DeckMeta(string? deckCode, string? heroClass, string format) =>
        ConstructedStats.ArchetypeOf(deckCode, heroClass, ConstructedMetaFor(format), _cards) is { } archetype
            ? Loc.F("Main.Meta.DeckInMeta", archetype.Name, archetype.WinRate.ToString("P0", CultureInfo.CurrentCulture))
            : string.Empty;

    /// <summary>Win rates within a point of 50 % stay plain; the rest go green or red (hearthpulse.net's colour scale).</summary>
    internal static int WinSign(double winRate) => winRate >= 0.51 ? 1 : winRate <= 0.49 ? -1 : 0;

    private IEnumerable<MetaArchetype> SortArchetypes(IEnumerable<MetaArchetype> archetypes)
    {
        Func<MetaArchetype, IComparable> key = _metaSort switch
        {
            "Name" => a => a.Name,
            "Popularity" => a => a.Share > 0 ? a.Share : a.Games,
            "Games" => a => a.Games,
            _ => a => a.WinRate,
        };
        if (_metaSort == "Name")
        {
            return _metaSortAscending
                ? archetypes.OrderBy(a => a.Name, StringComparer.CurrentCulture)
                : archetypes.OrderByDescending(a => a.Name, StringComparer.CurrentCulture);
        }

        // "Tier" orders by win rate too: the tiers are win-rate bands, and within one the better archetype comes first.
        var ordered = _metaSortAscending ? archetypes.OrderBy(key) : archetypes.OrderByDescending(key);
        return ordered.ThenByDescending(a => a.Games);
    }

    private void RebuildConstructedMeta()
    {
        // Another format's stats never stand in: the tier list stays empty until the page's own are in.
        if (PageMeta is not { } meta)
        {
            MetaClasses = [];
            ShowArchetypeRows([]);
            ShowMetaDecks();
            MatchupHeads = [];
            MatchupMatrix = [];
            _matrixFormat = null;
            SelectedArchetype = null;
            RebuildArchetypeDetail();
            RaiseMetaStates();
            return;
        }

        MetaLoading = false;
        MetaClasses = new[] { new MetaChoice(string.Empty, Loc.T("Main.Meta.AllClasses"), _metaClass.Length == 0) }
            .Concat(meta.Archetypes
                .Select(a => a.Class)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ConstructedMeta.ClassName, StringComparer.CurrentCulture)
                .Select(c => new MetaChoice(c, ConstructedMeta.ClassName(c), string.Equals(c, _metaClass, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var archetypes = SortArchetypes(meta.Archetypes
                .Where(a => _metaClass.Length == 0 || string.Equals(a.Class, _metaClass, StringComparison.OrdinalIgnoreCase))
                .Where(a => a.Games >= _metaMinGames))
            .ToList();
        var topShare = archetypes.Count == 0 ? 0 : archetypes.Max(a => a.Share > 0 ? a.Share : 0);
        var selectedId = _selectedArchetype?.Id;

        // A row that shows what it showed before stays the same object in both lists, so it keeps its container and its
        // choice; the others are new rows in their places.
        var shown = new Dictionary<int, MetaArchetypeRow>();
        foreach (var row in MetaArchetypes)
        {
            shown.TryAdd(row.Id, row);
        }

        var split = archetypes.Exists(a => a.FirstGames > 0 || a.CoinGames > 0);
        var rows = archetypes
            .Select((a, i) => ArchetypeRow(a, i + 1, topShare) with { ShowsPlaySplit = split })
            .Select(r => shown.TryGetValue(r.Id, out var old) && old.Equals(r) ? old : r)
            .ToList();
        ShowArchetypeRows(rows);

        // The top decks, with what the player's collection says of each (MainViewModel.Collection.cs).
        ShowMetaDecks();

        // An early tier list has no matrix yet: the one shown for the same format stays (dimmed) until the full stats.
        if (!MetaDetailWaiting)
        {
            RebuildMatchupMatrix(meta.Matchups);
            _matrixFormat = meta.Format;
        }
        else if (_matrixFormat != meta.Format)
        {
            RebuildMatchupMatrix(MetaMatchups.Empty);
            _matrixFormat = null;
        }

        RaiseMetaStates();

        // The choice stays on the same archetype's row (a new object when what it shows changed) or moves to the first
        // one; an emptied list clears it. The side card is built again either way: the stats under it may be new.
        var selected = rows.FirstOrDefault(r => r.Id == selectedId) ?? rows.FirstOrDefault();
        if (ReferenceEquals(selected, _selectedArchetype))
        {
            Raise(nameof(SelectedArchetypeTitle));
            RebuildArchetypeDetail();
        }
        else
        {
            SelectedArchetype = selected;
        }

        var updated = meta.Updated == DateTime.MinValue ? string.Empty : Loc.F("Main.Meta.Updated", meta.Updated.ToLocalTime());
        var status = meta.Archetypes.Count == 0
            ? Loc.T("Main.Meta.NoStats")
            : Loc.P("Main.Meta.Status", meta.Games, meta.Source, ConstructedMeta.Label(ConstructedMeta.Ranks, meta.Rank), ConstructedMeta.Label(ConstructedMeta.Periods, meta.Period), updated);
        ShowMetaStatus(() => status);
    }

    /// <summary>Puts <paramref name="rows"/> (table order) into the table list and, under their tier heads, into the tier list.</summary>
    private void ShowArchetypeRows(IReadOnlyList<MetaArchetypeRow> rows)
    {
        var tiers = new List<object>(rows.Count + MetaTiers.Letters.Count);
        if (PageMeta is not null)
        {
            // Without stats of the page's format there are no tiers to name either.
            foreach (var tier in MetaTiers.Letters)
            {
                // A tier without archetypes is left out, as on the Battlegrounds tabs: the list opens on the best tier
                // there is, and each head still gives its bound and count.
                var count = rows.Count(r => r.Tier == tier);
                if (count == 0)
                {
                    continue;
                }

                tiers.Add(new TierHeadRow(
                    tier,
                    Loc.F("Main.Meta.TierTitle", tier),
                    Loc.P(MetaTiers.IsLast(tier) ? "Main.Meta.TierBelow" : "Main.Meta.TierFrom", count, MetaTiers.Floor(tier).ToString("P0", CultureInfo.CurrentCulture))));
                tiers.AddRange(rows.Where(r => r.Tier == tier));
            }
        }

        MetaTierRows.Sync(tiers);
        MetaArchetypes.Sync(rows);
        Raise(nameof(HasMetaArchetypes));
        Raise(nameof(MetaHasPlaySplit));
        Raise(nameof(MetaEmpty));
    }

    private static MetaArchetypeRow ArchetypeRow(MetaArchetype a, int rank, double topShare)
    {
        var interval = MetaTiers.Interval(a.WinRate, a.Games);
        var english = string.Equals(a.Key, a.Name, StringComparison.OrdinalIgnoreCase) ? string.Empty : a.Key;
        return new MetaArchetypeRow(
            a.Id,
            rank,
            a.Name,
            english,
            ClassHero(a.Class),
            // The English name stays beside a translated one, as on hearthpulse.net, so a name heard on stream still finds it.
            (english.Length > 0 ? english + " · " : string.Empty) + Loc.P("Main.Meta.ArchetypeDetail", a.Games, ConstructedMeta.ClassName(a.Class)),
            MetaTiers.Of(a.WinRate),
            a.WinRate.ToString("P1", CultureInfo.CurrentCulture),
            interval is { } ci
                ? Loc.F("Main.Meta.Interval", ci.Low.ToString("P1", CultureInfo.CurrentCulture), ci.High.ToString("P1", CultureInfo.CurrentCulture))
                : string.Empty,
            a.WinRate - 0.5,
            WinSign(a.WinRate),
            Math.Clamp((a.WinRate - 0.40) / 0.25, 0.04, 1),
            a.Share > 0 ? a.Share.ToString("P1", CultureInfo.CurrentCulture) : "—",
            // No share, no bar: a bar beside "—" would claim a popularity the source did not give.
            a.Share > 0 && topShare > 0 ? Math.Clamp(a.Share / topShare, 0.03, 1) : 0,
            a.Games.ToString("N0", CultureInfo.CurrentCulture),
            a.FirstGames > 0 ? a.FirstWinRate.ToString("P0", CultureInfo.CurrentCulture) : "—",
            a.CoinGames > 0 ? a.CoinWinRate.ToString("P0", CultureInfo.CurrentCulture) : "—");
    }
}

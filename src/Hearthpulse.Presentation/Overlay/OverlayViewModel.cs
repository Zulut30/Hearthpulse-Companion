using System.Globalization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// Flattens live snapshots into display rows. Lists are only replaced when their content
/// changes, so item containers (and their images) survive the ~10 Hz snapshot stream.
/// </summary>
public sealed partial class OverlayViewModel : Observable
{
    private readonly Dictionary<string, string> _fingerprints = new();
    private BattlegroundsView? _bg;
    private CardDb _cards = CardDb.Empty;
    private string _locale = "enUS";

    // ---- visibility -----------------------------------------------------------------------
    private bool _showConstructed;
    private bool _showOpponent;
    private bool _showBattlegrounds;
    private bool _showHeroPick;
    private bool _showTrinkets;
    private bool _showShop;
    private bool _showDiscover;
    private bool _showOdds;
    private bool _oddsWanted;

    public bool ShowConstructed { get => _showConstructed; private set => Set(ref _showConstructed, value); }
    public bool ShowOpponent { get => _showOpponent; private set => Set(ref _showOpponent, value); }
    public bool ShowBattlegrounds { get => _showBattlegrounds; private set => Set(ref _showBattlegrounds, value); }
    public bool ShowHeroPick { get => _showHeroPick; private set => Set(ref _showHeroPick, value); }
    public bool ShowTrinkets { get => _showTrinkets; private set => Set(ref _showTrinkets, value); }
    public bool ShowShop { get => _showShop; private set => Set(ref _showShop, value); }
    public bool ShowDiscover { get => _showDiscover; private set => Set(ref _showDiscover, value); }
    public bool ShowOdds { get => _showOdds; private set => Set(ref _showOdds, value); }

    // ---- pick suggestions (Settings: "Pick suggestions") ----------------------------------------
    private PickHints _pickHints = PickHints.StatsOnly;

    /// <summary>
    /// "Mark the best": BEST PICK / BUY badges, gold styling and Discover ranks are shown. Otherwise every Recommended /
    /// Best flag on the badge items is false and <see cref="DiscoverBadge.Rank"/> is 0.
    /// </summary>
    public bool ShowBestBadges => _pickHints == PickHints.Highlight;

    /// <summary>"Stats only": the same numbers on every option and nothing singled out.</summary>
    public bool StatsOnly => _pickHints == PickHints.StatsOnly;

    /// <summary>Pick suggestions are not "Off": the hero, trinket, tavern and Discover panels may show.</summary>
    public bool ShowPickPanels => _pickHints != PickHints.Off;

    // ---- constructed ----------------------------------------------------------------------
    private string _deckName = string.Empty;
    private string _deckRecord = string.Empty;
    private string _deckCounts = string.Empty;
    private SyncList<DeckTile> _deck = new();
    private bool _showChance = true;

    public string DeckName { get => _deckName; private set => Set(ref _deckName, value); }
    /// <summary>The deck's record or its archetype's meta win rate; empty without either (no line).</summary>
    public string DeckRecord { get => _deckRecord; private set => Set(ref _deckRecord, value); }

    /// <summary>"24 in deck": the game shows the deck's count only on hover; your own hand you can see.</summary>
    public string DeckCounts { get => _deckCounts; private set => Set(ref _deckCounts, value); }
    public IReadOnlyList<DeckTile> Deck => _deck;
    public bool ShowChance { get => _showChance; private set => Set(ref _showChance, value); }

    private string _opponentTitle = string.Empty;
    private string _opponentSubtitle = string.Empty;
    private string _opponentHeroId = string.Empty;
    private int _opponentHand;
    private int _opponentDeck;
    private string _playedEarlier = string.Empty;
    private string _playedEarlierTip = string.Empty;
    private SyncList<PlayedTile> _played = new();

    public string OpponentTitle { get => _opponentTitle; private set => Set(ref _opponentTitle, value); }
    public string OpponentSubtitle { get => _opponentSubtitle; private set => Set(ref _opponentSubtitle, value); }
    public string OpponentHeroId { get => _opponentHeroId; private set => Set(ref _opponentHeroId, value); }
    public int OpponentHand { get => _opponentHand; private set => Set(ref _opponentHand, value); }
    public int OpponentDeck { get => _opponentDeck; private set => Set(ref _opponentDeck, value); }
    public IReadOnlyList<PlayedTile> Played => _played;

    /// <summary>
    /// Card rows the opponent panel lists before its oldest played cards fold into one line (D3, A10): at the default
    /// spots a panel this long still ends above the counters panel under it. The opponent's hand is not listed: the
    /// marks on their fanned hand (<c>opponent-hand-fan</c>) show the turn each card was drawn and what created it.
    /// </summary>
    public const int OpponentRowBudget = 18;

    /// <summary>"Played earlier: 5" under the played rows when the oldest folded; empty otherwise.</summary>
    public string PlayedEarlier { get => _playedEarlier; private set => Set(ref _playedEarlier, value); }

    /// <summary>The folded cards, one per line, oldest first.</summary>
    public string PlayedEarlierTip { get => _playedEarlierTip; private set => Set(ref _playedEarlierTip, value); }

    /// <summary>
    /// The played cards the panel lists (the newest by the turn each was first played, in the view's order) and the ones
    /// folded into the "played earlier" line, oldest first.
    /// </summary>
    internal static (IReadOnlyList<PlayedCard> Listed, IReadOnlyList<PlayedCard> Folded) FoldPlayed(IReadOnlyList<PlayedCard> played)
    {
        const int room = OpponentRowBudget;
        if (played.Count <= room)
        {
            return (played, []);
        }

        // Room for the "played earlier" line itself
        var keep = played.Select((card, index) => (card, index)).OrderByDescending(p => p.card.FirstTurn).ThenByDescending(p => p.index).Take(room - 1)
            .Select(p => p.index).ToHashSet();
        return ([.. played.Where((_, i) => keep.Contains(i))],
                [.. played.Where((_, i) => !keep.Contains(i)).OrderBy(p => p.FirstTurn)]);
    }

    // ---- battlegrounds --------------------------------------------------------------------
    private int _turn;
    private string _phase = string.Empty;
    private SyncList<TierButton> _tiers = new();
    private IReadOnlyList<string> _tribes = Array.Empty<string>();
    private string _rating = string.Empty;
    private string _ratingDelta = string.Empty;
    private int _ratingDeltaValue;
    private IReadOnlyList<HeroPickCard> _heroPicks = Array.Empty<HeroPickCard>();
    private IReadOnlyList<ShopBadge> _shopBadges = Array.Empty<ShopBadge>();
    private IReadOnlyList<TrinketBadge> _trinketPicks = Array.Empty<TrinketBadge>();
    private IReadOnlyList<DiscoverBadge> _discoverBadges = Array.Empty<DiscoverBadge>();
    private string _oddsWin = string.Empty;
    private string _oddsTie = string.Empty;
    private string _oddsLoss = string.Empty;
    private string _oddsNote = string.Empty;
    private string _oddsDamageWin = string.Empty;
    private string _oddsDamageWinTip = string.Empty;
    private string _oddsDamageLoss = string.Empty;
    private string _oddsDamageLossTip = string.Empty;
    private string _oddsLethal = string.Empty;
    private string _oddsKill = string.Empty;
    private bool _lethalDanger;
    private string _usageNote = string.Empty;
    private int _selectedTier;
    private IReadOnlyList<BrowserGroup> _browser = Array.Empty<BrowserGroup>();
    private string _anomaly = string.Empty;
    private LobbyPopup? _lobby;

    public int Turn { get => _turn; private set => Set(ref _turn, value); }
    public string Phase { get => _phase; private set => Set(ref _phase, value); }
    public IReadOnlyList<TierButton> Tiers => _tiers;
    public IReadOnlyList<string> Tribes { get => _tribes; private set => Set(ref _tribes, value); }
    public string Rating { get => _rating; private set => Set(ref _rating, value); }
    public string RatingDelta { get => _ratingDelta; private set => Set(ref _ratingDelta, value); }
    public int RatingDeltaValue { get => _ratingDeltaValue; private set => Set(ref _ratingDeltaValue, value); }
    public IReadOnlyList<HeroPickCard> HeroPicks
    {
        get => _heroPicks;
        private set
        {
            if (Set(ref _heroPicks, value))
            {
                RaiseDataNotes();
            }
        }
    }
    public IReadOnlyList<ShopBadge> ShopBadges { get => _shopBadges; private set => Set(ref _shopBadges, value); }
    public IReadOnlyList<TrinketBadge> TrinketPicks { get => _trinketPicks; private set => Set(ref _trinketPicks, value); }
    public IReadOnlyList<DiscoverBadge> DiscoverBadges { get => _discoverBadges; private set => Set(ref _discoverBadges, value); }
    public string OddsWin { get => _oddsWin; private set => Set(ref _oddsWin, value); }
    public string OddsTie { get => _oddsTie; private set => Set(ref _oddsTie, value); }
    public string OddsLoss { get => _oddsLoss; private set => Set(ref _oddsLoss, value); }
    public string OddsNote { get => _oddsNote; private set => Set(ref _oddsNote, value); }

    /// <summary>Hero damage if we win ("You deal 8–16") / if we lose ("You take 9–17"); empty when that result never came up.</summary>
    public string OddsDamageWin { get => _oddsDamageWin; private set => Set(ref _oddsDamageWin, value); }
    public string OddsDamageWinTip { get => _oddsDamageWinTip; private set => Set(ref _oddsDamageWinTip, value); }
    public string OddsDamageLoss { get => _oddsDamageLoss; private set => Set(ref _oddsDamageLoss, value); }
    public string OddsDamageLossTip { get => _oddsDamageLossTip; private set => Set(ref _oddsDamageLossTip, value); }

    /// <summary>The share of fights that kill the player's hero ("12.0%", the label is in the view); empty when none does.</summary>
    public string OddsLethal { get => _oddsLethal; private set => Set(ref _oddsLethal, value); }

    /// <summary>The share of fights that kill the opponent's hero ("30.0%", the label is in the view); empty when none does.</summary>
    public string OddsKill { get => _oddsKill; private set => Set(ref _oddsKill, value); }

    /// <summary>The fight can kill the player: the odds panel turns red.</summary>
    public bool LethalDanger { get => _lethalDanger; private set => Set(ref _lethalDanger, value); }

    /// <summary>Why the stats pool's numbers are missing: today's free games are used up (plan 3.1); empty otherwise.</summary>
    public string UsageNote { get => _usageNote; private set => Set(ref _usageNote, value); }

    private string _discoverTitle = string.Empty;

    /// <summary>What the open choice comes from ("Hero power · …") and whether there is a clear best pick.</summary>
    public string DiscoverTitle { get => _discoverTitle; private set => Set(ref _discoverTitle, value); }
    public int SelectedTier { get => _selectedTier; private set => Set(ref _selectedTier, value); }
    public IReadOnlyList<BrowserGroup> Browser { get => _browser; private set => Set(ref _browser, value); }
    public string Anomaly { get => _anomaly; private set => Set(ref _anomaly, value); }
    /// <summary>
    /// The opponent under the mouse on the leaderboard ("lobby-popup"); null when none. The odds share its spot at the
    /// top centre and hide while it shows (<see cref="ShowOdds"/>).
    /// </summary>
    public LobbyPopup? Lobby
    {
        get => _lobby;
        private set
        {
            if (Set(ref _lobby, value))
            {
                ShowOdds = _oddsWanted && value is null;
                Raise(nameof(ShowLobbyPopup));
            }
        }
    }

    /// <summary>Current Battlegrounds game is Duos (the leaderboard is laid out in teams).</summary>
    public bool IsDuos { get; private set; }

    // ---- lobby list, session, board -------------------------------------------------------
    private bool _showLobby;
    private bool _showSession;
    private SyncList<LobbyRow> _lobbyRows = new();
    private string _sessionRating = string.Empty;
    private IReadOnlyList<PlaceChip> _sessionPlaces = Array.Empty<PlaceChip>();
    private bool _layoutEditing;

    public bool ShowLobby { get => _showLobby; private set => Set(ref _showLobby, value); }
    public bool ShowSession { get => _showSession; private set => Set(ref _showSession, value); }
    public IReadOnlyList<LobbyRow> LobbyRows => _lobbyRows;
    public string SessionRating { get => _sessionRating; private set => Set(ref _sessionRating, value); }
    public IReadOnlyList<PlaceChip> SessionPlaces { get => _sessionPlaces; private set => Set(ref _sessionPlaces, value); }

    /// <summary>Layout mode: every panel is shown with a frame and can be dragged, resized and reset.</summary>
    public bool LayoutEditing { get => _layoutEditing; set => Set(ref _layoutEditing, value); }

    public event Action? ResetLayoutRequested;

    // ---- toast ----------------------------------------------------------------------------
    private string _toastTitle = string.Empty;
    private string _toastDetail = string.Empty;
    private string _toastResult = string.Empty;
    private bool _showToast;

    public string ToastTitle { get => _toastTitle; private set => Set(ref _toastTitle, value); }
    public string ToastDetail { get => _toastDetail; private set => Set(ref _toastDetail, value); }
    public string ToastResult { get => _toastResult; private set => Set(ref _toastResult, value); }
    public bool ShowToast { get => _showToast; set => Set(ref _showToast, value); }

    public OverlayViewModel()
    {
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        SelectTier = new RelayCommand(p =>
        {
            var tier = Convert.ToInt32(p, CultureInfo.InvariantCulture);
            SelectedTier = SelectedTier == tier ? 0 : tier;
            RebuildBrowser();
            RebuildTiers();
        });
        FinishLayout = new RelayCommand(() => LayoutEditing = false);
        ResetLayout = new RelayCommand(() => ResetLayoutRequested?.Invoke());
        CloseBrowser = new RelayCommand(() =>
        {
            SelectedTier = 0;
            _selectedTribe = string.Empty;
            RebuildTribeChips();
            RebuildBrowser();
            RebuildTiers();
        });
    }

    /// <summary>
    /// The interface language changed: texts built here are rebuilt now or on the next snapshot (the host pushes the
    /// last one again), so the caches that skip unchanged lists are cleared.
    /// </summary>
    private void OnLanguageChanged()
    {
        _fingerprints.Clear();
        _handMarksKey = string.Empty;
        RaiseDataNotes();
        Lobby = null;
        RebuildBrowser();
        RebuildCounters();
        RebuildBgCounters();
        if (_lastOutcome is { } outcome)
        {
            ToastTexts(outcome);
        }
    }

    private GameOutcome? _lastOutcome;

    public RelayCommand SelectTier { get; }

    public RelayCommand CloseBrowser { get; }

    public RelayCommand FinishLayout { get; }

    public RelayCommand ResetLayout { get; }

    /// <summary>Shows a snapshot: each panel's rows and whether it shows, rebuilt only where something changed.</summary>
    /// <param name="deck">The player's record with the deck in play: the deck panel's subtitle.</param>
    /// <param name="matchup">
    /// The player's record against the opponent's class. Not shown any more (a score from past games changed no decision
    /// in this one); the hosts' feed still hands it in.
    /// </param>
    public void Update(LiveSnapshot snapshot, CardDb cards, AppSettings settings, DeckAggregate? deck, MatchupAggregate? matchup)
    {
        _cards = cards;
        _locale = cards.Locale;
        UpdateStreamer(snapshot, settings);
        // The OBS windows show the deck, lobby and odds panels with the overlay off too: the game is read while either is
        // on, but the overlay's other panels are worked out only while the overlay is on.
        var overlayOn = snapshot.InGame && settings.OverlayEnabled;
        var enabled = overlayOn || (snapshot.InGame && StreamWindowSpec.AnyOn(settings));

        var constructed = enabled ? snapshot.Constructed : null;
        var overlayConstructed = overlayOn ? constructed : null;
        ShowConstructed = constructed is not null && settings.ShowDeckTracker && constructed.DeckKnown;
        ShowOpponent = constructed is not null && settings.ShowOpponentTracker;
        ShowChance = settings.ShowDrawChance;
        if (constructed is not null)
        {
            UpdateConstructed(constructed, deck);
        }
        else
        {
            _deckLeft.Clear();
        }

        UpdateLive(constructed, settings);
        UpdateHandMarks(overlayConstructed, settings);
        UpdateCardPool(overlayConstructed, settings);
        UpdateArchetype(overlayConstructed, settings);
        UpdateTurnTimer(overlayConstructed, settings);
        UpdateLadder(snapshot, settings);
        UpdateSecrets(overlayConstructed, settings);
        UpdateGraveyard(overlayConstructed, settings);
        UpdateLikelyDeck(overlayConstructed, settings);
        UpdatePanelLock(settings);

        var bg = enabled ? snapshot.Battlegrounds : null;
        var overlayBg = overlayOn ? bg : null;
        _bg = bg;
        UpdateBgCounters(overlayBg, settings);
        UpdateBgLobby(overlayBg, settings);
        UpdateArena(snapshot.ArenaDraft, settings);
        // The bar waits for the first tavern turn: at the hero pick there is no turn, no tavern and no lobby read yet.
        ShowBattlegrounds = bg is { IsHeroSelection: false } && settings.ShowBattlegroundsBar;
        ApplyPickSettings(settings);
        UpdatePickRows(settings);
        ShowHeroPick = overlayBg is { IsHeroSelection: true } && ShowPickPanels && overlayBg.HeroOptions.Count > 0;
        var playing = overlayBg is { IsHeroSelection: false, HeroCardId: not null } && ShowPickPanels;
        ShowTrinkets = playing && overlayBg!.TrinketOptions.Count > 0;
        ShowDiscover = playing && overlayBg!.DiscoverAdvice.Count > 0;
        _oddsWanted = settings.ShowCombatOdds && bg is { InCombat: true, CombatOdds: not null };
        ShowOdds = _oddsWanted && Lobby is null;
        // Buy hints sit on the tavern row, so only while recruiting and while no choice covers the shop.
        ShowShop = playing && !overlayBg!.InCombat && overlayBg.TrinketOptions.Count == 0 && overlayBg.DiscoverOptions.Count == 0 && overlayBg.ShopAdvice.Count > 0;
        ShowLobby = bg is { IsHeroSelection: false } && settings.ShowLobbyPanel && bg.Opponents.Count > 0;
        if (bg is not null)
        {
            UpdateBattlegrounds(bg, snapshot);
            UpdateLobbyAndSession(bg, snapshot);
        }
        else if (SelectedTier != 0)
        {
            SelectedTier = 0;
            Browser = Array.Empty<BrowserGroup>();
        }

        // The session's rating and places while picking a hero, and only with a rating to show: during the game they
        // change no decision, and "MMR —" said nothing all game.
        ShowSession = bg is { IsHeroSelection: true } && settings.ShowSessionPanel && _sessionRated;

        UpdateBgBadges(overlayBg, settings);
        if (bg is null)
        {
            Lobby = null;
        }
    }

    private void ApplyPickSettings(AppSettings settings)
    {
        var hints = _pickHints != settings.PickHints;
        _pickHints = settings.PickHints;
        if (hints)
        {
            Raise(nameof(ShowBestBadges));
            Raise(nameof(StatsOnly));
            Raise(nameof(ShowPickPanels));
        }
    }

    private void UpdateConstructed(ConstructedView view, DeckAggregate? deck)
    {
        DeckName = view.DeckName ?? Loc.T("Overlay.Deck.Fallback");
        var (record, recordTip, _) = DeckHeader.Describe(deck, view.DeckMeta);
        DeckRecord = record;
        DeckRecordTip = recordTip;
        DeckCounts = Loc.F("Overlay.Deck.Counts", view.DeckCount);
        Replace(nameof(Deck), view.Deck, r => $"{r.CardId}{r.Remaining}{r.Total}{r.DrawChance:0.00}", () =>
        {
            var changed = DeckChanges(view.Deck);
            Sync(ref _deck, nameof(Deck), view.Deck
            .Select(r => new DeckTile(
                r.CardId,
                r.Name,
                r.Cost,
                r.Rarity == "LEGENDARY" && r.Total == 1 ? "★" : r.Remaining.ToString(CultureInfo.InvariantCulture),
                r.Total > 1 || r.Rarity == "LEGENDARY" || r.IsAdded,
                r.Remaining == 0,
                r.Rarity,
                r.Remaining > 0 && view.DeckCount > 0 ? OverlayFormat.Percent(r.DrawChance) : string.Empty,
                r.IsAdded,
                changed.Contains(DeckKey(r))))
            .ToList());
        });

        OpponentHeroId = view.OpponentHeroCardId;
        (OpponentTitle, OpponentSubtitle) = OpponentHeader(view);
        OpponentHand = view.OpponentHandCount;
        OpponentDeck = view.OpponentDeckCount;
        var (listed, folded) = FoldPlayed(view.OpponentPlayed);
        PlayedEarlier = folded.Count > 0 ? Loc.F("Overlay.Opponent.PlayedEarlier", folded.Sum(p => p.Count)) : string.Empty;
        PlayedEarlierTip = string.Join('\n', folded.Select(p => p.Count > 1 ? $"{p.Name} ×{p.Count}" : p.Name));
        Replace(nameof(Played), listed, p => $"{p.CardId}{p.Count}", () => Sync(ref _played, nameof(Played), listed
            .Select(p => new PlayedTile(p.CardId, p.Name, p.Cost, p.Count > 1 ? $"×{p.Count}" : string.Empty, Loc.F("Overlay.Turn.Short", p.FirstTurn), p.IsCreated))
            .ToList()));
    }

    public void ShowOutcome(GameOutcome outcome)
    {
        _lastOutcome = outcome;
        ToastTexts(outcome);
        ShowToast = true;
    }

    /// <summary>Title, colour key and detail of the end-of-game toast (rebuilt after a language change).</summary>
    private void ToastTexts(GameOutcome outcome)
    {
        var bg = outcome.Mode is "Battlegrounds" or "Duos";
        ToastTitle = bg ? Loc.F("Overlay.Toast.Place", Ordinal(outcome.Placement)) : outcome.Result == "Win" ? Loc.T("Overlay.Toast.Victory") : outcome.Result == "Loss" ? Loc.T("Overlay.Toast.Defeat") : ResultText(outcome.Result);
        ToastResult = bg ? (outcome.Placement <= (outcome.Mode == "Duos" ? 2 : 4) ? "Win" : "Loss") : outcome.Result;
        var hero = _cards.NameOf(outcome.HeroCardId);
        ToastDetail = outcome.RatingDelta is { } d
            ? Loc.F("Overlay.Toast.Rating", hero, d >= 0 ? "+" : "−", Math.Abs(d), outcome.RatingAfter)
            : hero;
    }

    /// <summary>
    /// Changes a bound list in place (<see cref="SyncList{T}"/>): the items control keeps the rows that did not change.
    /// Into or out of empty there is no row to keep: then the list is a new object and the view is told, so bindings on
    /// the list itself (the panel shows while it is not empty) look again in both UIs.
    /// </summary>
    private void Sync<T>(ref SyncList<T> list, string name, IReadOnlyList<T> items)
    {
        if (list.Count > 0 && items.Count > 0)
        {
            list.Sync(items);
            return;
        }

        if (list.Count != items.Count)
        {
            list = new SyncList<T>(items);
            Raise(name);
        }
    }

    private void Replace<T>(string key, IReadOnlyList<T> source, Func<T, string> fingerprint, Action apply)
    {
        var print = string.Join('|', source.Select(fingerprint));
        if (_fingerprints.TryGetValue(key, out var previous) && previous == print)
        {
            return;
        }

        _fingerprints[key] = print;
        apply();
    }

    public static string ClassName(string? cardClass) => string.IsNullOrEmpty(cardClass)
        ? string.Empty
        : Localized(cardClass switch
        {
            "DEMONHUNTER" => "Demon Hunter",
            "DEATHKNIGHT" => "Death Knight",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cardClass.ToLowerInvariant()),
        });

    /// <summary>A class name in the interface language ("Death Knight" has the key Overlay.Class.DeathKnight).</summary>
    private static string Localized(string className) => Loc.Or("Overlay.Class." + className.Replace(" ", string.Empty, StringComparison.Ordinal), className);

    /// <summary>A combat or game result ("Win", "Loss", "Tie") in the interface language; anything else as it is.</summary>
    private static string ResultText(string? result) => result switch
    {
        "Win" => Loc.T("Overlay.Result.Win"),
        "Loss" => Loc.T("Overlay.Result.Loss"),
        "Tie" => Loc.T("Overlay.Result.Tie"),
        _ => result ?? string.Empty,
    };

    /// <summary>
    /// A Monte Carlo result cannot prove 0% or 100%: show open bounds unless the odds came from an exact board
    /// (empty board, <see cref="Hearthpulse.Core.Battlegrounds.CombatOdds.Runs"/> == 0).
    /// </summary>
    private static string OddsPercent(double value, int runs)
    {
        if (runs <= 0)
        {
            return OverlayFormat.Percent(value);
        }

        if (value < OverlayFormat.HalfStep)
        {
            return OverlayFormat.Below(0.001, 1);
        }

        return value > 0.9995 ? OverlayFormat.Above(0.999, 1) : OverlayFormat.Percent(value, 1);
    }

    private static string Ordinal(int n) => n switch
    {
        1 => Loc.T("Overlay.Ordinal.First"),
        2 => Loc.T("Overlay.Ordinal.Second"),
        3 => Loc.T("Overlay.Ordinal.Third"),
        _ => Loc.F("Overlay.Ordinal.Other", n),
    };
}

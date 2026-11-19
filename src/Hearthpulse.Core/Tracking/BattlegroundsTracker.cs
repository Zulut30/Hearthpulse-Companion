using System.Globalization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>Battlegrounds state: tavern tier, lobby, opponents' last seen boards, combats.</summary>
public sealed partial class BattlegroundsTracker : IPowerListener
{
    private const string PlaceholderHero = "TB_BaconShop_HERO_PH";

    /// <summary>How long a combat latch may stay set without new combat evidence before it is treated as stale.</summary>
    private const int CombatLatchTimeoutMs = 120_000;

    private static readonly (string Tag, string Label)[] KeywordTags =
    [
        (GameTag.Taunt, "TAUNT"),
        (GameTag.DivineShield, "DIVINE_SHIELD"),
        (GameTag.Poisonous, "POISONOUS"),
        (GameTag.Venomous, "VENOMOUS"),
        (GameTag.Reborn, "REBORN"),
        (GameTag.Windfury, "WINDFURY"),
        (GameTag.Stealth, "STEALTH"),
    ];

    private readonly Dictionary<int, SeenBoard> _boards = new();

    // Minions removed by another minion's trigger after combat started and before the boards were captured
    // (e.g. Stitched Salvager's start-of-combat destroy), as they were when removed, by the trigger's source.
    private readonly Dictionary<int, List<SimMinion>> _removedBy = new();

    // Timewarped Nellie's Ship: the Pirates the local player Discovered for each ship's crew, by the ship's entity id,
    // with the turn of the pick; and the ship whose Discover is open (0 = none).
    private readonly Dictionary<int, (int Turn, List<string> Pirates)> _crew = new();
    private int _crewShip;

    // Minions that died after combat started and before the boards were captured (Start of Combat), in order, with
    // their controller; and for minions that entered the board in that time, how many of those deaths came before.
    private readonly List<(int Controller, SimMinion Dead)> _deathsBeforeCapture = new();
    private readonly Dictionary<int, int> _enteredAfterDeaths = new();

    // The opponent's player values the client does not show, as the log had them at each capture, by combat index: kept
    // out of the live simulation and handed out only after the game (Captures).
    private readonly Dictionary<int, IReadOnlyDictionary<string, int>> _hiddenOpponentTags = new();
    private bool _gameEnded;
    private readonly List<CombatRecord> _combats = new();

    // The fights of this game as the simulator got them, and what the log said about the minions of the fight being captured.
    private readonly List<CapturedFight> _captures = new();
    private readonly Dictionary<int, MinionFacts> _minionFacts = new();
    private readonly List<TierStep> _tiers = new();
    private readonly HashSet<string> _shopRaces = new(StringComparer.Ordinal);

    private bool _inCombat;

    /// <summary>Round the current combat started in, to notice a missed end-of-combat transition.</summary>
    private int _combatTurn;

    /// <summary>Environment.TickCount64 of the last combat block activity, for stale-latch recovery.</summary>
    private long _combatActivityAt;
    private bool _boardCaptured;
    private bool _boardsSnapshotted;
    private int _combatOpponent;
    private int _localHealthAtCombat;
    private int _opponentHealthAtCombat;
    private int _pendingDamage = -1;
    private int _localAlive;
    private int _opponentAlive;

    // The hero attack that closes the fight: open while its block runs, then who hit and for how much.
    private HeroHit? _heroHit;
    private (bool Ours, int Damage)? _heroBlow;
    private IReadOnlyList<BgMinion> _lastOwnBoard = Array.Empty<BgMinion>();

    /// <summary>Cards the player could see in the last tavern phase (tavern, hand, board, trinkets, hero power), for counters.</summary>
    private IReadOnlyList<string> _lastVisibleCards = Array.Empty<string>();

    // Every pick the player was offered this game and the cards played from hand, for the stats pool.
    private readonly List<BgChoice> _choices = new();
    private readonly List<BgPlay> _plays = new();
    private int _openChoice = -1;
    private IReadOnlyList<Entity> _openOptions = Array.Empty<Entity>();
    private IReadOnlyList<Entity> _trinketOffer = Array.Empty<Entity>();
    private IReadOnlyList<Entity> _discoverOffer = Array.Empty<Entity>();
    private Entity? _discoverSource;
    private volatile CombatOdds? _combatOdds;

    /// <summary>The estimated inputs of the fight <see cref="_combatOdds"/> belongs to (both sides).</summary>
    private IReadOnlyList<EstimatedItem> _combatEstimates = Array.Empty<EstimatedItem>();

    /// <summary>Number of the newest fight simulation; older results are dropped when they arrive late.</summary>
    private int _simulationGeneration;
    private CancellationTokenSource? _simulationCancel;

    /// <summary>The accurate odds, once the background simulation has them.</summary>
    private volatile CombatOdds? _refinedOdds;

    /// <summary>Which simulation the refined odds belong to.</summary>
    private int _refinedGeneration = -1;

    /// <summary>Index of the combat the running simulation belongs to.</summary>
    private int _simulatedCombatIndex = -1;

    // Player-level cards whose effects could take part in a fight (only registered effects are used).
    private static readonly string[] ExtraCardTypes = ["HERO_POWER", "BATTLEGROUND_TRINKET", "BATTLEGROUND_QUEST_REWARD"];

    // Game entity tags naming lobby-wide cards that could have combat effects.
    private static readonly string[] GlobalCardTags = [GameTag.BaconAnomaly, GameTag.BaconDeity];

    // Effect markers an enchantment can put on a minion.
    private static readonly string[] GrantableTags = ["DEATHRATTLE", "BACON_RALLY", "AVENGE"];

    public CardDb Cards { get; set; } = CardDb.Empty;

    /// <summary>
    /// Card effects the combat simulator applies. Minions with combat-relevant text and no registered effect make the
    /// odds an estimate.
    /// </summary>
    public EffectRegistry Effects { get; set; } = EffectRegistry.Default;

    /// <summary>
    /// Options of the background combat simulation started at the first attack (the log thread makes a short
    /// <see cref="QuickPass"/> first): fights until the odds are within 1 percentage point either side (95 %), at most
    /// 10000 and 200 ms, at least 200, on <see cref="SimOptions.BackgroundParallelism"/> threads.
    /// </summary>
    public SimOptions SimOptions { get; set; } = DefaultSimOptions;

    /// <summary>What <see cref="SimOptions"/> starts as.</summary>
    internal static SimOptions DefaultSimOptions { get; } = new()
    {
        TimeBudget = TimeSpan.FromMilliseconds(200),

        // A live simulation must not hog the CPU while the game is running: a lopsided fight stops once its odds are
        // precise enough (often a few hundred runs), a close one goes on to ±1 point, and a slow PC stops at 200 ms.
        MinRuns = 200,
        Runs = 10000,
        TargetHalfWidth = 0.01,
        Parallelism = SimOptions.BackgroundParallelism,
    };

    public Func<string, HeroStats?>? HeroStatsProvider { get; set; }

    /// <summary>Tribes read from client memory (authoritative when present).</summary>
    public IReadOnlyList<string>? MemoryRaces { get; set; }

    /// <summary>
    /// Live gold costs of the Battlegrounds buttons, including discounts (null when client memory is unavailable).
    /// </summary>
    public BattlegroundsButtonCosts? LiveButtonCosts { get; set; }

    public bool LocalFinished { get; private set; }

    /// <summary>Our hero's leaderboard place changed after the game was over for us: the final place is in.</summary>
    public bool FinalPlaceKnown { get; private set; }

    /// <summary>Deaths and summons for the Battlegrounds counters; registered as a Power.log listener by the session.</summary>
    public BattlegroundsCounterTracker Counters { get; } = new();

    public void OnGameStarted(GameState game)
    {
        _lastVisibleCards = Array.Empty<string>();
        _boards.Clear();
        _combats.Clear();
        _captures.Clear();
        _minionFacts.Clear();
        _tiers.Clear();
        _shopRaces.Clear();
        _inCombat = false;
        _combatTurn = 0;
        _combatActivityAt = 0;
        _boardCaptured = false;
        _lastOwnBoard = Array.Empty<BgMinion>();
        _choices.Clear();
        _plays.Clear();
        _openChoice = -1;
        _openOptions = Array.Empty<Entity>();
        _trinketOffer = Array.Empty<Entity>();
        _discoverOffer = Array.Empty<Entity>();
        _discoverSource = null;
        _combatOdds = null;
        _combatEstimates = Array.Empty<EstimatedItem>();
        MemoryRaces = null;
        LiveButtonCosts = null;
        LocalFinished = false;
        FinalPlaceKnown = false;
        _pendingDamage = -1;
        _deathsBeforeCapture.Clear();
        _enteredAfterDeaths.Clear();
        _hiddenOpponentTags.Clear();
        _crew.Clear();
        _crewShip = 0;
        _gameEnded = false;
        _transcript = null;
        ResetRounds();
    }

    public void OnGameEnded(GameState game) => _gameEnded = true;

    public void OnChoices(GameState game, string choiceType, Entity? source, IReadOnlyList<Entity> options)
    {
        RememberChoice(game, choiceType, source, options);
        NoteCrewOffer(source);

        // Lesser/Greater Trinket offers arrive as a GENERAL choice of trinket cards.
        if (options.Count > 0 && options.All(IsTrinket))
        {
            _trinketOffer = options.ToArray();
            _discoverOffer = Array.Empty<Entity>();
            _discoverSource = null;
            return;
        }

        // Every other "pick one of these cards" (Discover from hero powers, spells, minions, trinkets,
        // the triple reward or the Dark Gift button) offers freshly created cards set aside.
        if (choiceType == "GENERAL" && options.Count > 1 && options.All(e => e.Zone == "SETASIDE" && e.CardId.Length > 0))
        {
            _discoverOffer = options.ToArray();
            _discoverSource = source;
        }
    }

    public void OnChoiceMade(GameState game)
    {
        _discoverOffer = Array.Empty<Entity>();
        _trinketOffer = Array.Empty<Entity>();
        _discoverSource = null;
    }

    public void OnChosen(GameState game, Entity chosen)
    {
        NoteCrewPick(game, chosen);
        if (_openChoice < 0 || _openChoice >= _choices.Count || !_openOptions.Any(o => o.Id == chosen.Id) || chosen.CardId.Length == 0)
        {
            return;
        }

        var choice = _choices[_openChoice];
        _choices[_openChoice] = choice with { Picked = choice.Picked.Append(chosen.CardId).ToList() };
    }

    // Hero picks (MULLIGAN) are shared separately; everything else is kept with what caused it.
    private void RememberChoice(GameState game, string choiceType, Entity? source, IReadOnlyList<Entity> options)
    {
        _openChoice = -1;
        _openOptions = Array.Empty<Entity>();
        var offered = options.Select(o => o.CardId).Where(id => id.Length > 0).ToList();
        if (!game.IsBattlegrounds || choiceType == "MULLIGAN" || offered.Count == 0 || _choices.Count >= PoolDetails.MaxChoices)
        {
            return;
        }

        // Quests and their rewards (seasons with quests): by card type and the QUEST tag; not seen in a real log yet.
        var kind = options.All(IsTrinket) ? "trinket"
            : options.All(e => e.CardType == "BATTLEGROUND_QUEST_REWARD") ? "reward"
            : options.All(e => e.Has("QUEST")) ? "quest"
            : options.All(e => e.Zone == "SETASIDE") ? "discover"
            : "choice";
        _choices.Add(new BgChoice(game.BattlegroundsTurn, kind, string.IsNullOrEmpty(source?.CardId) ? null : source.CardId, offered, Array.Empty<string>()));
        _openChoice = _choices.Count - 1;
        _openOptions = options.ToArray();
    }

    public void OnEntityCreated(GameState game, Entity entity)
    {
        NoteShopCard(game, entity);
        if (_inCombat && !_boardCaptured && entity.IsMinion && entity.InPlay)
        {
            NoteEnteredBeforeCapture(entity);
        }

        TranscriptCreated(game, entity);
    }

    public void OnEntityRevealed(GameState game, Entity entity) => NoteShopCard(game, entity);

    public void OnTagChanged(GameState game, Entity entity, string tag, string previous, string value)
    {
        if (!game.IsBattlegrounds)
        {
            return;
        }

        if (_transcript is not null)
        {
            TranscriptTag(game, entity, tag, previous, value);
        }

        switch (tag)
        {
            case GameTag.BoardVisualState when entity.Id == game.GameEntityId:
                if (value == "2")
                {
                    if (!game.CombatOnScreen)
                    {
                        game.CombatOnScreenSince = Environment.TickCount64;
                    }

                    game.CombatOnScreen = true;
                    _deathsBeforeCapture.Clear();
                    _enteredAfterDeaths.Clear();
                    BeginCombat(game);
                    break;
                }

                // The server writes the whole fight, ending with this line, about a second after it starts; the client
                // then plays it for another 30 seconds or so. The client's replay (PowerParser) says when it is over
                // on screen, so the odds stay up while the fight plays. Without a replay this line is all there is.
                if (value == "1" && !game.CombatReplaySeen)
                {
                    game.CombatOnScreen = false;
                }

                if (_inCombat)
                {
                    EndCombat(game);
                }

                break;

            case GameTag.PlayerTechLevel when IsLocal(game, entity):
                var tier = entity.GetInt(GameTag.PlayerTechLevel);
                if (tier > 0 && (_tiers.Count == 0 || _tiers[^1].Tier != tier))
                {
                    _tiers.Add(new TierStep(game.BattlegroundsTurn, tier));
                }

                break;

            case GameTag.Damage or GameTag.Armor or GameTag.Health when entity.IsHero && !_inCombat:
                ResolvePendingDamage(game);
                break;

            case GameTag.Zone when _inCombat && !_boardCaptured && entity.IsMinion && previous == "PLAY" && value != "PLAY":
                RememberRemoval(game, entity);
                if (value == "GRAVEYARD")
                {
                    _deathsBeforeCapture.Add((entity.Controller, DeadMinion(entity)));
                }

                break;

            case GameTag.Zone when _inCombat && !_boardCaptured && entity.IsMinion && value == "PLAY":
                NoteEnteredBeforeCapture(entity);
                break;

            case GameTag.Zone when !_inCombat && previous == "HAND" && value == "PLAY" && entity.CardId.Length > 0 && entity.Controller == game.LocalPlayerId
                                   && (entity.IsMinion || entity.CardType == "BATTLEGROUND_SPELL") && _plays.Count < PoolDetails.MaxPlays:
                _plays.Add(new BgPlay(game.BattlegroundsTurn, entity.CardId));
                break;

            case GameTag.PlayerLeaderboardPlace when LocalFinished && entity == game.LocalHero:
                FinalPlaceKnown = true;
                break;

            case GameTag.PlayState when entity.Id == game.LocalPlayer?.EntityId:
                if (value is "WON" or "LOST" or "TIED" or "CONCEDED")
                {
                    LocalFinished = true;
                    // The fight that ends the game for us never gets its closing BOARD_VISUAL_STATE line: it is over now,
                    // before the game is recorded. Conceded while it played and before any hero attack, its end is not
                    // known: it is no result (not a tie).
                    if (_inCombat && value == "CONCEDED" && _heroHit is null && _heroBlow is null)
                    {
                        FinishTranscript(game, complete: false, "cut");
                        _inCombat = false;
                        _combatOpponent = 0;
                    }
                    else if (_inCombat)
                    {
                        EndCombat(game);
                    }
                }

                break;
        }
    }

    public void OnBlockStart(GameState game, string blockType, Entity? source)
    {
        if (!_inCombat)
        {
            return;
        }

        _combatActivityAt = Environment.TickCount64;
        TranscriptBlockStart(game, blockType, source);
        if (blockType == "ATTACK" && source is { IsHero: true } attacker)
        {
            var ours = attacker.Controller == game.LocalPlayerId;
            var defender = ours ? CombatOpponentHero(game) : game.LocalHero;
            _heroHit = new HeroHit(ours, defender, Durability(defender), game.OpenBlocks.Count);
        }

        if (_boardCaptured || blockType != "ATTACK")
        {
            return;
        }

        if (IsStartOfCombatAttack(game))
        {
            // Start-of-combat attacks (Illidan's Wingmen, Heroic Broodmother, Righteous Charge...) come before the other
            // start-of-combat effects have all resolved, and their attacker is not the fight's first attacker. Keep the
            // boards for history in case the fight ends before a regular attack, but simulate from the first regular one.
            if (!_boardsSnapshotted)
            {
                SnapshotBoards(game);
            }

            return;
        }

        CaptureBoards(game, source);
    }

    /// <summary>
    /// An ATTACK block inside a TRIGGER block of a hero power, minion or quest reward is a start-of-combat attack;
    /// regular attacks are not nested in such a block (blocks of the game or a player entity do not count).
    /// Inferred from the log's block structure; not yet checked against a real Illidan/Broodmother log.
    /// </summary>
    private static bool IsStartOfCombatAttack(GameState game) =>
        game.OpenBlocks.Any(b => b.Type == "TRIGGER" && b.Source is { } s && s.Id != game.GameEntityId
                                 && s.CardType is "HERO_POWER" or "MINION" or "BATTLEGROUND_QUEST_REWARD");

    public void OnBlockEnd(GameState game)
    {
        if (!_inCombat)
        {
            return;
        }

        _combatActivityAt = Environment.TickCount64;
        TranscriptBlockEnd(game);
        if (_heroHit is { } hit && game.OpenBlocks.Count <= hit.Depth)
        {
            FinishHeroHit();
        }

        if (_boardCaptured || _boardsSnapshotted)
        {
            (_localAlive, _opponentAlive) = MinionsInPlay(game, game.LocalPlayerId, game.OpponentPlayerId);
        }
    }

    /// <summary>
    /// How many minions two controllers have in play, in one pass and without LINQ: this runs at the end of every block
    /// of a combat, and two filtered scans of every entity of the game (2 000 late in a game) took a third of the time a
    /// Battlegrounds log needed to be read (docs/perf-architecture-audit.md, stage 10).
    /// </summary>
    internal static (int First, int Second) MinionsInPlay(GameState game, int first, int second)
    {
        var (ofFirst, ofSecond) = (0, 0);
        foreach (var entity in game.Entities.Values)
        {
            if (!entity.InPlay || !entity.IsMinion)
            {
                continue;
            }

            var controller = entity.Controller;
            if (controller == first)
            {
                ofFirst++;
            }

            if (controller == second)
            {
                ofSecond++;
            }
        }

        return (ofFirst, ofSecond);
    }

    private void FinishHeroHit()
    {
        if (_heroHit is { } hit)
        {
            _heroHit = null;
            _heroBlow = (hit.Ours, Math.Max(0, hit.Before - Durability(hit.Defender)));
        }
    }

    /// <summary>The opponent's hero on the combat board: a copy carrying the player's PLAYER_ID (Bob has none).</summary>
    private static Entity? CombatOpponentHero(GameState game) =>
        game.Entities.Values.FirstOrDefault(e => e.IsHero && e.Zone == "PLAY" && e.Controller == game.OpponentPlayerId && e.GetInt(GameTag.PlayerId) > 0);
}

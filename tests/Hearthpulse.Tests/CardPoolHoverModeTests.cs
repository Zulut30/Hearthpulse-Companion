using System.Text.RegularExpressions;
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.EctoplasmListTests;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC4: a card in hand shows its pool as the hover mode says, and the card under the mouse is read from
/// the game only then - with Ctrl (the default) only while Ctrl is held, always, or never; never with the pool off or
/// outside Ranked, Casual and Friendly; the player's own Discover shows its pool beside the options unless its switch is
/// off. A fake reader stands in for game memory.
/// </summary>
public sealed class CardPoolHoverModeTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream("""
        [
          {"id":"HERO_09","dbfId":1,"name":"Anduin","type":"HERO","cardClass":"PRIEST"},
          {"id":"HERO_01","dbfId":7,"name":"Garrosh","type":"HERO","cardClass":"WARRIOR"},
          {"id":"DR_2","dbfId":2,"name":"Marker","type":"MINION","cost":1,"set":"X","collectible":true},
          {"id":"GEN_A","dbfId":10,"name":"Beast Caller","type":"SPELL","cost":2,"set":"X","collectible":true},
          {"id":"PLAIN","dbfId":12,"name":"Chillwind Yeti","type":"MINION","cost":4,"set":"X","collectible":true},
          {"id":"REL","dbfId":13,"name":"Token Maker","type":"MINION","cost":2,"set":"X","collectible":true},
          {"id":"RELt","dbfId":14,"name":"Token","type":"MINION","cost":1,"set":"X"},
          {"id":"BEAST_1","dbfId":20,"name":"Wolf","type":"MINION","cost":1,"set":"X","collectible":true},
          {"id":"BEAST_2","dbfId":21,"name":"Bear","type":"MINION","cost":3,"set":"X","collectible":true},
          {"id":"LIST","dbfId":15,"name":"Fel Counter","type":"MINION","cost":3,"set":"X","collectible":true},
          {"id":"TLC_464","dbfId":16,"name":"Mountain Map","type":"SPELL","cost":1,"set":"X","collectible":true}
        ]
        """u8.ToArray()));

    private static readonly CardPoolCatalog Catalog = new(
    [
        Pool("GEN_A", "Beast Caller", "Discover a Beast."),
        Pool("PLAIN", "Chillwind Yeti", "A big yeti.", "MINION"),
        Pool("REL", "Token Maker", "Summon a 1/1 Token.", "MINION"),
        Pool("BEAST_1", "Wolf", "A wolf.", "MINION", "BEAST"),
        Pool("BEAST_2", "Bear", "A bear.", "MINION", "BEAST"),
        Pool("DR_2", "Marker", "A minion.", "MINION"),
        Pool("LIST", "Fel Counter", "Costs (1) less for each Fel spell you've cast this game.", "MINION"),
        // A pool that depends on the game (CardPoolTables): the catalog cannot answer it.
        Pool("TLC_464", "Mountain Map", "Discover a minion of a type you haven't played."),
    ], "test", DateTime.UtcNow);

    private readonly HearthstoneMemory _memory = new();
    private readonly GameSession _session;
    private DateTime _now = T0;
    private int _reads;
    private volatile string? _underMouse;
    private string _gameType = "GT_RANKED";

    public CardPoolHoverModeTests()
    {
        _session = new GameSession(HistoryStore.InMemory(), _memory)
        {
            Clock = () => _now,
            IsHearthstoneRunning = () => true,
            ReadsMemory = false,
            Cards = Cards,
            HoverSupported = true,
            // The test reads the card under the mouse itself (ReadHover), so no thread reads it in between.
            HoverThreadEnabled = false,
            HoverReader = () =>
            {
                Interlocked.Increment(ref _reads);
                return _underMouse;
            },
        };
        _session.SetCardPoolCatalog(Catalog);
        _session.OnSessionStarted("Hearthstone_2026_11_26_12_00_00", new DateTime(2026, 11, 26, 12, 0, 0), attachedMidSession: false);
    }

    public void Dispose() => _memory.Dispose();

    private static PoolCard Pool(string id, string name, string text, string type = "SPELL", params string[] races) =>
        new(id, 0, name, text, type, "COMMON", 1, type == "MINION" ? 1 : null, type == "MINION" ? 1 : null, ["NEUTRAL"], races, null, [], [], "X", true);

    /// <summary>A Ranked game (or <see cref="_gameType"/>) of a Priest with <paramref name="hand"/> in hand, published once.</summary>
    private void StartGame(AppSettings settings, params string[] hand)
    {
        _session.ApplySettings(settings);
        string[] lines =
        [
            "DebugPrintPower() - CREATE_GAME",
            "DebugPrintPower() -     GameEntity EntityID=1",
            "DebugPrintPower() -         tag=CARDTYPE value=GAME",
            "DebugPrintPower() -         tag=TURN value=5",
            "DebugPrintPower() -     Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=11]",
            "DebugPrintPower() -         tag=CONTROLLER value=1",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=20",
            "DebugPrintPower() -     Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115198130930503 lo=22]",
            "DebugPrintPower() -         tag=CONTROLLER value=2",
            "DebugPrintPower() -         tag=CARDTYPE value=PLAYER",
            "DebugPrintPower() -         tag=HERO_ENTITY value=21",
            .. Card(20, "HERO_09", "PLAY", 1, "HERO"),
            .. Card(21, "HERO_01", "PLAY", 2, "HERO"),
            $"DebugPrintGame() - GameType={_gameType}",
            "DebugPrintGame() - FormatType=FT_STANDARD",
            "DebugPrintGame() - PlayerID=1, PlayerName=velox#21276",
            "DebugPrintGame() - PlayerID=2, PlayerName=UNKNOWN HUMAN PLAYER",
            .. Card(99, "DR_2", "HAND", 1, "MINION"),
            .. hand.SelectMany((id, i) => Card(40 + i, id, "HAND", 1, "SPELL")),
        ];
        Feed(lines);
        Publish();
    }

    private void Feed(IEnumerable<string> lines) =>
        _session.OnLines("Power.log", lines.Select(line => "D 12:30:00.0000000 GameState." + line).ToList(), catchingUp: false);

    private void Publish()
    {
        _now += TimeSpan.FromSeconds(1);
        _session.OnTick();
    }

    private ConstructedView View => _session.Current.Constructed!;

    /// <summary>The player plays hand card 40, <paramref name="cardId"/>, and its Discover offers the Wolf and the Bear.</summary>
    private static string[] PlayAndDiscover(string cardId, string name) =>
    [
        $"DebugPrintPower() - BLOCK_START BlockType=PLAY Entity=[entityName={name} id=40 zone=HAND zonePos=2 cardId={cardId} player=1] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ",
        $"DebugPrintPower() -     TAG_CHANGE Entity=[entityName={name} id=40 zone=HAND zonePos=2 cardId={cardId} player=1] tag=ZONE value=PLAY ",
        "DebugPrintEntityChoices() - id=5 Player=velox#21276 TaskList=570 ChoiceType=GENERAL CountMin=1 CountMax=1",
        $"DebugPrintEntityChoices() -   Source=[entityName={name} id=40 zone=PLAY zonePos=0 cardId={cardId} player=1]",
        "DebugPrintEntityChoices() -   Entities[0]=[entityName=Wolf id=60 zone=SETASIDE zonePos=0 cardId=BEAST_1 player=1]",
        "DebugPrintEntityChoices() -   Entities[1]=[entityName=Bear id=61 zone=SETASIDE zonePos=0 cardId=BEAST_2 player=1]",
    ];

    [Fact]
    public void Without_ctrl_the_card_under_the_mouse_is_not_read_and_no_pool_shows()
    {
        StartGame(new AppSettings(), "GEN_A", "PLAIN");
        _underMouse = "GEN_A";

        Assert.Equal(CardPoolHover.Ctrl, new AppSettings().CardPoolHover);
        Assert.False(_session.HoverReadsWanted);
        Assert.False(_session.ReadHover());
        Publish();

        Assert.Equal(0, Volatile.Read(ref _reads));
        Assert.Null(View.CardPool);
        Assert.Null(View.MatchList);
    }

    [Fact]
    public void With_ctrl_held_the_hovered_cards_pool_shows_at_the_panels_own_spot_and_goes_when_ctrl_is_let_go()
    {
        StartGame(new AppSettings(), "GEN_A", "PLAIN");
        _underMouse = "GEN_A";

        _session.SetCardPoolControl(true);
        Assert.True(_session.HoverReadsWanted);
        Assert.True(_session.ReadHover());
        Publish();
        Assert.True(Volatile.Read(ref _reads) > 0);
        Assert.Equal(("GEN_A", PoolTrigger.Hover, 2), (View.CardPool?.SourceCardId, View.CardPool?.Trigger, View.CardPool?.Count));
        // Not beside the card: the movable panel's own spot.
        Assert.Null(View.CardPoolAnchor);

        // Another card under the mouse with Ctrl still held: nothing of its own, so nothing from the session (the overlay
        // keeps the shown pool while Ctrl is held).
        _underMouse = "PLAIN";
        _session.ReadHover();
        Publish();
        Assert.Null(View.CardPool);

        // Ctrl let go: the card is forgotten at once and no longer read.
        _underMouse = "GEN_A";
        _session.ReadHover();
        Publish();
        Assert.NotNull(View.CardPool);
        _session.SetCardPoolControl(false);
        Publish();
        Assert.Null(View.CardPool);
        Assert.False(_session.HoverReadsWanted);
        var reads = Volatile.Read(ref _reads);
        Assert.False(_session.ReadHover());
        Assert.Equal(reads, Volatile.Read(ref _reads));
    }

    [Fact]
    public void Always_reads_without_ctrl_and_off_never_reads()
    {
        StartGame(new AppSettings { CardPoolHover = CardPoolHover.Always }, "GEN_A");
        _underMouse = "GEN_A";

        Assert.True(_session.HoverReadsWanted);
        Assert.True(_session.ReadHover());
        Publish();
        Assert.Equal("GEN_A", View.CardPool?.SourceCardId);

        _session.ApplySettings(new AppSettings { CardPoolHover = CardPoolHover.Off });
        _session.SetCardPoolControl(true);
        Publish();
        Assert.Null(View.CardPool);
        Assert.False(_session.HoverReadsWanted);
        var reads = Volatile.Read(ref _reads);
        Assert.False(_session.ReadHover());
        Assert.Equal(reads, Volatile.Read(ref _reads));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_players_discover_shows_its_pool_beside_the_options_unless_its_switch_is_off(bool onDiscover)
    {
        StartGame(new AppSettings { CardPoolOnDiscover = onDiscover }, "GEN_A");
        Feed(PlayAndDiscover("GEN_A", "Beast Caller"));
        Publish();

        if (onDiscover)
        {
            Assert.Equal(("GEN_A", PoolTrigger.Choice), (View.CardPool?.SourceCardId, View.CardPool?.Trigger));
            Assert.Equal(new CardPoolAnchor(PoolAnchorZone.Choice, 0, 2), View.CardPoolAnchor);
        }
        else
        {
            Assert.Null(View.CardPool);
            Assert.Null(View.CardPoolAnchor);
        }

        // Nothing was read from the game for it: a Discover comes from the log.
        Assert.Equal(0, Volatile.Read(ref _reads));
    }

    /// <summary>
    /// AC4 with the "Read data from the game client" switch (P5, AC7): off, Ctrl held over a card with a pool reads nothing
    /// and shows nothing; turned off while a hovered card's pool shows, the card is forgotten at once and not read again.
    /// </summary>
    [Fact]
    public void With_reading_the_client_off_ctrl_over_a_card_reads_nothing()
    {
        StartGame(new AppSettings { ReadGameMemory = false }, "GEN_A");
        _underMouse = "GEN_A";

        _session.SetCardPoolControl(true);
        Assert.False(_session.HoverReadsWanted);
        Assert.False(_session.ReadHover());
        Publish();
        Assert.Equal(0, Volatile.Read(ref _reads));
        Assert.Null(View.CardPool);

        // Reading on again: Ctrl over the card reads it and its pool shows.
        _session.ApplySettings(new AppSettings());
        Publish();
        Assert.True(_session.HoverReadsWanted);
        Assert.True(_session.ReadHover());
        Publish();
        Assert.Equal("GEN_A", View.CardPool?.SourceCardId);

        // Off with Ctrl still held: forgotten at once, no read since.
        _session.SetGameMemoryEnabled(false);
        Assert.False(_session.HoverReadsWanted);
        var reads = Volatile.Read(ref _reads);
        Assert.False(_session.ReadHover());
        Publish();
        Assert.Null(View.CardPool);
        Assert.Equal(reads, Volatile.Read(ref _reads));
    }

    /// <summary>
    /// TASK-2026-11-26 M1: a Discover whose pool the catalog cannot answer (Mountain Map: it depends on the game) shows only
    /// its pick hint beside the options, and nothing while the API has no numbers for it.
    /// </summary>
    [Fact]
    public void A_discover_the_catalog_cannot_answer_shows_only_its_pick_hint()
    {
        StartGame(new AppSettings(), "TLC_464");
        Feed(PlayAndDiscover("TLC_464", "Mountain Map"));
        Publish();
        Assert.Null(View.CardPool);
        Assert.Null(View.DiscoverPicks);
        Assert.Null(View.CardPoolAnchor);

        // The numbers as the API gives them, already in memory: no request is made.
        var picks = new DiscoverChoiceSource(new HearthpulseData(new HearthpulseApi(new HttpClient(), "https://api.test/"), Path.Combine(TestDataFolder.Path, "card-choices-" + Guid.NewGuid().ToString("N"))))
        {
            Start = static (_, _) => { },
        };
        picks.Set(
            [new ChoiceCard("TLC_464", 16, new HashSet<string>(StringComparer.Ordinal) { "standard" }, true)],
            new CardChoices("TLC_464", "standard", "legend", [new CardChoiceOption("BEAST_1", 20, 0.7, 0.55, false), new CardChoiceOption("BEAST_2", 21, 0.3, 0.5, false)], null));
        _session.SetDiscoverPicks(picks);
        Publish();

        Assert.Equal(("TLC_464", PoolTrigger.Choice, 0), (View.CardPool?.SourceCardId, View.CardPool?.Trigger, View.CardPool?.Count));
        Assert.NotNull(View.CardPool!.Uncomputed);
        Assert.Equal(["BEAST_1", "BEAST_2"], View.DiscoverPicks?.Rows.Select(r => r.CardId));
        Assert.Equal(new CardPoolAnchor(PoolAnchorZone.Choice, 0, 2), View.CardPoolAnchor);
        Assert.Equal(0, Volatile.Read(ref _reads));
    }

    /// <summary>Q2: a card with only a "this game" list is read for with Ctrl held, and shows its list; not without Ctrl.</summary>
    [Fact]
    public void A_card_with_only_a_this_game_list_is_read_with_ctrl_held_and_shows_its_list()
    {
        StartGame(new AppSettings(), "LIST", "PLAIN");
        _underMouse = "LIST";

        Assert.False(_session.ReadHover());
        Publish();
        Assert.Equal(0, Volatile.Read(ref _reads));
        Assert.Null(View.MatchList);

        _session.SetCardPoolControl(true);
        Assert.True(_session.ReadHover());
        Publish();
        Assert.True(Volatile.Read(ref _reads) > 0);
        Assert.Null(View.CardPool);
        Assert.Equal("LIST", View.MatchList?.SourceCardId);
        Assert.Null(View.CardPoolAnchor);
    }

    /// <summary>Q2: with "Card pool" off, or in a mode the pool does not cover (Arena), Ctrl over a card reads nothing.</summary>
    [Theory]
    [InlineData("GT_RANKED", false)]
    [InlineData("GT_ARENA", true)]
    public void With_the_pool_off_or_in_arena_ctrl_over_a_card_reads_nothing(string gameType, bool showCardPool)
    {
        _gameType = gameType;
        StartGame(new AppSettings { ShowCardPool = showCardPool }, "GEN_A", "LIST");
        _underMouse = "GEN_A";

        _session.SetCardPoolControl(true);
        Assert.False(_session.HoverReadsWanted);
        Assert.False(_session.ReadHover());
        _underMouse = "LIST";
        Assert.False(_session.ReadHover());
        Publish();

        Assert.Equal(0, Volatile.Read(ref _reads));
        Assert.Null(View.CardPool);
        Assert.Null(View.MatchList);
    }

    /// <summary>
    /// m3: Ctrl reaches the session only through each host's subscription. Without it the default "with Ctrl" mode would
    /// quietly never read nor show, and the view-model and session tests would stay green.
    /// </summary>
    [Theory]
    [InlineData("src/Hearthpulse.App/App.xaml.cs")]
    [InlineData("src/Hearthpulse.Desktop/DesktopApp.cs")]
    public void Both_hosts_hand_ctrl_to_the_session(string file) =>
        Assert.Matches(new Regex(@"^\s*[\w.]+\.CardPoolControlChanged \+= [\w.]+\.SetCardPoolControl;", RegexOptions.Multiline), File.ReadAllText(Path.Combine(RepoFiles.Root, file)));

    [Fact]
    public void A_card_with_only_related_cards_opens_no_panel_and_is_not_read_for()
    {
        StartGame(new AppSettings { CardPoolHover = CardPoolHover.Always }, "REL", "PLAIN");
        _underMouse = "REL";

        // Its token is still the card preview's (deck rows), but no reason to read the card under the mouse.
        Assert.Equal(["RELt"], _session.RelatedCardsOf("REL").Select(r => r.CardId));
        Assert.False(_session.HoverReadsWanted);
        Assert.False(_session.ReadHover());
        Publish();
        Assert.Null(View.CardPool);
        Assert.Null(View.MatchList);
        Assert.Equal(0, Volatile.Read(ref _reads));
    }
}

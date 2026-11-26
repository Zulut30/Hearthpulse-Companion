using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.EctoplasmListTests;

namespace Hearthpulse.Tests;

/// <summary>
/// TASK-2026-11-26 AC4: a card in hand shows its pool as the hover mode says, and the card under the mouse is read from
/// the game only then - with Ctrl (the default) only while Ctrl is held, always, or never; the player's own Discover
/// shows its pool beside the options unless its switch is off. A fake reader stands in for game memory.
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
          {"id":"BEAST_2","dbfId":21,"name":"Bear","type":"MINION","cost":3,"set":"X","collectible":true}
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
    ], "test", DateTime.UtcNow);

    private readonly HearthstoneMemory _memory = new();
    private readonly GameSession _session;
    private DateTime _now = T0;
    private int _reads;
    private volatile string? _underMouse;

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

    /// <summary>A Ranked game of a Priest with <paramref name="hand"/> in hand, published once.</summary>
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
            "DebugPrintGame() - GameType=GT_RANKED",
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
        Feed(
        [
            "DebugPrintPower() - BLOCK_START BlockType=PLAY Entity=[entityName=Beast Caller id=40 zone=HAND zonePos=2 cardId=GEN_A player=1] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 ",
            "DebugPrintPower() -     TAG_CHANGE Entity=[entityName=Beast Caller id=40 zone=HAND zonePos=2 cardId=GEN_A player=1] tag=ZONE value=PLAY ",
            "DebugPrintEntityChoices() - id=5 Player=velox#21276 TaskList=570 ChoiceType=GENERAL CountMin=1 CountMax=1",
            "DebugPrintEntityChoices() -   Source=[entityName=Beast Caller id=40 zone=PLAY zonePos=0 cardId=GEN_A player=1]",
            "DebugPrintEntityChoices() -   Entities[0]=[entityName=Wolf id=60 zone=SETASIDE zonePos=0 cardId=BEAST_1 player=1]",
            "DebugPrintEntityChoices() -   Entities[1]=[entityName=Bear id=61 zone=SETASIDE zonePos=0 cardId=BEAST_2 player=1]",
        ]);
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

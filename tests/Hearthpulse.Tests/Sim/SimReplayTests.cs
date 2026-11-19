using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthpulse.Bench;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.BattlegroundsReplayTests;
using static Hearthpulse.Tests.Sim.SimTestKit;
using CombatEvent = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEvent;
using CombatEventKind = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEventKind;
using CombatTranscript = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatTranscript;
using MinionState = Hearthpulse.Core.Tracking.BattlegroundsTracker.MinionState;
using BoardSnapshot = Hearthpulse.Core.Tracking.BattlegroundsTracker.BoardSnapshot;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Step-by-step replay of real fights (docs/bg-combat-sim.md, «Пошаговая сверка»): every random decision of the engine goes
/// through one chooser with a named kind and explicit candidates; the tracker records the visible course of each fight;
/// <c>simreplay</c> answers each decision from that record and reports where the engine leaves the log.
/// </summary>
public sealed class SimReplayTests
{
    private static readonly DateOnly Day = new(2026, 10, 23);

    // ------------------------------------------------------------------------------------------------ decisions

    /// <summary>Code with comments and string literals blanked: what a source file actually calls.</summary>
    private static string Code(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/|//[^\n]*", string.Empty, RegexOptions.Singleline), "\"(?:[^\"\\\\\\n]|\\\\.)*\"", "\"\"");

    private static readonly Regex DirectRandom = new(@"\bRandom\b|\bRandomNumberGenerator\b|\bGuid\.NewGuid\b|\bEnvironment\.TickCount");

    [Fact]
    public void Effects_and_the_engine_draw_nothing_at_random_but_through_the_chooser()
    {
        var root = Path.Combine(RepoFiles.Root, "src", "Hearthpulse.Core", "Battlegrounds");
        var files = Directory.EnumerateFiles(Path.Combine(root, "Effects"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Simulation"), "*.cs"))
            .ToList();
        Assert.True(files.Count > 50, $"{files.Count} files");

        var offenders = files
            .Where(f => Path.GetFileName(f) != "CombatChooser.cs" && DirectRandom.IsMatch(Code(File.ReadAllText(f))))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        // A random pick made any other way is one a replay cannot answer and a test cannot script.
        Assert.Empty(offenders);
        Assert.Contains("random.Next(", File.ReadAllText(Path.Combine(root, "Simulation", "CombatChooser.cs")), StringComparison.Ordinal);
        // The check bites: these are what it looks for.
        Assert.Matches(DirectRandom, Code("var i = fight.Random.Next(2);"));
        Assert.Matches(DirectRandom, Code("var r = new System.Random();"));
        Assert.DoesNotMatch(DirectRandom, Code("fight.RandomOf(list, self); // Random in a comment\nvar s = \"Random\";"));
    }

    /// <summary>"Deathrattle: give a random other friendly minion +1/+1."</summary>
    [CardEffect("TEST_PICK")]
    internal sealed class PickBuff : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self)
        {
            if (fight.RandomFriendly(self) is { } target)
            {
                fight.Buff(target, 1, 1);
            }
        }
    }

    /// <summary>Keeps every decision and attack it sees, picking as the seeded default does.</summary>
    private sealed class Recording(Random random) : ICombatChooser
    {
        private readonly RandomChooser _picks = new(random);

        public List<(ChoiceKind Kind, string? Source, int Count, string[] Units)> Choices { get; } = new();

        public List<string> Steps { get; } = new();

        public int Choose(Fight fight, in CombatChoice choice)
        {
            Choices.Add((choice.Kind, choice.Source?.CardId, choice.Count, choice.Units?.Select(u => u.CardId).ToArray() ?? []));
            return _picks.Choose(fight, choice);
        }

        public void OnAttack(Fight fight, Unit attacker, Unit target, bool nested) => Steps.Add($"attack {attacker.CardId}>{target.CardId}{(nested ? " nested" : "")}");

        public void OnSummon(Fight fight, Unit unit) => Steps.Add($"summon {unit.CardId}");
    }

    [Fact]
    public void Every_decision_names_its_kind_its_source_and_its_candidates()
    {
        var chooser = new Recording(new Random(7));
        var ours = Side(Minion(1, 1, cardId: "TEST_PICK"), Minion(1, 9, cardId: "OUR_B"), Minion(1, 9, cardId: "OUR_C"));
        var theirs = Side(Minion(5, 50, cardId: "THEIR_X"), Minion(1, 50, Keyword.Taunt, cardId: "THEIR_TAUNT"), Minion(0, 50, cardId: "THEIR_Z"));

        CombatSimulator.RunSingle(ours, theirs, null, chooser, Effects(typeof(PickBuff)), trace: false);

        // Equal boards: who attacks first is the first decision, between the two sides.
        Assert.Equal((ChoiceKind.FirstAttacker, (string?)null, 2), (chooser.Choices[0].Kind, chooser.Choices[0].Source, chooser.Choices[0].Count));
        Assert.Empty(chooser.Choices[0].Units);
        // Our attackers may only hit the Taunt: it is the only candidate offered.
        var ourTargets = chooser.Choices.Where(c => c.Kind == ChoiceKind.AttackTarget && c.Source!.StartsWith("OUR_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(ourTargets);
        Assert.All(ourTargets, c => Assert.Equal(["THEIR_TAUNT"], c.Units));
        // Theirs choose among every living minion of ours.
        var theirTargets = chooser.Choices.First(c => c.Kind == ChoiceKind.AttackTarget && c.Source == "THEIR_X");
        Assert.Equal(theirTargets.Count, theirTargets.Units.Length);
        // The deathrattle's pick: made by the dead minion, among the other living friends.
        var pick = Assert.Single(chooser.Choices, c => c.Kind == ChoiceKind.RandomTarget);
        Assert.Equal("TEST_PICK", pick.Source);
        Assert.Equal(["OUR_B", "OUR_C"], pick.Units);
        Assert.Contains(chooser.Steps, s => s.StartsWith("attack ", StringComparison.Ordinal) && !s.EndsWith("nested", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_chooser_draws_as_the_running_pick_it_replaced()
    {
        // Three tied candidates: the old running pick drew Next(2), then Next(3), keeping the newest on a 0.
        var random = new ScriptedRandom(1, 0);
        var units = Array.Empty<Unit>();
        var tie = new CombatChoice(ChoiceKind.TieBreak, null, 3, units, null);

        Assert.Equal(2, new RandomChooser(random).Choose(null!, tie));
        Assert.Equal(2, random.Calls);
        // Any other decision is one uniform draw.
        var other = new ScriptedRandom(4);
        Assert.Equal(4, new RandomChooser(other).Choose(null!, new CombatChoice(ChoiceKind.RandomSummon, null, 6, null, [])));
        Assert.Equal(1, other.Calls);
    }

    [Fact]
    public void A_seed_still_gives_the_same_odds()
    {
        var ours = Side(Minion(3, 4), Minion(2, 6, Keyword.DivineShield), Minion(1, 1, cardId: "TEST_PICK"));
        var theirs = Side(Minion(4, 4, Keyword.Taunt), Minion(5, 2), Minion(1, 7));
        var options = new SimOptions { Runs = 400, MinRuns = 400, Effects = Effects(typeof(PickBuff)) };

        Assert.Equal(JsonSerializer.Serialize(CombatSimulator.Run(ours, theirs, null, options)),
            JsonSerializer.Serialize(CombatSimulator.Run(ours, theirs, null, options)));
    }

    // ------------------------------------------------------------------------------------------------ transcript

    private static CombatTranscript Transcript(params string[] fight)
    {
        // Bob's offer left in his play would stand on the opponent's board: it goes, as in a real tavern.
        var (_, _, tracker) = Play([.. Setup, Tag(51, "ZONE", "REMOVEDFROMGAME"), .. CombatSetup(), .. fight]);
        var capture = Assert.Single(tracker.Captures);
        return Assert.IsType<CombatTranscript>(capture.Transcript);
    }

    [Fact]
    public void A_fight_is_transcribed_from_the_first_attack_to_the_closing_hero_attack()
    {
        var transcript = Transcript(BattlegroundsReplayTests.Fight(win: true));

        Assert.True(transcript.Complete);
        Assert.Equal("hero", transcript.Ending);
        Assert.Equal(3, transcript.RegularAttacks);
        var attacks = transcript.Events.Where(e => e.Kind == CombatEventKind.Attack).Select(e => (e.Entity, e.Other, e.Value)).ToList();
        Assert.Equal([(101, 111, 0), (112, 101, 0), (101, 112, 0)], attacks);
        Assert.Equal([111, 112], transcript.Events.Where(e => e.Kind == CombatEventKind.Death).Select(e => e.Entity));
        // Damage is the change of the DAMAGE tag: 1, then 2 in total (1 more), then 3 (1 more).
        Assert.Equal([1, 1, 1], transcript.Events.Where(e => e is { Kind: CombatEventKind.Damage, Entity: 101 }).Select(e => e.Value));
        // Our minion's own trigger is a triggered effect with its card; the game's and the rules entity's are not.
        var trigger = Assert.Single(transcript.Events, e => e.Kind == CombatEventKind.Trigger);
        Assert.Equal((101, "BG26_801", false), (trigger.Entity, trigger.Card, trigger.Opponent));
        Assert.Single(transcript.Events, e => e.Kind == CombatEventKind.End);
        // ATK 5..9 and HEALTH 7: setting HEALTH to the 6 it already had is no change.
        Assert.Equal(6, transcript.Events.Count(e => e is { Kind: CombatEventKind.Tag, Entity: 101 }));

        // The boards before each regular attack, then the last one: as the simulator got them at the start.
        Assert.Equal(4, transcript.Boards.Count);
        Assert.Equal([(101, "BG26_801", 4, 6)], transcript.Boards[0].Ours.Select(m => (m.Entity, m.Card, m.Attack, m.Health)));
        Assert.Equal([111, 112], transcript.Boards[0].Theirs.Select(m => m.Entity));
        Assert.Equal([112], transcript.Boards[1].Theirs.Select(m => m.Entity));
        Assert.Equal([(101, 9, 4)], transcript.Boards[3].Ours.Select(m => (m.Entity, m.Attack, m.Health)));
        Assert.Empty(transcript.Boards[3].Theirs);
    }

    [Fact]
    public void The_opponents_hand_never_reaches_a_transcript()
    {
        string[] fight =
        [
            Start("ATTACK", 101), Game("PROPOSED_DEFENDER", 111), Tag(111, "DAMAGE", 2), Tag(101, "DAMAGE", 1), End,
            // A card the opponent gets in combat, a trigger from their hand and a change to it: hidden from the player.
            .. Entity(150, "OPP_HAND_CARD", Bob, "HAND", "MINION", "ATK=1", "HEALTH=1"),
            Start("TRIGGER", 150), Tag(150, "ATK", 3), End,
            // The same for us: our hand is on our screen.
            .. Entity(151, "OUR_HAND_CARD", Me, "HAND", "MINION", "ATK=1", "HEALTH=1"),
            Start("TRIGGER", 151), Tag(151, "ATK", 3), End,
            Start("DEATHS", 1), Tag(111, "ZONE", "GRAVEYARD"), End,
            Start("ATTACK", 112), Game("PROPOSED_DEFENDER", 101), Tag(101, "DAMAGE", 3), Tag(112, "DAMAGE", 4), End,
            Start("DEATHS", 1), Tag(112, "ZONE", "GRAVEYARD"), End,
            Start("TRIGGER", 46), Start("ATTACK", 10), Tag(110, "DAMAGE", 5), End, End,
        ];

        var transcript = Transcript(fight);

        Assert.True(transcript.Complete);
        Assert.DoesNotContain(transcript.Events, e => e.Entity == 150 || e.Card == "OPP_HAND_CARD");
        Assert.DoesNotContain("OPP_HAND_CARD", JsonSerializer.Serialize(transcript), StringComparison.Ordinal);
        Assert.Contains(transcript.Events, e => e is { Kind: CombatEventKind.Hand, Entity: 151, Card: "OUR_HAND_CARD" });
        Assert.Contains(transcript.Events, e => e is { Kind: CombatEventKind.Trigger, Entity: 151 });
        Assert.Contains(transcript.Events, e => e is { Kind: CombatEventKind.Tag, Entity: 151, Tag: "ATK", Value: 3 });
    }

    [Fact]
    public void A_tie_ends_before_the_tavern_board_comes_back()
    {
        string[] fight =
        [
            // The tavern copy of our minion, set aside for the fight.
            .. Entity(160, "BG26_801", Me, "SETASIDE", "MINION", "ATK=4", "HEALTH=6"),
            Start("ATTACK", 101), Game("PROPOSED_DEFENDER", 111), Tag(111, "DAMAGE", 2), Tag(101, "DAMAGE", 6), End,
            Start("DEATHS", 1), Tag(111, "ZONE", "GRAVEYARD"), Tag(101, "ZONE", "GRAVEYARD"), End,
            Start("ATTACK", 112), Game("PROPOSED_DEFENDER", 101), End,
            Start("DEATHS", 1), Tag(112, "ZONE", "GRAVEYARD"), End,
            // The game brings the tavern board back, then clears the fight.
            Start("TRIGGER", 46), Tag(160, "ZONE", "PLAY"), Tag(101, "ZONE", "REMOVEDFROMGAME"), End,
            Game("BOARD_VISUAL_STATE", 1),
        ];

        var transcript = Transcript(fight);

        Assert.True(transcript.Complete);
        Assert.Equal("restore", transcript.Ending);
        Assert.Empty(transcript.Boards[^1].Ours);
        Assert.Empty(transcript.Boards[^1].Theirs);
        Assert.DoesNotContain(transcript.Events, e => e.Entity == 160);
    }

    [Fact]
    public void Recording_is_off_when_the_tracker_is_told_so()
    {
        var parser = new Hearthpulse.Core.Power.PowerParser(Day);
        var tracker = new BattlegroundsTracker { RecordsTranscripts = false };
        parser.AddListener(tracker);
        foreach (var line in (string[])[.. Setup, .. CombatSetup(), .. BattlegroundsReplayTests.Fight(win: true)])
        {
            parser.Feed("D 12:30:00.0000000 GameState." + line);
        }

        Assert.Null(Assert.Single(tracker.Captures).Transcript);
    }

    // ------------------------------------------------------------------------------------------------ replay

    private static readonly Lazy<CardDb> FixtureCards = new(() =>
    {
        using var gz = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BattlegroundsGame", "cards.json.gz")),
            CompressionMode.Decompress);
        return CardDb.Parse("ruRU", gz);
    });

    /// <summary>The real fight kept in the repository (Fixtures/BattlegroundsGame): turn 1, two attacks, a tie.</summary>
    private static ReplayableFight FixtureFight() =>
        Assert.Single(SimReplayKit.Fights(BattlegroundsFixtureTests.Lines(), Day, FixtureCards.Value));

    [Fact]
    public void The_kept_real_fight_replays_step_by_step_without_a_divergence()
    {
        var fight = FixtureFight();

        var replayed = SimReplayKit.AssertReplays(fight, FixtureCards.Value);

        Assert.Equal(2, replayed.LogAttacks);
        Assert.Equal("Tie", replayed.Result);
        Assert.Equal("restore", replayed.Ending);
    }

    [Fact]
    public void A_board_the_engine_does_not_reach_is_the_first_divergence()
    {
        var fight = FixtureFight().Fight;
        var transcript = fight.Transcript!;
        // The log says the defender kept one more Health after the first attack than the engine leaves it.
        var after = transcript.Boards[1];
        var changed = after with { Theirs = after.Theirs.Select(m => m with { Health = m.Health + 1 }).ToList() };
        var boards = transcript.Boards.ToList();
        boards[1] = changed;

        var replayed = SimReplay.Replay("x", fight with { Transcript = transcript with { Boards = boards } }, FixtureCards.Value);

        var first = Assert.IsType<Divergence>(replayed!.First);
        Assert.Equal((1, "health", ReplayCategory.Damage), (first.Attack, first.Field, first.Category));
        Assert.Equal(changed.Theirs[0].Entity, first.Entity);
        Assert.False(replayed.Clean);
    }

    [Fact]
    public void A_defender_the_engine_does_not_offer_is_a_divergence()
    {
        var fight = FixtureFight().Fight;
        var transcript = fight.Transcript!;
        var events = transcript.Events.ToList();
        var first = events.FindIndex(e => e.Kind == CombatEventKind.Attack);
        events[first] = events[first] with { Other = 999_999 };

        var replayed = SimReplay.Replay("x", fight with { Transcript = transcript with { Events = events } }, FixtureCards.Value);

        Assert.Equal((0, "defender", ReplayCategory.Target), (replayed!.First!.Attack, replayed.First.Field, replayed.First.Category));
    }

    /// <summary>
    /// Their 5/50 hits our TEST_PICK, whose deathrattle buffs a random other friend: the log says the right one (#13). The
    /// replay must pick it, then our next attacker (#12) attacks; the log stops there.
    /// </summary>
    private static SavedFight Picking(int buffed)
    {
        SimMinion M(int id, int atk, int hp, string card) => Minion(atk, hp, cardId: card) with { EntityId = id };
        MinionState S(int id, string card, int atk, int hp) => new(id, card, atk, hp, Keyword.None);
        CombatEvent E(CombatEventKind kind, int entity, int other = 0, int value = 0, string? card = null, string? tag = null) =>
            new(kind, entity) { Other = other, Value = value, Card = card, Tag = tag };
        var transcript = new CombatTranscript
        {
            Events =
            [
                E(CombatEventKind.Attack, 21, 11), E(CombatEventKind.Damage, 11, value: 5), E(CombatEventKind.Damage, 21, value: 1),
                E(CombatEventKind.Death, 11),
                E(CombatEventKind.Trigger, 11, card: "TEST_PICK"), E(CombatEventKind.Tag, buffed, value: 2, tag: "ATK"), E(CombatEventKind.End, 0),
                E(CombatEventKind.Attack, 12, 21),
            ],
            Boards =
            [
                new BoardSnapshot(0, [S(11, "TEST_PICK", 1, 1), S(12, "OUR_B", 1, 9), S(13, "OUR_C", 1, 9)], [S(21, "THEIR_X", 5, 50)]),
                new BoardSnapshot(7, [S(12, "OUR_B", 1, 9), S(13, "OUR_C", 2, 10)], [S(21, "THEIR_X", 5, 49)]),
            ],
            Complete = false,
            Ending = "cut",
        };
        return new SavedFight
        {
            Ours = Side(M(11, 1, 1, "TEST_PICK"), M(12, 1, 9, "OUR_B"), M(13, 1, 9, "OUR_C")),
            Theirs = Side(M(21, 5, 50, "THEIR_X")),
            OursFirst = false,
            FirstAttackerEntityId = 21,
            Transcript = transcript,
        };
    }

    [Fact]
    public void A_random_pick_of_an_effect_is_answered_from_what_the_log_did()
    {
        var replayed = SimReplay.Replay("pick", Picking(buffed: 13), CardDb.Empty, Effects(typeof(PickBuff)));

        // The second board (after the deathrattle) matches only if the replay gave the buff to #13.
        Assert.Null(replayed!.First);
        Assert.False(replayed.Complete);
        Assert.Equal(2, replayed.Attacks);
    }

    [Fact]
    public void A_pick_the_log_made_outside_the_candidates_is_a_divergence()
    {
        var replayed = SimReplay.Replay("pick", Picking(buffed: 21), CardDb.Empty, Effects(typeof(PickBuff)));

        var first = Assert.IsType<Divergence>(replayed!.First);
        Assert.Equal(("choice:RandomTarget", ReplayCategory.Trigger), (first.Field, first.Category));
        Assert.Equal(1, first.Attack);
    }

    [Fact]
    public void The_report_counts_complete_fights_and_groups_first_divergences()
    {
        var d = new Divergence(2, 5, "X", "attacker", "a", "b", ReplayCategory.AttackerOrder, []);
        ReplayedFight[] fights =
        [
            new("a", 1, "Win", 3, 3, true, "hero", null),
            new("b", 2, "Loss", 4, 2, true, "hero", d),
            new("c", 3, "Tie", 2, 1, false, "cut", null),
        ];

        var report = SimReplay.Report(fights).ToList();

        Assert.Contains(report, l => l.StartsWith("replayed to the end without divergence: 1 / 2 = 50.0 %", StringComparison.Ordinal));
        Assert.Contains(report, l => l.Contains("AttackerOrder", StringComparison.Ordinal) && l.Contains("  1 ", StringComparison.Ordinal));
        Assert.Contains(report, l => l.Contains("incomplete (not counted): 1", StringComparison.Ordinal));
    }
}

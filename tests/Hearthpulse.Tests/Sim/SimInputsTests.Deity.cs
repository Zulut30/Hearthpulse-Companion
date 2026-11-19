using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Power;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// The Deity's inputs (package I2 of TASK-2026-11-19): its "Secret Deity" objective (BG_OldGod) in each player's SECRET
/// zone counts the awakening, and the opponent's copy is revealed when the combat starts and drawn with the owner's
/// stats. Line shapes from the owner's log of 2026-11-15 (game 3, combat 11: L746391-746504).
/// </summary>
public sealed partial class SimInputsTests
{
    private const string TheirName = "Bob's Opponent";

    /// <summary>
    /// A combat whose opponent objective is revealed with QUEST_PROGRESS 2 of 4 (as in G3 #11) and their player's Deity
    /// stats 722/788 (G2 #12, L387630); <paramref name="drawn"/> = the client is told to draw the Deity with those stats.
    /// </summary>
    private static PowerLog RevealedDeityGame(bool drawn)
    {
        var log = new PowerLog()
            .BattlegroundsGame(ourTags: [("BACON_OLD_GOD_ATTACK", "10"), ("BACON_OLD_GOD_HEALTH", "12")])
            .Deity(CThunDbf)
            // Our own objective, in our SECRET zone all game.
            .Entity(60, SimInputs.DeityObjective, Me, "SECRET", "SPELL", ("QUEST_PROGRESS", "1"), ("QUEST_PROGRESS_TOTAL", "4"),
                    ("BACON_EVOLUTION_CARD_OVERWRITE_ATK", "1"), ("BACON_DEITY_SIGIL", "1"))
            // The copy that becomes the opponent's: known to us, set aside.
            .Entity(70, SimInputs.DeityObjective, Me, "SETASIDE", "SPELL")
            .Minion(30, "BG36_116", Me, 1, 2, 2)
            .Minion(40, "BG36_116", Them, 1, 2, 2)
            .CombatStarts()
            .Power("HIDE_ENTITY - Entity=70 tag=ZONE value=SETASIDE")
            .Tag("70", "CONTROLLER", Them.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Show(70, SimInputs.DeityObjective, ("CONTROLLER", Them.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                  ("CARDTYPE", "SPELL"), ("TAG_SCRIPT_DATA_NUM_1", "2"), ("ZONE", "SECRET"), ("QUEST_PROGRESS", "2"),
                  ("QUEST_PROGRESS_TOTAL", "4"), ("QUEST_HIDE_PROGRESS", "1"), ("BACON_DEITY_SIGIL", "1"));
        if (drawn)
        {
            log.Tag("70", GameTag.BaconEvolutionOverwriteAttack, "1").Tag("70", "BACON_EVOLUTION_CARD_OVERWRITE_HEALTH", "1");
        }

        return log
            .Tag(TheirName, GameTag.BaconOldGodAttack, "722")
            .Tag(TheirName, GameTag.BaconOldGodHealth, "788")
            .Block("ATTACK", Ref(30, "Underrot Spawn", "BG36_116", Me));
    }

    [Fact]
    public void The_opponents_revealed_deity_gives_its_stats_and_awakening_progress_exactly()
    {
        var (game, tracker) = RevealedDeityGame(drawn: true).Replay(Cards, EffectRegistry.Empty);
        var capture = tracker.LastCapture!;

        // The client draws their Deity with these stats: on the player's screen, so the live odds use them.
        Assert.Equal((722, 788), (capture.Theirs.Tags[Deity.AttackTag], capture.Theirs.Tags[Deity.HealthTag]));
        Assert.DoesNotContain(capture.Theirs.Estimates, e => e.Input == SimInputs.DeityStats);
        Assert.False(tracker.LastCaptureOptions!.PostGame);
        Assert.Equal(0, tracker.BuildView(game).CombatOdds!.Estimated);
        // The awakening counts from both objectives, not from the printed four.
        Assert.Equal(new DeityAwakening(2, 4), capture.Theirs.DeityAwakening);
        Assert.Equal(new DeityAwakening(1, 4), capture.Ours.DeityAwakening);
        Assert.Equal(2, SimInputs.DeityDeathsStillNeeded(capture.Theirs, Tokens.CThunId, Deity.AberrationDeathsToAwaken));
        Assert.Equal(3, SimInputs.DeityDeathsStillNeeded(capture.Ours, Tokens.CThunId, Deity.AberrationDeathsToAwaken));
    }

    [Fact]
    public void Without_the_deity_drawn_with_their_stats_the_opponents_stats_stay_hidden_until_the_game_ends()
    {
        var (_, tracker) = RevealedDeityGame(drawn: false).Replay(Cards, EffectRegistry.Empty);
        var theirs = tracker.LastCapture!.Theirs;

        Assert.DoesNotContain(theirs.Tags, t => t.Value is 722 or 788);
        var estimate = Assert.Single(theirs.Estimates, e => e.Input == SimInputs.DeityStats);
        Assert.Equal((10, 12), (estimate.Value1, estimate.Value2));
        Assert.Empty(theirs.HiddenTags);
        // The objective's count is on the revealed card's text either way ("(N left!)", TAG_SCRIPT_DATA_NUM_1).
        Assert.Equal(new DeityAwakening(2, 4), theirs.DeityAwakening);

        var (_, ended) = RevealedDeityGame(drawn: false).Tag("2", "PLAYSTATE", "LOST").Replay(Cards, EffectRegistry.Empty);
        Assert.Equal(722, Assert.Single(ended.Captures).Capture.Theirs.HiddenTags[Deity.AttackTag]);
    }

    [Fact]
    public void The_deity_awakens_at_the_count_its_objective_still_needs()
    {
        // One Aberration dies in the fight. The objective had counted three of four: C'Thun awakens; two of four: it sleeps.
        // Its count already holds Start of Combat deaths, so those are not added again.
        var dead = Minion(0, 1, cardId: "AB", tribes: Tribe.Aberration);
        SimSide Ours(int progress, params SimMinion[] diedBefore) => new([Minion(0, 1, cardId: "AB4", tribes: Tribe.Aberration)])
        {
            Extras = [Tokens.CThunId],
            Tags = new Dictionary<string, int> { [Deity.AttackTag] = 3, [Deity.HealthTag] = 2 },
            DeityAwakening = new DeityAwakening(progress, 4),
            DeathsBeforeCapture = diedBefore,
        };

        var woke = Fight(Ours(3), Side(Minion(1, 50)), oursFirst: false, Effects(typeof(CThun)));
        var asleep = Fight(Ours(2), Side(Minion(1, 50)), oursFirst: false, Effects(typeof(CThun)));
        var counted = Fight(Ours(0, dead, dead, dead), Side(Minion(1, 50)), oursFirst: false, Effects(typeof(CThun)));

        Assert.True(woke.IndexOf("Deity BGFYM_000 awakens") > 0);
        Assert.Equal(-1, asleep.IndexOf("awakens"));
        Assert.Equal(-1, counted.IndexOf("awakens"));
        // Awake before the capture: never again, whatever the count says.
        Assert.Equal(int.MaxValue, SimInputs.DeityDeathsStillNeeded(Ours(5) with { Minions = [Minion(3, 2, cardId: Tokens.CThunId)] }, Tokens.CThunId, 4));
    }
}

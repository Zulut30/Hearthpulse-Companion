using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Broodmother (BG22_HERO_305p): "Avenge (4): Summon a 1/1 Whelp that attacks immediately. Improve this by +1/+1." The
/// Avenge progress made before the capture is the hero power's SCORE_VALUE_2 (an Avenge minion's progress tag in real
/// logs), X is SCORE_VALUE_1; the Whelp size is NUM_1/NUM_2 when they agree.
/// </summary>
public sealed class BroodmotherTests
{
    private const string Power = "BG22_HERO_305p";
    private const string Whelp = "summoned our BG22_HERO_305t";

    private static Dictionary<string, int> Tags(int whelp, int progress, int? threshold = null)
    {
        var tags = Numbers(whelp, whelp);
        tags["SCORE_VALUE_2"] = progress;
        if (threshold is { } x)
        {
            tags["SCORE_VALUE_1"] = x;
        }

        return tags;
    }

    private static SimMinion[] OneOnes(int count) => Enumerable.Range(0, count).Select(i => Minion(1, 1, cardId: $"M{i}")).ToArray();

    [Fact]
    public void Progress_from_start_of_combat_makes_the_first_death_in_the_fight_the_fourth()
    {
        // Three friendly deaths were counted before the capture (SCORE_VALUE_2 = 3). Their 1/100 kills M0: the fourth death,
        // so a 1/1 Whelp comes at once and attacks.
        var ours = WithCard(Side(OneOnes(2)), Power, Tags(1, progress: 3));
        var result = Fight(ours, Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(Broodmother)));

        Assert.Equal(1, result.DeathsBefore(Whelp + " 1/1"));
        Assert.True(result.IndexOf("our BG22_HERO_305t 1/1 attacks") > result.IndexOf(Whelp), string.Join('\n', result.Log));
        Assert.Empty(CombatSimulator.Explain(ours, isOpponent: false));
    }

    [Fact]
    public void A_captured_avenge_number_replaces_the_printed_four()
    {
        // SCORE_VALUE_1 = 2 (the live game's X), no progress: the second death summons the Whelp.
        var ours = WithCard(Side(OneOnes(3)), Power, Tags(2, progress: 0, threshold: 2));
        var result = Fight(ours, Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(Broodmother)));

        Assert.Equal(2, result.DeathsBefore(Whelp + " 2/2"));
    }

    [Fact]
    public void The_opponents_broodmother_counts_from_zero_with_a_one_one_whelp_and_is_missing_input()
    {
        // Their hero power is not captured: four of their deaths in the fight, then a printed 1/1 Whelp.
        var theirs = WithCard(Side(OneOnes(5)), Power);
        var result = Fight(Side(Minion(1, 100, cardId: "WALL")), theirs, oursFirst: false, Effects(typeof(Broodmother)));

        Assert.Equal(4, result.DeathsBefore("summoned their BG22_HERO_305t 1/1"));
        var item = Assert.Single(CombatSimulator.Explain(theirs, isOpponent: true));
        Assert.Equal((Power, UnsimulatedReason.MissingInput), (item.CardId, item.Reason));
    }

    [Fact]
    public void Whelp_numbers_that_disagree_are_not_trusted()
    {
        // NUM_1 = 4 alone may be the Avenge (4) itself (as on the Avenge minion in the logs), not a 4/4 Whelp: the Whelp is
        // the printed 1/1 and the fight is missing_input.
        var ours = WithCard(Side(OneOnes(5)), Power, Numbers(4));
        var result = Fight(ours, Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(Broodmother)));

        Assert.Single(result.Lines(Whelp + " 1/1"));
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(ours, isOpponent: false)).Reason);
    }
}

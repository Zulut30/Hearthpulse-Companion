using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Rapid Reanimation (BG25_HERO_103p): "Choose a friendly minion. Start of Combat: Destroy it. When you have space,
/// resummon an exact copy." The destroy happened before the capture; the capture remembers the destroyed minion by the hero
/// power's id. Expected results are worked out by hand in each comment.
/// </summary>
public sealed class RapidReanimationTests
{
    private const string Power = "BG25_HERO_103p";

    private static SimSide Teron(SimSide side, params SimMinion[] removed) => side with
    {
        Extras = [Power],
        RemovedByExtras = new Dictionary<string, IReadOnlyList<SimMinion>> { [Power] = removed },
    };

    private static SimMinion[] FullBoard(SimMinion? last = null) =>
        Enumerable.Range(0, 6).Select(i => Minion(1, 1, cardId: $"M{i}")).Append(last ?? Minion(1, 1, cardId: "M6")).ToArray();

    [Fact]
    public void On_a_full_board_the_exact_copy_comes_after_the_first_death_at_the_right_end()
    {
        // Seven 1/1s (the deathrattle summons refilled the board at Start of Combat), the destroyed minion was a buffed
        // 9/8 Taunt. M0 attacks their 1/100 and dies: room at last, the 9/8 Taunt copy appears at slot 6, once.
        var ours = Teron(Side(FullBoard()), Minion(2, 2, Keyword.Taunt, "COPY") with { Attack = 9, Health = 8, MaxHealth = 8 });
        var result = Fight(ours, Side(Minion(1, 100, cardId: "WALL")), oursFirst: true, Effects(typeof(RapidReanimation)));

        Assert.Equal("summoned our COPY 9/8 Taunt at 6", Assert.Single(result.Lines("summoned our ")));
        Assert.True(result.IndexOf("our M0 1/0 dies") < result.IndexOf("summoned our COPY"), string.Join('\n', result.Log));
        Assert.Contains("our Rapid Reanimation resummons COPY", result.Log);
    }

    [Fact]
    public void Nothing_is_owed_when_the_copy_is_already_on_the_board_or_there_was_space()
    {
        // With space at Start of Combat the copy came back before the capture (CREATOR = the hero power).
        var withCopy = Teron(Side(FullBoard(Minion(9, 8, cardId: "COPY") with { CreatorCardId = Power })), Minion(9, 8, cardId: "COPY"));
        var notFull = Teron(Side(Minion(1, 1, cardId: "M0"), Minion(9, 8, cardId: "COPY")), Minion(9, 8, cardId: "COPY"));

        Assert.Empty(Fight(withCopy, Side(Minion(1, 100)), oursFirst: true, Effects(typeof(RapidReanimation))).Lines("summoned our "));
        Assert.Empty(Fight(notFull, Side(Minion(1, 100)), oursFirst: true, Effects(typeof(RapidReanimation))).Lines("summoned our "));
    }

    [Fact]
    public void An_unused_power_summons_nothing()
    {
        // No minion was chosen: nothing was removed, a full board stays as it is.
        var ours = Side(FullBoard()) with { Extras = [Power] };

        Assert.Empty(Fight(ours, Side(Minion(1, 100)), oursFirst: true, Effects(typeof(RapidReanimation))).Lines("summoned our "));
    }

    [Fact]
    public void It_is_simulated_on_both_sides()
    {
        // The destroyed minion stood on the board the player saw: nothing hidden, no "not simulated".
        var ours = Teron(Side(FullBoard()), Minion(9, 8, cardId: "COPY"));

        Assert.Empty(CombatSimulator.Explain(ours, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(ours, isOpponent: true));
        var result = Fight(Side(Minion(1, 100, cardId: "WALL")), ours, oursFirst: false, Effects(typeof(RapidReanimation)));
        Assert.Equal("summoned their COPY 9/8 at 6", Assert.Single(result.Lines("summoned their ")));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Soul Fermenter (BG35_MagicItem_732): "Start of Combat: Destroy your 3 left-most minions. After your last minion dies,
/// resummon them." The destroy happened before the capture; the capture remembers the three by the trinket's id.
/// </summary>
public sealed class SoulFermenterTests
{
    private const string Trinket = "BG35_MagicItem_732";

    private static readonly SimMinion[] Destroyed =
    [
        Minion(2, 3, cardId: "A"),
        Minion(4, 4, Keyword.Taunt, "B"),
        Minion(1, 1, cardId: "C"),
    ];

    private static SimSide Fermenter(params SimMinion[] board) => Side(board) with
    {
        Extras = [Trinket],
        RemovedByExtras = new Dictionary<string, IReadOnlyList<SimMinion>> { [Trinket] = Destroyed },
    };

    [Fact]
    public void After_the_last_minion_dies_the_three_come_back_left_to_right_once()
    {
        // Our only minion, a 1/1, attacks their 1/100 and dies: A, B, C return at slots 0, 1, 2 with the stats and
        // keywords they had. They then die to the 1/100 too, and nothing comes back a second time.
        var result = Fight(Fermenter(Minion(1, 1, cardId: "LAST")), Side(Minion(1, 100, cardId: "WALL")), oursFirst: true, Effects(typeof(SoulFermenter)));

        Assert.Equal(["summoned our A 2/3 at 0", "summoned our B 4/4 Taunt at 1", "summoned our C 1/1 at 2"], result.Lines("summoned our "));
        Assert.True(result.IndexOf("our LAST 1/0 dies") < result.IndexOf("summoned our A"), string.Join('\n', result.Log));
        Assert.Equal(-1, result.Outcome);
    }

    [Fact]
    public void Nothing_comes_back_while_a_friendly_minion_lives()
    {
        // The 1/1 dies attacking their 1/100, the 0/1000 outlives the 400-attack limit: the board is never empty, so the three
        // stay away.
        var result = Fight(Fermenter(Minion(1, 1, cardId: "X"), Minion(0, 1000, cardId: "Y")), Side(Minion(1, 100, cardId: "WALL")), oursFirst: true,
            Effects(typeof(SoulFermenter)));

        Assert.Empty(result.Lines("summoned our "));
    }

    [Fact]
    public void Nothing_is_owed_when_they_came_back_before_the_capture()
    {
        // The board emptied at Start of Combat already: the three are on the captured board, created by the trinket.
        var back = Destroyed.Select(m => m with { CreatorCardId = Trinket }).ToArray();
        var result = Fight(Fermenter(back), Side(Minion(10, 100, cardId: "WALL")), oursFirst: true, Effects(typeof(SoulFermenter)));

        Assert.Empty(result.Lines("summoned our "));
        Assert.Empty(CombatSimulator.Explain(Fermenter(back), isOpponent: true));
    }
}

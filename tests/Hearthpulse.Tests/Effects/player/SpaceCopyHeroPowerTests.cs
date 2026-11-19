using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Stormpike Strength (BG22_HERO_003p): "When you have space in combat, summon a copy of your highest-Health minion.
/// (Unlocks on Turn 7.)" The hero power's TAG_SCRIPT_DATA_NUM_1 is 1 from the start of the game while the power is still
/// locked (real log 2026-11-15, game 2), so it says "owed" only from turn 7 on. simreplay of that log: on turns 3-5
/// (fights #2/3-#2/5) the simulator copied one of two 3/3 Gem Rats after the first death, the log never did.
/// </summary>
public sealed class SpaceCopyHeroPowerTests
{
    private const string Stormpike = "BG22_HERO_003p";

    /// <summary>A full board as on turn 3 of that game: two 3/3 Gem Rats with the most Health, five 1/1s.</summary>
    private static SimSide FullBoard(int turn) => WithCard(
        Side([Minion(3, 3, cardId: "BG32_330", tribes: Tribe.Quilboar), Minion(3, 3, cardId: "BG32_330", tribes: Tribe.Quilboar),
              .. Enumerable.Range(0, 5).Select(i => Minion(1, 1, cardId: $"M{i}"))]),
        Stormpike, Numbers(1)) with { Turn = turn };

    [Fact]
    public void A_locked_power_copies_nothing_though_its_flag_says_owed()
    {
        // Turn 3, flag 1 (as the log has it since the game started): the first death leaves room, no copy comes.
        var result = Fight(FullBoard(turn: 3), Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(SpaceCopyHeroPower)));

        Assert.Empty(result.Lines("summoned our "));
    }

    [Fact]
    public void An_unlocked_power_owes_its_copy_while_the_flag_is_set()
    {
        // Turn 7, a full board and the flag still 1 (no room at Start of Combat): one copy of a 3/3 Gem Rat after the first
        // death, right of the copied one.
        var result = Fight(FullBoard(turn: 7), Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(SpaceCopyHeroPower)));

        Assert.StartsWith("summoned our BG32_330 3/3", Assert.Single(result.Lines("summoned our ")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cleared_flag_means_the_copy_already_came()
    {
        // Turn 7, flag 0: the copy came at Start of Combat (it then stood on the board), nothing more this fight.
        var side = FullBoard(turn: 7);
        side = side with { ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>> { [Stormpike] = Numbers(0) } };
        var result = Fight(side, Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(SpaceCopyHeroPower)));

        Assert.Empty(result.Lines("summoned our "));
    }
}

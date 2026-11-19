using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// All-Purpose Kibble (BG32_MagicItem_200): "Whenever a friendly Beast attacks, give it +4 Attack and permanently improve
/// this." The number grows at once (assumed +1 per trigger: the text does not say).
/// </summary>
public sealed class AllPurposeKibbleTests
{
    private const string Kibble = "BG32_MagicItem_200";

    [Fact]
    public void Each_beast_attack_gets_the_current_number_and_improves_it()
    {
        // Their 0/30 never attacks. Our 1/10 Beast: +4 (5 damage), +5 (10), +6 (16): the third attack kills, 1+4+5+6 = 16.
        var ours = WithCard(Side(Minion(1, 10, cardId: "B", tribes: Tribe.Beast)), Kibble, Numbers(4));
        var result = Fight(ours, Side(Minion(0, 30, cardId: "WALL")), oursFirst: true, Effects(typeof(AllPurposeKibble)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(16, Assert.Single(result.Ours).Attack);
    }

    [Fact]
    public void The_captured_number_is_the_starting_point()
    {
        // The Kibble shows +10 now: +10 (11 damage), +11 (22): dead after two attacks, 1+10+11 = 22.
        var ours = WithCard(Side(Minion(1, 10, cardId: "B", tribes: Tribe.Beast)), Kibble, Numbers(10));
        var result = Fight(ours, Side(Minion(0, 30, cardId: "WALL")), oursFirst: true, Effects(typeof(AllPurposeKibble)));

        Assert.Equal(22, Assert.Single(result.Ours).Attack);
    }

    [Fact]
    public void Other_tribes_get_nothing()
    {
        var ours = WithCard(Side(Minion(1, 10, cardId: "M")), Kibble, Numbers(4));
        var result = Fight(ours, Side(Minion(0, 3, cardId: "WALL")), oursFirst: true, Effects(typeof(AllPurposeKibble)));

        Assert.Equal(1, Assert.Single(result.Ours).Attack);
    }

    [Fact]
    public void Without_its_number_it_plays_the_printed_four_and_is_missing_input()
    {
        // Not captured (the opponent's, or ours without the tag): +4, +5, +6 as printed, flagged.
        var ours = WithCard(Side(Minion(1, 10, cardId: "B", tribes: Tribe.Beast)), Kibble);
        var result = Fight(ours, Side(Minion(0, 30, cardId: "WALL")), oursFirst: true, Effects(typeof(AllPurposeKibble)));

        Assert.Equal(16, Assert.Single(result.Ours).Attack);
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(ours, isOpponent: true)).Reason);
        Assert.Empty(CombatSimulator.Explain(WithCard(Side(), Kibble, Numbers(4)), isOpponent: false));
    }
}

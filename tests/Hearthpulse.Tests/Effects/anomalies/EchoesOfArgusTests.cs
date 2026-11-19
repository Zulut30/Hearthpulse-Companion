using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Anomalies;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim.Anomalies;

/// <summary>
/// Echoes of Argus, an anomaly (BG27_Anomaly_802): "Your Battlecries and Deathrattles trigger an extra time." The
/// anomaly is lobby-wide, so each side gets it as an extra; Battlecries do not trigger in combat. Boards worked out by
/// hand in each comment. How the log tracker hands the anomaly to both sides: <c>BattlegroundsAnomalyCaptureTests</c>
/// (kept outside Effects/ so this file needs only the simulator and travels to BGSim).
/// </summary>
public sealed class EchoesOfArgusTests
{
    private const string Echoes = "BG27_Anomaly_802";

    private static SimMinion Bonehead => Minion(1, 1, cardId: Ids.Bonehead, tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    [Fact]
    public void Both_sides_deathrattles_trigger_an_extra_time()
    {
        // Our Bonehead 1/1 attacks theirs: both die (ours first, the attacking side), each deathrattle twice, four 1/1
        // Skeletons a side. Then every attack trades one Skeleton for one: four trades, both boards empty, a tie.
        var result = Fight(Side(Bonehead) with { Extras = [Echoes] }, Side(Bonehead) with { Extras = [Echoes] }, oursFirst: true,
                           Only(typeof(HarmlessBonehead), typeof(EchoesOfArgus)));

        Assert.Equal(0, result.Outcome);
        Assert.Equal(2, result.Lines("deathrattle of our BG28_300").Count);
        Assert.Equal(2, result.Lines("deathrattle of their BG28_300").Count);
        Assert.Equal(4, result.Lines("summoned our BG_ICC_026t").Count);
        Assert.Equal(4, result.Lines("summoned their BG_ICC_026t").Count);
        Assert.True(result.IndexOf("deathrattle of our BG28_300") < result.IndexOf("deathrattle of their BG28_300"));
    }

    [Fact]
    public void Without_the_anomaly_each_deathrattle_triggers_once()
    {
        // Control for the test above: two Skeletons a side.
        var result = Fight(Side(Bonehead), Side(Bonehead), oursFirst: true, Only(typeof(HarmlessBonehead), typeof(EchoesOfArgus)));

        Assert.Equal(2, result.Lines("summoned our BG_ICC_026t").Count);
        Assert.Equal(2, result.Lines("summoned their BG_ICC_026t").Count);
    }

    [Fact]
    public void The_anomaly_no_longer_makes_a_fight_not_simulated()
    {
        var side = Side(Minion(1, 1)) with { Extras = [Echoes] };

        Assert.Empty(CombatSimulator.Explain(side, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(side, isOpponent: true));
        Assert.Equal(Relevance.Relevant, CombatRelevance.Find(Echoes)?.Relevance);
    }
}

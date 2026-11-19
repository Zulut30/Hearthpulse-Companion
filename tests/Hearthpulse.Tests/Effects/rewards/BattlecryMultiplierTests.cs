using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Anomalies;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Rewards;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Rewards;

/// <summary>
/// "Your Battlecries trigger an extra time" in combat: Gilnean War Horn (quest reward BG27_Reward_802) and Echoes of Argus
/// (anomaly BG27_Anomaly_802). A Battlecry triggers in combat only through another effect: here Timewarped
/// Greenskeeper's Rally ("Trigger your right-most Battlecry and Deathrattle"). Boards worked out by hand in each comment:
/// our Greenskeeper 1/1 attacks their E 0/1 first and kills it, so the fight is that one attack.
/// </summary>
public sealed class BattlecryMultiplierTests
{
    private const string Greenskeeper = "BG34_Giant_041";

    private static SimMinion Keeper => Minion(1, 1, cardId: Greenskeeper);

    private static SimMinion Shouter => Minion(0, 5, cardId: "BC", mechanics: CardMechanic.Battlecry);

    private static SimSide Enemy => Side(Minion(0, 1, cardId: "E"));

    [Theory]
    [InlineData("", "BC 1/5")]
    [InlineData("BG27_Reward_802", "BC 2/5")]
    [InlineData("BG27_Anomaly_802", "BC 2/5")]
    public void An_extra_time_card_repeats_a_battlecry_triggered_in_combat(string extra, string shouter)
    {
        // The Rally triggers BC's Battlecry (+1 Attack per trigger): once, or twice with the reward or the anomaly.
        var ours = Side(Keeper, Shouter) with { Extras = extra.Length > 0 ? new[] { extra } : Array.Empty<string>() };
        var result = Fight(ours, Enemy, oursFirst: true,
                           Only(typeof(TimewarpedGreenskeeper), typeof(Shout), typeof(GilneanWarHorn), typeof(EchoesOfArgus)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["BG34_Giant_041 1/1", shouter], Board(result.Ours));
    }

    [Fact]
    public void The_opponents_war_horn_does_not_repeat_our_battlecries()
    {
        var result = Fight(Side(Keeper, Shouter), Enemy with { Extras = ["BG27_Reward_802"] }, oursFirst: true,
                           Only(typeof(TimewarpedGreenskeeper), typeof(Shout), typeof(GilneanWarHorn)));

        Assert.Equal(["BG34_Giant_041 1/1", "BC 1/5"], Board(result.Ours));
    }

    [Fact]
    public void Nest_swarmer_battlecry_works_when_triggered_in_combat()
    {
        // The Rally triggers the Swarmer's Battlecry: a 4/4 Beetle to its right. Then its Deathrattle (it is the right-most
        // minion with one, alive): the Beetle grows to 6/6 and a second one arrives right of the Swarmer as 6/6.
        var result = Fight(Side(Keeper, Minion(7, 7, cardId: "BG34_Giant_687", tribes: Tribe.Beast)), Enemy, oursFirst: true,
                           Only(typeof(TimewarpedGreenskeeper), typeof(TimewarpedNestSwarmer)));

        Assert.Equal(["BG34_Giant_041 1/1", "BG34_Giant_687 7/7", "BG28_603t 6/6", "BG28_603t 6/6"], Board(result.Ours));
        Assert.Single(result.Lines("battlecry of our BG34_Giant_687"));
    }

    /// <summary>A test Battlecry: "+1 Attack".</summary>
    [CardEffect("BC")]
    private sealed class Shout : CombatEffect
    {
        public override void Battlecry(Fight fight, Unit self) => fight.Buff(self, 1, 0);
    }
    [Theory]
    [InlineData(false, 1, "BG36_241 18/13 DivineShield, Windfury")]
    [InlineData(true, 2, "BG36_241 27/19 DivineShield, Windfury")]
    public void Temporal_tampering_casts_a_tavern_spell_an_extra_time(bool tampering, int casts, string vindicator)
    {
        // Crimson Vindicator 9/7 (Dragon, Divine Shield, Windfury) attacks E 0/1: its Rally casts Mighty Dragonbreath,
        // +3/+2 three times over for a Divine Shield Dragon (+9/+6); with the reward it is cast twice (+18/+12). E dies
        // to the first swing, so there is no second.
        var ours = Side(Minion(9, 7, Keyword.DivineShield | Keyword.Windfury, "BG36_241", Tribe.Dragon, mechanics: CardMechanic.Rally))
            with { Extras = tampering ? new[] { "BG28_Reward_501" } : Array.Empty<string>() };
        var result = Fight(ours, Enemy, oursFirst: true, Only(typeof(CrimsonVindicator), typeof(TemporalTampering)));

        Assert.Equal(casts, result.Lines("we cast BG36_246").Count);
        Assert.Equal([vindicator], Board(result.Ours));
    }
}

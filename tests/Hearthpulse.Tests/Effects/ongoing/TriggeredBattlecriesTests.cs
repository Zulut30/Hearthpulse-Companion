using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Battlecries of pool minions that act in combat only when an effect triggers them. Timewarped Greenskeeper 5/9 (Rally:
/// trigger your right-most Battlecry and Deathrattle) attacks first; their 0-Attack wall never attacks. Results are
/// worked out by hand in each comment.
/// </summary>
public sealed class TriggeredBattlecriesTests
{
    private const string BattlecryMinion = "BATTLECRY";

    private static SimMinion Keeper() => Minion(5, 9, cardId: "BG34_Giant_041", tribes: Tribe.Dragon);

    private static SimMinion With(string cardId, int attack, int health, Tribe tribes = Tribe.None, bool golden = false,
                                  CardMechanic mechanics = CardMechanic.Battlecry) =>
        Minion(attack, health, cardId: cardId, tribes: tribes, golden: golden, mechanics: mechanics);

    private static EffectRegistry Registry(Type effect, params Type[] more) =>
        Effects([typeof(TimewarpedGreenskeeper), effect, .. more]);

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    private static SimSide DeitySide(params SimMinion[] minions) => new(minions)
    {
        Extras = [Tokens.YShaarjId],
        Tags = new Dictionary<string, int> { [Deity.AttackTag] = 1, [Deity.HealthTag] = 1 },
    };

    [Fact]
    public void Joyous_gives_the_deity_its_numbers()
    {
        // +2/+1 from the card text; a Joyous carrying script data 3/2 gives +3/+2.
        var plain = Fight(DeitySide(Keeper(), With("BG36_110", 2, 3, Tribe.Aberration)), Side(Minion(0, 5, cardId: "W")), true,
                          Registry(typeof(Joyous), typeof(YShaarj)));
        Assert.Contains("our Deity gets +2/+1 (3/2)", plain.Log);

        var scripted = With("BG36_110", 2, 3, Tribe.Aberration) with { ScriptData1 = 3, ScriptData2 = 2 };
        var result = Fight(DeitySide(Keeper(), scripted), Side(Minion(0, 5, cardId: "W")), true, Registry(typeof(Joyous), typeof(YShaarj)));
        Assert.Contains("our Deity gets +3/+2 (4/3)", result.Log);
    }

    [Fact]
    public void Hackerfin_buffs_the_other_minions_by_its_shown_value()
    {
        // +3/+2 to the others: Keeper 8/11, A 4/3; Hackerfin stays 5/3. The Keeper's 8 kills the 0/8.
        var ours = Side(Keeper(), Minion(1, 1, cardId: "A"), With("BG31_148", 5, 3, Tribe.Murloc));
        var result = Fight(ours, Side(Minion(0, 8, cardId: "W")), true, Registry(typeof(Hackerfin)));
        Assert.Equal(1, result.Outcome);
        Assert.Equal([(8, 11), (4, 3), (5, 3)], Stats(result.Ours));

        // Improved to +5/+4 (its script data): Keeper 10/13.
        var improved = Side(Keeper(), With("BG31_148", 5, 3, Tribe.Murloc) with { ScriptData1 = 5, ScriptData2 = 4 });
        Assert.Equal((10, 13), Stats(Fight(improved, Side(Minion(0, 10, cardId: "W")), true, Registry(typeof(Hackerfin))).Ours)[0]);
    }

    [Fact]
    public void Nerubian_deathswarmer_gives_every_undead_attack_itself_included()
    {
        // +1 Attack to the Undead: U 2/1 and the Deathswarmer 2/4; the Keeper (a Dragon) is not Undead.
        var ours = Side(Keeper(), Minion(1, 1, cardId: "U", tribes: Tribe.Undead), With("BG25_011", 1, 4, Tribe.Undead));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), true, Registry(typeof(NerubianDeathswarmer)));

        Assert.Equal([(5, 9), (2, 1), (2, 4)], Stats(result.Ours));
    }

    [Fact]
    public void Electric_synthesizer_buffs_the_other_dragons()
    {
        // +1/+1 to the other Dragons: Keeper 6/10, D 2/2; the Synthesizer stays 3/4. Golden: +2/+2.
        var ours = Side(Keeper(), Minion(1, 1, cardId: "D", tribes: Tribe.Dragon), With("BG26_963", 3, 4, Tribe.Dragon));
        Assert.Equal([(6, 10), (2, 2), (3, 4)], Stats(Fight(ours, Side(Minion(0, 6, cardId: "W")), true, Registry(typeof(ElectricSynthesizer))).Ours));

        var golden = Side(Keeper(), Minion(1, 1, cardId: "D", tribes: Tribe.Dragon), With("BG26_963", 6, 8, Tribe.Dragon, golden: true));
        Assert.Equal([(7, 11), (3, 3), (6, 8)], Stats(Fight(golden, Side(Minion(0, 7, cardId: "W")), true, Registry(typeof(ElectricSynthesizer))).Ours));
    }

    [Fact]
    public void Forest_rover_battlecry_improves_beetles_and_its_deathrattle_summons_one()
    {
        // Battlecry: Beetles +2/+1 (the 2/2 Beetle → 4/3, later Beetles too). Deathrattle (right-most, triggered while it
        // lives): a 2/2 Beetle +2/+1 = 4/3 to its right.
        var ours = Side(Keeper(), Tokens.Make(Tokens.BeetleId, 2, 2, Tribe.Beast),
                        With("BG31_801", 1, 1, Tribe.Beast, mechanics: CardMechanic.Battlecry | CardMechanic.Deathrattle));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), true, Registry(typeof(ForestRover)));

        Assert.Equal([(5, 9), (4, 3), (1, 1), (4, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Cthrax_wrecker_battlecry_and_deathrattle_both_feed_the_deity()
    {
        var ours = DeitySide(Keeper(), With("BGDUO_701", 4, 4, mechanics: CardMechanic.Battlecry | CardMechanic.Deathrattle | CardMechanic.Rally));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), true, Registry(typeof(CThraxWrecker), typeof(YShaarj)));

        Assert.Equal(["our Deity gets +4/+4 (5/5)", "our Deity gets +4/+4 (9/9)"], result.Lines("our Deity gets"));
    }

    [Fact]
    public void A_battlecry_the_simulator_has_no_effect_for_still_counts()
    {
        // A plain Battlecry card (it gets a card in the Tavern): triggered, no stats change.
        var ours = Side(Keeper(), With(BattlecryMinion, 1, 1));
        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), true, Registry(typeof(Joyous)));

        Assert.Single(result.Lines("battlecry of our BATTLECRY"));
        Assert.Equal([(5, 9), (1, 1)], Stats(result.Ours));
    }

    [Fact]
    public void The_battlecries_are_registered()
    {
        foreach (var id in new[] { "BG36_110", "BG31_148", "BG25_011", "BG26_963" })
        {
            Assert.True(EffectRegistry.Default.Implements(id, golden: false, isOpponent: true));
            Assert.True(EffectRegistry.Default.Implements(id, golden: true));
        }
    }
}

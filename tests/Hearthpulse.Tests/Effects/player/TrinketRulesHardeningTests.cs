using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Trinket rules a Stryker run on Effects/* (2026-11-19, stryker-sim.md in the task folder) changed without a test failing:
/// the edge of each card's text that the existing tests in <see cref="PlayerEffectsTests"/> do not reach.
/// </summary>
public sealed class TrinketRulesHardeningTests
{
    private static SimSide With(SimSide side, params string[] extras) => side with { Extras = extras };

    [Fact]
    public void Deathly_phylacterys_extra_trigger_is_not_multiplied_by_titus()
    {
        // Titus makes Bonehead's deathrattle trigger twice; the Phylactery adds one more trigger, once: three in all.
        var bonehead = Minion(1, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);
        var titus = Minion(0, 50, cardId: "BG25_354", tribes: Tribe.Undead);
        var result = Fight(With(Side(bonehead, titus), "BG30_MagicItem_700"), Side(Minion(1, 50, cardId: "E")), false,
            Effects(typeof(DeathlyPhylactery), typeof(TitusRivendare), typeof(HarmlessBonehead)));

        Assert.Equal(3, result.Lines("deathrattle of our BG28_300").Count);
    }

    [Fact]
    public void Unholy_sanctum_buffs_a_lone_minion_in_the_left_most_slot()
    {
        // Cord Puller trades with their 1/1; its Microbot is our only minion, so it is the right-most one: 1/1 + 2/2.
        var cordPuller = Minion(1, 1, cardId: "BG29_611", mechanics: CardMechanic.Deathrattle);
        var result = Fight(With(Side(cordPuller), "BG32_MagicItem_862"), Side(Minion(1, 1, cardId: "E")), false,
            Effects(typeof(UnholySanctum), typeof(CordPuller)));

        Assert.Equal(1, result.Outcome);
        var microbot = Assert.Single(result.Ours);
        Assert.Equal((3, 3), (microbot.Attack, microbot.Health));
    }

    [Fact]
    public void Gilnean_thorned_rose_itself_deals_the_one_damage()
    {
        // As in PlayerEffectsTests, A, B and C die and the 0/10 D gets +4/+5. The damage that follows has no source minion
        // ("takes 1" with no "from"); a hit by their 1/50 would read "takes 1 from their …" and is not the Rose's.
        var result = Fight(With(Side(Minion(1, 1, cardId: "A"), Minion(1, 1, cardId: "B"), Minion(1, 1, cardId: "C"), Minion(0, 10, cardId: "D")), "BG30_MagicItem_864"),
            Side(Minion(1, 50)), false, Effects(typeof(GilneanThornedRose)));

        Assert.Single(result.Log, l => l.StartsWith("our D ", StringComparison.Ordinal) && l.EndsWith(" takes 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Mechagon_adapter_does_not_shield_a_minion_that_is_not_a_mech()
    {
        var result = Fight(With(Side(Minion(0, 1, Keyword.DivineShield, cardId: "B", tribes: Tribe.Beast)), "BG30_MagicItem_910"),
            Side(Minion(1, 10, cardId: "E")), false, Effects(typeof(MechagonAdapter)));

        Assert.Single(result.Log, l => l.Contains("loses Divine Shield", StringComparison.Ordinal));
    }
}

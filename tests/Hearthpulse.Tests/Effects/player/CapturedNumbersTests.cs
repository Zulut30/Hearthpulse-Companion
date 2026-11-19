using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Numbers a hero power or trinket shows are read from its captured tags (balance changes on the server without a new
/// client): TAG_SCRIPT_DATA_NUM_1/2 hold the number the text shows, not a bonus on top of it (real logs: Dragon Skull 5/5
/// for "+5/+5", Nomi Sticker 3/2 for "+3/+2"); Avenge X and progress are SCORE_VALUE_1/2. The text's number (HearthstoneJSON
/// latest) is the fallback.
/// </summary>
public sealed class CapturedNumbersTests
{
    [Fact]
    public void Ceremonial_sword_gives_the_captured_attack()
    {
        // "+4 Attack" shows +6 now: our 1/10 hits their 0/10 for 7, then for 13: 1+6+6 = 13.
        var ours = WithCard(Side(Minion(1, 10)), "BG30_MagicItem_925", Numbers(6));
        var result = Fight(ours, Side(Minion(0, 10)), oursFirst: true, Effects(typeof(CeremonialSword)));

        Assert.Equal(13, Assert.Single(result.Ours).Attack);
    }

    [Fact]
    public void Faerie_dragon_scale_has_the_captured_uses_left()
    {
        // NUM_1 counts the uses left (3, 2, 1 in a real fight). Our 1/20 Dragon attacks their 1/5 three times (their own
        // attacks cost them 1 too): three shields with the printed 3, one with NUM_1 = 1.
        var dragon = Side(Minion(1, 20, tribes: Tribe.Dragon));
        var full = Fight(WithCard(dragon, "BG32_MagicItem_363"), Side(Minion(1, 5)), oursFirst: true, Effects(typeof(FaerieDragonScale)));
        var one = Fight(WithCard(dragon, "BG32_MagicItem_363", Numbers(1)), Side(Minion(1, 5)), oursFirst: true, Effects(typeof(FaerieDragonScale)));

        Assert.True(full.Log.Count(l => l.Contains("loses Divine Shield", StringComparison.Ordinal)) == 3, string.Join("\n", full.Log));
        Assert.Equal(1, one.Log.Count(l => l.Contains("loses Divine Shield", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_captured_number_equal_to_the_printed_one_is_not_counted_twice()
    {
        // Fang Anklet shows +1/+1 (NUM 1/1): Buzzing Vermin's 2/2 Beetle comes out 3/3, not 4/4 (the effects audit, S5).
        var ours = WithCard(Side(Minion(1, 1, Keyword.Taunt, "BG31_803", Tribe.Beast)), "BG35_MagicItem_701", Numbers(1, 1));
        var result = Fight(ours, Side(Minion(5, 50, cardId: "WALL")), oursFirst: false);

        Assert.StartsWith("summoned our BG28_603t 3/3", Assert.Single(result.Lines("summoned our BG28_603t")), StringComparison.Ordinal);
    }

    [Fact]
    public void Golden_beetle_band_avenges_eight_from_the_captured_progress_with_the_players_beetle()
    {
        // Avenge (8) (latest text; it was 7), six deaths counted before the capture. Their 1/100 kills A: seven, nothing.
        // B attacks and dies: eight, two Beetles of the player's current size, 5/4 as our Beetle summoner shows it.
        var vermin = Minion(0, 30, cardId: "BG31_803", tribes: Tribe.Beast) with { ScriptData1 = 5, ScriptData2 = 4 };
        var ours = WithCard(Side(Minion(1, 1, cardId: "A"), Minion(1, 1, cardId: "B"), vermin), "BG32_MagicItem_860t",
            new Dictionary<string, int> { ["SCORE_VALUE_2"] = 6 });
        var result = Fight(ours, Side(Minion(1, 100, cardId: "WALL")), oursFirst: false, Effects(typeof(BeetleBand)));

        Assert.Equal(2, result.Lines("summoned our BG28_603t 5/4").Count);
        Assert.Equal(2, result.DeathsBefore("summoned our BG28_603t"));
    }

    [Fact]
    public void A_captured_avenge_number_replaces_the_printed_one()
    {
        // Bird Feeder "Avenge (2)" captured as SCORE_VALUE_1 = 1: the first death already gives our 0/10 +1/+1.
        var ours = WithCard(Side(Minion(1, 1, cardId: "A"), Minion(0, 10, cardId: "B")), "BG32_MagicItem_864",
            new Dictionary<string, int> { ["SCORE_VALUE_1"] = 1 });
        var result = Fight(ours, Side(Minion(1, 100)), oursFirst: false, Effects(typeof(BirdFeeder)));

        Assert.True(result.IndexOf("our B 1/11 attacks") >= 0, string.Join('\n', result.Log));
    }

    [Fact]
    public void Reinforced_shield_starts_from_the_captured_uses_left()
    {
        // NUM_1 = 1 use left at the capture: of the two reborn copies only R1's gets Divine Shield; R2's dies without one.
        var ours = WithCard(Side(Minion(1, 1, Keyword.Reborn, "R1"), Minion(1, 1, Keyword.Reborn, "R2")), "BG30_MagicItem_886", Numbers(1));
        var result = Fight(ours, Side(Minion(1, 100)), oursFirst: false, Effects(typeof(ReinforcedShield)));

        Assert.True(result.IndexOf("our R1 1/1 DivineShield attacks") >= 0, string.Join('\n', result.Log));
        Assert.True(result.IndexOf("attacks our R2 1/1") >= 0, string.Join('\n', result.Log));
        Assert.DoesNotContain(result.Log, l => l.Contains("our R2 1/1 DivineShield", StringComparison.Ordinal));
    }

    [Fact]
    public void The_opponents_avenge_starts_from_the_start_of_combat_deaths_the_player_saw()
    {
        // Their Bird Feeder (Avenge (2)) is not captured: one of their minions died at Start of Combat, so their first death
        // in the fight already gives their 0/10 +1/+1.
        var theirs = WithCard(Side(Minion(1, 1, cardId: "A"), Minion(0, 10, cardId: "B")), "BG32_MagicItem_864")
            with { DeathsBeforeCapture = [Minion(1, 1, cardId: "GONE")] };
        var result = Fight(Side(Minion(1, 100)), theirs, oursFirst: true, Effects(typeof(BirdFeeder)));

        Assert.True(result.IndexOf("their B 1/11 attacks") >= 0, string.Join('\n', result.Log));
    }

    [Fact]
    public void The_opponents_growing_number_is_estimated_not_missing()
    {
        // Their Kibble's number is hidden from the player: with the tracker's estimate (the printed card) the fight is simulated.
        var theirs = WithCard(Side(Minion(1, 10, tribes: Tribe.Beast)), "BG32_MagicItem_200")
            with { Estimates = [new SimEstimate(SimInputs.TrinketValue, 0, 0, "BG32_MagicItem_200: its number is hidden from the player: the printed card")] };

        Assert.Empty(CombatSimulator.Explain(theirs, isOpponent: true));
        Assert.Single(CombatSimulator.Explain(theirs with { Estimates = [] }, isOpponent: true));
    }

    [Fact]
    public void Sprout_it_out_reads_its_captured_stats()
    {
        // "+1/+2 and Taunt" shows +2/+3 now: the reborn 1/1 comes back 3/4 Taunt.
        var ours = WithCard(Side(Minion(1, 1, Keyword.Reborn, "R")), "TB_BaconShop_HP_107", Numbers(2, 3));
        var result = Fight(ours, Side(Minion(1, 10)), oursFirst: false, Effects(typeof(SproutItOut)));

        Assert.True(result.IndexOf("our R 3/4 Taunt attacks") >= 0, string.Join('\n', result.Log));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>
/// Timewarped minions out of today's pool that return with the rotation (TASK-2026-11-19 E6a), normal and golden. Boards
/// worked out by hand in each comment; the usual setup is their Killer trading with our minion, then only their 0-Attack
/// Wall is left.
/// </summary>
public sealed class TimewarpedRotationTests
{
    [Theory]
    [InlineData(false, "BG34_Giant_027 16/7 Taunt")]
    [InlineData(true, "BG34_Giant_027(G) 32/15 Taunt")]
    public void Arm_gives_an_attacked_friendly_minion_attack_before_the_damage(bool golden, string arm)
    {
        // Their E 1/1 attacks our Taunt Arm: +8 (golden +16) Attack first, then it kills E and takes 1.
        var stats = golden ? 16 : 8;
        var result = Fight(Side(Minion(stats, stats, Keyword.Taunt, "BG34_Giant_027", golden: golden), Minion(1, 10, cardId: "X")),
                           Side(Minion(1, 1, cardId: "E")), oursFirst: false, Only(typeof(TimewarpedArm)));

        Assert.Equal([arm, "X 1/10"], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, "Q1 4/12", "Q2 4/12")]
    [InlineData(true, "Q1 7/14", "Q2 7/14")]
    public void Bristler_gives_its_blood_gems_to_two_different_quilboar(bool golden, string first, string second)
    {
        // Our Bristler has gained +3/+2 from Blood Gems. The Killer kills it (pick 0): Q1 and Q2 get +3/+2 (golden: twice), N
        // is no Quilboar.
        var stats = golden ? 12 : 6;
        var bristler = Minion(stats, stats, cardId: "BG34_Giant_104", tribes: Tribe.Quilboar, golden: golden, mechanics: CardMechanic.Deathrattle)
            with { BloodGemAttack = 3, BloodGemHealth = 2 };
        var ours = Side(bristler, Minion(1, 10, cardId: "Q1", tribes: Tribe.Quilboar), Minion(1, 10, cardId: "Q2", tribes: Tribe.Quilboar),
                        Minion(1, 10, cardId: "N", tribes: Tribe.Beast));
        var result = Fight(ours, Side(Killer(20, 1), Wall), oursFirst: false, Only(typeof(TimewarpedBristler)));

        Assert.Equal([first, second, "N 1/10"], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, "B 5/14")]
    [InlineData(true, "B 9/18")]
    public void Goldrinn_gives_the_beasts_four_four(bool golden, string beast)
    {
        var stats = golden ? 12 : 6;
        var ours = Side(Minion(stats, stats, cardId: "BG34_Giant_362", tribes: Tribe.Beast, golden: golden, mechanics: CardMechanic.Deathrattle),
                        Minion(1, 10, cardId: "B", tribes: Tribe.Beast), Minion(1, 10, cardId: "N"));
        var result = Fight(ours, Side(Killer(20, 1), Wall), oursFirst: false, Only(typeof(TimewarpedGoldrinn)));

        Assert.Equal([beast, "N 1/10"], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, "BG34_Giant_656 9/15 Taunt", "X 4/13")]
    [InlineData(true, "BG34_Giant_656(G) 18/30 Taunt", "X 7/16")]
    public void Grease_bot_buffs_the_warband_when_a_divine_shield_breaks(bool golden, string bot, string other)
    {
        // Their E 1/1 hits our shielded Taunt Grease Bot: the shield breaks (no damage), E dies; every friendly minion
        // gets +3/+3 (golden +6/+6).
        var (attack, health) = golden ? (12, 24) : (6, 12);
        var ours = Side(Minion(attack, health, Keyword.Taunt | Keyword.DivineShield, "BG34_Giant_656", Tribe.Mech, golden), Minion(1, 10, cardId: "X"));
        var result = Fight(ours, Side(Minion(1, 1, cardId: "E")), oursFirst: false, Only(typeof(TimewarpedGreaseBot)));

        Assert.Equal([bot, other], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Guard_rally_gives_other_minions_divine_shield(bool golden, bool first, bool second)
    {
        // Our Guard attacks their Wall first; the Rally shields A (golden: A and B), never the Guard itself.
        var (attack, health) = golden ? (10, 20) : (5, 10);
        var ours = Side(Minion(attack, health, Keyword.DivineShield, "BG34_Giant_068", Tribe.Mech, golden), Minion(1, 10, cardId: "A"),
                        Minion(1, 10, cardId: "B"));
        var result = Fight(ours, Side(Wall), oursFirst: true, Only(typeof(TimewarpedGuard)));

        Assert.Equal(first, result.Ours[1].DivineShield);
        Assert.Equal(second, result.Ours[2].DivineShield);
    }

    [Theory]
    [InlineData(false, "R 3/3", "BG34_Giant_024 7/8")]
    [InlineData(true, "R 5/5", "BG34_Giant_024(G) 14/16")]
    public void Jelly_belly_buffs_the_warband_after_a_reborn(bool golden, string reborn, string jelly)
    {
        // Their Killer 5/1 attacks R (pick 0): both die; R comes back 1/1, then every friendly minion gets +2/+2 (golden +4/+4).
        var (attack, health) = golden ? (10, 12) : (5, 6);
        var ours = Side(Minion(1, 1, Keyword.Reborn, "R"), Minion(attack, health, cardId: "BG34_Giant_024", tribes: Tribe.Undead, golden: golden));
        var result = Fight(ours, Side(Killer(5, 1)), oursFirst: false, Only(typeof(TimewarpedJellyBelly)));

        Assert.Equal([reborn, jelly], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    public void Karathress_gets_deep_blues_after_each_summon(bool golden, int copies)
    {
        // The Killer 1/1 kills our 0/1 Bonehead, whose deathrattle summons two Skeletons: a Deep Blues each (golden: two).
        var (attack, health) = golden ? (28, 12) : (14, 6);
        var ours = Side(Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle),
                        Minion(attack, health, cardId: "BG34_PreMadeChamp_056", tribes: Tribe.Naga, golden: golden));
        var result = Fight(ours, Side(Killer(1, 1)), oursFirst: false, Only(typeof(HarmlessBonehead), typeof(TimewarpedKarathress)));

        Assert.Equal(copies, result.Lines("we get BG26_502t").Count);
    }

    [Theory]
    [InlineData(false, "C 2/11")]
    [InlineData(true, "C 3/12")]
    public void Leapfrogger_passes_its_deathrattle_on_with_its_own_numbers(bool golden, string last)
    {
        // Their Killer 10/1 attacks our Taunt Leapfrogger (pick 0 of the Taunts): both die; B gets +1/+1 (golden +2/+2) and
        // the deathrattle. B attacks K2 and both die: B's gained deathrattle gives C the same bonus, the golden one's +2/+2.
        var stats = golden ? 6 : 3;
        var ours = Side(Minion(stats, stats, Keyword.Taunt, "BG34_Giant_031", Tribe.Beast, golden, CardMechanic.Deathrattle),
                        Minion(1, 1, Keyword.Taunt, "B", Tribe.Beast), Minion(1, 10, cardId: "C", tribes: Tribe.Beast));
        var theirs = Side(Killer(10, 1), Minion(10, 1, cardId: "K2"), Wall);
        var result = Fight(ours, theirs, oursFirst: false, Only(typeof(TimewarpedLeapfrogger), typeof(TimewarpedGoldenLeapfrogger)));

        Assert.Equal([last], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Magnanimoose_summons_and_gets_a_minion_of_the_opponents_board(bool golden, int count)
    {
        // The Killer kills our Magnanimoose (both die). From their board as captured (Killer, Wall) pick 0: a Killer copy
        // is summoned in its slot and one goes to our hand (golden: twice).
        var (attack, health) = golden ? (16, 4) : (8, 2);
        var killer = golden ? 20 : 10;
        var ours = Side(Minion(attack, health, cardId: "BG34_Giant_619", tribes: Tribe.Beast, golden: golden, mechanics: CardMechanic.Deathrattle),
                        Minion(1, 10, cardId: "X"));
        var result = Fight(ours, Side(Killer(killer, 1), Wall), oursFirst: false, Only(typeof(TimewarpedMagnanimoose)));

        Assert.Equal([.. Times(count, $"KILLER {killer}/1"), "X 1/10"], Board(result.Ours));
        Assert.Equal(count, result.Lines("we get KILLER").Count);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Spirit_of_air_gives_windfury_divine_shield_and_taunt(bool golden, int count)
    {
        var (attack, health) = golden ? (10, 6) : (5, 3);
        var ours = Side(Minion(attack, health, cardId: "BG34_Giant_592", tribes: Tribe.Elemental, golden: golden, mechanics: CardMechanic.Deathrattle),
                        Minion(1, 10, cardId: "A"), Minion(1, 10, cardId: "B"));
        var result = Fight(ours, Side(Killer(20, 1), Wall), oursFirst: false, Only(typeof(TimewarpedSpiritOfAir)));

        var given = result.Ours.Count(m => m.Taunt && m.DivineShield && (m.Keywords & Keyword.Windfury) != 0);
        Assert.Equal(count, given);
        Assert.True(result.Ours[0].Taunt);
    }

    [Theory]
    [InlineData(false, "A 9/18")]
    [InlineData(true, "A 33/42")]
    public void Trickster_gives_its_maximum_stats_to_another_minion(bool golden, string receiver)
    {
        var stats = golden ? 16 : 8;
        var ours = Side(Minion(stats, stats, cardId: "BG34_Giant_010", tribes: Tribe.Demon, golden: golden, mechanics: CardMechanic.Deathrattle),
                        Minion(1, 10, cardId: "A"));
        var result = Fight(ours, Side(Killer(20, 1), Wall), oursFirst: false, Only(typeof(TimewarpedTrickster)));

        Assert.Equal([receiver], Board(result.Ours));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ultralisk_also_damages_the_targets_neighbours(bool golden)
    {
        // Our Ultralisk attacks M (pick 1) between L and R: all three take its Attack and die in one attack.
        var stats = golden ? 16 : 8;
        var theirs = Side(Minion(0, stats, cardId: "L"), Minion(0, stats, cardId: "M"), Minion(0, stats, cardId: "R"));
        var result = Fight(Side(Minion(stats, stats, cardId: "BG34_Treasure_994", golden: golden)), theirs, oursFirst: true,
                           Only(typeof(TimewarpedUltralisk)), 1);

        Assert.Equal(1, result.Outcome);
        Assert.Empty(result.Theirs);
        Assert.Single(result.Log, l => l.Contains(" attacks ", StringComparison.Ordinal));
    }
}

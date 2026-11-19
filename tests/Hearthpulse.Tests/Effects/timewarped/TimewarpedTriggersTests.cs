using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>Timewarped "whenever/after" triggers. Boards worked out by hand in each comment.</summary>
public sealed class TimewarpedTriggersTests
{
    private const string Chimera = "BG34_Giant_679";

    private static SimMinion Bonehead => Minion(1, 1, Keyword.Taunt, Ids.Bonehead, Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    private static SimSide ChimeraBoard(SimMinion chimera) =>
        Side(chimera, Minion(1, 10, cardId: "B", tribes: Tribe.Beast), Minion(1, 10, cardId: "D", tribes: Tribe.Demon),
             Minion(1, 10, cardId: "BD", tribes: Tribe.Beast | Tribe.Demon));

    [Fact]
    public void Chimera_gives_one_minion_of_each_type_two_one_preferring_single_type_minions()
    {
        // Their E 1/1 hits our Taunt Chimera 1/8 (now 1/7) and dies to it. Beast: B (one type) before BD and the
        // all-type Chimera; Demon: D; Dragon: only the Chimera is left for it; no type remains for BD. +2/+1 each.
        var result = Fight(ChimeraBoard(Minion(1, 8, Keyword.Taunt, Chimera, Tribe.All)), Side(Minion(1, 1, cardId: "E")), oursFirst: false,
                           Only(typeof(TimewarpedChimera)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["BG34_Giant_679 3/8 Taunt", "B 3/11", "D 3/11", "BD 1/10"], Board(result.Ours));
    }

    [Fact]
    public void Golden_chimera_does_it_twice()
    {
        // Golden 2/16 hit for 1 → 2/15; twice the same picks: +4/+2 each.
        var result = Fight(ChimeraBoard(Minion(2, 16, Keyword.Taunt, Chimera, Tribe.All, golden: true)), Side(Minion(1, 1, cardId: "E")),
                           oursFirst: false, Only(typeof(TimewarpedChimera)));

        Assert.Equal(["BG34_Giant_679(G) 6/17 Taunt", "B 5/12", "D 5/12", "BD 1/10"], Board(result.Ours));
    }

    [Fact]
    public void Chimera_does_not_trigger_when_divine_shield_takes_the_hit()
    {
        var result = Fight(ChimeraBoard(Minion(1, 8, Keyword.Taunt | Keyword.DivineShield, Chimera, Tribe.All)), Side(Minion(1, 1, cardId: "E")),
                           oursFirst: false, Only(typeof(TimewarpedChimera)));

        Assert.Equal(["BG34_Giant_679 1/8 Taunt", "B 1/10", "D 1/10", "BD 1/10"], Board(result.Ours));
    }

    [Fact]
    public void Deadstomper_gives_every_minion_four_attack_after_each_summon()
    {
        // The Bonehead dies to Killer 5/1 and summons two Skeletons. After the first: Skeleton 5/1, Deadstomper 10/12;
        // after the second: 9/1, 5/1, 14/12.
        var result = Fight(Side(Bonehead, Minion(6, 12, cardId: "BG34_Giant_654", tribes: Tribe.Undead | Tribe.Beast)), Side(Killer(5, 1), Wall),
                           oursFirst: false, Only(typeof(HarmlessBonehead), typeof(TimewarpedDeadstomper)));

        Assert.Equal(["BG_ICC_026t 9/1", "BG_ICC_026t 5/1", "BG34_Giant_654 14/12"], Board(result.Ours));
    }

    [Fact]
    public void Deathswarmer_gives_the_undead_attack_when_it_takes_damage()
    {
        // Their E 1/1 hits our Taunt Deathswarmer 1/9 (1/8) and dies to it: Undead +1 Attack (itself 2/8, U 3/5), N stays.
        var ours = Side(Minion(1, 9, Keyword.Taunt, "BG34_Giant_081", Tribe.Undead), Minion(2, 5, cardId: "U", tribes: Tribe.Undead),
                        Minion(2, 5, cardId: "N", tribes: Tribe.Beast));
        var result = Fight(ours, Side(Minion(1, 1, cardId: "E")), oursFirst: false, Only(typeof(TimewarpedDeathswarmer)));

        Assert.Equal(["BG34_Giant_081 2/8 Taunt", "U 3/5", "N 2/5"], Board(result.Ours));
    }

    [Theory]
    [InlineData(false, "BG34_PreMadeChamp_020 12/18", "Dr 7/5")]
    [InlineData(true, "BG34_PreMadeChamp_020(G) 24/36", "Dr 13/9")]
    public void Duskmaw_buffs_the_dragons_after_each_friendly_death(bool golden, string duskmaw, string dragon)
    {
        // Killer 5/1 and our Taunt X 1/1 trade: Avenge (1) gives our Dragons +6/+4 (golden +12/+8), the Duskmaw included.
        var ours = Side(Minion(1, 1, Keyword.Taunt, "X"), Minion(golden ? 12 : 6, golden ? 28 : 14, cardId: "BG34_PreMadeChamp_020", tribes: Tribe.Dragon, golden: golden),
                        Minion(1, 1, cardId: "Dr", tribes: Tribe.Dragon));
        var result = Fight(ours, Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(TimewarpedDuskmaw)));

        Assert.Equal([duskmaw, dragon], Board(result.Ours));
    }

    [Fact]
    public void Gemsplitter_makes_later_blood_gems_give_more_attack()
    {
        // Their A 1/1 pops the Gemsplitter's Divine Shield and dies: Blood Gems now give +2/+1. Our Lil' Quilboar 5/5
        // attacks K 5/1 (pick 0); both die; three Gems on each living Quilboar: +6/+3. We win.
        var ours = Side(Minion(5, 5, cardId: "BG34_Giant_608", tribes: Tribe.Quilboar, mechanics: CardMechanic.Deathrattle),
                        Minion(5, 10, Keyword.Taunt | Keyword.DivineShield, "BG34_Giant_644", Tribe.Quilboar),
                        Minion(1, 10, cardId: "Q", tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(1, 1, cardId: "A"), Minion(5, 1, cardId: "K")), oursFirst: false,
                           Only(typeof(TimewarpedGemsplitter), typeof(TimewarpedLilQuilboar)), 0, 0);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["BG34_Giant_644 11/13 Taunt", "Q 7/13"], Board(result.Ours));
    }

    [Fact]
    public void Hyena_grows_when_a_friendly_beast_dies_only()
    {
        // Their Killer kills our Taunt Beast: the Hyena 3/5 becomes 5/7. A Taunt Mech dying instead changes nothing.
        var beast = Fight(Side(Minion(1, 1, Keyword.Taunt, "B", Tribe.Beast), Minion(3, 5, cardId: "BG34_Giant_581", tribes: Tribe.Beast)),
                          Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(TimewarpedHyena)));
        var mech = Fight(Side(Minion(1, 1, Keyword.Taunt, "M", Tribe.Mech), Minion(3, 5, cardId: "BG34_Giant_581", tribes: Tribe.Beast)),
                         Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(TimewarpedHyena)));

        Assert.Equal(["BG34_Giant_581 5/7"], Board(beast.Ours));
        Assert.Equal(["BG34_Giant_581 3/5"], Board(mech.Ours));
    }

    [Fact]
    public void Jungle_king_buffs_summoned_beasts_with_its_printed_bonus_without_script_data()
    {
        // The Sewer Rat dies to Killer 5/1 and summons a 2/3 Taunt Half-Shell (a Beast): +4/+3 → 6/6. Skeletons (Undead)
        // would get nothing. The King's grown bonus is its script data (E5, LeftoverTimewarpedTests); without it the printed one.
        var ours = Side(Minion(3, 2, Keyword.Taunt, Tokens.SewerRatId, Tribe.Beast, mechanics: CardMechanic.Deathrattle),
                        Minion(8, 12, cardId: "BG34_PreMadeChamp_004", tribes: Tribe.Beast));
        var result = Fight(ours, Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(SewerRat), typeof(TimewarpedJungleKing)));

        Assert.Equal(["BG19_010t 6/6 Taunt", "BG34_PreMadeChamp_004 8/12"], Board(result.Ours));
        Assert.Equal(CoverageStatus.Implemented, SimCoverage.Classify("BG34_PreMadeChamp_004", CoverageKind.Minion).Status);
    }

    [Fact]
    public void Piper_improves_blood_gems_three_times_per_combat()
    {
        // Our minions have no Attack, so only their E 1/100 attacks. It hits our Taunt Piper 0/30 thirty times (three of
        // the hits count: Gems +4/+1), then Lil (pick 0 of Lil and Q) dies: three Gems on Q 0/50 → 12/53, which Q has
        // when it first attacks.
        var ours = Side(Minion(0, 30, Keyword.Taunt, "BG34_Giant_069", Tribe.Quilboar),
                        Minion(0, 1, cardId: "BG34_Giant_608", tribes: Tribe.Quilboar, mechanics: CardMechanic.Deathrattle),
                        Minion(0, 50, cardId: "Q", tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(1, 100, cardId: "E")), oursFirst: false, Only(typeof(TimewarpedPiper), typeof(TimewarpedLilQuilboar)));

        Assert.NotEmpty(result.Lines("our Q 12/53 attacks"));
    }

    [Theory]
    [InlineData(false, "BG_ICC_026t 2/2")]
    [InlineData(true, "BG_ICC_026t 3/3")]
    public void Tamuzo_doubles_or_triples_a_summoned_minion(bool golden, string skeleton)
    {
        var tamuzo = Minion(golden ? 10 : 5, golden ? 10 : 5, cardId: "BG34_Giant_595", tribes: Tribe.Beast, golden: golden);
        var result = Fight(Side(Bonehead, tamuzo), Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(HarmlessBonehead), typeof(TimewarpedTamuzo)));

        Assert.Equal([skeleton, skeleton], Board(result.Ours).Take(2));
    }
}

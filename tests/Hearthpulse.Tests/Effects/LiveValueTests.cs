using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Tests.Sim.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Effects whose number the game keeps on the minion (TAG_SCRIPT_DATA_NUM_1/2, seen with the printed value in the real
/// log of 15.11.2026, build 255790) read it from the capture, so a server-side balance change reaches the simulator
/// without a code change; the printed number is only the fallback (covered by each effect's own tests). Each test gives
/// the minion a value different from the printed one. Also: corrected targets found while comparing the effects with the
/// latest card text.
/// </summary>
public sealed class LiveValueTests
{
    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    private static SimMinion WithData(SimMinion minion, int first, int second = 0) =>
        minion with { ScriptData1 = first, ScriptData2 = second };

    [Fact]
    public void Glim_guardian_gains_its_script_data_attack()
    {
        // Script data 3 (printed +2): 1/4 → 4/4 kills the 0/4 at once.
        var glim = WithData(Minion(1, 4, cardId: "BG29_888", tribes: Tribe.Dragon), 3);
        var result = Fight(Side(glim), Side(Minion(0, 4, cardId: "E")), true, Effects(typeof(GlimGuardian)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(4, 4)], Stats(result.Ours));
    }

    [Fact]
    public void Wolf_pup_gives_its_script_data()
    {
        // Script data 5/2 (printed +4/+1): A 1/1 → 6/3; the Pup's 3 kills the 0/3.
        var pup = WithData(Minion(3, 6, cardId: "BG36_207", tribes: Tribe.Beast), 5, 2);
        var result = Fight(Side(pup, Minion(1, 1, cardId: "A")), Side(Minion(0, 3, cardId: "E")), true, Effects(typeof(WolfPup)));

        Assert.Equal([(3, 6), (6, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Bonker_plays_as_many_gems_as_its_script_data()
    {
        // Script data 2 (printed 1 gem): A 1/1 → 3/3 on the first swing; the Bonker's 2 kills the 0/2.
        var bonker = WithData(Minion(2, 7, Keyword.Windfury, "BG20_104", Tribe.Quilboar), 2);
        var result = Fight(Side(bonker, Minion(1, 1, cardId: "A")), Side(Minion(0, 2, cardId: "E")), true, Effects(typeof(Bonker)));

        Assert.Equal([(2, 7), (3, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Razorfen_vineweaver_plays_as_many_gems_as_its_script_data()
    {
        // Script data 5 (printed 4): 4/4 → 9/9 kills the 0/9.
        var vineweaver = WithData(Minion(4, 4, cardId: "BG33_883", tribes: Tribe.Quilboar), 5);
        var result = Fight(Side(vineweaver), Side(Minion(0, 9, cardId: "E")), true, Effects(typeof(RazorfenVineweaver)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(9, 9)], Stats(result.Ours));
    }

    [Fact]
    public void Cage_gnawer_gives_its_script_data()
    {
        // Script data 3/2 (printed +2/+1): the Gnawer attacks, the Beasts (itself) get +3/+2: 5/9 kills the 0/5.
        var gnawer = WithData(Minion(2, 7, cardId: "BG36_211", tribes: Tribe.Beast), 3, 2);
        var result = Fight(Side(gnawer), Side(Minion(0, 5, cardId: "E")), true, Effects(typeof(CageGnawer)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(5, 9)], Stats(result.Ours));
    }

    [Fact]
    public void Roaring_recruiter_gives_its_script_data()
    {
        // Script data 4/2 (printed +3/+1): D 1/1 attacks as 5/3 and kills the 0/5.
        var recruiter = WithData(Minion(2, 8, cardId: "BG29_816", tribes: Tribe.Dragon), 4, 2);
        var result = Fight(Side(Minion(1, 1, cardId: "D", tribes: Tribe.Dragon), recruiter), Side(Minion(0, 5, cardId: "E")), true,
                           Effects(typeof(RoaringRecruiter)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(5, 3), (2, 8)], Stats(result.Ours));
    }

    [Fact]
    public void Flittering_bat_summons_beasts_with_its_script_data_stats()
    {
        // Script data 2/3 (printed 1/1): the Rally summons a 2/3 Foraging Bat.
        var bat = WithData(Minion(1, 4, cardId: "BG36_200", tribes: Tribe.Beast), 2, 3);
        var result = Fight(Side(bat), Side(Minion(0, 1, cardId: "E")), true, Effects(typeof(FlitteringBat)));

        Assert.Contains("summoned our BG36_200t 2/3 at 1", result.Log);
    }

    [Fact]
    public void Murcules_gives_its_script_data_to_the_hand()
    {
        // Script data 5/4 (printed +3/+3): Murcules kills their Taunt 1/1, the 1/4 Murloc in hand becomes 6/8; their 6/20
        // kills the Taunt Bassgill, whose deathrattle summons it.
        var murcules = WithData(Minion(5, 3, cardId: "BG27_023", tribes: Tribe.Murloc), 5, 4);
        var ours = Side(murcules, Minion(5, 2, cardId: "BG26_350", tribes: Tribe.Murloc, keywords: Keyword.Taunt))
            with { Hand = [Minion(1, 4, cardId: "M", tribes: Tribe.Murloc)] };
        var result = Fight(ours, Side(Minion(1, 1, cardId: "E1", keywords: Keyword.Taunt), Minion(6, 20, cardId: "E2")), true,
                           Effects(typeof(Murcules), typeof(Bassgill)));

        Assert.Contains("summoned our M 6/8 at 1", result.Log);
    }

    [Fact]
    public void Devout_hellcaller_gains_its_script_data()
    {
        // Script data 3/3 (printed +2/+2): the Hellcaller hits 4 (10→6), the Demon 2 (→4): Hellcaller 7/7 kills.
        var hellcaller = WithData(Minion(4, 4, cardId: "BG33_155", tribes: Tribe.Demon), 3, 3);
        var result = Fight(Side(hellcaller, Minion(2, 5, cardId: "D", tribes: Tribe.Demon)), Side(Minion(0, 10)), true,
                           Effects(typeof(DevoutHellcaller)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(7, 7), (2, 5)], Stats(result.Ours));
    }

    [Fact]
    public void Drifting_sacrifice_gives_the_deity_its_script_data()
    {
        // Script data 3/2 (printed +2/+1): the sleeping Y'Shaarj 1/1 becomes 4/3 when the Sacrifice dies.
        var sacrifice = WithData(Minion(1, 1, Keyword.Reborn, "BG36_113", Tribe.Aberration, mechanics: CardMechanic.Deathrattle), 3, 2);
        var ours = new SimSide([sacrifice, Minion(0, 1, cardId: "AB1", tribes: Tribe.Aberration)])
        {
            Extras = [Tokens.YShaarjId],
            Tags = new Dictionary<string, int> { ["BACON_OLD_GOD_ATTACK"] = 1, ["BACON_OLD_GOD_HEALTH"] = 1 },
        };
        var result = Fight(ours, Side(Minion(1, 50)), oursFirst: false, Effects(typeof(YShaarj), typeof(DriftingSacrifice)));

        Assert.Contains(result.Log, l => l.Contains("our Deity gets +3/+2 (4/3)", StringComparison.Ordinal));
    }

    [Fact]
    public void Relentless_deflector_avenges_after_its_script_data_deaths()
    {
        // Script data 2 (printed Avenge (3)): two friendly deaths give the Deflector Divine Shield (and Taunt).
        var deflector = WithData(Minion(0, 4, cardId: "BG34_405", tribes: Tribe.Mech), 2);
        var result = Fight(Side(Minion(0, 1), Minion(0, 1), deflector, Minion(0, 3)), Side(Minion(1, 50)), oursFirst: false,
                           Effects(typeof(RelentlessDeflector)), 0, 0);

        var avenge = result.Log.ToList().FindIndex(l => l.StartsWith("avenge: ", StringComparison.Ordinal));
        Assert.True(avenge > 0);
        Assert.Equal(2, result.Log.Take(avenge).Count(l => l.EndsWith(" dies", StringComparison.Ordinal)));
    }

    [Fact]
    public void Scarlet_survivor_uses_its_script_data_threshold()
    {
        // Script data 5 (printed 6): the 2/3 Survivor reaches 5 Attack from the test Rally (+3 to Dragons) and gains
        // Divine Shield; with the printed 6 it would not.
        var survivor = WithData(Minion(2, 3, cardId: "BG35_814", tribes: Tribe.Dragon), 5);
        var result = Fight(Side(Minion(1, 10, cardId: "TEST_RALLY_PUMP_DRAGONS"), survivor), Side(Minion(0, 1)), oursFirst: true,
                           Effects(typeof(ScarletSurvivor), typeof(RallyPumpDragons)));

        Assert.Equal((5, 3), (result.Ours[1].Attack, result.Ours[1].Health));
        Assert.True(result.Ours[1].DivineShield);
    }

    [Fact]
    public void Nadina_triggered_while_alive_can_shield_itself()
    {
        // "Give 3 friendly Dragons Divine Shield" does not exclude Nadina. Deathstrider triggers the left-most deathrattle
        // (Nadina's, while it lives) after the Rally minion attacks: Nadina is the only Dragon and gets the shield.
        var nadina = Minion(0, 4, cardId: "BGS_040", tribes: Tribe.Dragon, mechanics: CardMechanic.Deathrattle);
        var rally = Minion(1, 10, cardId: "R", mechanics: CardMechanic.Rally);
        var result = Fight(Side(nadina, Minion(0, 11, cardId: "BG36_208"), rally), Side(Minion(0, 1, cardId: "E")), true,
                           Effects(typeof(NadinaTheRed), typeof(Deathstrider)));

        Assert.Equal(1, result.Outcome);
        Assert.True(result.Ours[0].DivineShield);
    }
}

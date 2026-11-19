using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// "Whenever you cast a spell on this / on a Murloc / on a Naga / on a Mech" minions, set off by Seafloor Recruiter's
/// Rally (Chef's Choice on the minion to its right). Card texts in each test; stats worked out by hand.
/// </summary>
public sealed class SpellOnMinionTests
{
    [Fact]
    public void Fleeing_fugitive_gains_one_health_when_a_spell_is_cast_on_it()
    {
        // "Whenever you cast a spell on this, gain +1 Health." Fugitive 5/2 → 5/3; Recruiter 3/5 kills the 0/3.
        var result = Fight(Side(RecruiterMinion(), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)), Wall(3), true,
            Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(3, 5), (5, 3)], Stats(result.Ours));
        Assert.Single(result.Lines("we cast BG28_518 on our BG36_921 5/2"));
    }

    [Fact]
    public void Golden_fugitive_gains_two_and_golden_recruiter_casts_twice()
    {
        // Golden Fugitive 10/4: +2 Health per spell; golden Recruiter 6/10 casts twice → 10/8.
        var result = Fight(Side(RecruiterMinion(golden: true), Minion(10, 4, cardId: "BG36_921", tribes: Tribe.Naga, golden: true)),
            Wall(6), true, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive)));

        Assert.Equal([(6, 10), (10, 8)], Stats(result.Ours));
    }

    [Fact]
    public void Fleeing_fugitive_ignores_a_spell_on_another_minion()
    {
        // The spell goes to the Naga N right of the Recruiter; the Fugitive further right stays 5/2.
        var result = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "N", tribes: Tribe.Naga), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive)));

        Assert.Equal([(3, 5), (1, 1), (5, 2)], Stats(result.Ours));
    }

    [Fact]
    public void Torrential_ruiner_buffs_all_your_minions_for_a_spell_on_a_naga()
    {
        // "Whenever you cast a spell on a Naga, give your minions +2/+3." Spell on the Naga N: Recruiter 5/8, N 3/4,
        // Ruiner 6/3 → 8/6. Golden Ruiner 12/6: +4/+6 → Recruiter 7/11, N 5/7, Ruiner 16/12.
        var normal = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "N", tribes: Tribe.Naga), Minion(6, 3, cardId: "BG36_622", tribes: Tribe.Naga)),
            Wall(5), true, Effects(typeof(SeafloorRecruiter), typeof(TorrentialRuiner)));
        var golden = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "N", tribes: Tribe.Naga), Minion(12, 6, cardId: "BG36_622", tribes: Tribe.Naga, golden: true)),
            Wall(7), true, Effects(typeof(SeafloorRecruiter), typeof(TorrentialRuiner)));

        Assert.Equal(1, normal.Outcome);
        Assert.Equal([(5, 8), (3, 4), (8, 6)], Stats(normal.Ours));
        Assert.Equal([(7, 11), (5, 7), (16, 12)], Stats(golden.Ours));
    }

    [Fact]
    public void Torrential_ruiner_ignores_a_spell_on_another_type()
    {
        var result = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "B", tribes: Tribe.Beast), Minion(6, 3, cardId: "BG36_622", tribes: Tribe.Naga)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(TorrentialRuiner)));

        Assert.Equal([(3, 5), (1, 1), (6, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Shamanic_tidecaller_buffs_murlocs_on_the_board_and_in_the_hand()
    {
        // "Whenever you cast a spell on a Murloc, give Murlocs in your hand and board +3/+4." Spell on the Murloc M 1/1:
        // M 4/5, Tidecaller 6/9 → 9/13, Recruiter (Naga) stays 3/5; in hand the Murloc HM 2/2 → 5/6, the Beast HB stays.
        var ours = Side(RecruiterMinion(), Minion(1, 1, cardId: "M", tribes: Tribe.Murloc), Minion(6, 9, cardId: "BG36_704", tribes: Tribe.Murloc),
                Minion(0, 1, cardId: "WATCH")) with
        {
            Hand = [Minion(2, 2, cardId: "HM", tribes: Tribe.Murloc), Minion(3, 3, cardId: "HB", tribes: Tribe.Beast)],
        };
        var result = Fight(ours, Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(ShamanicTidecaller), typeof(Watcher)));

        Assert.Equal([(3, 5), (4, 5), (9, 13), (0, 1)], Stats(result.Ours));
        Assert.Single(result.Lines("after BG28_518 hand: HM 5/6, HB 3/3"));
    }

    [Fact]
    public void Golden_shamanic_tidecaller_gives_six_eight()
    {
        // Golden 12/18: +6/+8 → M 7/9, Tidecaller 18/26.
        var result = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "M", tribes: Tribe.Murloc), Minion(12, 18, cardId: "BG36_704", tribes: Tribe.Murloc, golden: true)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(ShamanicTidecaller)));

        Assert.Equal([(3, 5), (7, 9), (18, 26)], Stats(result.Ours));
    }

    [Fact]
    public void Glambot_magnetizes_a_satellite_to_the_mech_before_the_spell()
    {
        // "Whenever you cast a spell on a Mech, Magnetize a 5/5 Satellite to it." Spell on the Mech M 2/2 → 7/7, before
        // Chef's Choice gives its card. Golden: "twice" → 12/12.
        var normal = Fight(
            Side(RecruiterMinion(), Minion(2, 2, cardId: "M", tribes: Tribe.Mech), Minion(5, 5, cardId: "BG36_853", tribes: Tribe.Mech)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(Glambot)));
        var golden = Fight(
            Side(RecruiterMinion(), Minion(2, 2, cardId: "M", tribes: Tribe.Mech), Minion(10, 10, cardId: "BG36_853", tribes: Tribe.Mech, golden: true)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(Glambot)));

        Assert.Equal([(3, 5), (7, 7), (5, 5)], Stats(normal.Ours));
        Assert.True(normal.IndexOf("magnetize BG31_171t 5/5 to M 2/2") < normal.IndexOf("we get a random card"));
        Assert.Equal([(3, 5), (12, 12), (10, 10)], Stats(golden.Ours));
        Assert.Equal(2, golden.Lines("magnetize BG31_171t").Count);
    }

    [Fact]
    public void Glambot_ignores_a_spell_on_a_minion_that_is_not_a_mech()
    {
        var result = Fight(
            Side(RecruiterMinion(), Minion(2, 2, cardId: "B", tribes: Tribe.Beast), Minion(5, 5, cardId: "BG36_853", tribes: Tribe.Mech)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(Glambot)));

        Assert.Empty(result.Lines("magnetize"));
    }

    [Fact]
    public void Gatekeeper_amalgam_casts_misplaced_tea_set_when_a_spell_is_cast_on_it()
    {
        // "Whenever you cast a spell on this, it casts Misplaced Tea Set." Tea Set: "Give a friendly minion of each type
        // +3/+3." Board: Recruiter (Naga), Amalgam (all types), D (Dragon), M (Murloc). Types with one candidate go first:
        // Beast (only the Amalgam), then Dragon (D), Murloc (M), Naga (Recruiter): each gets +3/+3 once.
        // Recruiter 6/8, Amalgam 9/9, D 4/4, M 4/4. The Tea Set comes before Chef's Choice resolves.
        var result = Fight(
            Side(RecruiterMinion(), Minion(6, 6, cardId: "BG36_640", tribes: Tribe.All),
                Minion(1, 1, cardId: "D", tribes: Tribe.Dragon), Minion(1, 1, cardId: "M", tribes: Tribe.Murloc)),
            Wall(6), true, Effects(typeof(SeafloorRecruiter), typeof(GatekeeperAmalgam)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(6, 8), (9, 9), (4, 4), (4, 4)], Stats(result.Ours));
        var chef = result.IndexOf("we cast BG28_518 on our BG36_640");
        var tea = result.IndexOf("we cast BG28_888");
        Assert.True(chef >= 0 && chef < tea && tea < result.IndexOf("we get a random card"));
    }

    [Fact]
    public void Golden_gatekeeper_amalgam_casts_it_twice()
    {
        // Twice +3/+3 each: Recruiter 9/11, Amalgam 18/18, D 7/7, M 7/7.
        var result = Fight(
            Side(RecruiterMinion(), Minion(12, 12, cardId: "BG36_640", tribes: Tribe.All, golden: true),
                Minion(1, 1, cardId: "D", tribes: Tribe.Dragon), Minion(1, 1, cardId: "M", tribes: Tribe.Murloc)),
            Wall(9), true, Effects(typeof(SeafloorRecruiter), typeof(GatekeeperAmalgam)));

        Assert.Equal(2, result.Lines("we cast BG28_888").Count);
        Assert.Equal([(9, 11), (18, 18), (7, 7), (7, 7)], Stats(result.Ours));
    }

    [Fact]
    public void Misplaced_tea_set_gives_each_type_one_distinct_minion_like_the_real_log()
    {
        // The real-log board shape (all-type minion, Demon/Dragon, plain Dragons): Beast has only the Amalgam, then Demon
        // only DD, then Naga only the Recruiter, then Dragon picks between D1 and D2 (pick 1 → D2). D1 gets nothing.
        // With a Tavern spell bonus of +1/+1 each buff is +4/+4. Picks: 0 = the Recruiter's target, then one per type.
        var ours = Side(RecruiterMinion(), Minion(6, 6, cardId: "BG36_640", tribes: Tribe.All),
                Minion(1, 1, cardId: "D1", tribes: Tribe.Dragon), Minion(1, 1, cardId: "D2", tribes: Tribe.Dragon),
                Minion(1, 1, cardId: "DD", tribes: Tribe.Demon | Tribe.Dragon)) with
        {
            Tags = new Dictionary<string, int> { ["TAVERN_SPELL_ATTACK_INCREASE"] = 1, ["TAVERN_SPELL_HEALTH_INCREASE"] = 1 },
        };
        var result = Fight(ours, Wall(7), true, Effects(typeof(SeafloorRecruiter), typeof(GatekeeperAmalgam)), 0, 0, 0, 0, 1);

        Assert.Equal([(7, 9), (10, 10), (1, 1), (5, 5), (5, 5)], Stats(result.Ours));
    }

    [Fact]
    public void Twilight_tidehunter_buffs_the_left_most_minion_in_hand()
    {
        // "Whenever you cast a spell on this, give the left-most minion in your hand +9/+9." Hand: A 1/1, B 2/2 → A 10/10.
        // Golden: +18/+18 → A 19/19.
        SimSide Board(bool golden) => Side(RecruiterMinion(),
                Minion(golden ? 6 : 3, golden ? 14 : 7, cardId: "BG36_703", tribes: Tribe.Murloc, golden: golden), Minion(0, 1, cardId: "WATCH")) with
        {
            Hand = [Minion(1, 1, cardId: "A"), Minion(2, 2, cardId: "B")],
        };
        var normal = Fight(Board(false), Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(TwilightTidehunter), typeof(Watcher)));
        var golden = Fight(Board(true), Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(TwilightTidehunter), typeof(Watcher)));

        Assert.Single(normal.Lines("after BG28_518 hand: A 10/10, B 2/2"));
        Assert.Single(golden.Lines("after BG28_518 hand: A 19/19, B 2/2"));
    }
}

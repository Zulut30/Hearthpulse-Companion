using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// The in-combat spell pipeline (Fight.Spells.cs): order of "whenever" hooks, the spell, "after" hooks; extra casts;
/// targets; Seafloor Recruiter's Rally ("Cast Chef's Choice on the minion to the right"; golden "twice") and Chef's
/// Choice ("Choose a minion. Get a different minion of the same type."). Boards are worked out by hand in each comment.
/// </summary>
public sealed class SpellPipelineTests
{
    [Fact]
    public void Whenever_hooks_run_before_the_spell_and_after_hooks_after_it()
    {
        // Recruiter attacks their 0/3 and casts Chef's Choice on the Naga to its right: the watcher's "whenever" line, then
        // the spell's own effect (a random Naga to the hand), then the watcher's "after" line, all before the damage.
        var result = Fight(
            Side(RecruiterMinion(), Minion(1, 1, cardId: "WATCH", tribes: Tribe.Naga)),
            Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(Watcher)));

        Assert.Equal(1, result.Outcome);
        var cast = result.IndexOf("we cast BG28_518 on our WATCH 1/1");
        var whenever = result.IndexOf("whenever BG28_518");
        var get = result.IndexOf("we get a random card");
        var after = result.IndexOf("after BG28_518");
        Assert.True(cast >= 0 && cast < whenever && whenever < get && get < after, string.Join("\n", result.Log));
        Assert.True(after < result.IndexOf(" takes 3"));
    }

    [Fact]
    public void Recruiter_casts_on_nothing_without_a_minion_to_its_right()
    {
        var result = Fight(Side(Minion(1, 1, cardId: "WATCH", tribes: Tribe.Naga), RecruiterMinion()), Wall(1), true,
            Effects(typeof(SeafloorRecruiter), typeof(Watcher)));

        Assert.Empty(result.Lines("we cast"));
    }

    [Fact]
    public void Golden_recruiter_casts_twice_on_the_same_minion()
    {
        // Golden 6/10 Recruiter: two casts on the Naga to its right, each followed by a random card.
        var result = Fight(Side(RecruiterMinion(golden: true), Minion(1, 1, cardId: "N", tribes: Tribe.Naga)), Wall(6), true,
            Effects(typeof(SeafloorRecruiter)));

        Assert.Equal(2, result.Lines("we cast BG28_518 on our N").Count);
        Assert.Equal(2, result.Lines("we get a random card").Count);
    }

    [Fact]
    public void Chefs_choice_on_a_minion_without_a_type_gets_nothing()
    {
        var result = Fight(Side(RecruiterMinion(), Minion(1, 1, cardId: "P")), Wall(3), true, Effects(typeof(SeafloorRecruiter)));

        Assert.Single(result.Lines("we cast BG28_518 on our P"));
        Assert.Empty(result.Lines("we get"));
    }

    [Fact]
    public void Opponent_spells_reach_only_the_opponents_hooks()
    {
        // Their Recruiter 3/5 attacks our Fugitive 5/2 and casts on their own Fugitive (5/2 → 5/3); ours gets nothing.
        // The exchange kills both (3 ≥ 2, 5 ≥ 5): their Fugitive 5/3 is left alone.
        var result = Fight(
            Side(Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)),
            Side(RecruiterMinion(), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)),
            false, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive)));

        Assert.Single(result.Lines("they cast BG28_518 on their BG36_921 5/2"));
        Assert.Empty(result.Lines("we cast"));
        Assert.Equal(-1, result.Outcome);
        Assert.Equal([(5, 3)], Stats(result.Theirs));
    }

    [Fact]
    public void Balinda_extra_casts_repeat_the_whole_cast()
    {
        // Balinda Stonehearth (E2): "Your spells that target friendly minions cast twice." Chef's Choice on the Fugitive is
        // cast twice through the pipeline: the Fugitive 5/2 gains +1 Health each time (5/4), two random cards come.
        // Golden Balinda: "three times" → 5/5.
        SimSide Board(bool golden) => Side(RecruiterMinion(), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga),
            Minion(golden ? 2 : 1, golden ? 2 : 1, cardId: "BG35_883", golden: golden));
        var normal = Fight(Board(false), Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive), typeof(BalindaStonehearth)));
        var golden = Fight(Board(true), Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive), typeof(BalindaStonehearth)));

        Assert.Equal(2, normal.Lines("we cast BG28_518").Count);
        Assert.Equal((5, 4), (normal.Ours[1].Attack, normal.Ours[1].Health));
        Assert.Equal(2, normal.Lines("we get a random card").Count);
        Assert.Equal(3, golden.Lines("we cast BG28_518").Count);
        Assert.Equal((5, 5), (golden.Ours[1].Attack, golden.Ours[1].Health));
    }

    [Fact]
    public void Balinda_does_not_repeat_a_spell_without_a_target()
    {
        var result = Fight(Side(VindicatorMinion(), Minion(1, 1, cardId: "BG35_883")), Wall(17), true,
            Effects(typeof(CrimsonVindicator), typeof(BalindaStonehearth)));

        Assert.Single(result.Lines("we cast BG36_246"));
    }

    [Fact]
    public void Simple_cast_overload_still_runs_the_pipeline()
    {
        // The old CastSpell(side, id, target, resolve) runs "whenever", the effect, then "after".
        var result = Fight(Side(Minion(1, 1, cardId: "OLD"), Minion(0, 1, cardId: "WATCH")), Wall(1), true,
            Effects(typeof(OldStyleCaster), typeof(Watcher)));

        var whenever = result.IndexOf("whenever TEST_SPELL");
        var resolved = result.IndexOf("resolved TEST_SPELL");
        var after = result.IndexOf("after TEST_SPELL");
        Assert.True(whenever >= 0 && whenever < resolved && resolved < after);
    }

    [Fact]
    public void A_blood_gem_played_by_a_minion_is_not_a_spell_cast()
    {
        // Bonker's Rally plays a Blood Gem (+1/+1) on the Fugitive: 5/2 → 6/3, and no "+1 Health on a spell": a gem a
        // minion plays is not a cast (real-log evidence in Fight.PlayBloodGem).
        var result = Fight(
            Side(Minion(2, 7, cardId: "BG20_104", tribes: Tribe.Quilboar), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)),
            Wall(2), true, Effects(typeof(Bonker), typeof(FleeingFugitive)));

        Assert.Empty(result.Lines("we cast"));
        Assert.Equal((6, 3), (result.Ours[1].Attack, result.Ours[1].Health));
    }

    [CardEffect("OLD")]
    private sealed class OldStyleCaster : CombatEffect
    {
        public override void OnAttack(Fight fight, Unit self, Unit target) =>
            fight.CastSpell(self.Side, "TEST_SPELL", null, f => f.Trace("resolved TEST_SPELL"));
    }
}

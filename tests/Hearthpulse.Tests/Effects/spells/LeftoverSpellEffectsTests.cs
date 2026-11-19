using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// Replica Cathedral, Honeycomb Ring, Bloodbound Earrings, Boon of Beetles, Dark Paradox's "Get" form and Locked-up
/// Mutineer (TASK-2026-11-19 E5). Crimson Vindicator 8/9 (Divine Shield Dragon, "Rally: Cast Mighty Dragonbreath": +3/+2 to
/// your minions, again to your Dragons, again to your minions with Divine Shield) and Seafloor Recruiter ("Rally: Cast
/// Chef's Choice on the minion to the right") cast in combat. Results are worked out by hand in each comment.
/// </summary>
public sealed class LeftoverSpellEffectsTests
{
    private const string Cathedral = "BG30_MagicItem_434";
    private const string Ring = "BG36_MagicItem_371";
    private const string Earrings = "BG32_MagicItem_808";

    private static SimSide WithExtra(SimSide side, string cardId, params (string Tag, int Value)[] tags) => side with
    {
        Extras = [.. side.Extras, cardId],
        ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>(side.ExtraTags)
        {
            [cardId] = tags.ToDictionary(t => t.Tag, t => t.Value, StringComparer.Ordinal),
        },
    };

    [Fact]
    public void Replica_cathedral_casts_the_first_spell_of_the_combat_twice_and_no_other()
    {
        // Without it: two attacks of the Vindicator, two casts (26 then 35 damage into the 0/40 wall).
        // With it: the first attack's Dragonbreath is cast twice (8/9 -> 26/21 before damage, 26 to the wall), the
        // second once (35): three casts.
        var plain = Fight(Side(VindicatorMinion()), Wall(40), true, Effects(typeof(CrimsonVindicator), typeof(ReplicaCathedral)));
        Assert.Equal(2, plain.Lines("we cast " + Dragonbreath).Count);

        var result = Fight(WithExtra(Side(VindicatorMinion()), Cathedral), Wall(40), true,
                           Effects(typeof(CrimsonVindicator), typeof(ReplicaCathedral)));
        Assert.Equal(3, result.Lines("we cast " + Dragonbreath).Count);
        Assert.Equal(1, result.Outcome);
    }

    [Fact]
    public void Honeycomb_ring_improves_the_tavern_spell_bonus_after_a_spell_on_a_minion()
    {
        // Recruiter (3) attacks first and casts Chef's Choice on X: the Ring raises the bonus to +1/+1. X has no Attack and
        // is skipped; the Vindicator's Dragonbreath then gives (3+1)/(2+1) three times: 8/9 -> 20/18, and 3 + 20 kills the
        // 0/23 wall. Without the Ring: 17/15.
        SimSide Ours() => Side(RecruiterMinion(), Minion(0, 4, cardId: "X", tribes: Tribe.Beast), VindicatorMinion());
        var registry = Effects(typeof(CrimsonVindicator), typeof(SeafloorRecruiter), typeof(HoneycombRing));

        var plain = Fight(Ours(), Wall(23), true, registry);
        Assert.Equal((17, 15), Stat(plain, VindicatorMinion().CardId));

        var result = Fight(WithExtra(Ours(), Ring), Wall(23), true, registry);
        Assert.Equal((20, 18), Stat(result, VindicatorMinion().CardId));
    }

    [Fact]
    public void Bloodbound_earrings_play_a_gem_on_every_minion_when_the_count_runs_out()
    {
        // One spell left (its captured NUM_1): the Dragonbreath (+9/+6 to the Vindicator) is followed by a Blood Gem
        // (+1/+1) on each of our minions: Vindicator 18/16, Y 0/1 +3/+2 (Dragonbreath, first part) +1/+1 = 4/4.
        var ours = WithExtra(Side(VindicatorMinion(), Minion(0, 1, cardId: "Y")), Earrings, ("TAG_SCRIPT_DATA_NUM_1", 1));
        var registry = Effects(typeof(CrimsonVindicator), typeof(BloodboundEarrings));
        var result = Fight(ours, Wall(18), true, registry);

        Assert.Equal((18, 16), Stat(result, VindicatorMinion().CardId));
        Assert.Equal((4, 4), Stat(result, "Y"));

        // Without a captured count it starts at the printed 4: one cast plays nothing.
        var printed = Fight(WithExtra(Side(VindicatorMinion()), Earrings), Wall(17), true, registry);
        Assert.Equal((17, 15), Stat(printed, VindicatorMinion().CardId));
    }

    [Fact]
    public void Boon_of_beetles_summons_its_taunt_beetles_as_space_opens()
    {
        // Our board is full, so the Boon still owes two Beetles: its NUM_1/2 = 3/3, NUM_3 = 2. Their 10/100 kills one of our
        // 0/1s, the first space brings a 3/3 Taunt Beetle; the next attack must hit that Taunt Beetle, and its death brings
        // the second. Then the Boon is spent.
        var ours = WithExtra(Side(Enumerable.Range(1, 7).Select(i => Minion(0, 1, cardId: "W" + i)).ToArray()), BoonOfBeetles.CardId,
                             ("TAG_SCRIPT_DATA_NUM_1", 3), ("TAG_SCRIPT_DATA_NUM_2", 3), ("TAG_SCRIPT_DATA_NUM_3", 2));
        var result = Fight(ours, Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(BoonOfBeetles)));

        var beetles = result.Lines("summoned our " + Tokens.BeetleId);
        Assert.Equal(2, beetles.Count);
        Assert.Contains("3/3", beetles[0], StringComparison.Ordinal);
        Assert.Contains("Taunt", beetles[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Boon_of_beetles_with_one_left_summons_one_and_a_full_board_alone_summons_none()
    {
        var one = WithExtra(Side(Enumerable.Range(1, 7).Select(i => Minion(0, 1, cardId: "W" + i)).ToArray()), BoonOfBeetles.CardId,
                            ("TAG_SCRIPT_DATA_NUM_3", 1));
        Assert.Single(Fight(one, Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(BoonOfBeetles))).Lines("summoned our " + Tokens.BeetleId));

        // Their wall never attacks and ours have no Attack: nobody dies, no space, no Beetle (the fight ends in a tie).
        var stuck = WithExtra(Side(Enumerable.Range(1, 7).Select(i => Minion(0, 1, cardId: "W" + i)).ToArray()), BoonOfBeetles.CardId,
                              ("TAG_SCRIPT_DATA_NUM_3", 2));
        Assert.Empty(Fight(stuck, Wall(5), true, Effects(typeof(BoonOfBeetles))).Lines("summoned our " + Tokens.BeetleId));
    }

    [Fact]
    public void Dark_paradox_get_form_gets_a_card_on_each_attack()
    {
        // Rally: one random minion; golden: two. One attack each (the 2-Attack wall dies to the first hit).
        var plain = Fight(Side(Minion(5, 6, cardId: "BG36_360t5", tribes: Tribe.All, mechanics: CardMechanic.Rally)), Wall(2), true,
                          Effects(typeof(DarkParadoxGetForm)));
        Assert.Single(plain.Lines("we get a random card"));

        var golden = Fight(Side(Minion(5, 6, cardId: "BG36_360t5", tribes: Tribe.All, golden: true, mechanics: CardMechanic.Rally)), Wall(2), true,
                           Effects(typeof(DarkParadoxGetForm)));
        Assert.Equal(2, golden.Lines("we get a random card").Count);
    }

    [Fact]
    public void Locked_up_mutineer_gets_one_lockbox_and_none_with_lockbox_portrait()
    {
        // Two Mutineers die to their 10/100: the first gets a Lockbox, the second finds one already held ("opens sooner").
        var two = Side(Minion(0, 3, cardId: "BG36_521", tribes: Tribe.Pirate), Minion(0, 3, cardId: "BG36_521", tribes: Tribe.Pirate));
        var result = Fight(two, Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(LockedUpMutineer)));
        Assert.Single(result.Lines("we get " + LockedUpMutineer.LockboxId));

        // Lockbox Portrait keeps a Lockbox in the hand: no card is added.
        var portrait = two with { Extras = ["BG36_MagicItem_301"] };
        Assert.Empty(Fight(portrait, Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(LockedUpMutineer))).Lines("we get "));
    }

    private static (int, int) Stat(FightResult result, string cardId)
    {
        var minion = result.Ours.First(m => m.CardId == cardId);
        return (minion.Attack, minion.Health);
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>Timewarped Collector, Nest Swarmer, Pagle and Hag. Boards worked out by hand in each comment.</summary>
public sealed class TimewarpedAttacksAndHagTests
{
    private const string Collector = "BG34_Giant_680";
    private const string Swarmer = "BG34_Giant_687";
    private const string Hag = "BG34_Giant_342";

    [Fact]
    public void Collector_also_damages_the_targets_neighbours()
    {
        // Our 12/12 attacks T (pick 1): T, and L and R next to it, take 12 and die; only T hits back (12/11). We win.
        var result = Fight(Side(Minion(12, 12, cardId: Collector, tribes: Tribe.Pirate)),
                           Side(Minion(1, 12, cardId: "L"), Minion(1, 12, cardId: "T"), Minion(1, 12, cardId: "R")), oursFirst: true,
                           Only(typeof(TimewarpedCollector)), 1);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(["BG34_Giant_680 12/11"], Board(result.Ours));
        Assert.Equal(3, result.Log.Count(l => l.StartsWith("their ", StringComparison.Ordinal) && l.EndsWith(" dies", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(true, "BG34_Giant_680(G) 24/24")]
    [InlineData(false, null)]
    public void Collector_rally_gives_divine_shield_with_four_golden_minions(bool fourGolden, string? collector)
    {
        // The golden Collector 24/24 attacks E 30/1 and kills it. With three more Golden minions (four with itself) the
        // Rally gives it Divine Shield first, which takes E's 30; with one of them plain it dies.
        var ours = Side(Minion(24, 24, cardId: Collector, tribes: Tribe.Pirate, golden: true), Minion(1, 1, cardId: "G1", golden: true),
                        Minion(1, 1, cardId: "G2", golden: true), Minion(1, 1, cardId: "G3", golden: fourGolden));
        var result = Fight(ours, Side(Minion(30, 1, cardId: "E")), oursFirst: true, Only(typeof(TimewarpedCollector)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(collector, Board(result.Ours).FirstOrDefault(l => l.StartsWith(Collector, StringComparison.Ordinal)));
    }

    [Fact]
    public void Nest_swarmer_rally_improves_beetles_then_summons_one_to_its_right()
    {
        // First attack on the 0-Attack Wall: Beetles +2/+2 (none yet; and for later ones), then a 2/2 Beetle arrives as 4/4.
        var result = Fight(Side(Minion(7, 7, cardId: Swarmer, tribes: Tribe.Beast)), Side(Wall), oursFirst: true, Only(typeof(TimewarpedNestSwarmer)));

        Assert.Equal("summoned our BG28_603t 4/4 at 1", result.Lines("summoned our BG28_603t")[0]);
        // Second attack: the first Beetle grows to 6/6, the new one arrives as 2/2 + two +2/+2 = 6/6.
        Assert.Equal("summoned our BG28_603t 6/6 at 1", result.Lines("summoned our BG28_603t")[1]);
    }

    [Theory]
    [InlineData(false, new[] { "BG28_603t 4/4" })]
    [InlineData(true, new[] { "BG28_603t 6/6", "BG28_603t 6/6" })]
    public void Nest_swarmer_deathrattle_summons_in_its_slot(bool golden, string[] board)
    {
        var swarmer = Minion(golden ? 14 : 7, golden ? 14 : 7, Keyword.Taunt, Swarmer, Tribe.Beast, golden, CardMechanic.Deathrattle);
        var result = Fight(Side(swarmer), Side(Killer(14, 1), Wall), oursFirst: false, Only(typeof(TimewarpedNestSwarmer)));

        Assert.Equal(board, Board(result.Ours));
    }

    [Fact]
    public void Hag_gives_the_undead_to_its_right_a_full_health_reborn()
    {
        // U (printed 1/1, buffed to 3/5, Taunt and Reborn from the Start of Combat) dies to Killer 10/1 and comes back as
        // 3/5 Taunt with its enchantment, not as a 1/1.
        var u = Minion(1, 1, Keyword.Taunt | Keyword.Reborn, "U", Tribe.Undead) with
        {
            Attack = 3, Health = 5, MaxHealth = 5, Enchantments = ["X_e"],
        };
        var result = Fight(Side(Minion(6, 8, cardId: Hag, tribes: Tribe.Undead), u), Side(Killer(10, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedHag)));

        Assert.Equal(["BG34_Giant_342 6/8", "U 3/5 Taunt"], Board(result.Ours));
        Assert.Equal(["X_e"], result.Ours[1].Enchantments);
        Assert.Single(result.Lines("reborn our U"));
    }

    [Fact]
    public void Without_the_hag_the_reborn_copy_is_plain()
    {
        var u = Minion(1, 1, Keyword.Taunt | Keyword.Reborn, "U", Tribe.Undead) with { Attack = 3, Health = 5, MaxHealth = 5 };
        var result = Fight(Side(Minion(6, 8, cardId: "OTHER", tribes: Tribe.Undead), u), Side(Killer(10, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedHag)));

        Assert.Equal(["OTHER 6/8", "U 1/1 Taunt"], Board(result.Ours));
    }

    [Fact]
    public void Golden_hag_covers_both_neighbours_but_not_a_non_undead()
    {
        // Left: an Undead L 4/4 with Reborn; right: a Beast B 4/4 with Reborn. Their Killer 10/1 picks L (pick 0), L comes
        // back 4/4; then nothing else dies (the Wall). B keeps a plain Reborn, which this fight never uses.
        var l = Minion(1, 1, Keyword.Taunt | Keyword.Reborn, "L", Tribe.Undead) with { Attack = 4, Health = 4, MaxHealth = 4 };
        var b = Minion(1, 1, Keyword.Reborn, "B", Tribe.Beast) with { Attack = 4, Health = 4, MaxHealth = 4 };
        var result = Fight(Side(l, Minion(12, 16, cardId: Hag, tribes: Tribe.Undead, golden: true), b), Side(Killer(10, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedHag)));

        Assert.Equal(["L 4/4 Taunt", "BG34_Giant_342(G) 12/16", "B 4/4 Reborn"], Board(result.Ours));
    }

    [Fact]
    public void Every_timewarped_effect_of_this_package_is_registered_for_both_versions()
    {
        string[] ids =
        [
            "BG34_Giant_071", "BG34_Giant_001", "BG34_Giant_618", "BG34_Giant_679", "BG34_Giant_680", "BG34_Giant_654",
            "BG34_Giant_081", "BG34_PreMadeChamp_020", "BG34_Giant_644", "BG34_Giant_342", "BG34_Giant_581", "BG34_Giant_674",
            "BG34_Giant_306", "BG34_PreMadeChamp_004", "BG34_Giant_608", "BG34_Giant_207", "BG34_Giant_687", "BG34_Giant_208",
            "BG34_Giant_069", "BG34_Giant_017", "BG34_Giant_595", "BG34_Giant_331", "BG34_Giant_328",
        ];
        foreach (var id in ids)
        {
            Assert.NotNull(EffectRegistry.Default.Get(id, golden: false));
            Assert.NotNull(EffectRegistry.Default.Get(id + "_G", golden: true));
        }

        Assert.Empty(EffectRegistry.Default.Problems);
    }
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Pagle_gets_a_triple_reward_once_per_combat_when_it_attacks_and_kills(bool golden, int rewards)
    {
        // Our Pagle kills E1 1/1 in its attack (pick 0): a Triple Reward (golden: two). Their E2 0/1 cannot attack; our
        // Pagle's next attack kills it too, but once per combat is used up.
        var pagle = Minion(golden ? 16 : 8, golden ? 12 : 6, cardId: "BG34_Giant_208", tribes: Tribe.Pirate, golden: golden);
        var result = Fight(Side(pagle), Side(Minion(1, 1, cardId: "E1"), Minion(0, 1, cardId: "E2")), oursFirst: true,
                           Only(typeof(TimewarpedPagle)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(Enumerable.Repeat("we get TB_BaconShop_Triples_01", rewards), result.Lines("we get "));
    }

    [Fact]
    public void Pagle_gets_nothing_for_a_kill_while_defending()
    {
        // Their E 1/1 attacks our Pagle 8/6 and dies to it: not "when this attacks".
        var result = Fight(Side(Minion(8, 6, cardId: "BG34_Giant_208", tribes: Tribe.Pirate)), Side(Minion(1, 1, cardId: "E")), oursFirst: false,
                           Only(typeof(TimewarpedPagle)));

        Assert.Equal(1, result.Outcome);
        Assert.Empty(result.Lines("we get "));
    }
    [Fact]
    public void The_opponents_beetle_behind_a_nest_swarmer_is_an_explicit_estimate()
    {
        // Their Beetle bonus is hidden from the player; a Timewarped Nest Swarmer summons their Beetle, so the capture
        // marks the Beetle's stats as estimated (our own Beetle, else the printed 2/2) instead of a silent 2/2.
        var theirs = Side(Minion(7, 7, cardId: Swarmer, tribes: Tribe.Beast));
        var estimates = Hearthpulse.Core.Tracking.SimEstimates.For(theirs, isOpponent: true, new Dictionary<string, int>(), local: null);

        Assert.Contains(estimates, e => e.Input == SimInputs.BeetleStats && (e.Value1, e.Value2) == (2, 2));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using Hearthpulse.Core.Tracking;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Package E7b (TASK-2026-11-19): the buddies, Battlecruiser upgrades, "after N …" cards, Dragon's Eye, I'll Take That!,
/// Lock and Load and the Undead "+X Attack this game" on Undead summoned in combat. One fight per behaviour, results worked
/// out by hand in each comment. Enemies "W" have no effect.
/// </summary>
public sealed class BuddiesAndGapsTests
{
    private const string Eclipsion = "TB_BaconShop_HERO_08_Buddy";
    private const string SquirrelBomb = "TB_BaconShop_HERO_17_Buddy";
    private const string Soulkeeper = "TB_BaconShop_HERO_702_Buddy";
    private const string EpicEgg = "TB_BaconShop_HERO_43_Buddy";
    private const string TombDiver = "TB_BaconShop_HERO_41_Buddy";
    private const string Raptor = "BG22_HERO_001_Buddy";
    private const string Battlecruiser = "BG31_HERO_801pt";
    private const string RandomCard = "we get a random card";

    private static (int, int) Stats(IEnumerable<SimMinion> side, string cardId) =>
        side.Where(m => m.CardId == cardId).Select(m => (m.Attack, m.Health)).Single();

    private static int Count(FightResult result, string text) => result.Log.Count(l => l.Contains(text, StringComparison.Ordinal));

    private static SimSide With(SimSide side, string extra, int? left = null) => side with
    {
        Extras = [extra],
        ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [extra] = left is { } n ? new Dictionary<string, int> { [ProgressGet.LeftTag] = n } : new Dictionary<string, int>(),
        },
    };

    // ---- Buddies.

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Eclipsion_makes_the_first_attacks_immune_one_attack_each(bool golden, int immune)
    {
        // A 1/9 Windfury attacks the 3/5 twice in a row: immune for its first attack (golden: both), hurt by the others.
        var result = Fight(Side(Minion(1, 9, Keyword.Windfury, cardId: "A"), Minion(0, 30, cardId: Eclipsion, golden: golden)),
            Side(Minion(3, 5, cardId: "W")));
        Assert.Equal(immune, Count(result, "is immune while attacking (this attack)"));
        Assert.Equal(immune, result.Log.Count(l => l.StartsWith("our A ", StringComparison.Ordinal) && l.EndsWith(" is immune", StringComparison.Ordinal)));
    }

    [Fact]
    public void Eclipsion_keeps_the_first_attacker_alive()
    {
        // A 2/2 kills the 3/1 and takes nothing: 2/2 is left.
        var result = Fight(Side(Minion(2, 2, cardId: "A"), Minion(0, 30, cardId: Eclipsion)), Side(Minion(3, 1, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((2, 2), Stats(result.Ours, "A"));
    }

    [Theory]
    [InlineData(false, 42)] // the Mech 0/1 and the bomb died: 2 × 4
    [InlineData(true, 34)]  // 2 × 8
    public void Squirrel_bomb_deals_damage_for_each_friendly_mech_that_died(bool golden, int left)
    {
        // The 1/50 kills the Mech first (left-most), then the bomb: its Deathrattle hits the 1/50 (the only enemy).
        var result = Fight(Side(Minion(0, 1, cardId: "M", tribes: Tribe.Mech), Minion(0, 1, cardId: SquirrelBomb, tribes: Tribe.Mech | Tribe.Beast, golden: golden)),
            Side(Minion(1, 50, cardId: "W")), oursFirst: false);
        Assert.Equal((1, left), Stats(result.Theirs, "W"));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    public void Mawsworn_soulkeeper_summons_random_undead(bool golden, int count)
    {
        // The 3/100 kills the 0/1: its Deathrattle summons Undead of the pool (each later Deathrattle summon comes after them).
        var result = Fight(Side(Minion(0, 1, cardId: Soulkeeper, tribes: Tribe.Undead, golden: golden)), Side(Minion(3, 100, cardId: "W")), oursFirst: false);
        var summoned = result.Log.Where(l => l.Contains("summoned our", StringComparison.Ordinal)).Take(count).ToList();
        Assert.Equal(count, summoned.Count);
        Assert.All(summoned, line => Assert.Contains(UndeadPool.Minions, m => line.Contains("summoned our " + m.CardId + " ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Brann_epic_egg_summons_and_gets_battlecry_minions(bool golden, int count)
    {
        var result = Fight(Side(Minion(0, 3, Keyword.Taunt, cardId: EpicEgg, golden: golden)), Side(Minion(3, 100, cardId: "W")), oursFirst: false);
        var summoned = result.Log.Where(l => l.Contains("summoned our", StringComparison.Ordinal)).Take(count).ToList();
        Assert.Equal(count, summoned.Count);
        Assert.All(summoned, line => Assert.Contains(BattlecryPool.Minions, m => line.Contains("summoned our " + m.CardId + " ", StringComparison.Ordinal)));
        Assert.Equal(count, Count(result, "we get BG") + Count(result, "we get TB") + Count(result, "we get BGS"));
    }

    [Fact]
    public void Tomb_diver_makes_the_right_most_minion_golden()
    {
        // The 1/1 kills the Taunt diver; Y 3/4 (right-most) turns golden 6/8; X 2/4 then kills the 1/1 and takes 1.
        var result = Fight(Side(Minion(0, 1, Keyword.Taunt, cardId: TombDiver), Minion(2, 4, cardId: "X"), Minion(3, 4, cardId: "Y")),
            Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal(1, result.Outcome);
        Assert.True(result.Ours.Single(m => m.CardId == "Y").Golden);
        Assert.Equal((6, 8), Stats(result.Ours, "Y"));
        Assert.False(result.Ours.Single(m => m.CardId == "X").Golden);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 8)]
    public void Spirit_raptor_calls_upon_the_elements_it_remembers(bool golden, int attack)
    {
        // The 1/1 kills the Taunt raptor; Fire doubles L 2/5 (golden: twice), Water gives R Divine Shield and Taunt; L kills
        // the 1/1 and takes 1.
        var raptor = Minion(0, 1, Keyword.Taunt, cardId: Raptor, tribes: Tribe.Undead | Tribe.Beast, golden: golden) with
        {
            Enchantments = [SpiritRaptor.FireMemory, SpiritRaptor.WaterMemory],
        };
        var result = Fight(Side(raptor, Minion(2, 5, cardId: "L"), Minion(1, 5, cardId: "R")), Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal(1, result.Outcome);
        Assert.Equal((attack, 4), Stats(result.Ours, "L"));
        var right = result.Ours.Single(m => m.CardId == "R");
        Assert.True(right.DivineShield && right.Taunt);
    }

    [Fact]
    public void Spirit_raptor_without_memories_does_nothing()
    {
        var result = Fight(Side(Minion(0, 1, Keyword.Taunt, cardId: Raptor), Minion(2, 5, cardId: "L")), Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal((2, 4), Stats(result.Ours, "L"));
    }

    // ---- Battlecruiser upgrades: the number on the enchantment.

    private static SimMinion Cruiser(string enchantment, SimEnchantment[] values, int attack = 2, int health = 10, Keyword keywords = Keyword.None) =>
        Minion(attack, health, keywords, cardId: Battlecruiser, tribes: Tribe.Mech) with
        {
            Enchantments = values.Length == 0 ? [enchantment] : values.Select(v => v.CardId).ToList(),
            EnchantmentValues = values,
        };

    [Theory]
    [InlineData(new[] { 5 }, 6)]
    [InlineData(new[] { 3, 4 }, 8)] // two upgrades: +3 and +4
    [InlineData(new int[0], 3)]     // no number kept: the first rank's +2
    public void Advanced_ballistics_gives_the_enchantments_number(int[] numbers, int attack)
    {
        // The cruiser's Rally before it kills the 0/1: A 1/10 gets +X Attack.
        var values = numbers.Select(n => new SimEnchantment(AdvancedBallistics.Id) { ScriptData1 = n }).ToArray();
        var result = Fight(Side(Cruiser(AdvancedBallistics.Id, values), Minion(1, 10, cardId: "A")), Side(Minion(0, 1, cardId: "W")));
        Assert.Equal(1, result.Outcome);
        Assert.Equal((attack, 10), Stats(result.Ours, "A"));
    }

    [Theory]
    [InlineData(4, 4, 4, 8)]
    [InlineData(0, 0, 1, 5)] // no number: +1/+1
    public void Caduceus_reactor_buffs_the_left_most_minion(int number1, int number2, int attack, int health)
    {
        // The 1/1 kills the Taunt cruiser; A 0/5 gets +X/+X, then kills the 1/1 and takes 1.
        var values = number1 > 0 ? new[] { new SimEnchantment(CaduceusReactor.Id) { ScriptData1 = number1, ScriptData2 = number2 } } : [];
        var cruiser = Cruiser(CaduceusReactor.Id, values, attack: 0, health: 1, Keyword.Taunt);
        var result = Fight(Side(cruiser, Minion(0, 5, cardId: "A")), Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal((attack, health), Stats(result.Ours, "A"));
    }

    // ---- Dragon's Eye.

    [Theory]
    [InlineData(Tribe.Dragon, true, 2)]
    [InlineData(Tribe.Dragon, false, 1)]
    [InlineData(Tribe.Quilboar, true, 1)]
    public void Dragons_eye_makes_a_dragons_battlecry_trigger_an_extra_time(Tribe tribe, bool eye, int triggers)
    {
        // Timewarped Greenskeeper's Rally triggers the right-most Battlecry once; it kills the 0/5 with that attack.
        var keeper = Minion(5, 9, cardId: "BG34_Giant_041", tribes: Tribe.Dragon);
        var ours = Side(keeper, Minion(0, 5, cardId: "CRY", tribes: tribe, mechanics: CardMechanic.Battlecry));
        if (eye)
        {
            ours = ours with { Extras = ["BG36_MagicItem_215"] };
        }

        var result = Fight(ours, Side(Minion(0, 5, cardId: "W")), true, Effects(typeof(TimewarpedGreenskeeper), typeof(DragonsEye)));
        Assert.Equal(triggers, Count(result, "battlecry of our CRY"));
    }

    // ---- "After N …, get a random X."

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void Lucky_tabby_gets_a_card_when_its_count_runs_out(int left, int cards)
    {
        // The 1/50 kills the three 0/1s: three friendly deaths.
        var ours = With(Side(Minion(0, 1, cardId: "X"), Minion(0, 1, cardId: "Y"), Minion(0, 1, cardId: "Z")), "BG30_MagicItem_931", left);
        var result = Fight(ours, Side(Minion(1, 50, cardId: "W")), oursFirst: false);
        Assert.Equal(cards, Count(result, RandomCard));
    }

    [Fact]
    public void Wildfeather_duster_counts_beasts_summoned_in_combat()
    {
        // The rat's Turtle is a Beast summoned in combat: with 1 left, a card.
        var ours = With(Side(Tokens.SewerRat()), "BG35_MagicItem_700", 1);
        var result = Fight(ours, Side(Minion(1, 50, cardId: "W")), oursFirst: false);
        Assert.Equal(1, Count(result, RandomCard));
    }

    [Fact]
    public void Our_progress_card_is_read_and_the_opponents_needs_its_estimate()
    {
        var ours = With(Side(Minion(1, 1)), "BG30_MagicItem_931");
        Assert.Empty(CombatSimulator.Explain(ours, isOpponent: false));

        var theirs = Side(Minion(1, 1)) with { Extras = ["BG30_MagicItem_931"] };
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(theirs, isOpponent: true)).Reason);

        var estimates = SimEstimates.For(theirs, isOpponent: true, new Dictionary<string, int>(StringComparer.Ordinal), local: null);
        var estimate = Assert.Single(estimates, e => e.Input == SimInputs.ProgressLeft);
        Assert.Equal(6, estimate.Value1);
        Assert.Empty(CombatSimulator.Explain(theirs with { Estimates = estimates }, isOpponent: true));
    }

    [Theory]
    [InlineData(1, 1)] // one Boar died before: 2 left, the second death here completes three
    [InlineData(0, 0)] // 3 left: the third death has no living Boar to trigger
    public void Timewarped_boar_gets_a_card_on_every_third_boar_death(int deadBefore, int cards)
    {
        const string boar = TimewarpedBoar.Id;
        var ours = Side(Minion(0, 1, cardId: boar, tribes: Tribe.Beast), Minion(0, 1, cardId: boar, tribes: Tribe.Beast), Minion(0, 10, cardId: boar, tribes: Tribe.Beast));
        if (deadBefore > 0)
        {
            ours = ours with { PlayerEnchantments = [new SimEnchantment(TimewarpedBoar.DeadBoarsEnchantment) { ScriptData1 = deadBefore }] };
        }

        var result = Fight(ours, Side(Minion(1, 50, cardId: "W")), oursFirst: false);
        Assert.Equal(cards, Count(result, RandomCard));
    }

    [Fact]
    public void Ill_take_that_gets_a_copy_of_the_first_kill_only()
    {
        // A 5/5 kills both 0/1s; only the first one is got. The use of the power is an estimate on both sides.
        var ours = Side(Minion(5, 5, cardId: "A")) with { Extras = [IllTakeThat.Id] };
        var estimates = SimEstimates.For(ours, isOpponent: false, new Dictionary<string, int>(StringComparer.Ordinal), local: null);
        Assert.Contains(estimates, e => e.Input == SimInputs.HeroPowerUsed);
        ours = ours with { Estimates = estimates };
        Assert.Empty(CombatSimulator.Explain(ours, isOpponent: false));

        var result = Fight(ours, Side(Minion(0, 1, cardId: "W1"), Minion(0, 1, cardId: "W2")));
        Assert.Equal(1, Count(result, "we get W"));
    }

    [Fact]
    public void Lock_and_load_waits_with_a_full_board_and_is_missing_input_then()
    {
        var space = Side(Minion(1, 1)) with { Extras = ["BG22_HERO_000p_Alt"] };
        Assert.Empty(CombatSimulator.Explain(space, isOpponent: false));

        var full = Side(Enumerable.Range(0, 7).Select(_ => Minion(1, 1)).ToArray()) with { Extras = ["BG22_HERO_000p_Alt"] };
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(full, isOpponent: true)).Reason);
    }

    // ---- "Your Undead have +X Attack this game" on Undead summoned in combat.

    [Fact]
    public void Undead_summoned_in_combat_get_the_players_undead_attack()
    {
        // The 1/1 kills Harmless Bonehead: two 1/1 Skeletons + 5 Attack (6/1); the first trades with the 1/1.
        var ours = Side(Minion(0, 1, cardId: "BG28_300", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle)) with
        {
            PlayerEnchantments = [new SimEnchantment(SimInputs.UndeadArmyPlayerEnchantment) { Attack = 5 }],
        };
        var result = Fight(ours, Side(Minion(1, 1, cardId: "W")), oursFirst: false);
        Assert.Equal(1, result.Outcome);
        Assert.Equal((6, 1), Stats(result.Ours, Tokens.SkeletonId));
    }

    [Fact]
    public void The_opponents_undead_attack_is_read_from_their_undead_on_the_board()
    {
        var undead = Minion(4, 2, cardId: "U", tribes: Tribe.Undead) with
        {
            Enchantments = [SimInputs.UndeadArmyEnchantment],
            EnchantmentValues = [new SimEnchantment(SimInputs.UndeadArmyEnchantment) { Attack = 3 }],
        };
        Assert.Equal(3, SimInputs.UndeadAttackBonus(Side(undead, Minion(1, 1))));
        Assert.Equal(0, SimInputs.UndeadAttackBonus(Side(Minion(1, 1))));
    }
}

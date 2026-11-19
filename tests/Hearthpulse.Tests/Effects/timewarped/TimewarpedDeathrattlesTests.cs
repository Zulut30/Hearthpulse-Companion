using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>
/// Timewarped deathrattles (texts from HearthstoneJSON enUS latest). Boards worked out by hand in each comment: unless
/// said otherwise their Killer attacks first and trades with our Taunt minion, then only their 0-Attack Wall is left, so
/// the survivors are the board right after the deathrattle (a tie at the attack cap).
/// </summary>
public sealed class TimewarpedDeathrattlesTests
{
    private const string Skeleton = "BG_ICC_026t 1/1";

    private static SimMinion Dr(int attack, int health, string cardId, Tribe tribe, Keyword keywords = Keyword.Taunt, bool golden = false) =>
        Minion(attack, health, keywords, cardId, tribe, golden, CardMechanic.Deathrattle);

    [Fact]
    public void Busker_deathrattle_changes_nothing_but_counts_as_a_trigger()
    {
        // "Gain 1 Gold next turn": no board change, yet Death's Embrace (+2/+2 per trigger) on G sees it: 2/3 → 4/5.
        var gifted = Minion(2, 3, cardId: "G") with { Enchantments = ["BG36_MidGameEffect_000t29e"] };
        var result = Fight(Side(Dr(4, 2, "BG34_Giant_001", Tribe.Pirate), gifted), Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedBusker), typeof(DeathsEmbraceGreater)));

        Assert.Equal(0, result.Outcome);
        Assert.Equal(["G 4/5"], Board(result.Ours));
        Assert.Single(result.Lines("deathrattle of our BG34_Giant_001"));
    }

    [Fact]
    public void Caretaker_summons_five_skeletons()
    {
        // Killer 5/1 and the 5/5 Caretaker trade; five Skeletons fill its slot.
        var result = Fight(Side(Dr(5, 5, "BG34_Giant_618", Tribe.Undead)), Side(Killer(5, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedCaretaker)));

        Assert.Equal(Times(5, Skeleton), Board(result.Ours));
    }

    [Fact]
    public void Skeletons_that_do_not_fit_give_the_undead_attack()
    {
        // Six of ours; after the Caretaker dies five are left, so two Skeletons fit and three do not: the Undead get +3
        // Attack (the two Skeletons 4/1, U 1/5 → 4/5); the non-Undead A–D stay 0/5.
        var result = Fight(
            Side(Dr(5, 5, "BG34_Giant_618", Tribe.Undead), Minion(1, 5, cardId: "U", tribes: Tribe.Undead),
                 Minion(0, 5, cardId: "A"), Minion(0, 5, cardId: "B"), Minion(0, 5, cardId: "C"), Minion(0, 5, cardId: "D")),
            Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(TimewarpedCaretaker)));

        Assert.Equal(["BG_ICC_026t 4/1", "BG_ICC_026t 4/1", "U 4/5", "A 0/5", "B 0/5", "C 0/5", "D 0/5"], Board(result.Ours));
    }

    [Fact]
    public void Golden_caretaker_summons_ten_skeletons_and_the_three_left_over_give_attack()
    {
        // Golden: ten Skeletons, seven fit, three give the Undead +3 Attack: seven 4/1 Skeletons.
        var result = Fight(Side(Dr(10, 10, "BG34_Giant_618", Tribe.Undead, golden: true)), Side(Killer(10, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedCaretaker)));

        Assert.Equal(Times(7, "BG_ICC_026t 4/1"), Board(result.Ours));
    }

    [Fact]
    public void Icky_imp_summons_imps_with_its_attack_and_maximum_health()
    {
        // Buffed to 15/20, it takes 30 and dies: two Imps 15/20 (its maximum Health, not the -10 it ended on).
        var imp = Dr(12, 12, "BG34_Giant_674", Tribe.Demon) with { Attack = 15, Health = 20, MaxHealth = 20 };
        var result = Fight(Side(imp), Side(Killer(30, 1), Wall), oursFirst: false, Only(typeof(TimewarpedIckyImp)));

        Assert.Equal(Times(2, "BG_BRM_006t 15/20"), Board(result.Ours));
    }

    [Fact]
    public void Golden_icky_imp_summons_four()
    {
        var result = Fight(Side(Dr(24, 24, "BG34_Giant_674", Tribe.Demon, golden: true)), Side(Killer(30, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedIckyImp)));

        Assert.Equal(Times(4, "BG_BRM_006t 24/24"), Board(result.Ours));
    }

    [Fact]
    public void Lil_quilboar_plays_three_blood_gems_on_every_quilboar()
    {
        // A Blood Gem gives +1/+1 plus the player's bonus tags (here +2/+1): +3/+2 each, three Gems +9/+6 on Q only.
        var ours = Side(Dr(5, 5, "BG34_Giant_608", Tribe.Quilboar), Minion(1, 10, cardId: "Q", tribes: Tribe.Quilboar), Minion(1, 10, cardId: "N"))
            with { Tags = new Dictionary<string, int> { ["BACON_BLOODGEMBUFFATKVALUE"] = 2, ["BACON_BLOODGEMBUFFHEALTHVALUE"] = 1 } };
        var result = Fight(ours, Side(Killer(5, 1), Wall), oursFirst: false, Only(typeof(TimewarpedLilQuilboar)));

        Assert.Equal(["Q 10/16", "N 1/10"], Board(result.Ours));
    }

    [Fact]
    public void Jazzer_makes_later_blood_gems_give_more_health()
    {
        // Their A attacks first and picks the first Taunt, the Jazzer (5/3): both die, Blood Gems now give +1/+3. Our
        // Lil' Quilboar then attacks B (pick 0): both die, three Gems on Q 1/10: +3/+9 → 4/19.
        var ours = Side(Dr(5, 3, "BG34_Giant_306", Tribe.Quilboar), Dr(5, 5, "BG34_Giant_608", Tribe.Quilboar),
                        Minion(1, 10, cardId: "Q", tribes: Tribe.Quilboar));
        var result = Fight(ours, Side(Minion(5, 1, cardId: "A"), Minion(5, 1, cardId: "B"), Wall), oursFirst: false,
                           Only(typeof(TimewarpedJazzer), typeof(TimewarpedLilQuilboar)), 0, 0);

        Assert.Equal(["Q 4/19"], Board(result.Ours));
        Assert.True(result.IndexOf("deathrattle of our BG34_Giant_306") < result.IndexOf("deathrattle of our BG34_Giant_608"));
    }
    private static SimMinion Bonehead => Minion(1, 1, cardId: Ids.Bonehead, tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    private static SimMinion Warghoul(bool golden = false) => Dr(golden ? 18 : 9, golden ? 6 : 3, "BG34_Giant_331", Tribe.Undead, golden: golden);

    [Fact]
    public void Warghoul_triggers_the_deathrattle_of_the_neighbour_that_has_one()
    {
        // Killer 9/1 and the Taunt Warghoul 9/3 trade. Of its neighbours only the Bonehead has a Deathrattle: it triggers
        // while the Bonehead lives, two Skeletons to its right (where the Warghoul stood).
        var result = Fight(Side(Bonehead, Warghoul(), Minion(0, 5, cardId: "X")), Side(Killer(9, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TimewarpedWarghoul)));

        Assert.Equal(["BG28_300 1/1", Skeleton, Skeleton, "X 0/5"], Board(result.Ours));
    }

    [Fact]
    public void Warghoul_picks_one_neighbour_at_random()
    {
        // Both neighbours are Boneheads: the Killer's target is pick 0, the Warghoul's choice pick 1, the right one.
        var result = Fight(Side(Bonehead, Warghoul(), Bonehead), Side(Killer(9, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TimewarpedWarghoul)), 0, 1);

        Assert.Equal(["BG28_300 1/1", "BG28_300 1/1", Skeleton, Skeleton], Board(result.Ours));
    }

    [Fact]
    public void Golden_warghoul_triggers_both_neighbours_left_first()
    {
        var result = Fight(Side(Bonehead, Warghoul(golden: true), Bonehead), Side(Killer(9, 1), Wall), oursFirst: false,
                           Only(typeof(HarmlessBonehead), typeof(TimewarpedWarghoul)));

        Assert.Equal(["BG28_300 1/1", Skeleton, Skeleton, "BG28_300 1/1", Skeleton, Skeleton], Board(result.Ours));
    }

    [Fact]
    public void Warghoul_never_triggers_another_warghoul()
    {
        // The left neighbour is a second (non-Taunt) Warghoul: excluded, so nothing triggers.
        var other = Minion(9, 3, cardId: "BG34_Giant_331", tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);
        var result = Fight(Side(other, Warghoul()), Side(Killer(9, 1), Wall), oursFirst: false, Only(typeof(TimewarpedWarghoul)));

        Assert.Single(result.Lines("deathrattle of our BG34_Giant_331"));
        Assert.Equal(["BG34_Giant_331 9/3"], Board(result.Ours));
    }
    [Fact]
    public void Tide_razor_summons_four_random_pirates_up_to_the_tavern_tier()
    {
        // Tavern tier 1: the pool's Tier 1 Pirates are Southsea Busker and Aureate Laureate. The Killer's target is the
        // first pick; then Busker, Laureate, Laureate, Busker, left to right in the Razor's slot.
        var busker = PiratePool.IndexOf("BG26_135", 1);
        var laureate = PiratePool.IndexOf("BG32_236", 1);
        var result = Fight(Side(Dr(12, 8, "BG34_Giant_328", Tribe.None)) with { TavernTier = 1 }, Side(Killer(12, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedTideRazor)), 0, busker, laureate, laureate, busker);

        Assert.Equal(["BG26_135 3/1", "BG32_236 2/2 DivineShield", "BG32_236 2/2 DivineShield", "BG26_135 3/1"], Board(result.Ours));
        Assert.Equal(["we get BG26_135", "we get BG32_236", "we get BG32_236", "we get BG26_135"], result.Lines("we get "));
        Assert.Equal(2, PiratePool.Minions.Count(m => m.TechLevel <= 1));
    }

    [Fact]
    public void Golden_tide_razor_fills_the_board()
    {
        // Eight Pirates, seven fit; all eight are got.
        var result = Fight(Side(Dr(24, 16, "BG34_Giant_328", Tribe.None, golden: true)), Side(Killer(24, 1), Wall), oursFirst: false,
                           Only(typeof(TimewarpedTideRazor)));

        Assert.Equal(7, result.Ours.Count);
        Assert.Equal(8, result.Lines("we get ").Count);
        Assert.All(result.Ours, m => Assert.True((m.Tribes & Tribe.Pirate) != 0));
    }

    [Fact]
    public void The_pirate_pool_is_the_card_data_of_the_relevance_table_patch()
    {
        Assert.Equal(PiratePool.Patch, CombatRelevance.Patch);
        Assert.Equal(-1, PiratePool.IndexOf("BG36_360")); // Dark Paradox: its type changes every game
        Assert.All(PiratePool.Minions, m => Assert.True((m.Tribes & Tribe.Pirate) != 0));
    }
}

using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

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
}

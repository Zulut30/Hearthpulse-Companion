using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Engine rules that a Stryker run on Simulation/* (2026-11-19, stryker-sim.md in the task folder) changed without a test
/// failing: target choice with Stealth, Venomous spent on the first kill, who attacks first, which side's deaths resolve
/// first by player id, Mega-Windfury, and summon buffs on maximum Health. Boards are worked out by hand in each comment.
/// </summary>
public sealed class CombatEngineHardeningTests
{
    private static List<string> Attacks(FightResult result) => result.Log.Where(l => l.Contains(" attacks ", StringComparison.Ordinal)).ToList();

    // ------------------------------------------------------------------------------------------- stealth

    [Fact]
    public void A_visible_minion_is_attacked_before_a_stealthed_one()
    {
        // Pick 0 would be the stealthed S if it could be chosen; only P can.
        var result = Fight(Side(Minion(1, 50, cardId: "A")), Side(Minion(0, 5, Keyword.Stealth, "S"), Minion(0, 5, cardId: "P")), oursFirst: true);

        Assert.Contains("attacks their P ", Attacks(result)[0]);
    }

    [Fact]
    public void A_stealthed_taunt_does_not_pull_attacks_from_a_visible_minion()
    {
        var result = Fight(Side(Minion(1, 50, cardId: "A")), Side(Minion(0, 5, Keyword.Stealth | Keyword.Taunt, "ST"), Minion(0, 5, cardId: "P")), oursFirst: true);

        Assert.Contains("attacks their P ", Attacks(result)[0]);
    }

    [Fact]
    public void When_only_stealthed_minions_are_left_their_taunt_is_attacked_first()
    {
        // Nothing visible: Stealth no longer protects, and Taunt counts again (pick 0 would be S without it).
        var result = Fight(Side(Minion(1, 50, cardId: "A")), Side(Minion(0, 5, Keyword.Stealth, "S"), Minion(0, 5, Keyword.Stealth | Keyword.Taunt, "ST")), oursFirst: true);

        Assert.Contains("attacks their ST ", Attacks(result)[0]);
    }

    [Fact]
    public void When_only_stealthed_minions_are_left_any_of_them_can_be_attacked()
    {
        var result = Fight(Side(Minion(1, 50, cardId: "A")), Side(Minion(0, 5, Keyword.Stealth, "S1"), Minion(0, 5, Keyword.Stealth, "S2")), oursFirst: true, picks: [1]);

        Assert.Contains("attacks their S2 ", Attacks(result)[0]);
    }

    [Fact]
    public void A_minion_that_never_attacked_keeps_its_stealth_after_the_fight()
    {
        var result = Fight(Side(Minion(5, 5, cardId: "A"), Minion(0, 5, Keyword.Stealth, "S")), Side(Minion(1, 1, cardId: "E")), oursFirst: true);

        Assert.Equal(1, result.Outcome);
        Assert.True(Assert.Single(result.Ours, m => m.CardId == "S").Stealth);
    }

    // ------------------------------------------------------------------------------------------- venomous

    [Fact]
    public void Venomous_is_spent_on_the_first_minion_it_damages()
    {
        // Our V 1/20 Venomous kills A outright; B 1/5 then needs five hits of 1 from V (its attacks and its blows back).
        var result = Fight(Side(Minion(1, 20, Keyword.Venomous, "V")), Side(Minion(1, 5, cardId: "A"), Minion(1, 5, cardId: "B")), oursFirst: true);

        Assert.Equal(1, result.Outcome);
        Assert.Equal(5, result.Lines("their B ").Count(l => l.Contains(" takes 1 from our V", StringComparison.Ordinal)));
        Assert.False(Assert.Single(result.Ours).Venomous);
    }

    // ------------------------------------------------------------------------------------------- first attacker

    [Theory]
    [InlineData(true, 1)] // we have more minions: we attack first, even though the coin (pick 1) says them
    [InlineData(false, 0)] // they have more: they attack first, even though the coin (pick 0) says us
    public void Unknown_first_attacker_is_the_side_with_more_minions(bool oursBigger, int coin)
    {
        SimSide two = Side(Minion(1, 50, cardId: "X"), Minion(1, 50, cardId: "Y")), one = Side(Minion(1, 50, cardId: "Z"));
        var result = Fight(oursBigger ? two : one, oursBigger ? one : two, oursFirst: null, picks: [coin]);

        Assert.StartsWith(oursBigger ? "our " : "their ", Attacks(result)[0]);
    }

    [Fact]
    public void The_logged_first_attacker_starts_the_fight()
    {
        var ours = Side(Minion(1, 50, cardId: "X") with { EntityId = 11 }, Minion(1, 50, cardId: "Y") with { EntityId = 12 });
        var result = CombatSimulator.RunSingle(ours, Side(Minion(1, 50, cardId: "Z")), true, new ScriptedRandom(), firstAttackerEntityId: 12);

        Assert.StartsWith("our Y ", Attacks(result)[0]);
        Assert.StartsWith("our X ", Attacks(result)[2]);
    }

    [Fact]
    public void A_logged_first_attacker_that_is_not_on_the_board_leaves_the_usual_order()
    {
        var ours = Side(Minion(1, 50, cardId: "X") with { EntityId = 11 }, Minion(1, 50, cardId: "Y") with { EntityId = 12 });
        var result = CombatSimulator.RunSingle(ours, Side(Minion(1, 50, cardId: "Z")), true, new ScriptedRandom(), firstAttackerEntityId: 99);

        Assert.StartsWith("our X ", Attacks(result)[0]);
    }

    // ------------------------------------------------------------------------------------------- simultaneous deaths

    private static SimMinion Bonehead => Minion(1, 1, cardId: Ids.Bonehead, tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle);

    private static int OursBeforeTheirs(int ourPlayerId, int theirPlayerId, bool oursFirst)
    {
        // Bonehead 1/1 against Bonehead 1/1: both die in one attack, and both deathrattles resolve in one death phase.
        var result = Fight(Side(Bonehead) with { PlayerId = ourPlayerId }, Side(Bonehead) with { PlayerId = theirPlayerId }, oursFirst,
                           Only(typeof(HarmlessBonehead)));
        return result.IndexOf("deathrattle of our BG28_300").CompareTo(result.IndexOf("deathrattle of their BG28_300"));
    }

    [Fact]
    public void Simultaneous_deaths_resolve_for_the_player_who_entered_the_game_first()
    {
        Assert.True(OursBeforeTheirs(7, 3, oursFirst: true) > 0); // they have the lower id: theirs first though we attacked
        Assert.True(OursBeforeTheirs(3, 7, oursFirst: false) < 0);
    }

    [Fact]
    public void Without_both_player_ids_simultaneous_deaths_resolve_for_the_attacking_side_first()
    {
        Assert.True(OursBeforeTheirs(0, 5, oursFirst: false) > 0);
        Assert.True(OursBeforeTheirs(5, 0, oursFirst: true) < 0);
        Assert.True(OursBeforeTheirs(4, 4, oursFirst: false) > 0);
    }

    // ------------------------------------------------------------------------------------------- windfury

    [Fact]
    public void Mega_windfury_attacks_four_times_a_turn()
    {
        Assert.Equal(4, Minion(1, 1, Keyword.MegaWindfury).AttacksPerTurn);
        Assert.Equal(2, Minion(1, 1, Keyword.Windfury).AttacksPerTurn);
        Assert.Equal(1, Minion(1, 1).AttacksPerTurn);

        // Four swings in a row before the enemy's first.
        var result = Fight(Side(Minion(1, 50, Keyword.MegaWindfury, "M")), Side(Minion(1, 50, cardId: "E")), oursFirst: true);
        Assert.All(Attacks(result).Take(4), line => Assert.StartsWith("our M ", line));
        Assert.StartsWith("their E ", Attacks(result)[4]);
    }

    // ------------------------------------------------------------------------------------------- summon buffs

    [Fact]
    public void A_summon_buff_raises_maximum_health_as_well_as_health()
    {
        // Hammer of Twilight (greater, BG36_MagicItem_403t): summoned minions get +2/+1. Our Bonehead hits their 1/3 and
        // dies; its two Skeletons come as 3/2. Their 1/2 hits a Skeleton (3/1 left) and dies. Both Skeletons have
        // maximum Health 2.
        var ours = Side(Bonehead) with { Extras = ["BG36_MagicItem_403t"] };
        var result = Fight(ours, Side(Minion(1, 3, cardId: "E")), oursFirst: true, Only(typeof(HarmlessBonehead), typeof(HammerOfTwilight)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(2, result.Ours.Count);
        Assert.All(result.Ours, skeleton => Assert.Equal((3, 2), (skeleton.Attack, skeleton.MaxHealth)));
    }
}

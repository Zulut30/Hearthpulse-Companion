using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Rewards;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Timewarped;

/// <summary>
/// Timewarped Nellie's Ship and Jungle King, the Beetle bonus of Turquoise Skitterer and Ravaging Scorpid from their script
/// data, and Rallying Cry (TASK-2026-11-19 E5). Results are worked out by hand from the card text in each comment.
/// </summary>
public sealed class LeftoverTimewarpedTests
{
    private const string Ship = "BG34_Giant_074t";

    private static SimMinion ShipMinion(bool golden = false) =>
        Minion(golden ? 4 : 2, golden ? 12 : 6, cardId: Ship, tribes: Tribe.Beast | Tribe.Pirate, golden: golden, mechanics: CardMechanic.Deathrattle) with { EntityId = 77 };

    [Fact]
    public void Our_ship_summons_and_gets_its_recorded_crew()
    {
        // The crew P 3/4 is summoned in the ship's slot and a copy is added to the hand.
        var crewed = ShipMinion() with { Crew = [Minion(3, 4, cardId: "P", tribes: Tribe.Pirate)] };
        var result = Fight(Side(crewed), Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(TimewarpedNelliesShip)));

        Assert.Single(result.Lines("summoned our P 3/4"));
        Assert.Contains("we get P", result.Log);
    }

    [Fact]
    public void Our_ship_without_a_crew_summons_nothing()
    {
        var result = Fight(Side(ShipMinion()), Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(TimewarpedNelliesShip)));
        Assert.Empty(result.Lines("summoned our "));
    }

    [Fact]
    public void The_opponents_ship_crew_is_an_estimated_pirate_up_to_their_tier()
    {
        // Their golden ship's crew is hidden: two random Pirates of their Tavern tier or lower, each summoned and got.
        var theirs = Side(ShipMinion(golden: true)) with { TavernTier = 2 };
        var result = Fight(Side(Minion(10, 100, cardId: "K")), theirs, true, Effects(typeof(TimewarpedNelliesShip)));

        var summoned = result.Lines("summoned their ");
        Assert.Equal(2, summoned.Count);
        Assert.Equal(2, result.Log.Count(l => l.StartsWith("they get ", StringComparison.Ordinal)));
        Assert.All(summoned, line => Assert.Contains(PiratePool.Minions, p => p.TechLevel <= 2 && line.StartsWith($"summoned their {p.CardId} ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Jungle_king_gives_its_improved_bonus_from_script_data()
    {
        // Their 10/100 kills MAKER (the Stealth King cannot be attacked), whose deathrattle summons a 1/1 Beast. The King's
        // script data 7/5 (its bonus improved by spells this game) makes it 8/6, and it attacks next as that; with no script
        // data the printed +4/+3 makes it 5/4.
        var king = Minion(8, 12, Keyword.Stealth, "BG34_PreMadeChamp_004", Tribe.Beast) with { ScriptData1 = 7, ScriptData2 = 5 };
        var beastMaker = Minion(0, 1, cardId: "MAKER");
        var registry = Effects(typeof(TimewarpedJungleKing), typeof(BeastOnDeath));

        var result = Fight(Side(beastMaker, king), Side(Minion(10, 100, cardId: "E")), false, registry);
        Assert.Contains(result.Log, l => l.StartsWith("our BEAST 8/6 attacks", StringComparison.Ordinal));

        var plain = Fight(Side(beastMaker, king with { ScriptData1 = 0, ScriptData2 = 0 }), Side(Minion(10, 100, cardId: "E")), false, registry);
        Assert.Contains(plain.Log, l => l.StartsWith("our BEAST 5/4 attacks", StringComparison.Ordinal));
    }

    [Fact]
    public void Skitterer_and_scorpid_bonus_comes_from_script_data_three_and_four()
    {
        // Skitterer shows Beetle 2/2 (NUM_1/2) and "+7/+6" (NUM_3/4): its deathrattle gives Beetles +7/+6, then summons the
        // 2/2 Beetle as 9/8.
        var skitterer = Minion(5, 5, cardId: "BG31_809", tribes: Tribe.Beast, mechanics: CardMechanic.Deathrattle)
            with { ScriptData1 = 2, ScriptData2 = 2, ScriptData3 = 7, ScriptData4 = 6 };
        var result = Fight(Side(skitterer), Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(TurquoiseSkitterer)));
        Assert.Single(result.Lines("summoned our " + Tokens.BeetleId + " 9/8"));

        // Without NUM_3/4 the printed +5/+5: 7/7.
        var printed = Fight(Side(skitterer with { ScriptData3 = 0, ScriptData4 = 0 }), Side(Minion(10, 100, cardId: "E")), false, Effects(typeof(TurquoiseSkitterer)));
        Assert.Single(printed.Lines("summoned our " + Tokens.BeetleId + " 7/7"));
    }

    [Fact]
    public void Scorpid_gives_its_shown_bonus_after_each_friendly_attack()
    {
        // Our Beetle 2/2 attacks their 0/50; then the Scorpid's "+3/+2" (NUM_3/4) makes it 5/4.
        var scorpid = Minion(6, 7, cardId: "BG36_209", tribes: Tribe.Beast, mechanics: CardMechanic.Deathrattle) with { ScriptData3 = 3, ScriptData4 = 2 };
        var beetle = Minion(2, 2, cardId: Tokens.BeetleId, tribes: Tribe.Beast);
        var result = Fight(Side(beetle, Minion(0, 9, cardId: "X"), scorpid), Side(Minion(0, 2, cardId: "W")), true, Effects(typeof(RavagingScorpid)));

        Assert.Equal((5, 4), result.Ours.Where(m => m.CardId == Tokens.BeetleId).Select(m => (m.Attack, m.Health)).Single());
    }

    [Fact]
    public void Rallying_cry_triggers_each_rally_an_extra_time()
    {
        // Roadboar ("Rally: Get a Blood Gem") attacks once into the 0/1 wall: one Blood Gem, two with Rallying Cry.
        SimSide Ours() => Side(Minion(4, 4, cardId: "BG20_101", tribes: Tribe.Quilboar, mechanics: CardMechanic.Rally));
        var registry = Effects(typeof(GetOnRally), typeof(RallyingCry));

        Assert.Single(Fight(Ours(), Side(Minion(0, 1, cardId: "W")), true, registry).Lines("we get BG20_GEM"));
        var result = Fight(Ours() with { Extras = ["BG33_Reward_021"] }, Side(Minion(0, 1, cardId: "W")), true, registry);
        Assert.Equal(2, result.Lines("we get BG20_GEM").Count);
    }

    /// <summary>Test-only: "Deathrattle: Summon a 1/1 Beast" (stands in for any Beast summon).</summary>
    [CardEffect("MAKER")]
    private sealed class BeastOnDeath : CombatEffect
    {
        public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Tokens.Make("BEAST", 1, 1, Tribe.Beast));
    }
}

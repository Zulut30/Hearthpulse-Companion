using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using static Hearthpulse.Tests.Sim.Deaths.DeathsTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;
using Ids = Hearthpulse.Tests.Sim.Deaths.Ids;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// <see cref="Fight.GiveKeywords"/>, <see cref="Fight.RemoveKeywords"/> and aura changes during a fight, through test-only
/// effects that act at Start of Combat. Survivors of a Stryker run on Simulation/* (stryker-sim.md in the task folder):
/// no test removed Divine Shield, Poisonous, Venomous, Stealth or Windfury, gave Poisonous, Stealth or Windfury, or gave an
/// aura to a minion summoned mid-fight. Boards with 0 Attack never attack, so the fight stalls into a tie and the
/// survivors show the keywords as they were left.
/// </summary>
public sealed class KeywordAndAuraEngineTests
{
    private const Keyword All = Keyword.Taunt | Keyword.DivineShield | Keyword.Poisonous | Keyword.Venomous | Keyword.Reborn
                                | Keyword.Stealth | Keyword.MegaWindfury;

    private static FightResult Stall(SimMinion ours, SimMinion theirs, params Type[] effects) =>
        Fight(Side(ours), Side(theirs), oursFirst: true, Only(effects));

    [Fact]
    public void Removing_every_keyword_leaves_none()
    {
        var result = Stall(Minion(0, 5, cardId: "STRIP_ALL"), Minion(0, 5, All, "T"), typeof(StripAll));

        Assert.Equal(0, result.Outcome);
        var target = Assert.Single(result.Theirs);
        Assert.Equal(Keyword.None, target.Keywords);
        Assert.Equal(1, target.AttacksPerTurn);
    }

    [Fact]
    public void Removing_one_keyword_keeps_the_others()
    {
        var result = Stall(Minion(0, 5, cardId: "STRIP_TAUNT"), Minion(0, 5, All, "T"), typeof(StripTaunt));

        Assert.Equal(All & ~Keyword.Taunt, Assert.Single(result.Theirs).Keywords);
        Assert.Equal(4, Assert.Single(result.Theirs).AttacksPerTurn);
    }

    [Fact]
    public void Removing_windfury_also_removes_mega_windfury()
    {
        var result = Stall(Minion(0, 5, cardId: "STRIP_WINDFURY"), Minion(0, 5, Keyword.MegaWindfury | Keyword.Taunt, "T"), typeof(StripWindfury));

        var target = Assert.Single(result.Theirs);
        Assert.Equal(Keyword.Taunt, target.Keywords);
        Assert.Equal(1, target.AttacksPerTurn);
    }

    [Fact]
    public void Giving_every_keyword_gives_each_of_them()
    {
        var result = Stall(Minion(0, 5, cardId: "GIVE_ALL"), Minion(0, 5, cardId: "T"), typeof(GiveAll));

        var target = Assert.Single(result.Theirs);
        Assert.Equal(All, target.Keywords);
        Assert.Equal(4, target.AttacksPerTurn);
    }

    [Fact]
    public void Giving_windfury_does_not_lower_mega_windfury()
    {
        var result = Stall(Minion(0, 5, cardId: "GIVE_WINDFURY"), Minion(0, 5, Keyword.MegaWindfury, "T"), typeof(GiveWindfury));

        Assert.Equal(4, Assert.Single(result.Theirs).AttacksPerTurn);
    }

    [Fact]
    public void A_minion_summoned_next_to_an_aura_gets_its_stats()
    {
        // AURA (0/10) gives other friendly minions +2/+2; the captured board already includes it, so only minions summoned
        // in the fight gain it. Our Bonehead hits their E 1/4 (1/3 left) and dies: two Skeletons, 1/1 + 2/2 = 3/3. Their E
        // attacks AURA (pick 0), and our first Skeleton kills E and takes 1. One Skeleton is untouched at 3/3.
        var result = Fight(Side(Minion(0, 10, cardId: "AURA"), Minion(1, 1, cardId: Ids.Bonehead, tribes: Tribe.Undead, mechanics: CardMechanic.Deathrattle)),
                           Side(Minion(1, 4, cardId: "E")), oursFirst: true, Only(typeof(HarmlessBonehead), typeof(EngineTests.AuraPlusTwo)));

        Assert.Equal(1, result.Outcome);
        var skeletons = result.Ours.Where(m => m.CardId != "AURA").ToList();
        Assert.Equal(2, skeletons.Count);
        Assert.All(skeletons, s => Assert.Equal((3, 3), (s.Attack, s.MaxHealth)));
        Assert.Contains(skeletons, s => s.Health == 3);
        Assert.Contains(skeletons, s => s.Health == 2);
    }

    [Fact]
    public void Losing_an_aura_lowers_health_only_down_to_the_new_maximum()
    {
        // Their B is captured as 2/5 with AURA's +2/+2 in it. Our A 1/50 kills AURA 0/1 (pick 0): B drops to 0/3 (Health
        // 5 → min(5, 3)), cannot attack, and dies to three hits of A.
        var result = Fight(Side(Minion(1, 50, cardId: "A")), Side(Minion(0, 1, cardId: "AURA"), Minion(2, 5, cardId: "B")),
                           oursFirst: true, Only(typeof(EngineTests.AuraPlusTwo)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal(3, result.Lines("their B ").Count(l => l.Contains(" takes 1 from our A", StringComparison.Ordinal)));
        Assert.Equal(50, Assert.Single(result.Ours).Health);
    }

    [CardEffect("STRIP_ALL")]
    private sealed class StripAll : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Side.Enemy))
            {
                fight.RemoveKeywords(enemy, All);
            }
        }
    }

    [CardEffect("STRIP_TAUNT")]
    private sealed class StripTaunt : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Side.Enemy))
            {
                fight.RemoveKeywords(enemy, Keyword.Taunt);
            }
        }
    }

    [CardEffect("STRIP_WINDFURY")]
    private sealed class StripWindfury : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Side.Enemy))
            {
                fight.RemoveKeywords(enemy, Keyword.Windfury);
            }
        }
    }

    [CardEffect("GIVE_ALL")]
    private sealed class GiveAll : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Side.Enemy))
            {
                fight.GiveKeywords(enemy, All);
            }
        }
    }

    [CardEffect("GIVE_WINDFURY")]
    private sealed class GiveWindfury : CombatEffect
    {
        public override void OnFightStart(Fight fight, Unit self)
        {
            foreach (var enemy in fight.Alive(self.Side.Enemy))
            {
                fight.GiveKeywords(enemy, Keyword.Windfury);
            }
        }
    }
}

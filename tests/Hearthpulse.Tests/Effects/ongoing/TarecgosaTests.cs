using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Tarecgosa (BG21_015): "This permanently keeps Bonus Keywords and stats gained in combat." Golden: "… double stats
/// gained in combat", applied at once (real log 15.11.2026, game 5 fight 8: Ship Master Eudora's +8/+8 became +16/+16 on
/// golden Tarecgosa only). Expected results are worked out by hand in each comment.
/// </summary>
public sealed class TarecgosaTests
{
    private static FightResult EudoraDies(SimMinion tarecgosa) =>
        // Eudora 10/3 hits their 3/13 Taunt and dies; her deathrattle gives our minions +8/+8. Their 3/3 then hits one
        // of ours, and Tarecgosa (next attacker) kills it. Attack is not touched by those hits.
        Fight(Side(Minion(10, 3, cardId: "BG33_828"), tarecgosa, Minion(1, 1, cardId: "X")),
              Side(Minion(3, 13, Keyword.Taunt, cardId: "E")),
              true, Effects(typeof(ShipMasterEudora), typeof(Tarecgosa)));

    [Fact]
    public void Golden_tarecgosa_gains_a_buff_twice()
    {
        // Golden 8/8 + 8 + 8 = 24 Attack; X gets the buff once (9).
        var result = EudoraDies(Minion(8, 8, cardId: "BG21_015", tribes: Tribe.Dragon, golden: true));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([24, 9], result.Ours.Select(m => m.Attack));
    }

    [Fact]
    public void Normal_tarecgosa_changes_nothing_in_combat()
    {
        var result = EudoraDies(Minion(4, 4, cardId: "BG21_015", tribes: Tribe.Dragon));

        Assert.Equal([12, 9], result.Ours.Select(m => m.Attack));
    }

    [Fact]
    public void Golden_tarecgosa_is_combat_relevant()
    {
        Assert.Equal(Relevance.Relevant, CombatRelevance.Find("BG21_015_G")?.Relevance);
        Assert.True(EffectRegistry.Default.Implements("BG21_015", golden: true, isOpponent: true));
    }
}

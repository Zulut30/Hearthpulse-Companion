using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Blood Golem Sticker (BG30_MagicItem_442): "Whenever a friendly Quilboar dies, summon a Golem with stats equal to its
/// Blood Gems." "Its Blood Gems" = what the dying Quilboar gained from Blood Gems (real log 2026-11-05: a Quilboar with
/// 27/24 of gems gave a 27/24 Golem in its slot). Jar o' Gems: "After a friendly Quilboar attacks each combat, this plays a
/// Blood Gem on all your Quilboar."
/// </summary>
public sealed class BloodGolemStickerTests
{
    private const string Sticker = "BG30_MagicItem_442";
    private const string Jar = "BG30_MagicItem_546";

    private static SimMinion Quilboar(string id, int attack, int health, int gemAttack = 0, int gemHealth = 0) =>
        Minion(attack, health, cardId: id, tribes: Tribe.Quilboar) with { BloodGemAttack = gemAttack, BloodGemHealth = gemHealth };

    [Fact]
    public void A_dying_quilboar_leaves_a_golem_with_its_blood_gems_in_its_slot()
    {
        // As in the real log: a 31/28 Quilboar with 27/24 of Blood Gems. Their 100/200 attacks first and kills it; the
        // 27/24 Golem stands in its slot, left of our 0/10.
        var ours = Side(Quilboar("Q", 31, 28, 27, 24), Minion(0, 10, cardId: "R")) with { Extras = [Sticker] };
        var result = Fight(ours, Side(Minion(100, 200, cardId: "BIG")), oursFirst: false, Effects(typeof(BloodGolemSticker)));

        Assert.Equal("summoned our BG30_MagicItem_442t 27/24 at 0", Assert.Single(result.Lines("summoned our ")));
    }

    [Fact]
    public void Only_quilboar_with_blood_gems_make_a_golem()
    {
        // A Quilboar without gems and a Beast with "gems" both die: no Golem.
        var ours = Side(Quilboar("Q", 1, 1), Minion(1, 1, cardId: "B", tribes: Tribe.Beast) with { BloodGemAttack = 5, BloodGemHealth = 5 })
            with { Extras = [Sticker] };
        var result = Fight(ours, Side(Minion(5, 100)), oursFirst: false, Effects(typeof(BloodGolemSticker)));

        Assert.Empty(result.Lines("summoned our "));
    }

    [Fact]
    public void Gems_played_in_the_fight_count_towards_the_golem()
    {
        // Our 4/20 Quilboar has 2/2 of gems. It attacks their 10/50 (4/10 left); Jar o' Gems then plays one +1/+1 gem on
        // it (5/11, gems 3/3). Their attack leaves it 5/1, its next attack kills it: a 3/3 Golem.
        var ours = Side(Quilboar("Q", 4, 20, 2, 2)) with { Extras = [Jar, Sticker] };
        var result = Fight(ours, Side(Minion(10, 50, cardId: "E")), oursFirst: true, Effects(typeof(BloodGolemSticker), typeof(JarOGems)));

        Assert.True(result.IndexOf("our Q 5/11 attacks") < 0 && result.IndexOf("attacks our Q 5/11") >= 0, string.Join('\n', result.Log));
        Assert.Equal("summoned our BG30_MagicItem_442t 3/3 at 0", Assert.Single(result.Lines("summoned our ")));
    }

    [Fact]
    public void It_is_simulated_on_both_sides()
    {
        // The gem totals are on the minions (a visible enchantment): nothing is missing for either side.
        var side = Side(Quilboar("Q", 1, 1, 3, 3)) with { Extras = [Sticker] };

        Assert.Empty(CombatSimulator.Explain(side, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(side, isOpponent: true));
    }

    [Fact]
    public void Jar_o_gems_plays_one_gem_on_every_living_quilboar_once_per_combat()
    {
        // Their 0/100 never attacks. Q1 attacks first: one gem of the player's value (+1/+1 bonus over 1/1 = +2/+2 with
        // these tags) on Q1 and Q2, not on the Beast. Later Quilboar attacks play nothing more.
        var tags = new Dictionary<string, int> { ["BACON_BLOODGEMBUFFATKVALUE"] = 1, ["BACON_BLOODGEMBUFFHEALTHVALUE"] = 1 };
        var ours = Side(Quilboar("Q1", 1, 10), Quilboar("Q2", 1, 10), Minion(1, 10, cardId: "B", tribes: Tribe.Beast)) with { Extras = [Jar], Tags = tags };
        var result = Fight(ours, Side(Minion(0, 100, cardId: "WALL")), oursFirst: true, Effects(typeof(JarOGems)));

        Assert.Equal(1, result.Outcome);
        Assert.Equal([("Q1", 3, 12), ("Q2", 3, 12), ("B", 1, 10)], result.Ours.Select(m => (m.CardId, m.Attack, m.Health)));
    }
}

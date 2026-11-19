using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Trinkets that react to deaths. Eternal Portrait (BG36_MagicItem_216): "… After a friendly Undead dies, your Eternal
/// Knights have +4/+2 this game." Snarky Portrait (BG36_MagicItem_450): "… Whenever a Fishbait dies, give your Beasts
/// +4/+4 and improve this." Evil Experiment (BG36_MagicItem_416): "After your Deity awakens, give it Reborn."
/// </summary>
public sealed class DeathTrinketTests
{
    private const string EternalPortraitId = "BG36_MagicItem_216";
    private const string SnarkyPortraitId = "BG36_MagicItem_450";
    private const string EvilExperimentId = "BG36_MagicItem_416";
    private const string Fishbait = "BG36_205";

    private static SimMinion Knight(Keyword keywords = Keyword.None, bool golden = false) =>
        Minion(golden ? 8 : 4, golden ? 4 : 2, keywords, Tokens.EternalKnightId, Tribe.Undead, golden);

    // ------------------------------------------------------------------------------------------- Eternal Portrait

    [Fact]
    public void Eternal_portrait_buffs_knights_after_an_undead_dies()
    {
        // Their 1/100 kills our Undead 1/1 first: the 4/2 Knight becomes 8/4; a golden 8/4 Knight gets the same +4/+2
        // (12/6). A dead non-Undead gives nothing.
        var undead = Minion(1, 1, cardId: "U", tribes: Tribe.Undead);
        var normal = Fight(WithCard(Side(undead, Knight()), EternalPortraitId), Side(Minion(1, 100)), oursFirst: false, Effects(typeof(EternalPortrait)));
        var golden = Fight(WithCard(Side(undead, Knight(golden: true)), EternalPortraitId), Side(Minion(1, 100)), oursFirst: false, Effects(typeof(EternalPortrait)));
        var other = Fight(WithCard(Side(Minion(1, 1, cardId: "N"), Knight()), EternalPortraitId), Side(Minion(1, 100)), oursFirst: false, Effects(typeof(EternalPortrait)));

        Assert.True(normal.IndexOf("our BG25_008 8/4 attacks") >= 0, string.Join('\n', normal.Log));
        Assert.True(golden.IndexOf("our BG25_008(G) 12/6 attacks") >= 0, string.Join('\n', golden.Log));
        Assert.True(other.IndexOf("our BG25_008 4/2 attacks") >= 0, string.Join('\n', other.Log));
    }

    [Fact]
    public void A_reborn_knight_gets_the_portrait_bonuses_of_this_fight()
    {
        // Their 10/100 kills U: Knight 8/4 Reborn. It attacks and dies; its copy comes back 4/1, gets +4/+2 for U's death
        // (8/3), then +4/+2 for its own death as an Undead (12/5).
        var ours = WithCard(Side(Minion(1, 1, cardId: "U", tribes: Tribe.Undead), Knight(Keyword.Reborn)), EternalPortraitId);
        var result = Fight(ours, Side(Minion(10, 100)), oursFirst: false, Effects(typeof(EternalPortrait)));

        Assert.True(result.IndexOf("our BG25_008 8/4 Reborn attacks") >= 0, string.Join('\n', result.Log));
        Assert.True(result.IndexOf("attacks our BG25_008 12/5") >= 0, string.Join('\n', result.Log));
    }

    // ------------------------------------------------------------------------------------------- Snarky Portrait

    [Fact]
    public void Snarky_portrait_buffs_beasts_when_a_fishbait_dies_and_improves()
    {
        // Their 1/100 kills our first Fishbait: the Beast gets +4/+4 (5/14), the other Fishbait nothing (it can't gain
        // stats). The Beast attacks (5/13); the second Fishbait dies: +5/+5 (10/18).
        var ours = WithCard(Side(Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast), Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast),
            Minion(1, 10, cardId: "B", tribes: Tribe.Beast)), SnarkyPortraitId, Numbers(4, 4));
        var result = Fight(ours, Side(Minion(1, 100)), oursFirst: false, Effects(typeof(SnarkyPortrait)));

        var first = result.IndexOf("our B 5/14 attacks");
        Assert.True(first >= 0, string.Join('\n', result.Log));
        Assert.True(result.IndexOf("their minion 1/95 attacks our BG36_205 0/1") > first, string.Join('\n', result.Log));
        Assert.True(result.IndexOf("our B 10/18 attacks") > first, string.Join('\n', result.Log));
    }

    [Fact]
    public void Snarky_portrait_counts_an_enemy_fishbait_too()
    {
        // Our Beast kills their Fishbait (the first of two targets): +4/+4, then it attacks their wall as 5/14.
        var ours = WithCard(Side(Minion(1, 10, cardId: "B", tribes: Tribe.Beast)), SnarkyPortraitId, Numbers(4, 4));
        var result = Fight(ours, Side(Minion(0, 1, cardId: Fishbait, tribes: Tribe.Beast), Minion(0, 100, cardId: "WALL")), oursFirst: true,
            Effects(typeof(SnarkyPortrait)), 0);

        Assert.True(result.IndexOf("our B 5/14 attacks") >= 0, string.Join('\n', result.Log));
    }

    // ------------------------------------------------------------------------------------------- Cyclist Portrait

    [Fact]
    public void Cyclist_portrait_changes_nothing_in_the_fight_and_is_accounted_for()
    {
        // "Your Showy Cyclists' Deathrattles give permanent stats": the same stats in this fight, so no hook, no flag.
        var side = WithCard(Side(Minion(4, 2, cardId: "BG31_925", tribes: Tribe.Naga, mechanics: CardMechanic.Deathrattle)), "BG36_MagicItem_362");

        Assert.True(EffectRegistry.Default.Implements("BG36_MagicItem_362", golden: false, isOpponent: true));
        Assert.Empty(CombatSimulator.Explain(side, isOpponent: true));
    }

    // ------------------------------------------------------------------------------------------- Evil Experiment

    [Fact]
    public void Evil_experiment_gives_the_awakened_deity_reborn_once()
    {
        // Four 0/1 Aberrations die to their 1/50; Y'Shaarj (3/2 from the player tags) awakens with Reborn and comes back
        // once; its reborn copy gets no second Reborn.
        var ours = new SimSide(Enumerable.Range(1, 4).Select(i => Minion(0, 1, cardId: $"AB{i}", tribes: Tribe.Aberration)).ToArray())
        {
            Extras = [Tokens.YShaarjId, EvilExperimentId],
            Tags = new Dictionary<string, int> { ["BACON_OLD_GOD_ATTACK"] = 3, ["BACON_OLD_GOD_HEALTH"] = 2 },
        };
        var result = Fight(ours, Side(Minion(1, 50)), oursFirst: false, Effects(typeof(YShaarj), typeof(EvilExperiment)));
        var without = Fight(ours with { Extras = [Tokens.YShaarjId] }, Side(Minion(1, 50)), oursFirst: false, Effects(typeof(YShaarj)));

        Assert.True(result.IndexOf("our BGFYM_011 3/2 Reborn attacks") >= 0, string.Join('\n', result.Log));
        Assert.Single(result.Lines("reborn our BGFYM_011"));
        Assert.Empty(without.Lines("reborn our BGFYM_011"));
    }
}

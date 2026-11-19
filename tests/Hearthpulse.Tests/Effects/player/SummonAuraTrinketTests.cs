using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using static Hearthpulse.Tests.Sim.Player.PlayerTestKit;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>
/// Trinkets that reach minions summoned in combat. Mama Bear Sticker (BG35_MagicItem_871): "Whenever you summon a Beast,
/// give it +7/+7." Hammer of Twilight (BG36_MagicItem_403): "Your minions have +1 Attack. (Improved by each card you've
/// discarded this game!)", greater (…403t): "+2/+1". A Reborn minion killed by their 1/10 shows what its copy gets.
/// </summary>
public sealed class SummonAuraTrinketTests
{
    private const string MamaBear = "BG35_MagicItem_871";
    private const string Hammer = "BG36_MagicItem_403";
    private const string GreaterHammer = "BG36_MagicItem_403t";

    private static FightResult RebornFight(SimSide ours, params Type[] effects) =>
        Fight(ours, Side(Minion(1, 10, cardId: "E")), oursFirst: false, Effects(effects));

    private static SimSide Reborn(Tribe tribes = Tribe.None) => Side(Minion(1, 1, Keyword.Reborn, "R", tribes));

    [Fact]
    public void Mama_bear_sticker_gives_a_summoned_beast_seven_seven()
    {
        // The reborn 1/1 Beast comes back 8/8; a reborn non-Beast stays 1/1.
        Assert.True(RebornFight(WithCard(Reborn(Tribe.Beast), MamaBear), typeof(MamaBearSticker)).IndexOf("our R 8/8 attacks") >= 0);
        Assert.True(RebornFight(WithCard(Reborn(), MamaBear), typeof(MamaBearSticker)).IndexOf("our R 1/1 attacks") >= 0);
    }

    [Fact]
    public void Mama_bear_sticker_reads_a_changed_number_from_the_capture()
    {
        // Our sticker shows +10/+9 (a balance change the client data does not have yet): 11/10.
        var result = RebornFight(WithCard(Reborn(Tribe.Beast), MamaBear, Numbers(10, 9)), typeof(MamaBearSticker));

        Assert.True(result.IndexOf("our R 11/10 attacks") >= 0, string.Join('\n', result.Log));
    }

    [Fact]
    public void Hammer_of_twilight_reaches_minions_summoned_in_combat()
    {
        // Printed: +1/+0 and +2/+1 (missing_input without the number); captured +5 Attack: 6/1.
        Assert.True(RebornFight(WithCard(Reborn(), Hammer), typeof(HammerOfTwilight)).IndexOf("our R 2/1 attacks") >= 0);
        Assert.True(RebornFight(WithCard(Reborn(), GreaterHammer), typeof(HammerOfTwilight)).IndexOf("our R 3/2 attacks") >= 0);
        Assert.True(RebornFight(WithCard(Reborn(), Hammer, Numbers(5)), typeof(HammerOfTwilight)).IndexOf("our R 6/1 attacks") >= 0);
    }

    [Fact]
    public void Hammer_of_twilight_needs_its_number()
    {
        Assert.Equal(UnsimulatedReason.MissingInput, Assert.Single(CombatSimulator.Explain(WithCard(Reborn(), Hammer), isOpponent: true)).Reason);
        Assert.Empty(CombatSimulator.Explain(WithCard(Reborn(), GreaterHammer, Numbers(4, 3)), isOpponent: false));
    }
}

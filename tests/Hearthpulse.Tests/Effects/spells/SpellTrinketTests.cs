using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// Trinkets that react to spells cast in combat (card text in each test). Casters: Crimson Vindicator (Mighty
/// Dragonbreath: +3/+2 to all, again to Dragons, again to Divine Shield) and Seafloor Recruiter (Chef's Choice on the
/// minion to its right). Stats worked out by hand.
/// </summary>
public sealed class SpellTrinketTests
{
    private static SimSide With(SimSide side, string trinket) => side with { Extras = [.. side.Extras, trinket] };

    [Fact]
    public void Lovely_locket_casts_the_spell_again_on_another_friendly_minion()
    {
        // "After you cast a spell on a friendly minion, this casts it on another friendly minion." Chef's Choice on the
        // Fugitive F1 (5/2 → 5/3), then the Locket casts it on a random other minion: pick 1 of [Recruiter, F2] = F2 → 5/3.
        // The Locket does not react to its own cast: two casts in all.
        var ours = With(Side(RecruiterMinion(), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga), Minion(5, 2, cardId: "BG36_921", tribes: Tribe.Naga)),
            "BG36_MagicItem_211");
        var result = Fight(ours, Wall(3), true, Effects(typeof(SeafloorRecruiter), typeof(FleeingFugitive), typeof(LovelyLocket)), 0, 1);

        Assert.Equal(2, result.Lines("we cast BG28_518").Count);
        Assert.Equal([(3, 5), (5, 3), (5, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Lovely_locket_ignores_a_spell_without_a_target()
    {
        var ours = With(Side(VindicatorMinion()), "BG36_MagicItem_211");
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(LovelyLocket)));

        Assert.Single(result.Lines("we cast BG36_246"));
    }

    [Fact]
    public void Corrupted_baton_gives_the_deity_stats_after_a_tavern_spell()
    {
        // "After you cast a Tavern spell, give your Deity +4/+4." Greater: "+10/+10". Y'Shaarj 3/2 → 7/6, or 13/12.
        var lesser = Fight(WithDeity(With(Side(VindicatorMinion()), "BG36_MagicItem_404"), 3, 2), Wall(17), true,
            Effects(typeof(CrimsonVindicator), typeof(CorruptedBaton), typeof(YShaarj)));
        var greater = Fight(WithDeity(With(Side(VindicatorMinion()), "BG36_MagicItem_404t"), 3, 2), Wall(17), true,
            Effects(typeof(CrimsonVindicator), typeof(CorruptedBaton), typeof(YShaarj)));

        Assert.Single(lesser.Lines("our Deity gets +4/+4 (7/6)"));
        Assert.Single(greater.Lines("our Deity gets +10/+10 (13/12)"));
    }

    [Fact]
    public void A_trinkets_captured_script_data_wins_over_the_printed_value()
    {
        // Our Corrupted Baton's captured TAG_SCRIPT_DATA_NUM_1/2 say +6/+5 (a live balance change): Y'Shaarj 3/2 → 9/7.
        var ours = WithDeity(With(Side(VindicatorMinion()), "BG36_MagicItem_404"), 3, 2) with
        {
            ExtraTags = new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["BG36_MagicItem_404"] = new Dictionary<string, int> { ["TAG_SCRIPT_DATA_NUM_1"] = 6, ["TAG_SCRIPT_DATA_NUM_2"] = 5 },
            },
        };
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(CorruptedBaton), typeof(YShaarj)));

        Assert.Single(result.Lines("our Deity gets +6/+5 (9/7)"));
    }

    [Fact]
    public void Miniature_ship_gives_pirates_four_four()
    {
        // "After you cast a Tavern spell, give your Pirates +4/+4." Pirate 1/1: Dragonbreath +3/+2 → 4/3, then +4/+4 → 8/7;
        // the Beast 1/1 → 4/3 only.
        var ours = With(Side(VindicatorMinion(), Minion(1, 1, cardId: "PIR", tribes: Tribe.Pirate), Minion(1, 1, cardId: "B", tribes: Tribe.Beast)),
            "BG35_MagicItem_710");
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(MiniatureShip)));

        Assert.Equal([(17, 15), (8, 7), (4, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Comfy_coffin_gives_undead_attack_this_game()
    {
        // "Whenever you cast a Tavern spell, your Undead have +1 Attack this game." Greater: "+2". Undead 1/1 → 2/1 (or
        // 3/1), then Dragonbreath → 5/3 (or 6/3).
        var lesser = Fight(With(Side(VindicatorMinion(), Minion(1, 1, cardId: "U", tribes: Tribe.Undead)), "BG30_MagicItem_547"), Wall(17), true,
            Effects(typeof(CrimsonVindicator), typeof(ComfyCoffin)));
        var greater = Fight(With(Side(VindicatorMinion(), Minion(1, 1, cardId: "U", tribes: Tribe.Undead)), "BG30_MagicItem_547t"), Wall(17), true,
            Effects(typeof(CrimsonVindicator), typeof(ComfyCoffin)));

        Assert.Equal((5, 3), (lesser.Ours[1].Attack, lesser.Ours[1].Health));
        Assert.Equal((6, 3), (greater.Ours[1].Attack, greater.Ours[1].Health));
    }

    [Fact]
    public void Bluegill_flippers_buff_the_left_most_minion_of_the_warband()
    {
        // "Whenever you cast a Tavern spell, give the left-most minion in your hand and warband +3/+3." The Vindicator is
        // left-most: 8/9 → 11/12 → Dragonbreath 20/18; P 1/1 → 4/3.
        var ours = With(Side(VindicatorMinion(), Minion(1, 1, cardId: "P")), "BG32_MagicItem_893");
        var result = Fight(ours, Wall(20), true, Effects(typeof(CrimsonVindicator), typeof(BluegillFlippers)));

        Assert.Equal([(20, 18), (4, 3)], Stats(result.Ours));
    }

    [Fact]
    public void Flighty_portrait_buffs_flighty_scouts_on_the_board_and_in_hand()
    {
        // "After you cast a Tavern spell, give Flighty Scouts in your hand and warband +3/+3." Scout 3/3 → Dragonbreath
        // 6/5 → 9/8; the Scout in hand 3/3 → 6/6, the other card stays. The test watcher is a player-level card after the
        // Portrait, so it prints the hand after the Portrait's buff.
        var ours = With(With(Side(VindicatorMinion(), Minion(3, 3, cardId: "BG32_330")), "BG36_MagicItem_820"), "WATCH") with
        {
            Hand = [Minion(3, 3, cardId: "BG32_330"), Minion(1, 1, cardId: "X")],
        };
        var result = Fight(ours, Wall(17), true, Effects(typeof(CrimsonVindicator), typeof(FlightyPortrait), typeof(Watcher)));

        Assert.Equal((9, 8), (result.Ours[1].Attack, result.Ours[1].Health));
        Assert.Single(result.Lines("after BG36_246 hand: BG32_330 6/6, X 1/1"));
    }
}

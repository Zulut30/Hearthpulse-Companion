using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using static Hearthpulse.Tests.Sim.SimTestKit;
using static Hearthpulse.Tests.Sim.Spells.SpellTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// Geomagus Roogug ("Divine Shield. Whenever a Blood Gem is played on this, this plays a Blood Gem on a different friendly
/// minion."; golden "2 Blood Gems"), The Shadow of Doubt ("Whenever a card is added to your hand, give your Deity +4/+5.";
/// golden "+8/+10") and the "Get a card" minions that feed it. Stats worked out by hand.
/// </summary>
public sealed class HandAndBloodGemTests
{
    private static SimMinion Bonker() => Minion(2, 7, cardId: "BG20_104", tribes: Tribe.Quilboar, mechanics: CardMechanic.Rally);

    private static SimMinion Roogug(bool golden = false) =>
        Minion(golden ? 8 : 4, golden ? 12 : 6, Keyword.DivineShield, "BG28_583", Tribe.Quilboar, golden);

    [Fact]
    public void Roogug_plays_a_gem_on_another_minion_for_each_gem_played_on_it()
    {
        // Bonker's Rally plays a gem (+1/+1) on Roogug 4/6 → 5/7; Roogug plays one on a random other minion: pick 1 of
        // [Bonker, P] = P 1/1 → 2/2; then Bonker's own gem on P → 3/3. Three gems in all; Bonker kills the 0/2.
        var result = Fight(Side(Bonker(), Roogug(), Minion(1, 1, cardId: "P")), Wall(2), true,
            Effects(typeof(Bonker), typeof(GeomagusRoogug)), 0, 1);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(2, 7), (5, 7), (3, 3)], Stats(result.Ours));
        Assert.Equal(3, result.Log.Count(l => l.EndsWith("gets a Blood Gem", StringComparison.Ordinal)));
    }

    [Fact]
    public void Golden_roogug_plays_two_gems_with_the_players_gem_value()
    {
        // Blood Gems are +2/+3 here (bonus tags +1/+2). Bonker's gem on golden Roogug 8/12 → 10/15; Roogug plays two on P
        // (pick 1) → 5/7; Bonker's gem on P → 7/10.
        var ours = Side(Bonker(), Roogug(golden: true), Minion(1, 1, cardId: "P")) with
        {
            Tags = new Dictionary<string, int> { [BloodGems.AttackTag] = 1, [BloodGems.HealthTag] = 2 },
        };
        var result = Fight(ours, Wall(2), true, Effects(typeof(Bonker), typeof(GeomagusRoogug)), 0, 1);

        Assert.Equal([(2, 7), (10, 15), (7, 10)], Stats(result.Ours));
    }

    [Fact]
    public void Roogug_reacts_only_to_gems_played_on_itself()
    {
        // Bonker plays a gem on P, Q and Roogug in turn. The gems on P and Q set nothing off (P 2/2, Q 2/2); the one on
        // Roogug (5/7) does once: pick 0 of [Bonker, P, Q] = Bonker 2/7 → 3/8.
        var result = Fight(Side(Bonker(), Minion(1, 1, cardId: "P"), Minion(1, 1, cardId: "Q"), Roogug()), Wall(2), true,
            Effects(typeof(Bonker), typeof(GeomagusRoogug)), 0, 0);

        Assert.Equal([(3, 8), (2, 2), (2, 2), (5, 7)], Stats(result.Ours));
        Assert.Equal(4, result.Log.Count(l => l.EndsWith("gets a Blood Gem", StringComparison.Ordinal)));
    }

    [Fact]
    public void Two_roogugs_passing_gems_stop_at_the_depth_limit()
    {
        // Each Roogug always picks the other one (pick 1 of [Bonker, other]): the chain must end, not hang.
        var picks = Enumerable.Repeat(1, 200).Prepend(0).ToArray();
        var result = Fight(Side(Bonker(), Roogug(), Roogug()), Wall(2), true, Effects(typeof(Bonker), typeof(GeomagusRoogug)), picks);

        var gems = result.Log.Count(l => l.EndsWith("gets a Blood Gem", StringComparison.Ordinal));
        Assert.InRange(gems, 3, 60);
        Assert.Equal(1, result.Outcome);
    }

    [Fact]
    public void Shadow_of_doubt_gives_the_deity_stats_for_a_card_got_by_a_rally()
    {
        // Bronze Timewalker's "Rally: Get a random Chromadrake" adds a card: the sleeping Y'Shaarj 3/2 → 7/7.
        var ours = WithDeity(Side(Minion(4, 5, cardId: "BG36_242", tribes: Tribe.Dragon, mechanics: CardMechanic.Rally),
            Minion(6, 8, cardId: "BG36_109", tribes: Tribe.Aberration)), 3, 2);
        var result = Fight(ours, Wall(4), true, Effects(typeof(GetOnRally), typeof(TheShadowOfDoubt), typeof(YShaarj)));

        Assert.Single(result.Lines("we get a random card"));
        Assert.Single(result.Lines("our Deity gets +4/+5 (7/7)"));
    }

    [Fact]
    public void Golden_shadow_of_doubt_gives_eight_ten_per_card()
    {
        // Golden Timewalker gets two cards; golden Shadow gives +8/+10 for each: 3/2 → 11/12 → 19/22.
        var ours = WithDeity(Side(Minion(8, 10, cardId: "BG36_242", tribes: Tribe.Dragon, golden: true, mechanics: CardMechanic.Rally),
            Minion(12, 16, cardId: "BG36_109", tribes: Tribe.Aberration, golden: true)), 3, 2);
        var result = Fight(ours, Wall(8), true, Effects(typeof(GetOnRally), typeof(TheShadowOfDoubt), typeof(YShaarj)));

        Assert.Equal(2, result.Lines("we get a random card").Count);
        Assert.Equal(["our Deity gets +8/+10 (11/12)", "our Deity gets +8/+10 (19/22)"], result.Lines("our Deity gets"));
    }

    [Fact]
    public void Shadow_of_doubt_sees_a_card_from_a_deathrattle_like_the_real_log()
    {
        // Their 5/100 kills N'raqi Sapper 5/4 (pick 0): "Deathrattle: Get an Energizing Chamber" → Deity 3/2 → 7/7.
        var ours = WithDeity(Side(Minion(5, 4, cardId: "BG36_103", tribes: Tribe.Aberration, mechanics: CardMechanic.Deathrattle),
            Minion(6, 8, cardId: "BG36_109", tribes: Tribe.Aberration)), 3, 2);
        var result = Fight(ours, Side(Minion(5, 100, cardId: "K")), false, Effects(typeof(GetOnDeathrattle), typeof(TheShadowOfDoubt), typeof(YShaarj)));

        var dies = result.IndexOf("our BG36_103 5/-1 dies");
        Assert.True(dies >= 0 && dies < result.IndexOf("we get BG36_371"));
        Assert.Single(result.Lines("our Deity gets +4/+5 (7/7)"));
    }

    [Fact]
    public void Shadow_of_doubt_works_for_the_opponent_too()
    {
        // No hand is read: their Timewalker's card counts for their Shadow.
        var theirs = WithDeity(Side(Minion(4, 5, cardId: "BG36_242", tribes: Tribe.Dragon, mechanics: CardMechanic.Rally),
            Minion(6, 8, cardId: "BG36_109", tribes: Tribe.Aberration)), 3, 2);
        var result = Fight(Side(Minion(0, 50, cardId: "W")), theirs, false, Effects(typeof(GetOnRally), typeof(TheShadowOfDoubt), typeof(YShaarj)));

        Assert.True(result.Lines("their Deity gets +4/+5").Count >= 1);
    }

    [Fact]
    public void Get_minions_add_their_cards_golden_two()
    {
        // Rescue Bot "Deathrattle: Get a Repair Job" (golden 2), Drustfallen Butcher "Avenge (4): Get a Butchering".
        // Their 9/200 kills (pick 0 each time) the golden Bot first, then four 0/1s while the Butcher (last) watches.
        var ours = Side(
            Minion(2, 1, cardId: "BG36_854", golden: true, mechanics: CardMechanic.Deathrattle),
            Minion(0, 1, cardId: "W1"), Minion(0, 1, cardId: "W2"), Minion(0, 1, cardId: "W3"),
            Minion(0, 50, cardId: "BG32_324", mechanics: CardMechanic.Avenge));
        var result = Fight(ours, Side(Minion(9, 200, cardId: "K")), false, Effects(typeof(GetOnDeathrattle), typeof(DrustfallenButcher)));

        Assert.Equal(2, result.Lines("we get BG36_624").Count);
        Assert.Single(result.Lines("we get BG28_604"));
    }

    [Fact]
    public void Lichling_hoarder_gets_a_plain_copy_of_a_starting_minion()
    {
        // "Avenge (3): Get a plain copy of a minion that started in your warband this combat." Their 9/200 kills W0 (pick 0),
        // our W1 (buffed to 5/5) attacks it and dies, their 9/200 kills W2 (pick 0): the third death. The copy is the
        // starting minion at pick 1 = W1, with its printed 0/1.
        var ours = Side(
            Minion(0, 1, cardId: "W0"), Minion(0, 1, cardId: "W1") with { Attack = 5, Health = 5, MaxHealth = 5 }, Minion(0, 1, cardId: "W2"),
            Minion(0, 50, cardId: "BG36_848", mechanics: CardMechanic.Avenge));
        var result = Fight(ours, Side(Minion(9, 200, cardId: "K")), false, Effects(typeof(LichlingHoarder)), 0, 0, 0, 1);

        Assert.Single(result.Lines("we get W1"));
    }
}

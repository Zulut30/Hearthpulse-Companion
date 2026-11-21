using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Tests;

/// <summary>
/// How full each set of a made-up collection is (<see cref="ExpansionCompletion"/>, docs/collection-cosmetics.md):
/// unique cards, playable copies up to the deck limit, golden copies, dust to finish by the crafting prices, copies no
/// dust buys, own printings only, rarities, format groups and the order of release.
/// </summary>
public sealed class ExpansionCompletionTests
{
    private static readonly DateTime ReadAt = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static CardDb Cards(string json) => CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static CardCollection Owned(params (string Id, int Normal, int Golden)[] cards) =>
        new(cards.ToDictionary(c => c.Id, c => new OwnedCopies(c.Normal, c.Golden, 0), StringComparer.Ordinal), 0, ReadAt);

    private static readonly IReadOnlySet<string> Standard = new HashSet<string>(StringComparer.Ordinal) { "TITANS", "CORE" };

    // The hand-made set of AC7: a legendary with no copy, epic A with one, epic B with two, a common with none.
    private static readonly CardDb HandMade = Cards("""
        [
          {"id":"T_LEG","dbfId":1,"name":"Legend","rarity":"LEGENDARY","set":"TITANS","type":"MINION","collectible":true},
          {"id":"T_EPA","dbfId":2,"name":"Epic A","rarity":"EPIC","set":"TITANS","type":"SPELL","collectible":true},
          {"id":"T_EPB","dbfId":3,"name":"Epic B","rarity":"EPIC","set":"TITANS","type":"MINION","collectible":true},
          {"id":"T_COM","dbfId":4,"name":"Common","rarity":"COMMON","set":"TITANS","type":"MINION","collectible":true},
          {"id":"T_TOKEN","dbfId":5,"name":"Token","rarity":"COMMON","set":"TITANS","type":"MINION"}
        ]
        """);

    [Fact]
    public void The_hand_made_set_counts_as_worked_out_by_hand()
    {
        var set = Assert.Single(ExpansionCompletion.Build(Owned(("T_EPA", 1, 0), ("T_EPB", 2, 0), ("T_TOKEN", 2, 0)), HandMade, Standard));

        Assert.Equal("TITANS", set.Set);
        Assert.Equal((2, 4), (set.UniqueOwned, set.UniqueTotal));
        Assert.Equal((3, 7), (set.PlayableOwned, set.PlayableTotal));
        // Legendary 1 x 1600, epic A 1 x 400, common 2 x 40.
        Assert.Equal(2080, set.DustToComplete);
        Assert.False(set.DustEstimated);
        Assert.Equal(0, set.UncraftableMissing);
        Assert.Equal(SetGroup.Standard, set.Group);
        Assert.Equal(CardSets.ReleaseOrder("TITANS"), set.ReleaseOrder);
    }

    [Fact]
    public void Rarities_are_broken_down_in_their_order()
    {
        var set = ExpansionCompletion.Build(Owned(("T_EPA", 1, 0), ("T_EPB", 2, 0)), HandMade, Standard)[0];

        Assert.Equal(
            [
                new RarityCompletion("COMMON", 1, 0, 2, 0, 80),
                new RarityCompletion("EPIC", 2, 2, 4, 3, 400),
                new RarityCompletion("LEGENDARY", 1, 0, 1, 0, 1600),
            ],
            set.ByRarity);
    }

    [Fact]
    public void Copies_over_the_limit_count_up_to_it_in_any_finish_and_golden_on_its_own()
    {
        var set = ExpansionCompletion.Build(Owned(("T_LEG", 0, 3), ("T_EPA", 5, 0), ("T_EPB", 1, 1), ("T_COM", 0, 1)), HandMade, Standard)[0];

        Assert.Equal((4, 4), (set.UniqueOwned, set.UniqueTotal));
        Assert.Equal((6, 7), (set.PlayableOwned, set.PlayableTotal));
        // Golden up to the limit: legendary 1 of 3, epic B 1, common 1.
        Assert.Equal(3, set.GoldenOwned);
        Assert.Equal(40, set.DustToComplete);
    }

    [Fact]
    public void A_printing_from_another_set_fills_no_slot()
    {
        var cards = Cards("""
            [
              {"id":"T_EPA","dbfId":2,"name":"Epic A","rarity":"EPIC","set":"TITANS","type":"SPELL","collectible":true},
              {"id":"CORE_T_EPA","dbfId":20,"name":"Epic A","rarity":"EPIC","set":"CORE","type":"SPELL","collectible":true,"countAsCopyOfDbfId":2}
            ]
            """);

        var sets = ExpansionCompletion.Build(Owned(("CORE_T_EPA", 2, 0)), cards, Standard);
        var titans = sets.Single(s => s.Set == "TITANS");
        var core = sets.Single(s => s.Set == "CORE");

        Assert.Equal((0, 2), (titans.PlayableOwned, titans.PlayableTotal));
        Assert.Equal(800, titans.DustToComplete);
        Assert.Equal((2, 2), (core.PlayableOwned, core.PlayableTotal));
    }

    [Fact]
    public void Copies_no_dust_buys_are_counted_apart_and_cost_nothing()
    {
        var cards = Cards("""
            [
              {"id":"FREE_1","dbfId":1,"name":"Free","rarity":"FREE","set":"LEGACY","type":"MINION","collectible":true},
              {"id":"LEG_C","dbfId":2,"name":"Legacy common","rarity":"COMMON","set":"LEGACY","type":"MINION","collectible":true},
              {"id":"CORE_1","dbfId":3,"name":"Core","rarity":"RARE","set":"CORE","type":"MINION","collectible":true},
              {"id":"EVENT_1","dbfId":4,"name":"Event","rarity":"LEGENDARY","set":"EVENT","type":"MINION","collectible":true},
              {"id":"VAN_1","dbfId":5,"name":"Vanilla","rarity":"EPIC","set":"VANILLA","type":"MINION","collectible":true}
            ]
            """);

        var sets = ExpansionCompletion.Build(Owned(), cards, Standard).ToDictionary(s => s.Set, StringComparer.Ordinal);

        Assert.Equal((2, 80L), (sets["LEGACY"].UncraftableMissing, sets["LEGACY"].DustToComplete));
        Assert.Equal((2, 0L), (sets["CORE"].UncraftableMissing, sets["CORE"].DustToComplete));
        Assert.Equal((1, 0L), (sets["EVENT"].UncraftableMissing, sets["EVENT"].DustToComplete));
        Assert.Equal((2, 0L), (sets["VANILLA"].UncraftableMissing, sets["VANILLA"].DustToComplete));
    }

    [Fact]
    public void Dust_is_an_estimate_when_a_missing_craftable_card_is_also_handed_out()
    {
        var cards = Cards("""
            [
              {"id":"T_REWARD","dbfId":1,"name":"Reward","rarity":"LEGENDARY","set":"TITANS","type":"MINION","collectible":true,"howToEarn":"Given at the start."},
              {"id":"T_COM","dbfId":2,"name":"Common","rarity":"COMMON","set":"TITANS","type":"MINION","collectible":true}
            ]
            """);

        Assert.True(ExpansionCompletion.Build(Owned(), cards, Standard)[0].DustEstimated);
        // Owned, the reward no longer makes the price uncertain.
        Assert.False(ExpansionCompletion.Build(Owned(("T_REWARD", 1, 0)), cards, Standard)[0].DustEstimated);
    }

    [Fact]
    public void Sets_are_grouped_and_ordered_newest_first_without_skins_and_hidden_core()
    {
        var cards = Cards("""
            [
              {"id":"A","dbfId":1,"name":"A","rarity":"COMMON","set":"LEGACY","type":"MINION","collectible":true},
              {"id":"B","dbfId":2,"name":"B","rarity":"COMMON","set":"TITANS","type":"MINION","collectible":true},
              {"id":"C","dbfId":3,"name":"C","rarity":"COMMON","set":"VANILLA","type":"MINION","collectible":true},
              {"id":"D","dbfId":4,"name":"D","rarity":"COMMON","set":"BRAND_NEW_SET","type":"MINION","collectible":true},
              {"id":"E","dbfId":5,"name":"E","rarity":"COMMON","set":"CATACLYSM","type":"MINION","collectible":true},
              {"id":"HERO_99","dbfId":6,"name":"Skin","rarity":"FREE","set":"HERO_SKINS","type":"HERO","collectible":true},
              {"id":"CORE_HIDDEN_1","dbfId":7,"name":"Hidden","rarity":"COMMON","set":"CORE_HIDDEN","type":"MINION","collectible":true}
            ]
            """);

        var sets = ExpansionCompletion.Build(Owned(), cards, Standard);
        var byDefault = ExpansionCompletion.Build(Owned(), cards).ToDictionary(s => s.Set, s => s.Group, StringComparer.Ordinal);

        Assert.Equal(["BRAND_NEW_SET", "CATACLYSM", "TITANS", "VANILLA", "LEGACY"], sets.Select(s => s.Set));
        Assert.Equal([SetGroup.Wild, SetGroup.Wild, SetGroup.Standard, SetGroup.Classic, SetGroup.Wild], sets.Select(s => s.Group));
        Assert.Equal(int.MaxValue, sets[0].ReleaseOrder);
        // Without a list of its own, Standard is the card pool's.
        Assert.Equal(SetGroup.Standard, byDefault["CATACLYSM"]);
        Assert.Equal(SetGroup.Wild, byDefault["TITANS"]);
    }

    [Fact]
    public void The_sum_adds_every_count_and_rarity()
    {
        var cards = Cards("""
            [
              {"id":"A","dbfId":1,"name":"A","rarity":"COMMON","set":"TITANS","type":"MINION","collectible":true},
              {"id":"B","dbfId":2,"name":"B","rarity":"LEGENDARY","set":"TITANS","type":"MINION","collectible":true,"howToEarn":"x"},
              {"id":"C","dbfId":3,"name":"C","rarity":"COMMON","set":"CORE","type":"MINION","collectible":true}
            ]
            """);
        var sets = ExpansionCompletion.Build(Owned(("A", 1, 1), ("C", 2, 0)), cards, Standard);

        var sum = ExpansionCompletion.Sum(sets);

        Assert.Equal(string.Empty, sum.Set);
        Assert.Equal(SetGroup.Standard, sum.Group);
        Assert.Equal((2, 3), (sum.UniqueOwned, sum.UniqueTotal));
        Assert.Equal((4, 5), (sum.PlayableOwned, sum.PlayableTotal));
        Assert.Equal(1, sum.GoldenOwned);
        Assert.Equal(1600, sum.DustToComplete);
        Assert.True(sum.DustEstimated);
        Assert.Equal([new RarityCompletion("COMMON", 2, 2, 4, 4, 0), new RarityCompletion("LEGENDARY", 1, 0, 1, 0, 1600)], sum.ByRarity);
        Assert.Equal(0, ExpansionCompletion.Sum([]).UniqueTotal);
    }

    [Fact]
    public void The_made_up_scene_fills_sets_to_different_degrees_and_owns_part_of_the_skins()
    {
        var cards = Cards("""
            [
              {"id":"A1","dbfId":1,"name":"A1","rarity":"COMMON","set":"TITANS","type":"MINION","collectible":true},
              {"id":"A2","dbfId":2,"name":"A2","rarity":"LEGENDARY","set":"TITANS","type":"MINION","collectible":true},
              {"id":"B1","dbfId":3,"name":"B1","rarity":"EPIC","set":"BE","type":"MINION","collectible":true},
              {"id":"HERO_01a","dbfId":4,"name":"Skin 1","set":"HERO_SKINS","type":"HERO","cardClass":"WARRIOR","collectible":true},
              {"id":"HERO_01b","dbfId":5,"name":"Skin 2","set":"HERO_SKINS","type":"HERO","cardClass":"WARRIOR","collectible":true},
              {"id":"GAME_005","dbfId":1746,"name":"The Coin","set":"CORE","type":"SPELL"}
            ]
            """);

        var scene = CosmeticsScene.Build(cards, confirmed: true);

        Assert.NotNull(scene);
        Assert.True(scene.Collection.Confirmed && scene.Cosmetics.Confirmed);
        Assert.Contains(HeroSkins.Build(scene.Collection, cards), s => s.Owned);
        Assert.Contains(HeroSkins.Build(scene.Collection, cards), s => !s.Owned);
        Assert.Contains(scene.Cosmetics.CardBacks, b => b.Name == CosmeticsScene.LongName);
        Assert.Equal(1746, scene.Cosmetics.Coins[0].CardDbfId);
        Assert.True(CosmeticsLimits.Fits(scene.Cosmetics));
        Assert.Null(CosmeticsScene.Build(CardDb.Empty, confirmed: false));
    }
}

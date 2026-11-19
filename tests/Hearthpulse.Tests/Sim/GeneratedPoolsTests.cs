using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// The generated Ghastcoiler and Undead pools against the card data of the patch they name (Fixtures/BgPools: cards of
/// HearthstoneJSON 256589, patch 37.0.2). Battlegrounds stats change server-side without a client build: the pools were
/// once left at 37.0.0.255130 stats (Eudora 8/3, The Last One Standing 15/15) and without Rot Hide Gnoll.
/// </summary>
public sealed class GeneratedPoolsTests
{
    private static readonly Lazy<CardDb> Cards = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "BgPools", "cards-256589.json"));
        return CardDb.Parse("enUS", stream);
    });

    private static readonly Dictionary<string, Tribe> Tribes = new(StringComparer.Ordinal)
    {
        ["BEAST"] = Tribe.Beast, ["DEMON"] = Tribe.Demon, ["DRAGON"] = Tribe.Dragon, ["ELEMENTAL"] = Tribe.Elemental,
        ["MECHANICAL"] = Tribe.Mech, ["MURLOC"] = Tribe.Murloc, ["NAGA"] = Tribe.Naga, ["PIRATE"] = Tribe.Pirate,
        ["QUILBOAR"] = Tribe.Quilboar, ["UNDEAD"] = Tribe.Undead, ["ABERRATION"] = Tribe.Aberration, ["ALL"] = Tribe.All,
    };

    [Fact]
    public void Both_pools_name_the_patch_of_the_relevance_table()
    {
        Assert.Equal("37.0.2.256589", CombatRelevance.Patch);
        Assert.Equal(GhastcoilerPool.Patch, CombatRelevance.Patch);
        Assert.Equal(UndeadPool.Patch, CombatRelevance.Patch);
    }

    [Theory]
    [InlineData("BG36_362")] // Sacrificial Wrathguard: 6/3 (5/3 in 37.0.0.255130)
    [InlineData("BG33_828")] // Ship Master Eudora: 10/3 (8/3)
    [InlineData("BG27_016")] // Champion of Sargeras: 12/12 (8/8)
    [InlineData("BG28_300")] // Harmless Bonehead
    [InlineData("BG25_022")] // Scarlet Skull
    public void Ghastcoiler_pool_prints_the_card_data(string cardId) =>
        AssertPrinted(GhastcoilerPool.Minions, cardId);

    [Theory]
    [InlineData("BG34_320")] // The Last One Standing: 10/10 (15/15)
    [InlineData("BG25_013")] // Rot Hide Gnoll: back in the pool, a Tier 1 Undead
    [InlineData("BG28_300")] // Harmless Bonehead
    [InlineData("BG25_022")] // Scarlet Skull
    public void Undead_pool_prints_the_card_data(string cardId) =>
        AssertPrinted(UndeadPool.Minions, cardId);

    [Fact]
    public void A_pool_minion_without_a_deathrattle_or_undead_type_is_in_neither_pool()
    {
        // Ultraviolet Ascendant: an Elemental with a Start of Combat effect.
        Assert.True(Cards.Value.Get("BG31_810") is { IsBattlegroundsPoolMinion: true });
        Assert.Equal(-1, GhastcoilerPool.IndexOf("BG31_810"));
        Assert.Equal(-1, UndeadPool.IndexOf("BG31_810"));
    }

    private static void AssertPrinted(IReadOnlyList<SimMinion> pool, string cardId)
    {
        var card = Cards.Value.Get(cardId);
        Assert.NotNull(card);
        Assert.True(card.IsBattlegroundsPoolMinion, cardId);
        var entry = Assert.Single(pool, m => m.CardId == cardId);
        var tribes = card.Races.Aggregate(Tribe.None, (all, race) => all | Tribes[race]);
        Assert.Equal((card.Attack, card.Health, card.TechLevel, tribes), (entry.Attack, entry.Health, entry.TechLevel, entry.Tribes));
        Assert.False(entry.Golden);
    }
}

using System.Text;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Tests;

/// <summary>Hero skins come from the card data, owned when the collection has a copy (<see cref="HeroSkins"/>).</summary>
public sealed class HeroSkinsTests
{
    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes("""
        [
          {"id":"HERO_08b","dbfId":1,"name":"Medivh","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE","collectible":true},
          {"id":"HERO_08a","dbfId":2,"name":"Khadgar","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE","collectible":true},
          {"id":"HERO_01a","dbfId":3,"name":"Magni","set":"HERO_SKINS","type":"HERO","cardClass":"WARRIOR","collectible":true},
          {"id":"HERO_08z","dbfId":4,"name":"Not collectible","set":"HERO_SKINS","type":"HERO","cardClass":"MAGE"},
          {"id":"ICC_833","dbfId":5,"name":"Frost Lich Jaina","set":"ICECROWN","type":"HERO","cardClass":"MAGE","collectible":true},
          {"id":"HERO_SKIN_SPELL","dbfId":6,"name":"Not a hero","set":"HERO_SKINS","type":"SPELL","cardClass":"MAGE","collectible":true}
        ]
        """)));

    [Fact]
    public void Skins_are_the_collectible_heroes_of_the_skin_set_by_class_and_name()
    {
        var skins = HeroSkins.Build(null, Cards);

        Assert.Equal(["HERO_08a", "HERO_08b", "HERO_01a"], skins.Select(s => s.CardId));
        Assert.Equal(["MAGE", "MAGE", "WARRIOR"], skins.Select(s => s.CardClass));
        Assert.All(skins, s => Assert.False(s.Owned));
    }

    [Fact]
    public void A_skin_is_owned_when_the_collection_has_a_copy_of_it()
    {
        var collection = new CardCollection(new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            ["HERO_08b"] = new(1, 0, 0),
            ["HERO_01a"] = new(0, 0, 1),
        }, 0, DateTime.UtcNow);

        var owned = HeroSkins.Build(collection, Cards).ToDictionary(s => s.CardId, s => s.Owned, StringComparer.Ordinal);

        Assert.True(owned["HERO_08b"]);
        Assert.True(owned["HERO_01a"]);
        Assert.False(owned["HERO_08a"]);
    }
}

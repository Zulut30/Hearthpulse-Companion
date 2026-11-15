using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Site;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Tests;

public sealed class TrinketSliceContextTests
{
    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(51, true)]
    public void A_trinket_uses_one_mmrs_placement_sample_pick_rate_and_date(int narrowGames, bool narrow)
    {
        var wideDate = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var narrowDate = wideDate.AddDays(1);
        BgSiteTierList List(int games, double place, double rate, DateTime date) => new(BgSiteList.Trinkets,
            [new BgSiteTierEntry("TRINKET", 1, "A", "Trinket", "Trinket", 0, place, rate, null, null, games)],
            [], date, "hsreplay", null, null, null);
        var heroes = new Dictionary<int, IReadOnlyDictionary<string, MetaHero>> { [50] = new Dictionary<string, MetaHero>(), [20] = new Dictionary<string, MetaHero>() };
        var meta = new BattlegroundsMeta(heroes, [(20, 6000)], [], new Dictionary<string, MetaTrinket>(),
            new Dictionary<int, IReadOnlyDictionary<string, MetaCard>>(), wideDate)
        {
            Source = BattlegroundsMeta.Attribution,
        }.WithExternal(new BgExternalLists([], new Dictionary<int, BgSiteTierList>
        {
            [50] = List(500, 4.2, 0.1, wideDate),
            [20] = List(narrowGames, 3.2, 0.3, narrowDate),
        }), CardDb.Empty);
        var view = new BattlegroundsView(5, 3, 6, "HERO", 30, 0, 3, false, false, false,
            [], [], false, [], [], [], [], null, 0, [], [])
        {
            TrinketOptions = [new BgTrinketOption("TRINKET", "Trinket")],
        };

        var advice = new BattlegroundsAdvisor { Meta = meta }.Enrich(view, 7000, CardDb.Empty);

        var row = Assert.Single(advice.TrinketOptions).Meta;
        Assert.NotNull(row);
        Assert.True(row.External);
        Assert.Equal(narrow ? 3.2 : 4.2, row.AveragePlacement);
        Assert.Equal(narrow ? narrowGames : 500, row.DataPoints);
        Assert.Equal(narrow ? 0.3 : 0.1, row.PickRate);
        Assert.Equal(narrow ? narrowDate : wideDate, row.Updated);
        Assert.Equal((row.AveragePlacement, row.PickRate, row.DataPoints), meta.Trinket("TRINKET", 20)!.Value);
    }
}

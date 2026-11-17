using Hearthpulse.Core.Api;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The hero and trinket pick badges for render checks (<c>--render-ui</c>, group <c>picks</c>): made-up offers of two
/// to four cards with numbers, comps and a data date on every badge, and a lobby of seven opponents whose leaderboard
/// labels name a tribe. The frames show whether every badge is whole on the screen over its own card of the game at
/// the data copy's overlay scale, and that no label lies under a trinket badge.
/// </summary>
public static class PickScene
{
    /// <summary>The player's place among the eight: that tile has no label.</summary>
    public const int OwnPlace = 5;

    private static readonly DateTime Fetched = new(2026, 11, 15, 17, 1, 0, DateTimeKind.Utc);

    private static readonly string[] Heroes = ["TB_BaconShop_HERO_37", "TB_BaconShop_HERO_49", "TB_BaconShop_HERO_72", "BG20_HERO_242"];

    private static readonly string[] Lobby = ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "ELEMENTAL"];

    /// <summary>The hero choice with <paramref name="count"/> heroes (2 to 4), each with numbers, a lobby adjustment and two comps.</summary>
    public static BattlegroundsView HeroPick(int count) =>
        new(0, 1, 6, null, 30, 0, 1, false, false, true,
            [.. Heroes.Take(Math.Clamp(count, 1, Heroes.Length)).Select((id, i) => new BgHeroOption(id, id, 0, new HeroStats(12 + i, 4.1, 0.5, 0.1),
                new HeroMeta(4.23 + (i * 0.04), 4.11 + (i * 0.07), 0.19 + (i * 0.1), 0, i == 0 ? "A" : "B"), i == 0,
                [new BgHeroComp("Свинобразы - APM-усиление таверны", "S", "easy", "Скупайте и продавайте существ, усиливая таверну."), new BgHeroComp("Элементали - Живой азерит", "S", "medium", null)]))],
            Lobby, true, [], [], [], [], null, 0, [], []);

    /// <summary>
    /// A data summary with the trinket numbers' collector late and every other section fresh: the trinket badges show
    /// their "update delayed" line (frame <c>overlay-trinket-late</c>). Its sequence is 0, so it replaces the host's.
    /// </summary>
    public static DataHealthReport TrinketsLate()
    {
        var late = DataSections.Sources(DataSection.BgTrinkets);
        return new DataHealthReport(
            DataSections.All.SelectMany(DataSections.Sources).Distinct(StringComparer.Ordinal).Select(id => late.Contains(id)
                ? new DataSourceHealth(id, DataFreshness.Stale, 90, 30, Fetched.AddHours(-70), Fetched.AddHours(5))
                : new DataSourceHealth(id, DataFreshness.Fresh, 4.5, 30, Fetched, Fetched.AddHours(5))),
            Fetched.AddHours(4));
    }

    /// <summary>
    /// A game on turn 6 with <paramref name="count"/> trinkets offered (2 to 4), each with a place, a pick rate and a
    /// date, and seven opponents whose boards were seen.
    /// </summary>
    public static BattlegroundsView Trinkets(int count)
    {
        static BgMinion Elemental() => new("BG28_707", "Living Azerite", 2, 2, Golden: false, TechLevel: 1, Keywords: []);
        var opponents = Enumerable.Range(1, 8).Where(place => place != OwnPlace)
            .Select(place => new BgOpponent(place, Heroes[place % Heroes.Length], $"Opponent {place}", 30, 0, 3, place, false, 0, [Elemental(), Elemental(), Elemental()], 5, "Win", 3, false))
            .ToList();
        return new(6, 3, 6, Heroes[0], 30, 0, OwnPlace, false, false, false, [], Lobby, true, opponents, [], [], [], null, 0, [], [])
        {
            TrinketOptions = [.. Enumerable.Range(0, Math.Clamp(count, 1, 4)).Select(i => new BgTrinketOption($"BG30_MagicItem_40{i}", $"Trinket {i + 1}",
                new TrinketMeta(4.52 - (i * 0.1), 0.14 + (i * 0.06), 2528, i == 0 ? "C" : "B", External: true, GamesAreMinimum: true, Updated: Fetched), i == 0))],
        };
    }
}

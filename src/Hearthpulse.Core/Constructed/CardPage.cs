using System.Globalization;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;

namespace Hearthpulse.Core.Constructed;

/// <summary>
/// One card's numbers in a slice of ranked games, as fractions (0.55 = 55 %). A number the source did not give is null.
/// </summary>
/// <param name="DeckShare">Share of decks that run the card.</param>
/// <param name="DeckWinRate">Win rate of decks that run it.</param>
/// <param name="AverageCopies">Copies in a deck that runs it.</param>
/// <param name="TimesPlayed">Times the card was played in the slice: the only sample size the source gives.</param>
/// <param name="PlayedWinRate">Win rate of games where it was played; null when it was never played.</param>
/// <param name="DrawnWinRate">Win rate of games where it was drawn.</param>
/// <param name="KeepRate">Share of opening hands that kept it.</param>
/// <param name="OpeningHandWinRate">Win rate of games where it stayed in the opening hand.</param>
/// <param name="AverageTurnPlayed">The turn it is played on, on average.</param>
public sealed record CardStat(
    string CardId,
    int DbfId,
    double? DeckShare,
    double? DeckWinRate,
    double? AverageCopies,
    int? TimesPlayed,
    double? PlayedWinRate,
    double? DrawnWinRate,
    double? KeepRate,
    double? OpeningHandWinRate,
    double? AverageTurnPlayed);

/// <summary>Every card's numbers of one format, with what the answer says about itself.</summary>
/// <param name="Format">"standard" or "wild".</param>
/// <param name="RankRange">The ranks as the server names them (GOLD, LEGEND), or null.</param>
/// <param name="TimeRange">The period as the server names it (LAST_14_DAYS), or null.</param>
/// <param name="FetchedAt">When the server collected the list, UTC; null when the answer did not say.</param>
public sealed record CardStatsSlice(string Format, string? RankRange, string? TimeRange, DateTime? FetchedAt, IReadOnlyDictionary<string, CardStat> Cards)
{
    /// <summary>The card by its id, else by its dbf id (the list may name another printing's id); null when it is not listed.</summary>
    public CardStat? Find(string cardId, int dbfId) =>
        Cards.TryGetValue(cardId, out var stat) ? stat
        : dbfId > 0 ? Cards.Values.FirstOrDefault(c => c.DbfId == dbfId)
        : null;
}

/// <summary>An archetype of the loaded meta whose lists run the card.</summary>
/// <param name="Lists">The archetype's lists in the meta.</param>
/// <param name="ListsWithCard">Those of them that run the card.</param>
/// <param name="AverageCopies">Copies in a list that runs it.</param>
/// <param name="WinRate">The archetype's own win rate (not the card's).</param>
/// <param name="Games">The archetype's games.</param>
public sealed record CardPageArchetype(int Id, string Name, string Class, int Lists, int ListsWithCard, double AverageCopies, double WinRate, int Games)
{
    public double ListShare => Lists == 0 ? 0 : (double)ListsWithCard / Lists;
}

/// <summary>The card in one archetype's opening hand.</summary>
/// <param name="InHandWinRate">Win rate of games where it stayed in the opening hand.</param>
/// <param name="Impact"><paramref name="InHandWinRate"/> minus the archetype's win rate over the same games.</param>
/// <param name="InHand">Opening hands it stayed in: the sample.</param>
public sealed record CardPageMulligan(int ArchetypeId, string Name, string Class, double KeepRate, double InHandWinRate, double Impact, int InHand);

/// <summary>The player's own games with the card, from the local history only. Counted per game.</summary>
/// <param name="Games">The player's games of the format.</param>
/// <param name="TrackedGames">Those that saved card data (mulligan or draws): the sample behind the other numbers.</param>
/// <param name="WhenDrawn">Games it was drawn in (a kept opening card counts).</param>
/// <param name="WhenPlayed">Games it was played in.</param>
/// <param name="Offered">Games it was in the opening hand before the mulligan.</param>
/// <param name="Kept">Those of them where a copy was kept.</param>
public sealed record CardPageMine(int Games, int TrackedGames, WinLoss WhenDrawn, WinLoss WhenPlayed, int Offered, int Kept);

/// <summary>What the card page shows for one card in one format (docs/card-page.md).</summary>
/// <param name="FormatType">"FT_STANDARD" or "FT_WILD".</param>
/// <param name="Text">The card text without markup.</param>
/// <param name="Stats">The card's numbers; null when the list has no such card or was not loaded.</param>
/// <param name="StatsLoaded">The list itself was there (so a null <paramref name="Stats"/> means "no data for this card").</param>
/// <param name="StatsDate">When the list was collected, UTC.</param>
/// <param name="MetaDate">When the meta behind <paramref name="Archetypes"/> and <paramref name="Mulligan"/> was updated, UTC.</param>
public sealed record CardPage(
    Card Card,
    string FormatType,
    string Text,
    CardStat? Stats,
    bool StatsLoaded,
    string? RankRange,
    string? TimeRange,
    DateTime? StatsDate,
    IReadOnlyList<CardPageArchetype> Archetypes,
    IReadOnlyList<CardPageMulligan> Mulligan,
    DateTime? MetaDate,
    CardPageMine Mine);

/// <summary>Builds the card page: pure functions over what was loaded and the player's own games.</summary>
public static partial class CardPages
{
    /// <param name="formatType">"FT_STANDARD" or "FT_WILD".</param>
    /// <param name="stats">The format's card list, or null when it could not be loaded.</param>
    /// <param name="meta">The format's loaded meta, or null.</param>
    /// <param name="games">The player's games with their card columns (<c>HistoryStore.WithCards</c>); other formats and modes are left out here.</param>
    public static CardPage Build(Card card, string formatType, CardStatsSlice? stats, ConstructedMeta? meta, IReadOnlyList<GameRecord> games, CardDb cards)
    {
        var key = ConstructedMeta.FormatKeyOf(formatType);
        var slice = stats is not null && string.Equals(stats.Format, key, StringComparison.Ordinal) ? stats : null;
        var ofFormat = meta is not null && string.Equals(meta.Format, key, StringComparison.Ordinal) ? meta : null;
        return new CardPage(
            card,
            formatType,
            PlainText(card.Text, cards.Locale),
            slice?.Find(card.Id, card.DbfId),
            slice is not null,
            slice?.RankRange,
            slice?.TimeRange,
            slice?.FetchedAt,
            ofFormat is null ? [] : Archetypes(card, ofFormat),
            ofFormat is null ? [] : Mulligan(card, ofFormat, cards),
            ofFormat is { Updated: var updated } && updated != DateTime.MinValue ? updated : null,
            Mine(card, formatType, games));
    }

    /// <summary>
    /// The archetypes whose lists run the card: the largest share of lists first, then the most played. Lists that
    /// cannot be decoded are not counted on either side.
    /// </summary>
    public static IReadOnlyList<CardPageArchetype> Archetypes(Card card, ConstructedMeta meta)
    {
        var byArchetype = meta.Archetypes.ToDictionary(a => a.Id);
        var result = new List<CardPageArchetype>();
        foreach (var group in meta.Decks.GroupBy(d => d.ArchetypeId))
        {
            if (!byArchetype.TryGetValue(group.Key, out var archetype))
            {
                continue;
            }

            var lists = group.Select(d => DeckString.TryDecode(d.DeckString)).OfType<DeckDefinition>().ToList();
            var copies = lists.Select(l => l.Cards.GetValueOrDefault(card.DbfId)).Where(n => n > 0).ToList();
            if (copies.Count > 0)
            {
                result.Add(new CardPageArchetype(archetype.Id, archetype.Name, archetype.Class, lists.Count, copies.Count, copies.Average(), archetype.WinRate, archetype.Games));
            }
        }

        return result.OrderByDescending(a => a.ListShare).ThenByDescending(a => a.Games).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The card in each archetype's opening hand, the largest sample first. Archetypes that share one set of mulligan
    /// numbers give one row (the most played of them).
    /// </summary>
    public static IReadOnlyList<CardPageMulligan> Mulligan(Card card, ConstructedMeta meta, CardDb cards)
    {
        var seen = new HashSet<int>();
        var result = new List<CardPageMulligan>();
        foreach (var archetype in meta.Archetypes.OrderByDescending(a => a.Games))
        {
            var row = archetype.Mulligan.FirstOrDefault(m =>
                string.Equals(m.CardId, card.Id, StringComparison.Ordinal) || (card.DbfId > 0 && cards.Get(m.CardId)?.DbfId == card.DbfId));
            if (row is null || row.InHand <= 0 || !seen.Add(archetype.MulliganArchetypeId ?? archetype.Id))
            {
                continue;
            }

            result.Add(new CardPageMulligan(
                archetype.Id,
                archetype.Name,
                archetype.Class,
                row.KeepRate,
                row.InHandWinRate,
                row.InHandWinRate - (archetype.MulliganBaseline ?? archetype.WinRate),
                row.InHand));
        }

        return result.OrderByDescending(m => m.InHand).ToList();
    }

    /// <summary>The player's own Ranked and Casual games of the format: how often the card was drawn, played and kept.</summary>
    public static CardPageMine Mine(Card card, string formatType, IReadOnlyList<GameRecord> games)
    {
        var mine = games.Where(g => !g.IsAbandoned && StatsFilter.IsConstructed(g, formatType)).ToList();
        var tracked = mine.Where(g => g.CardsDrawn is not null || g.Mulligan is not null).ToList();
        var drawn = tracked.Where(g => GameCards.Turns(g.CardsDrawn).Any(c => c.CardId == card.Id)).ToList();
        var played = tracked.Where(g => GameCards.Turns(g.CardsPlayed).Any(c => c.CardId == card.Id)).ToList();
        var offered = tracked.Select(g => GameCards.Mulligan(g.Mulligan)).Where(m => m is not null && m.Offered.Contains(card.Id)).ToList();
        return new CardPageMine(mine.Count, tracked.Count, WinLoss.Of(drawn), WinLoss.Of(played), offered.Count, offered.Count(m => m!.Kept.Contains(card.Id)));
    }

    /// <summary>
    /// Card text for reading in a line of the page: no markup, layout codes or in-game slots, plural forms ("|4(card,cards)") resolved
    /// by the number before them, one space between words.
    /// </summary>
    public static string PlainText(string? text, string? locale = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // "[x]" and "&" are the client's line-layout codes ("<b>Battlecry:</b>&\n ..."), "{0}" a slot filled in a game.
        var t = Slot().Replace(text, string.Empty).Replace("&", string.Empty, StringComparison.Ordinal);
        t = t.Replace("[x]", string.Empty, StringComparison.Ordinal).Replace('\n', ' ').Replace('_', ' ').Replace(' ', ' ');
        t = Tag().Replace(t, string.Empty);
        t = Counter().Replace(t, string.Empty);
        t = Amount().Replace(t, "$1");
        t = Plural().Replace(t, m =>
        {
            var forms = m.Groups[2].Value.Split(',');
            var number = int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 2;
            return m.Groups[1].Value + " " + forms[Math.Min(PluralForm(number, forms.Length, locale), forms.Length - 1)];
        });
        return Spaces().Replace(t.Replace("@", string.Empty, StringComparison.Ordinal), " ").Trim();
    }

    /// <summary>Which of the listed forms a number takes: Russian has three (1 карта, 2 карты, 5 карт), the others one and many.</summary>
    private static int PluralForm(int number, int forms, string? locale)
    {
        if (forms >= 3 && locale is "ruRU")
        {
            var (tens, ones) = (number % 100, number % 10);
            return ones == 1 && tens != 11 ? 0 : ones is >= 2 and <= 4 && tens is < 12 or > 14 ? 1 : 2;
        }

        return number == 1 ? 0 : 1;
    }

    [GeneratedRegex("<[^<>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\{\d\}")]
    private static partial Regex Slot();

    /// <summary>A live counter in brackets ("(@ left!)"): it has no value outside a game.</summary>
    [GeneratedRegex(@"\s*\([^()]*@[^()]*\)")]
    private static partial Regex Counter();

    /// <summary>"$3" (spell damage) and "#3" (healing) are plain numbers on the page.</summary>
    [GeneratedRegex(@"[$#](\d+)")]
    private static partial Regex Amount();

    [GeneratedRegex(@"(\d+)\s*\|4\(([^()]*)\)")]
    private static partial Regex Plural();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
